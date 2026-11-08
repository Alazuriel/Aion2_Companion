using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// Only sends the immutable v0.8 gzip/JSONL research trace. It does not query community models.
/// V2 uploads are separate from the v1 600-event Community Sync consent and queue.
/// Network endpoint and JSON request fields must match the deployed receiver.
/// </summary>
public sealed class PhasewardTraceV2Sync
{
    public const string ConsentSchema = "phaseward-diagnostic-v2";
    public const int ChunkBytes = 256 * 1024;
    public const long MaximumArchiveBytes = 32L * 1024 * 1024;
    public const int MaximumQueueItems = 20;
    private const string Endpoint = "phaseward/traces";
    private static readonly object Gate = new();
    private static readonly HttpClient Http = new() {
        BaseAddress = AccountApiClient.ApiBaseUri,
        Timeout = TimeSpan.FromSeconds(45)
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true
    };

    public static PhasewardTraceV2Sync Shared { get; } = new();
    public event EventHandler? Changed;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private DateTimeOffset _nextAttempt;
    private int _errors;
    private string _status = "disabled";
    public string Status => Volatile.Read(ref _status);

    private PhasewardTraceV2Sync() { }

    private static string AccountHash(string id) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    private static string Root => Path.Combine(CompanionSettings.SettingsDirectory, "phaseward-trace-v2-outbox");
    private static string DirectoryForAccount(string id) => Path.Combine(Root, AccountHash(id));
    private static string? ActiveId => AccountService.Shared.Snapshot.State == AccountConnectionState.Connected
        ? AccountService.Shared.Snapshot.Profile?.AccountId : null;
    public static string? ConnectedAccountId => ActiveId;

    public static void EnableForAccount(string accountId)
    {
        // Only a positive decision on the separate raw-packet notice may enable V2.
        if (string.IsNullOrWhiteSpace(accountId) ||
            !CompanionSettings.PhasewardTraceV2RawUploadAcknowledged) return;
        CompanionSettings.PhasewardTraceV2AccountHash = AccountHash(accountId);
        CompanionSettings.PhasewardTraceV2EnableOnNextLogin = false;
        CompanionSettings.PhasewardTraceV2RevokePending = false;
        CompanionSettings.PhasewardTraceV2EnablePending = true;
        CompanionSettings.PhasewardDetailedTraceEnabled = true;
        CompanionSettings.PhasewardTraceV2UploadEnabled = true;
        Shared.OnPreferenceChanged();
    }
    public static void DisableForActiveAccount()
    {
        CompanionSettings.PhasewardTraceV2EnableOnNextLogin = false;
        CompanionSettings.PhasewardTraceV2UploadEnabled = false;
        CompanionSettings.PhasewardTraceV2RawUploadAcknowledged = false;
        CompanionSettings.PhasewardTraceV2EnablePending = false;
        CompanionSettings.PhasewardTraceV2RevokePending = true;
        DeletePendingForActiveAccount();
        Shared.OnPreferenceChanged();
    }

    private static bool HasLocalOptIn(string? accountId) =>
        !string.IsNullOrWhiteSpace(accountId) &&
        CompanionSettings.PhasewardTraceV2UploadEnabled &&
        CompanionSettings.PhasewardTraceV2RawUploadAcknowledged &&
        CompanionSettings.PhasewardDetailedTraceEnabled &&
        string.Equals(CompanionSettings.PhasewardTraceV2AccountHash,
            AccountHash(accountId), StringComparison.Ordinal);

    public static int PendingCount()
    {
        string? id = ActiveId;
        if (string.IsNullOrWhiteSpace(id)) return 0;
        lock (Gate)
        {
            string dir = DirectoryForAccount(id);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.state.json").Length : 0;
        }
    }

