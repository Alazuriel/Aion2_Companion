// SYNTRA - issue #21: privacy-safe diagnostics, no game or user data.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AION_2_Companion.Core.Diagnostics;

public sealed class DiagnosticBreadcrumb
{
    [JsonPropertyName("event_code")] public string Code { get; set; } = "";
    [JsonPropertyName("utc")] public DateTimeOffset Utc { get; set; }
}
public sealed class DiagnosticHeartbeat
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("last_ui_utc")] public DateTimeOffset? LastUiUtc { get; set; }
    [JsonPropertyName("last_worker_utc")] public DateTimeOffset LastWorkerUtc { get; set; }
    [JsonPropertyName("clean_exit")] public bool CleanExit { get; set; }
    [JsonPropertyName("managed_failure_recorded")] public bool ManagedFailureRecorded { get; set; }
    [JsonPropertyName("breadcrumbs")] public List<DiagnosticBreadcrumb> Breadcrumbs { get; set; } = new();
}
public sealed class DiagnosticReport
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("report_id")] public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("created_utc")] public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("app_version")] public string AppVersion { get; set; } = DiagnosticSchema.AppVersion;
    [JsonPropertyName("os_family")] public string OsFamily { get; set; } = "Windows";
    [JsonPropertyName("os_major")] public int OsMajor { get; set; } = Environment.OSVersion.Version.Major;
    [JsonPropertyName("incident_kind")] public string IncidentKind { get; set; } = "unknown";
    [JsonPropertyName("confidence")] public string Confidence { get; set; } = "uncertain";
    [JsonPropertyName("subsystem")] public string Subsystem { get; set; } = "unknown";
    [JsonPropertyName("exception_category")] public string? ExceptionCategory { get; set; }
    [JsonPropertyName("ui_heartbeat_age_seconds")] public int? UiHeartbeatAgeSeconds { get; set; }
    [JsonPropertyName("worker_heartbeat_age_seconds")] public int? WorkerHeartbeatAgeSeconds { get; set; }
    [JsonPropertyName("recovered")] public bool? Recovered { get; set; }
    [JsonPropertyName("breadcrumbs")] public List<DiagnosticBreadcrumb> Breadcrumbs { get; set; } = new();
    [JsonPropertyName("completeness")] public string Completeness { get; set; } = "partial";
}
public static class DiagnosticSchema
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static readonly string AppVersion = GetVersion();
    public static readonly HashSet<string> AllowedIncidents = new(StringComparer.Ordinal)
        { "managed_crash", "managed_error", "suspected_ui_hang", "unexpected_termination", "manual_report" };
    public static readonly HashSet<string> AllowedConfidence = new(StringComparer.Ordinal)
        { "confirmed", "suspected", "uncertain", "manual" };
    public static readonly HashSet<string> AllowedSubsystem = new(StringComparer.Ordinal)
        { "main", "ui", "capture", "overlay", "phaseward", "forgedata", "nsl", "account", "characters", "unknown" };
    public static readonly HashSet<string> AllowedEvents = new(StringComparer.Ordinal)
        { "app_start", "ui_ready", "capture_start", "capture_stop", "capture_error", "phaseward_load", "phaseward_load_started", "phaseward_error", "phaseward_boss_seen", "phaseward_event", "phaseward_end", "overlay_open", "overlay_close", "forgedata_error", "nsl_error", "account_error", "characters_error", "manual_report", "ui_resumed", "normal_exit" };
    public static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
        { "NullReferenceException", "InvalidOperationException", "ArgumentException", "IndexOutOfRangeException", "IOException", "UnauthorizedAccessException", "OutOfMemoryException", "TaskCanceledException", "OperationCanceledException", "AccessViolationException", "UnknownManagedException" };
    private static string GetVersion()
    {
        string? s = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var m = Regex.Match(s ?? "", @"^\d+(?:\.\d+){1,3}");
        return m.Success ? m.Value : "unknown";
    }
    public static string Categorize(Exception exception)
    {
        var name = exception.GetType().Name;
        return AllowedCategories.Contains(name) ? name : "UnknownManagedException";
    }
    public static DiagnosticReport Build(string incident, string confidence, string subsystem, DiagnosticHeartbeat? heartbeat,
        string? exceptionCategory = null, DateTimeOffset? now = null)
    {
        var when = now ?? DateTimeOffset.UtcNow;
        var report = new DiagnosticReport
        {
            CreatedUtc = when,
            IncidentKind = AllowedIncidents.Contains(incident) ? incident : "unexpected_termination",
            Confidence = AllowedConfidence.Contains(confidence) ? confidence : "uncertain",
            Subsystem = AllowedSubsystem.Contains(subsystem) ? subsystem : "unknown",
            ExceptionCategory = exceptionCategory != null && AllowedCategories.Contains(exceptionCategory) ? exceptionCategory : null,
            UiHeartbeatAgeSeconds = Age(heartbeat?.LastUiUtc, when),
            WorkerHeartbeatAgeSeconds = Age(heartbeat?.LastWorkerUtc, when),
            Breadcrumbs = (heartbeat?.Breadcrumbs ?? new()).Where(x => AllowedEvents.Contains(x.Code) && x.Utc <= when && x.Utc > when.AddMinutes(-3)).TakeLast(32).ToList(),
            Completeness = heartbeat is null ? "minimal" : "partial"
        };
        return report;
    }
    private static int? Age(DateTimeOffset? time, DateTimeOffset now) => time.HasValue
        ? (int)Math.Clamp((now - time.Value).TotalSeconds, 0, 86400) : null;
    // Explicit second-stage whitelist, applied before EVERY disk write and upload.
    public static bool IsSafe(DiagnosticReport r)
    {
        if (r.SchemaVersion != 1 || !Regex.IsMatch(r.ReportId, "^[a-f0-9]{32}$") ||
            !Regex.IsMatch(r.AppVersion, @"^(unknown|\d+(?:\.\d+){1,3})$") || r.OsFamily != "Windows" ||
            r.OsMajor < 0 || r.OsMajor > 100 || !AllowedIncidents.Contains(r.IncidentKind) ||
            !AllowedConfidence.Contains(r.Confidence) || !AllowedSubsystem.Contains(r.Subsystem) ||
            (r.ExceptionCategory != null && !AllowedCategories.Contains(r.ExceptionCategory)) ||
            (r.UiHeartbeatAgeSeconds is < 0 or > 86400) || (r.WorkerHeartbeatAgeSeconds is < 0 or > 86400) ||
            r.Completeness is not ("minimal" or "partial") || r.Breadcrumbs.Count > 32 ||
            r.Breadcrumbs.Any(x => !AllowedEvents.Contains(x.Code) || x.Utc > r.CreatedUtc.AddMinutes(5) || x.Utc < r.CreatedUtc.AddDays(-1)))
            return false;
        return true;
    }
    public static string SerializeForReviewAndUpload(DiagnosticReport report)
    {
        if (!IsSafe(report)) throw new InvalidDataException("Unsafe diagnostic schema");
        var json = JsonSerializer.Serialize(report, Json);
        if (Encoding.UTF8.GetByteCount(json) > 32 * 1024) throw new InvalidDataException("Diagnostic exceeds size limit");
        return json;
    }
}

