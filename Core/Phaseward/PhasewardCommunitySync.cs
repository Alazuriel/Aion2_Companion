using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// Outbound-only desktop uploader. NO model/observation download endpoint is implemented.
/// Meta and consent reads contain only operational feature/permission state.
/// A separate explicit upload opt-in is required even if an older local-only queue toggle was enabled.
/// </summary>
public sealed class PhasewardCommunitySync
{
    public static PhasewardCommunitySync Shared { get; } = new();
    public event EventHandler? Changed;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _single = new(1, 1);
    private Task? _background;
    private DateTimeOffset _nextAttempt;
    private string? _lastAccount;
    private bool _knownConsent;
    private bool _intakeEnabled;
    private bool _pendingRevoke;
    private bool _pendingEnable;
    private string _status = "disabled";
    private int _failureCount;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(25);
    public string Status => Volatile.Read(ref _status);
    public bool IsBackendReady => _intakeEnabled && _knownConsent;

    private PhasewardCommunitySync() { }

    public void Start()
    {
        if (_background is not null) return;
        _background = Task.Run(RunAsync);
        AccountService.Shared.Changed += AccountChanged;
        Wake();
    }

    public void Stop()
    {
        AccountService.Shared.Changed -= AccountChanged;
        _lifetime.Cancel();
        Wake();
    }

    private void AccountChanged(object? sender, EventArgs e) => Wake();

    public void OnUploadPreferenceChanged(bool enabled)
    {
        // This is called only by the separate, explicit Upload consent checkbox.
        if (enabled) _pendingEnable = true;
        else _pendingRevoke = true;
        // Opt-out/re-consent must bypass a previous upload retry delay.
        _nextAttempt = DateTimeOffset.MinValue;
        Wake();
    }

