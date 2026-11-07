using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Integrity;

public enum IntegrityMonitorState
{
    Disabled,
    NotAvailable,
    Waiting,
    Checking,
    Healthy,
    ProblemDetected,
    PreparingRepair,
    RepairReady,
    RestartRequired,
    RepairFailed,
}

public sealed record IntegrityMonitorSnapshot(
    IntegrityMonitorState State,
    int IssueCount = 0,
    bool ApplicationFilesVerified = false,
    bool LanguageAndForgeDataVerified = false,
    DateTimeOffset? CheckedAtUtc = null,
    string? Detail = null);

public sealed class IntegrityMonitorService : IDisposable
{
    private sealed class RepairState
    {
        public string Version { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public string? PackagePath { get; set; }
    }

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan HashThrottle = TimeSpan.FromMilliseconds(1);
    private const int MaxAutomaticRepairAttempts = 2;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _backgroundTask;
    private Func<bool>? _isHighActivity;
    private UpdateInfo? _pendingRepair;
    private string? _pendingPackagePath;

    public static IntegrityMonitorService Shared { get; } = new();
    public IntegrityMonitorSnapshot Snapshot { get; private set; } =
        new(IntegrityMonitorState.Waiting);
    public event EventHandler? StatusChanged;

    private static string RepairStatePath =>
        Path.Combine(CompanionSettings.SettingsDirectory, "integrity-repair.json");

    public void Start(Func<bool>? isHighActivity = null)
    {
        if (_backgroundTask is not null)
            return;
        _isHighActivity = isHighActivity;
        if (!CompanionSettings.IntegrityMonitoringEnabled)
            UpdateSnapshot(new(IntegrityMonitorState.Disabled));
        _backgroundTask = RunLoopAsync(_shutdown.Token);
    }

    public void SetEnabled(bool enabled)
    {
        CompanionSettings.IntegrityMonitoringEnabled = enabled;
        if (!enabled)
        {
            _pendingRepair = null;
            _pendingPackagePath = null;
            DeleteRepairState();
            UpdateSnapshot(new(IntegrityMonitorState.Disabled));
            return;
        }

        _ = CheckAndPrepareRepairAsync(_shutdown.Token);
    }

    public Task CheckNowAsync(CancellationToken cancellationToken = default) =>
        CheckAndPrepareRepairAsync(cancellationToken);

    public bool TryLaunchPendingRepair()
    {
        if (_pendingRepair is null || string.IsNullOrWhiteSpace(_pendingPackagePath) ||
            !File.Exists(_pendingPackagePath))
            return false;

        RepairState state = LoadRepairState();
        state.Version = _pendingRepair.Version;
        state.Attempts++;
        state.PackagePath = _pendingPackagePath;
        SaveRepairState(state);
        UpdateSnapshot(Snapshot with { State = IntegrityMonitorState.RestartRequired });
        UpdateService.LaunchUpdatePackage(_pendingRepair, _pendingPackagePath);
        return true;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(InitialDelay, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (CompanionSettings.IntegrityMonitoringEnabled)
                    await CheckAndPrepareRepairAsync(cancellationToken);
                await Task.Delay(PeriodicInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CheckAndPrepareRepairAsync(CancellationToken cancellationToken)
    {
        if (!await _checkGate.WaitAsync(0, cancellationToken))
            return;
        try
        {
            UpdateSnapshot(new(IntegrityMonitorState.Checking));
            string version = ChangelogService.GetCurrentVersion();
            IntegrityVerificationResult result = await IntegrityVerifier.VerifyAsync(
                AppContext.BaseDirectory, version, IntegrityTrust.PublicKey, HashThrottle,
                cancellationToken, _isHighActivity);

            if (!result.ManifestAvailable)
            {
                UpdateSnapshot(new(IntegrityMonitorState.NotAvailable));
                return;
            }

            if (result.IsHealthy)
            {
                DeleteRepairState();
                _pendingRepair = null;
                _pendingPackagePath = null;
                UpdateSnapshot(new(
                    IntegrityMonitorState.Healthy, 0,
                    result.ApplicationFiles > 0,
                    result.LanguageFiles > 0 && result.ForgeDataFiles > 0,
                    DateTimeOffset.UtcNow));
                return;
            }

            UpdateSnapshot(new(
                IntegrityMonitorState.ProblemDetected,
                result.Issues.Count,
                false,
                false,
                DateTimeOffset.UtcNow,
                SummarizeIssues(result)));

            RepairState repairState = LoadRepairState();
            if (repairState.Attempts >= MaxAutomaticRepairAttempts &&
                string.Equals(repairState.Version, version, StringComparison.OrdinalIgnoreCase))
            {
                UpdateSnapshot(Snapshot with { State = IntegrityMonitorState.RepairFailed });
                return;
            }

            UpdateSnapshot(Snapshot with { State = IntegrityMonitorState.PreparingRepair });
            UpdateInfo? repair = await UpdateService.GetRepairInstallerAsync(cancellationToken);
            if (repair is null)
            {
                UpdateSnapshot(Snapshot with { State = IntegrityMonitorState.RepairFailed });
                return;
            }

            string packagePath = await UpdateService.DownloadAsync(repair, cancellationToken: cancellationToken);
            _pendingRepair = repair;
            _pendingPackagePath = packagePath;
            repairState.Version = repair.Version;
            repairState.PackagePath = packagePath;
            SaveRepairState(repairState);
            UpdateSnapshot(Snapshot with { State = IntegrityMonitorState.RepairReady });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Net.Http.HttpRequestException)
        {
            DebugConsole.Log("integrity", $"Automatic repair could not be prepared: {ex.Message}");
            UpdateSnapshot(Snapshot with { State = IntegrityMonitorState.RepairFailed, Detail = ex.Message });
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private static string SummarizeIssues(IntegrityVerificationResult result)
    {
        int missing = result.Issues.Count(issue => issue.Kind == IntegrityIssueKind.Missing);
        int corrupt = result.Issues.Count(issue => issue.Kind == IntegrityIssueKind.Corrupt);
        int version = result.Issues.Count(issue => issue.Kind == IntegrityIssueKind.VersionMismatch);
        int invalid = result.Issues.Count(issue => issue.Kind == IntegrityIssueKind.InvalidManifest);
        return $"missing={missing}; corrupt={corrupt}; version={version}; manifest={invalid}";
    }

    private void UpdateSnapshot(IntegrityMonitorSnapshot snapshot)
    {
        Snapshot = snapshot;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private static RepairState LoadRepairState()
    {
        try
        {
            if (File.Exists(RepairStatePath))
                return JsonSerializer.Deserialize<RepairState>(File.ReadAllText(RepairStatePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }

    private static void SaveRepairState(RepairState state)
    {
        Directory.CreateDirectory(CompanionSettings.SettingsDirectory);
        string temporary = RepairStatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, RepairStatePath, true);
    }

    private static void DeleteRepairState()
    {
        try { File.Delete(RepairStatePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
    }
}