    public static void DeletePendingForActiveAccount()
    {
        var account = ActiveId;
        if (account is null) return;
        lock (Gate)
        {
            string dir = DirectoryForAccount(account);
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*"))
            {
                if (!file.EndsWith(".state.json", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Called after a LOCAL v0.8 trace has been fully written. Never retroactively scans old archives.</summary>
    public static bool TryQueueCompletedArchive(string localArchivePath, string? accountId)
    {
        if (!HasLocalOptIn(accountId) || !File.Exists(localArchivePath)) return false;
        lock (Gate)
        {
            try
            {
                var input = new FileInfo(localArchivePath);
                if (input.Length == 0 || input.Length > MaximumArchiveBytes) return false;
                string dir = DirectoryForAccount(accountId!);
                Directory.CreateDirectory(dir);
                if (Directory.GetFiles(dir, "*.state.json").Length >= MaximumQueueItems) return false;
                // Metadata is derived ONLY from the v0.8 header. Never invent an NPC/variant.
                TraceHeader? header = ReadHeader(localArchivePath);
                if (header is null || header.BossNpcId <= 0 || header.SchemaVersion != 2) return false;
                string submissionId = Guid.NewGuid().ToString("D");
                string destination = Path.Combine(dir, submissionId + ".jsonl.gz");
                string tmp = destination + ".tmp";
                try
                {
                    File.Copy(localArchivePath, tmp);
                    byte[] hash = SHA256.HashData(File.ReadAllBytes(tmp));
                    File.Move(tmp, destination);
                    var state = new TraceState {
                        SubmissionId = submissionId,
                        ArchiveSha256 = Convert.ToHexString(hash).ToLowerInvariant(),
                        ArchiveBytes = input.Length,
                        ChunkCount = checked((int)((input.Length + ChunkBytes - 1) / ChunkBytes)),
                        BossNpcId = header.BossNpcId,
                        StartedAt = header.StartedAt,
                        ClientVersion = CurrentClientVersion(),
                        CaptureComplete = header.CaptureComplete,
                        Truncated = header.Truncated,
                        LostFrames = header.LostFrames,
                        FramesSaved = header.FramesSaved
                    };
                    SaveState(dir, state, allowCreate: true);
                    Shared.Wake();
                    return true;
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (JsonException) { return false; }
        }
    }

    private static TraceHeader? ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string? header = reader.ReadLine();
        if (header is null || header.Length > 16_384) return null;
        using var doc = JsonDocument.Parse(header);
        var data = doc.RootElement;
        if (!StringProp(data, "record_type").Equals("header", StringComparison.Ordinal)) return null;
        return new TraceHeader {
            SchemaVersion = IntProp(data, "schema_version"),
            BossNpcId = IntProp(data, "boss_npc_id"),
            StartedAt = StringProp(data, "started_at"),
            FinishedAt = StringProp(data, "finished_at"),
            Outcome = StringProp(data, "outcome"),
            CastCount = IntProp(data, "cast_count"),
            BossHpCount = IntProp(data, "boss_hp_count"),
            CaptureComplete = BoolProp(data, "capture_complete"),
            Truncated = BoolProp(data, "truncated"),
            LostFrames = LongProp(data, "lost_frames"),
            FramesSaved = IntProp(data, "frames_saved")
        };
    }
    // The assembly metadata is the actual built client version, not a fake constant.
    // Capture it in the durable state before the first POST for stable UUID idempotency.
    private static string CurrentClientVersion() =>
        typeof(PhasewardTraceV2Sync).Assembly.GetName().Version?.ToString() ?? string.Empty;

    private static string StringProp(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty : string.Empty;
    private static int IntProp(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.TryGetInt32(out int n) ? n : 0;
    private static long LongProp(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.TryGetInt64(out long n) ? n : 0;
    private static bool BoolProp(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    public void Start()
    {
        if (_loop is not null) return;
        AccountService.Shared.Changed += AccountChanged;
        _loop = Task.Run(BackgroundAsync);
    }
    public void Stop()
    {
        AccountService.Shared.Changed -= AccountChanged;
        _stop.Cancel();
        Wake();
    }
    private void AccountChanged(object? sender, EventArgs e)
    {
        string? id = ConnectedAccountId;
        if (id is not null && CompanionSettings.PhasewardTraceV2EnableOnNextLogin)
            EnableForAccount(id);
        Wake();
    }
    public void OnPreferenceChanged() { _nextAttempt = DateTimeOffset.MinValue; Wake(); Changed?.Invoke(this, EventArgs.Empty); }
    public void Wake() { try { if (_wake.CurrentCount == 0) _wake.Release(); } catch (SemaphoreFullException) { } }
    private void SetStatus(string status)
    {
        if (_status == status) return;
        Volatile.Write(ref _status, status);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void Backoff(string why, TimeSpan? serverDelay = null)
    {
        _errors = Math.Min(_errors + 1, 8);
        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(900, 10 * (1 << Math.Min(6, _errors))));
        if (serverDelay > delay) delay = serverDelay.Value;
        _nextAttempt = DateTimeOffset.UtcNow + delay;
        SetStatus(why);
    }
    private async Task BackgroundAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await RunOnceAsync(_stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception) { Backoff("retry_later"); }
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                Task delay = Task.Delay(TimeSpan.FromSeconds(20), linked.Token);
                Task woke = _wake.WaitAsync(linked.Token);
                await Task.WhenAny(delay, woke);
                linked.Cancel();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>Public for offline/staging integration tests. Never pulls community model data.</summary>
    public async Task<string> RunOnceAsync(CancellationToken ct = default)
    {
        await _runGate.WaitAsync(ct);
        try
        {
            if (_nextAttempt > DateTimeOffset.UtcNow) return "cooldown";
            string? accountId = ActiveId;
            if (accountId is not null && CompanionSettings.PhasewardTraceV2EnableOnNextLogin)
                EnableForAccount(accountId);
            if (string.IsNullOrWhiteSpace(accountId)) { SetStatus("sign_in_required"); return "sign_in_required"; }
            string? token = await AccountService.Shared.GetAccessTokenAsync(false, ct);
            if (string.IsNullOrWhiteSpace(token)) { SetStatus("sign_in_required"); return "sign_in_required"; }
            if (CompanionSettings.PhasewardTraceV2RevokePending &&
                CompanionSettings.PhasewardTraceV2AccountHash == AccountHash(accountId))
            {
                using var revoke = await SendJsonAsync(HttpMethod.Put, "phaseward/traces/consent", token,
                    new { enabled = false, schema_version = ConsentSchema }, ct);
                if (revoke.IsSuccessStatusCode)
                {
                    CompanionSettings.PhasewardTraceV2RevokePending = false;
                    DeletePendingForActiveAccount();
                    _errors = 0;
                }
                else { Backoff("revocation_pending"); return "revocation_pending"; }
            }
            if (!CompanionSettings.PhasewardTraceV2RawUploadAcknowledged &&
                (CompanionSettings.PhasewardTraceV2UploadEnabled ||
                 CompanionSettings.PhasewardTraceV2EnableOnNextLogin))
            { SetStatus("raw_consent_ack_required"); return "raw_consent_ack_required"; }
            if (!HasLocalOptIn(accountId)) { SetStatus("disabled"); return "disabled"; }
            // Even when the local switch is on, server capability MUST explicitly advertise v2.
            using (var meta = await SendAsync(HttpMethod.Get, "phaseward/meta", token: null, ct: ct))
            {
                if (!meta.IsSuccessStatusCode) { Backoff("meta_unavailable"); return "meta_unavailable"; }
                using var json = await ParseJsonAsync(meta, ct);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid meta body");
                if (!BoolProp(root, "trace_v2_enabled")) { SetStatus("server_v2_off"); return "server_v2_off"; }
                if (IntProp(root, "trace_v2_max_chunk_bytes") is int max && max > 0 && max < ChunkBytes)
                { SetStatus("server_limit_mismatch"); return "server_limit_mismatch"; }
            }
            if (CompanionSettings.PhasewardTraceV2EnablePending)
            {
                using var update = await SendJsonAsync(HttpMethod.Put, "phaseward/traces/consent", token,
                    new { enabled = true, schema_version = ConsentSchema, raw_packet_upload_ack = true }, ct);
                if (!update.IsSuccessStatusCode)
                { Backoff(await ApiStatusAsync(update, "consent_pending", ct), update.Headers.RetryAfter?.Delta); return "consent_pending"; }
                CompanionSettings.PhasewardTraceV2EnablePending = false;
            }
            using (var consent = await SendAsync(HttpMethod.Get, "phaseward/traces/consent", token, ct: ct))
            {
                if (!consent.IsSuccessStatusCode) { Backoff("consent_unavailable"); return "consent_unavailable"; }
                using var json = await ParseJsonAsync(consent, ct);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid consent body");
                if (root.TryGetProperty("consent", out var nested) && nested.ValueKind == JsonValueKind.Object) root = nested;
                if (!BoolProp(root, "enabled")) { SetStatus("server_consent_off"); return "server_consent_off"; }
            }
            string directory = DirectoryForAccount(accountId);
            TraceState? state = LoadFirstPending(directory);
            if (state is null) { _errors = 0; SetStatus("ready_empty"); return "ready_empty"; }
            string archive = Path.Combine(directory, state.SubmissionId + ".jsonl.gz");
            if (!File.Exists(archive)) { Quarantine(directory, state); SetStatus("missing_archive"); return "missing_archive"; }
            var info = new FileInfo(archive);
            if (info.Length != state.ArchiveBytes || info.Length > MaximumArchiveBytes)
            { Quarantine(directory, state); SetStatus("changed_archive"); return "changed_archive"; }
            string calculated = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive, ct))).ToLowerInvariant();
            if (!string.Equals(calculated, state.ArchiveSha256, StringComparison.Ordinal))
            { Quarantine(directory, state); SetStatus("changed_archive"); return "changed_archive"; }
            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                // Repair the adapter from the genuine immutable v0.8 JSONL header.
                // This also migrates PREVIOUSLY QUEUED v0.9 .state.json entries without backfill.
                TraceHeader? header;
                try { header = ReadHeader(archive); }
                catch (Exception ex) when (ex is IOException or JsonException)
                { Quarantine(directory, state); SetStatus("invalid_local_trace_header"); return "invalid_local_trace_header"; }
                if (header is null || header.SchemaVersion != 2 ||
                    header.BossNpcId != state.BossNpcId || header.StartedAt != state.StartedAt ||
                    header.CaptureComplete != state.CaptureComplete || header.Truncated != state.Truncated ||
                    header.LostFrames != state.LostFrames || header.FramesSaved != state.FramesSaved ||
                    header.FramesSaved < 1 || header.CastCount < 0 || header.BossHpCount < 0 ||
                    header.Outcome is not ("killed" or "wipe" or "unknown") ||
                    !DateTimeOffset.TryParse(header.FinishedAt, out _) ||
                    !DateTimeOffset.TryParse(header.StartedAt, out _))
                { Quarantine(directory, state); SetStatus("invalid_local_trace_header"); return "invalid_local_trace_header"; }
                if (string.IsNullOrWhiteSpace(state.ClientVersion))
                {
                    state.ClientVersion = CurrentClientVersion();
                    SaveState(directory, state); // prior to POST: replay exactly the same manifest
                }
                if (!Regex.IsMatch(state.ClientVersion, @"^[A-Za-z0-9._+-]{1,36}$"))
                { SetStatus("client_version_unavailable"); return "client_version_unavailable"; }
                var manifest = new {
                    schema_version = 2,
                    submission_id = state.SubmissionId,
                    client_version = state.ClientVersion,
                    boss_npc_id = header.BossNpcId,
                    variant = (string?)null, // the v0.8 recorder stores no authoritative variant
                    started_at = header.StartedAt,
                    finished_at = header.FinishedAt,
                    outcome = header.Outcome,
                    capture_complete = header.CaptureComplete,
                    truncated = header.Truncated,
                    lost_frames = header.LostFrames,
                    frames_saved = header.FramesSaved,
                    cast_count = header.CastCount,
                    boss_hp_count = header.BossHpCount,
                    chunk_count = state.ChunkCount,
                    compressed_bytes = state.ArchiveBytes,
                    compressed_sha256 = state.ArchiveSha256,
                    consent = new { schema_version = ConsentSchema, raw_packet_upload = true }
                };
                using var created = await SendJsonAsync(HttpMethod.Post, Endpoint, token, manifest, ct);
                if (created.StatusCode == HttpStatusCode.Conflict)
                { Quarantine(directory, state); SetStatus(await ApiStatusAsync(created, "create_conflict", ct)); return "create_conflict"; }
                if (created.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge)
                { Quarantine(directory, state); SetStatus(await ApiStatusAsync(created, "rejected_manifest", ct)); return "rejected_manifest"; }
                if (!created.IsSuccessStatusCode) return await RetryStatusAsync(created, "create_failed", ct);
                using var doc = await ParseJsonAsync(created, ct);
                var response = doc.RootElement;
                if (response.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid create response");
                state.SessionId = StringProp(response, "session_id");
                if (state.SessionId.Length == 0) state.SessionId = StringProp(response, "id");
                if (state.SessionId.Length == 0 && response.TryGetProperty("session", out var nested) && nested.ValueKind == JsonValueKind.Object)
                    state.SessionId = StringProp(nested, "id");
                if (!Regex.IsMatch(state.SessionId, @"^[A-Za-z0-9_-]{1,128}$"))
                { SetStatus("server_session_schema_unknown"); return "server_session_schema_unknown"; }
                var sessionStatus = StringProp(response, "status");
                if (sessionStatus == "complete") state.NextChunk = state.ChunkCount;
                else if (sessionStatus != "receiving")
                { SetStatus("server_session_schema_unknown"); return "server_session_schema_unknown"; }
                SaveState(directory, state);
            }
            // Upload a small bounded batch each pass. Chunk index and bytes stay stable on retry.
            using var file = File.OpenRead(archive);
            int count = 0;
            while (state.NextChunk < state.ChunkCount && count++ < 4)
            {
                if (!HasLocalOptIn(ActiveId) || ActiveId != accountId) return "consent_changed";
                int index = state.NextChunk;
                file.Position = (long)index * ChunkBytes;
                int size = checked((int)Math.Min(ChunkBytes, file.Length - file.Position));
                byte[] bytes = new byte[size];
                await file.ReadExactlyAsync(bytes, ct);
                string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                string path = Endpoint + "/" + state.SessionId + "/chunks/" + index;
                using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = new ByteArrayContent(bytes) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation("X-Chunk-SHA256", hash);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { state.SessionId = ""; state.NextChunk = 0; SaveState(directory, state); Backoff("session_expired"); return "session_expired"; }
                if (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.UnprocessableEntity)
                { Quarantine(directory, state); SetStatus(await ApiStatusAsync(response, "chunk_rejected", ct)); return "chunk_rejected"; }
                if (!response.IsSuccessStatusCode) return await RetryStatusAsync(response, "chunk_failed", ct);
                state.NextChunk++;
                SaveState(directory, state);
            }
            if (state.NextChunk < state.ChunkCount) { SetStatus("uploading_" + state.NextChunk + "/" + state.ChunkCount); Wake(); return "partial"; }
            if (!HasLocalOptIn(ActiveId) || ActiveId != accountId) return "consent_changed";
            using var done = await SendJsonAsync(HttpMethod.Post, Endpoint + "/" + state.SessionId + "/complete",
                token, new { submission_id = state.SubmissionId, archive_sha256 = state.ArchiveSha256 }, ct);
            if (done.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) { state.SessionId = ""; state.NextChunk = 0; SaveState(directory, state); Backoff("session_expired"); return "session_expired"; }
            if (done.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            { Quarantine(directory, state); SetStatus(await ApiStatusAsync(done, "finalize_rejected", ct)); return "finalize_rejected"; }
            if (!done.IsSuccessStatusCode) return await RetryStatusAsync(done, "finalize_failed", ct);
            // Require PHP's actual finalize receipt before deleting local raw bytes.
            if (done.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Accepted))
            { SetStatus("finalize_ack_schema_unknown"); return "finalize_ack_schema_unknown"; }
            using (var receipt = await ParseJsonAsync(done, ct))
            {
                if (StringProp(receipt.RootElement, "status") != "queued" ||
                    StringProp(receipt.RootElement, "session_id") != state.SessionId)
                { SetStatus("finalize_ack_schema_unknown"); return "finalize_ack_schema_unknown"; }
            }
            // Complete server ACK: only then delete the original outbox archive and its state.
            lock (Gate)
            {
                File.Delete(StatePath(directory, state));
                File.Delete(archive);
            }
            _errors = 0;
            _nextAttempt = DateTimeOffset.MinValue;
            SetStatus("uploaded");
            return "uploaded";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { Backoff("offline"); return "offline"; }
        catch (TaskCanceledException) { Backoff("timeout"); return "timeout"; }
        catch (JsonException) { Backoff("response_schema_unknown"); return "response_schema_unknown"; }
        catch (InvalidOperationException) { Backoff("response_schema_unknown"); return "response_schema_unknown"; }
        catch (IOException) { Backoff("local_io_error"); return "local_io_error"; }
        catch (UnauthorizedAccessException) { Backoff("local_access_denied"); return "local_access_denied"; }
        finally { _runGate.Release(); }
    }

    // Diagnostics expose only the HTTP code and allowlisted API error identifiers,
    // never raw response bodies, tokens, account IDs, packet bytes or traces.
    private static async Task<string> ApiStatusAsync(HttpResponseMessage response, string category, CancellationToken ct)
    {
        string suffix = string.Empty;
        if (response.Content.Headers.ContentLength is not long bytes || bytes <= 4096)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                byte[] buffer = new byte[4097];
                int used = 0;
                while (used < buffer.Length)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(used), ct);
                    if (read == 0) break;
                    used += read;
                }
                if (used > 4096) return category + "_http_" + (int)response.StatusCode;
                using var doc = JsonDocument.Parse(buffer.AsMemory(0, used), new JsonDocumentOptions { MaxDepth = 8 });
                var root = doc.RootElement;
                string code = StringProp(root, "error");
                if (code.Length == 0) code = StringProp(root, "code");
                if (Regex.IsMatch(code, @"^[a-z][a-z0-9_]{0,63}$")) suffix = "_" + code;
            }
            catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException or InvalidOperationException) { }
        }
        return category + "_http_" + (int)response.StatusCode + suffix;
    }
    private async Task<string> RetryStatusAsync(HttpResponseMessage response, string generic, CancellationToken ct)
    {
        string category = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "auth_or_consent_required" : generic;
        Backoff(await ApiStatusAsync(response, category, ct), response.Headers.RetryAfter?.Delta);
        return category;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    private static async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path,
        string token, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, Json));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 }, ct);
    }
    private static TraceState? LoadFirstPending(string directory)
    {
        lock (Gate)
        {
            if (!Directory.Exists(directory)) return null;
            foreach (string path in Directory.GetFiles(directory, "*.state.json").OrderBy(x => x))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<TraceState>(File.ReadAllText(path), Json);
                    if (state is not null && Guid.TryParse(state.SubmissionId, out _) &&
                        state.ChunkCount > 0 && state.ArchiveBytes > 0 && state.ArchiveBytes <= MaximumArchiveBytes)
                        return state;
                    File.Move(path, path + ".blocked", true);
                }
                catch (Exception ex) when (ex is IOException or JsonException) { return null; }
            }
            return null;
        }
    }
    private static string StatePath(string directory, TraceState state) => Path.Combine(directory, state.SubmissionId + ".state.json");
    private static void SaveState(string directory, TraceState state, bool allowCreate = false)
    {
        lock (Gate)
        {
            string dest = StatePath(directory, state);
            // A user may revoke consent and delete an entry during a network request.
            // Never recreate a cancelled upload state when the response arrives later.
            if (!allowCreate && !File.Exists(dest)) return;
            string temp = dest + ".tmp";
            try
            {
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(state, Json));
                File.Move(temp, dest, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    private static void Quarantine(string directory, TraceState state)
    {
        string path = StatePath(directory, state);
        if (File.Exists(path)) File.Move(path, path + ".blocked", true);
    }

    private sealed class TraceHeader
    {
        public int SchemaVersion { get; set; }
        public int BossNpcId { get; set; }
        public string StartedAt { get; set; } = "";
        public string FinishedAt { get; set; } = "";
        public string Outcome { get; set; } = "";
        public int CastCount { get; set; }
        public int BossHpCount { get; set; }
        public bool CaptureComplete { get; set; }
        public bool Truncated { get; set; }
        public long LostFrames { get; set; }
        public int FramesSaved { get; set; }
    }
    public sealed class TraceState
    {
        public string SubmissionId { get; set; } = "";
        public string ArchiveSha256 { get; set; } = "";
        public long ArchiveBytes { get; set; }
        public int ChunkCount { get; set; }
        public int BossNpcId { get; set; }
        public string StartedAt { get; set; } = "";
        public string ClientVersion { get; set; } = "";
        public bool CaptureComplete { get; set; }
        public bool Truncated { get; set; }
        public long LostFrames { get; set; }
        public int FramesSaved { get; set; }
        public string SessionId { get; set; } = "";
        public int NextChunk { get; set; }
    }
}