    public void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); }
            catch (SemaphoreFullException) { }
        }
    }

    private void SetStatus(string status)
    {
        if (_status == status) return;
        Volatile.Write(ref _status, status);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try { await RunOnceAsync(_lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception) { Backoff("offline"); } // never crash capture or leak HTTP bodies
            try
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                Task timer = Task.Delay(PollInterval, wait.Token);
                Task signal = _wake.WaitAsync(wait.Token);
                Task done = await Task.WhenAny(timer, signal);
                wait.Cancel();
                try { await done; }
                catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested) { }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
        }
    }

    private void Backoff(string reason, TimeSpan? serverDelay = null)
    {
        _failureCount = Math.Min(_failureCount + 1, 7);
        var seconds = Math.Min(300, 8 * (1 << Math.Min(_failureCount, 5)));
        var delay = TimeSpan.FromSeconds(seconds);
        if (serverDelay > delay) delay = serverDelay.Value;
        _nextAttempt = DateTimeOffset.UtcNow + delay;
        SetStatus(reason);
    }

    public async Task<string> RunOnceAsync(CancellationToken ct = default)
    {
        await _single.WaitAsync(ct);
        try
        {
            if (_nextAttempt > DateTimeOffset.UtcNow) return "cooldown";
            var account = AccountService.Shared.Snapshot;
            string? accountId = account.Profile?.AccountId;
            if (account.State != AccountConnectionState.Connected || string.IsNullOrWhiteSpace(accountId))
            {
                _knownConsent = false;
                _intakeEnabled = false;
                SetStatus("sign_in_required");
                return "sign_in_required";
            }
            if (!string.Equals(_lastAccount, accountId, StringComparison.Ordinal))
            {
                _lastAccount = accountId;
                _knownConsent = false;
                _intakeEnabled = false;
                _failureCount = 0;
                // Only the explicitly approved account can ever enable uploads. 
                _pendingEnable = CompanionSettings.PhasewardCommunityEnablePending;
            }
            if (CompanionSettings.PhasewardCommunityEnableOnNextLogin)
            {
                CompanionSettings.PhasewardCommunityUploadAccountHash =
                    PhasewardCommunityOutbox.AccountHash(accountId);
                CompanionSettings.PhasewardCommunityUploadConsentConfirmed = true;
                CompanionSettings.PhasewardCommunityQueueEnabled = true;
                CompanionSettings.PhasewardLearningEnabled = true;
                CompanionSettings.PhasewardCommunityRevokePending = false;
                CompanionSettings.PhasewardCommunityEnablePending = true;
                CompanionSettings.PhasewardCommunityEnableOnNextLogin = false;
                _pendingEnable = true;
            }
            if (CompanionSettings.PhasewardCommunityUploadAccountHash !=
                PhasewardCommunityOutbox.AccountHash(accountId))
            {
                _intakeEnabled = false;
                _knownConsent = false;
                SetStatus("account_change_reapprove");
                return "account_change_reapprove";
            }
            if (!CompanionSettings.PhasewardCommunityUploadConsentConfirmed &&
                !CompanionSettings.PhasewardCommunityRevokePending)
            {
                SetStatus("disabled"); return "disabled";
            }
            string? token = await AccountService.Shared.GetAccessTokenAsync(false, ct);
            if (string.IsNullOrWhiteSpace(token)) { SetStatus("sign_in_required"); return "sign_in_required"; }
            // Avoid hammering meta/consent every 25s when nothing is queued.
            if (_knownConsent && _intakeEnabled && !_pendingEnable &&
                !CompanionSettings.PhasewardCommunityEnablePending &&
                CompanionSettings.PhasewardCommunityUploadConsentConfirmed &&
                CompanionSettings.PhasewardCommunityQueueEnabled &&
                PhasewardCommunityOutbox.GetFirstPendingPath(accountId) is null)
            {
                SetStatus("ready");
                return "empty";
            }
            using var client = new AccountApiClient();
            if (!CompanionSettings.PhasewardCommunityUploadConsentConfirmed ||
                !CompanionSettings.PhasewardCommunityQueueEnabled)
            {
                _intakeEnabled = false;
                if (_pendingRevoke || CompanionSettings.PhasewardCommunityRevokePending)
                {
                    using var revokeBody = JsonBody("{\"enabled\":false,\"schema_version\":\"community-v1\"}");
                    using var revoke = await client.SendAsync(HttpMethod.Put, "phaseward/consent", token, revokeBody, ct);
                    if (revoke.IsSuccessStatusCode)
                    {
                        _pendingRevoke = false; _knownConsent = false; _failureCount = 0;
                        CompanionSettings.PhasewardCommunityRevokePending = false;
                    }
                    else { Backoff("consent_revoke_pending"); return "consent_revoke_pending"; }
                }
                SetStatus("disabled");
                return "disabled";
            }
            using (var meta = await client.SendAsync(HttpMethod.Get, "phaseward/meta", cancellationToken: ct))
            {
                if (!meta.IsSuccessStatusCode) { Backoff("feature_unavailable"); return "feature_unavailable"; }
                using var doc = await ReadJsonAsync(meta, ct);
                var data = doc.RootElement;
                if (!data.TryGetProperty("intake_enabled", out var intake) ||
                    intake.ValueKind != JsonValueKind.True)
                {
                    _intakeEnabled = false;
                    SetStatus("feature_disabled");
                    return "feature_disabled";
                }
                // Local payload bounds are fixed to the approved v1 ceiling.
                if (data.TryGetProperty("max_events", out var events) &&
                    events.TryGetInt32(out var maxEvents) && maxEvents < 600)
                { SetStatus("limits_incompatible"); return "limits_incompatible"; }
                if (data.TryGetProperty("max_bytes", out var bytes) &&
                    bytes.TryGetInt32(out var maxBytes) && maxBytes < PhasewardCommunityOutbox.MaxBodyBytes)
                { SetStatus("limits_incompatible"); return "limits_incompatible"; }
                _intakeEnabled = true;
            }
            if (_pendingEnable || CompanionSettings.PhasewardCommunityEnablePending)
            {
                using var consentBody = JsonBody("{\"enabled\":true,\"schema_version\":\"community-v1\"}");
                using var write = await client.SendAsync(HttpMethod.Put, "phaseward/consent", token, consentBody, ct);
                if (!write.IsSuccessStatusCode) { Backoff("consent_pending"); return "consent_pending"; }
                _pendingEnable = false;
                CompanionSettings.PhasewardCommunityEnablePending = false;
            }
            using (var consent = await client.SendAsync(HttpMethod.Get, "phaseward/consent", token, cancellationToken: ct))
            {
                if (!consent.IsSuccessStatusCode) { Backoff("consent_pending"); return "consent_pending"; }
                using var doc = await ReadJsonAsync(consent, ct);
                var data = doc.RootElement;
                if (data.TryGetProperty("consent", out var nested) && nested.ValueKind == JsonValueKind.Object)
                    data = nested;
                _knownConsent = data.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
                if (!_knownConsent) { SetStatus("server_consent_required"); return "server_consent_required"; }
            }
            // Never access old unbound v0.5 files; only the active account's isolated queue.
            if (!CompanionSettings.PhasewardCommunityUploadConsentConfirmed ||
                !CompanionSettings.PhasewardCommunityQueueEnabled)
            { SetStatus("disabled"); return "disabled"; }
            string? path = PhasewardCommunityOutbox.GetFirstPendingPath(accountId);
            if (path is null) { _failureCount = 0; SetStatus("ready"); return "empty"; }
            byte[] payload = await File.ReadAllBytesAsync(path, ct);
            if (payload.Length == 0 || payload.Length > PhasewardCommunityOutbox.MaxBodyBytes)
            { PhasewardCommunityOutbox.BlockPending(path); SetStatus("invalid_local_payload"); return "invalid_local_payload"; }
            using var body = new ByteArrayContent(payload);
            body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.SendAsync(HttpMethod.Post, "phaseward/observations", token, body, ct);
            if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK)
            {
                // Only 202 Accepted or 200 same-payload duplicate is a positive acknowledgement.
                // We deliberately do not fetch server-side learned models or community data.
                PhasewardCommunityOutbox.Acknowledge(path, payload);
                _failureCount = 0;
                _nextAttempt = DateTimeOffset.MinValue;
                SetStatus("sent");
                return "sent";
            }
            if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge)
            { PhasewardCommunityOutbox.BlockPending(path); SetStatus("rejected_" + (int)response.StatusCode); return "rejected"; }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            { _knownConsent = false; Backoff("authorization_required"); return "authorization_required"; }
            if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            { Backoff("server_busy", response.Headers.RetryAfter?.Delta); return "server_busy"; }
            Backoff("server_" + (int)response.StatusCode);
            return "server_error";
        }
        catch (HttpRequestException) { Backoff("offline"); return "offline"; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { Backoff("timeout"); return "timeout"; }
        catch (JsonException) { Backoff("server_schema_unknown"); return "server_schema_unknown"; }
        catch (IOException) { Backoff("local_io_error"); return "local_io_error"; }
        finally { _single.Release(); }
    }

    private static ByteArrayContent JsonBody(string body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 12 }, ct);
    }
}