// DPAPI via Windows crypt32; no sensitive plaintext fallback. The app does NOT write raw logs.
internal static class DiagnosticDpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI required");
        Blob source = new() { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob result = default;
        try
        {
            Marshal.Copy(bytes, 0, source.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref source, "Nexforge sanitized diagnostics", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (!ok || result.Data == IntPtr.Zero || result.Length <= 0 || result.Length > 131072)
                throw new System.Security.Cryptography.CryptographicException("DPAPI operation failed");
            var output = new byte[result.Length];
            Marshal.Copy(result.Data, output, 0, output.Length);
            return output;
        }
        finally { if (result.Data != IntPtr.Zero) LocalFree(result.Data); Marshal.FreeHGlobal(source.Data); }
    }
}

public static class DiagnosticStore
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nexforge", "Companion", "Diagnostics");
    public static string HeartbeatFile(string session) => Path.Combine(Folder, "heartbeat-" + session + ".json");
    public static void Save(DiagnosticReport report)
    {
        var json = DiagnosticSchema.SerializeForReviewAndUpload(report);
        var encrypted = DiagnosticDpapi.Transform(Encoding.UTF8.GetBytes(json), true);
        Directory.CreateDirectory(Folder);
        var path = ReportFile(report.ReportId);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, encrypted); File.Move(temp, path, true);
        Rotate();
    }
    public static DiagnosticReport? Read(string id)
    {
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) return null;
        try
        {
            var path = ReportFile(id);
            if (!File.Exists(path) || new FileInfo(path).Length > 131072) return null;
            var plaintext = DiagnosticDpapi.Transform(File.ReadAllBytes(path), false);
            var parsed = JsonSerializer.Deserialize<DiagnosticReport>(plaintext, DiagnosticSchema.Json);
            return parsed != null && parsed.ReportId == id && DiagnosticSchema.IsSafe(parsed) ? parsed : null;
        }
        catch { return null; }
    }
    public static IReadOnlyList<DiagnosticReport> Pending()
    {
        if (!Directory.Exists(Folder)) return Array.Empty<DiagnosticReport>();
        return Directory.EnumerateFiles(Folder, "*.ndg").Select(p => Read(Path.GetFileNameWithoutExtension(p)))
            .Where(r => r != null).Cast<DiagnosticReport>().OrderBy(r => r.CreatedUtc).Take(16).ToList();
    }
    // Exact, encrypted outgoing payload stays byte-for-byte identical across ambiguous timeouts.
    // A recovered watchdog event can update the local report, but NEVER rewrite the pinned POST body.
    public static string PreviewUpload(string id)
    {
        var report = Read(id) ?? throw new InvalidDataException("Local report unavailable");
        return ReadPinnedBody(id) ?? PinUpload(id);
    }
    public static string PinUpload(string id)
    {
        var report = Read(id) ?? throw new InvalidDataException("Local report unavailable");
        var pinned = ReadPinnedBody(id);
        if (pinned != null) return pinned;
        var body = DiagnosticApiV42.Serialize(report);
        var sealedBytes = DiagnosticDpapi.Transform(Encoding.UTF8.GetBytes(body), true);
        var filename = PinnedFile(id);
        var temporary = filename + ".tmp";
        Directory.CreateDirectory(Folder);
        try
        {
            File.WriteAllBytes(temporary, sealedBytes);
            File.Move(temporary, filename); // No overwrite, a concurrent upload must not change its content.
        }
        catch (IOException) when (File.Exists(filename)) { /* Keep existing identity and request. */ }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        return ReadPinnedBody(id) ?? throw new InvalidDataException("Could not pin upload");
    }
    private static string? ReadPinnedBody(string id)
    {
        string filename = PinnedFile(id);
        if (!File.Exists(filename)) return null;
        if (new FileInfo(filename).Length > 131072) throw new InvalidDataException("Pinned upload too large");
        string body = Encoding.UTF8.GetString(DiagnosticDpapi.Transform(File.ReadAllBytes(filename), false));
        if (!DiagnosticApiV42.IsValidPinnedBody(body, id))
            throw new InvalidDataException("Unsafe pinned upload");
        return body;
    }
    public static bool HasAuthorizedPending(string id)
    {
        if (!ValidId(id) || !File.Exists(AuthorizationFile(id))) return false;
        try
        {
            var contents = DiagnosticDpapi.Transform(File.ReadAllBytes(AuthorizationFile(id)), false);
            return Encoding.ASCII.GetString(contents) == "authorized-pending-v1";
        }
        catch { return false; }
    }
    public static void MarkAuthorizedPending(string id)
    {
        if (Read(id) is null || ReadPinnedBody(id) is null) throw new InvalidDataException("Cannot authorize missing payload");
        File.WriteAllBytes(AuthorizationFile(id), DiagnosticDpapi.Transform(Encoding.ASCII.GetBytes("authorized-pending-v1"), true));
    }
    // Revokes the retry authorization. Pinned bytes deliberately remain for future dedupe if user re-consents.
    public static void CancelAuthorizedPending(string id)
    {
        if (ValidId(id) && File.Exists(AuthorizationFile(id))) File.Delete(AuthorizationFile(id));
    }
    // Persist HTTP 429 Retry-After encrypted so restart cannot circumvent the service rate limit.
    public static void SetRetryAfter(string id, int? delaySeconds)
    {
        if (!ValidId(id) || Read(id) is null) return;
        if (!delaySeconds.HasValue || delaySeconds <= 0) return;
        var until = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(delaySeconds.Value, 1, 86400));
        var plaintext = Encoding.ASCII.GetBytes(until.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllBytes(RateLimitFile(id), DiagnosticDpapi.Transform(plaintext, true));
    }
    public static int GetRetryAfterSeconds(string id)
    {
        if (!ValidId(id) || !File.Exists(RateLimitFile(id))) return 0;
        try
        {
            var text = Encoding.ASCII.GetString(DiagnosticDpapi.Transform(File.ReadAllBytes(RateLimitFile(id)), false));
            if (!DateTimeOffset.TryParseExact(text, "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var until)) return 0;
            var seconds = (int)Math.Clamp(Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds), 0, 86400);
            if (seconds == 0) File.Delete(RateLimitFile(id));
            return seconds;
        }
        catch { return 0; }
    }
    public static void CompleteSent(string id, string receipt)
    {
        if (!ValidId(id) || !Guid.TryParseExact(receipt, "D", out _))
            throw new InvalidDataException("Invalid API receipt");
        // Persist minimal encrypted receipt BEFORE deleting the unsent report; never plaintext.
        var body = JsonSerializer.Serialize(new { report_id = id, report_ref = receipt, received_utc = DateTimeOffset.UtcNow });
        var crypt = DiagnosticDpapi.Transform(Encoding.UTF8.GetBytes(body), true);
        File.WriteAllBytes(ReceiptFile(id) + ".tmp", crypt);
        File.Move(ReceiptFile(id) + ".tmp", ReceiptFile(id), true);
        Delete(id);
    }
    public static void Delete(string id)
    {
        if (!ValidId(id)) return;
        foreach (var file in new[] { ReportFile(id), PinnedFile(id), AuthorizationFile(id), RateLimitFile(id) })
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }
    private static bool ValidId(string id) => Regex.IsMatch(id, "^[a-f0-9]{32}$");
    private static string PinnedFile(string id) => Path.Combine(Folder, id + ".ndq");
    private static string AuthorizationFile(string id) => Path.Combine(Folder, id + ".nda");
    private static string ReceiptFile(string id) => Path.Combine(Folder, id + ".ndr");
    private static string RateLimitFile(string id) => Path.Combine(Folder, id + ".ndb");
    public static void MarkRecovered(string id)
    {
        var report = Read(id);
        if (report == null || report.IncidentKind != "suspected_ui_hang") return;
        report.Recovered = true;
        Save(report);
    }
    public static void Rotate()
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var f in Directory.EnumerateFiles(Folder, "*.ndg").Select(p => new FileInfo(p)).OrderByDescending(f => f.CreationTimeUtc).Skip(16))
            { try { f.Delete(); } catch { } }
        foreach (var f in Directory.EnumerateFiles(Folder, "*.ndg"))
            { try { if (File.GetCreationTimeUtc(f) < DateTime.UtcNow.AddDays(-30)) File.Delete(f); } catch { } }
        RotateAuxiliary();
    }
        // Clean up orphaned upload snapshots and expiry of encrypted server receipts.
    private static void RotateAuxiliary()
    {
        foreach (var extension in new[] { "*.ndq", "*.nda", "*.ndb" })
        {
            foreach (var file in Directory.EnumerateFiles(Folder, extension))
            {
                try
                {
                    var report = ReportFile(Path.GetFileNameWithoutExtension(file));
                    if (!File.Exists(report)) File.Delete(file);
                }
                catch { }
            }
        }
        foreach (var file in Directory.EnumerateFiles(Folder, "*.ndr"))
            { try { if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddDays(-30)) File.Delete(file); } catch { } }
    }
    private static string ReportFile(string id) => Path.Combine(Folder, id + ".ndg");
}
