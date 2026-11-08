using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// Future Phaseward Community Learning transport. All outgoing messages are persisted once,
/// with a stable UUID and exact JSON bytes. Production sending remains disabled until the
/// matching server receiver has been deployed, staging-tested, and explicitly released.
/// </summary>
public static class PhasewardCommunityOutbox
{
    // Security and release gate. This is not a user setting; enable it only after backend validation.
    public static bool BackendApprovedAndVerified => PhasewardCommunitySync.Shared.IsBackendReady;
    public const int MaxQueued = 100;
    public const int MaxBodyBytes = 128 * 1024;
    private static readonly object Gate = new();
    private static readonly SemaphoreSlim UploadGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The original v0.5 unbound queue stays offline forever. No silent migration/backfill.
    public static string LegacyQueueDirectory => Path.Combine(
        CompanionSettings.SettingsDirectory, "phaseward-community-pending");

    public static string? CurrentAccountId =>
        AccountService.Shared.Snapshot.State == AccountConnectionState.Connected
            ? AccountService.Shared.Snapshot.Profile?.AccountId : null;

    public static string AccountHash(string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId))).ToLowerInvariant();

    public static string DirectoryForAccount(string accountId) => Path.Combine(
        CompanionSettings.SettingsDirectory, "phaseward-community-accounts", AccountHash(accountId));

    public static string QueueDirectory => CurrentAccountId is string accountId
        ? DirectoryForAccount(accountId) : LegacyQueueDirectory;

    public static int LegacyPendingCount() => Directory.Exists(LegacyQueueDirectory)
        ? Directory.GetFiles(LegacyQueueDirectory, "*.json").Length : 0;

    public static string? GetFirstPendingPath(string accountId)
    {
        string dir = DirectoryForAccount(accountId);
        lock (Gate)
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json").OrderBy(x => x).FirstOrDefault() : null;
    }

    // Never delete another session's data. Guard against the file changing mid-flight.
    public static void Acknowledge(string path, byte[] sentBytes)
    {
        lock (Gate)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(sentBytes))
                File.Delete(path);
        }
    }

    public static void BlockPending(string path)
    {
        lock (Gate)
        {
            if (File.Exists(path))
                File.Move(path, path + ".blocked", true);
        }
    }

    public sealed record Submission(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("submission_id")] string SubmissionId,
        [property: JsonPropertyName("client_version")] string ClientVersion,
        [property: JsonPropertyName("consent")] ConsentInfo Consent,
        [property: JsonPropertyName("boss")] BossInfo Boss,
        [property: JsonPropertyName("fight")] FightInfo Fight,
        [property: JsonPropertyName("events")] IReadOnlyList<EventInfo> Events);
    public sealed record ConsentInfo(
        [property: JsonPropertyName("community_learning")] bool CommunityLearning,
        [property: JsonPropertyName("schema_version")] string SchemaVersion);
    public sealed record BossInfo(
        [property: JsonPropertyName("npc_id")] int NpcId,
        [property: JsonPropertyName("variant")] string? Variant);
    public sealed record FightInfo(
        [property: JsonPropertyName("started_at")] string StartedAt,
        [property: JsonPropertyName("duration_ms")] long DurationMs,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("capture_complete")] bool CaptureComplete,
        [property: JsonPropertyName("sample_truncated")] bool SampleTruncated);
    public sealed record EventInfo(
        [property: JsonPropertyName("t_ms")] long TimeMs,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("skill_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] uint? SkillId,
        [property: JsonPropertyName("hp_pct"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? HpPercent);

    public static Submission? BuildSubmission(
        PhasewardLearningRecorder.Pull? pull, DateTimeOffset finishedAt,
        string outcome = "unknown", bool sampleTruncated = false,
        string? fixedSubmissionId = null)
    {
        if (pull is null || pull.NpcTemplateId <= 0 ||
            pull.Samples is not { Count: >= 2 and <= 600 }) return null;
        // The true capture-completeness bit is not available to this recorder in v0.4.
        // We never claim a complete fight until the combat finalizer supplies evidence.
        long duration = (long)Math.Round((finishedAt - pull.StartedAtUtc).TotalMilliseconds);
        if (duration < 5000 || duration > 7200_000) return null;
        var items = new List<EventInfo>();
        foreach (var sample in pull.Samples.OrderBy(s => s.Second))
        {
            if (!double.IsFinite(sample.Second) || sample.Second < 0 ||
                sample.Second * 1000 > duration) continue;
            long t = (long)Math.Round(sample.Second * 1000);
            if (sample.Kind == "hp" && sample.HpPercent is double pct &&
                double.IsFinite(pct) && pct is >= 0 and <= 100)
                items.Add(new EventInfo(t, "hp", null, pct));
            else if (sample.Kind is "cast" or "hit" && sample.SkillId > 0)
                items.Add(new EventInfo(t, sample.Kind, sample.SkillId, null));
        }
        if (items.Count < 2) return null;
        string id = fixedSubmissionId ?? Guid.NewGuid().ToString("D");
        if (!Guid.TryParse(id, out _)) return null;
        string result = outcome is "killed" or "wipe" ? outcome : "unknown";
        return new Submission(1, id, typeof(PhasewardEngine).Assembly.GetName().Version?.ToString() ?? "0.2.0",
            new ConsentInfo(true, "community-v1"),
            new BossInfo(pull.NpcTemplateId, null),
            new FightInfo(pull.StartedAtUtc.ToUniversalTime().ToString("O"),
                duration, result, false, sampleTruncated), items);
    }

    public static bool TryQueue(PhasewardLearningRecorder.Pull pull, DateTimeOffset finishedAt,
        string outcome, bool sampleTruncated)
    {
        string? accountId = CurrentAccountId;
        if (!CompanionSettings.PhasewardCommunityQueueEnabled ||
            string.IsNullOrWhiteSpace(accountId)) return false;
        Submission? submission = BuildSubmission(pull, finishedAt, outcome, sampleTruncated);
        if (submission is null) return false;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(submission, JsonOptions);
        if (payload.Length > MaxBodyBytes) return false;
        lock (Gate)
        {
            try
            {
                string accountDir = DirectoryForAccount(accountId);
                Directory.CreateDirectory(accountDir);
                if (Directory.GetFiles(accountDir, "*.json").Length >= MaxQueued)
                    return false; // never erase unsent observations or block capture
                string path = Path.Combine(accountDir, submission.SubmissionId + ".json");
                string temp = path + ".tmp";
                if (File.Exists(path)) return false;
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None))
                    {
                        stream.Write(payload);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    public static int PendingCount()
    {
        lock (Gate)
        {
            try { return Directory.Exists(QueueDirectory) ?
                Directory.GetFiles(QueueDirectory, "*.json").Length : 0; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }
    }

    // Removes only our own queued JSON files. Does not touch local Phaseward models or NSL.
    public static void DeletePending()
    {
        lock (Gate)
        {
            if (!Directory.Exists(QueueDirectory)) return;
            foreach (string path in Directory.GetFiles(QueueDirectory, "*.json"))
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Send at most one current-account observation (never receives learned boss data).</summary>
    public static Task<string> TrySendOneAsync(CancellationToken ct = default) =>
        PhasewardCommunitySync.Shared.RunOnceAsync(ct);
}
