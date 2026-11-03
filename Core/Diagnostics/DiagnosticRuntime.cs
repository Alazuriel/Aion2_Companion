// LEGACY issue #21 - low-overhead heartbeat and an independent watchdog process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Views;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AION_2_Companion.Core.Diagnostics;

public static class DiagnosticRuntime
{
    private static readonly object Gate = new();
    private static readonly Queue<DiagnosticBreadcrumb> Breadcrumbs = new();
    private static readonly string Session = Guid.NewGuid().ToString("N");
    private static Timer? _worker;
    private static DispatcherTimer? _uiTimer;
    private static DateTimeOffset? _uiBeat;
    private static bool _clean;
    private static bool _crash;
    private static bool _started;

    public static void Start()
    {
        if (!OperatingSystem.IsWindows()) return; // No local plaintext fallback off Windows.
        try
        {
            DiagnosticStore.Rotate();
            lock (Gate) { _started = true; _clean = false; _crash = false; _uiBeat = null; }
            Breadcrumb("app_start");
            _worker = new Timer(_ => PersistHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) RecordManagedCrash(ex, "main");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                // An observed task fault is real, but it does NOT prove that the application crashed.
                try { RecordManagedException(e.Exception.InnerException ?? e.Exception, "main"); }
                catch { }
            };

            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                // Same installed executable in watchdog mode: independent OS process, no separate runtime dependency.
                Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    ArgumentList = { "--diagnostics-watchdog", Environment.ProcessId.ToString(), Session }
                });
            }
        }
        catch { /* Diagnostics must NEVER break capture, login, or app startup. */ }
    }

    public static void StartUi(Window owner)
    {
        if (!_started) return;
        lock (Gate) _uiBeat = DateTimeOffset.UtcNow;
        Breadcrumb("ui_ready");
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _uiTimer.Tick += (_, _) => { lock (Gate) _uiBeat = DateTimeOffset.UtcNow; };
        _uiTimer.Start();
        Dispatcher.UIThread.UnhandledException += (_, e) => RecordManagedCrash(e.Exception, "ui");
        Dispatcher.UIThread.Post(() =>
        {
            try { DiagnosticConsentWindow.ShowPending(owner); } catch { /* UI is optional */ }
        }, DispatcherPriority.Background);
    }

    public static void Breadcrumb(string eventCode)
    {
        // Closed vocabulary: passing a player name, path, skill or arbitrary string stores nothing.
        if (!DiagnosticSchema.AllowedEvents.Contains(eventCode)) return;
        lock (Gate)
        {
            Breadcrumbs.Enqueue(new DiagnosticBreadcrumb { Code = eventCode, Utc = DateTimeOffset.UtcNow });
            while (Breadcrumbs.Count > 32) Breadcrumbs.Dequeue();
        }
    }

    private static DiagnosticHeartbeat Snapshot(DateTimeOffset workerTime)
    {
        lock (Gate)
        {
            return new DiagnosticHeartbeat
            {
                SessionId = Session, LastUiUtc = _uiBeat,
                LastWorkerUtc = workerTime, CleanExit = _clean, ManagedFailureRecorded = _crash,
                Breadcrumbs = Breadcrumbs.ToList()
            };
        }
    }

    private static void PersistHeartbeat()
    {
        if (!_started) return;
        try
        {
            var data = Snapshot(DateTimeOffset.UtcNow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, DiagnosticSchema.Json);
            if (bytes.Length > 8192) return;
            Directory.CreateDirectory(DiagnosticStore.Folder);
            var dest = DiagnosticStore.HeartbeatFile(Session);
            var temp = dest + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, dest, true);
        }
        catch { /* No uncontrolled diagnostic exceptions or raw logging. */ }
    }

    public static void RecordManagedCrash(Exception exception, string subsystem)
    {
        try
        {
            lock (Gate) { if (_crash) return; _crash = true; }
            PersistHeartbeat();
            var snapshot = Snapshot(DateTimeOffset.UtcNow);
            DiagnosticStore.Save(DiagnosticSchema.Build("managed_crash", "confirmed", subsystem, snapshot,
                DiagnosticSchema.Categorize(exception)));
        }
        catch { /* Fail closed, don't log exception contents. */ }
    }

    public static void RecordManagedException(Exception exception, string subsystem)
    {
        try
        {
            Breadcrumb(subsystem == "phaseward" ? "phaseward_error" : "capture_error");
            var snapshot = Snapshot(DateTimeOffset.UtcNow);
            DiagnosticStore.Save(DiagnosticSchema.Build("managed_error", "confirmed", subsystem, snapshot,
                DiagnosticSchema.Categorize(exception)));
        }
        catch { /* Best effort, do not change crash state or expose exception message. */ }
    }

    public static void Stop(bool clean)
    {
        if (!_started) return;
        try
        {
            lock (Gate) _clean = clean;
            if (clean) Breadcrumb("normal_exit");
            _uiTimer?.Stop();
            _worker?.Change(Timeout.Infinite, Timeout.Infinite);
            PersistHeartbeat();
            _worker?.Dispose();
        }
        catch { }
        _started = false;
    }

    public static DiagnosticReport MakeManualReport()
    {
        Breadcrumb("manual_report");
        return DiagnosticSchema.Build("manual_report", "manual", "unknown", Snapshot(DateTimeOffset.UtcNow));
    }

    public static void RunWatchdog(string[] args)
    {
        // Called before Avalonia is initialized. Strict args: PID + non-persistent ephemeral session ID.
        if (!OperatingSystem.IsWindows() || args.Length != 3 ||
            !int.TryParse(args[1], out int pid) || pid <= 0 ||
            !System.Text.RegularExpressions.Regex.IsMatch(args[2], "^[a-f0-9]{32}$")) return;
        string heartbeatFile = DiagnosticStore.HeartbeatFile(args[2]);
        string? hangReport = null;
        int staleSamples = 0;
        DateTimeOffset lastSeenUi = DateTimeOffset.MinValue;
        try
        {
            using var parent = Process.GetProcessById(pid);
            while (true) // separate process stays alive until the Companion exits
            {
                Thread.Sleep(3000);
                DiagnosticHeartbeat? state = ReadHeartbeat(heartbeatFile, args[2]);
                bool exited;
                try { exited = parent.HasExited; } catch { exited = true; }
                if (exited)
                {
                    if (state is { CleanExit: false, ManagedFailureRecorded: false })
                    {
                        // Not a confirmed crash: Task Manager, OS shutdown, reboot, updater etc are ambiguous.
                        DiagnosticStore.Save(DiagnosticSchema.Build("unexpected_termination", "uncertain", "unknown", state));
                    }
                    break;
                }
                if (state == null || state.LastUiUtc == null || state.CleanExit) continue;
                var now = DateTimeOffset.UtcNow;
                bool workerHealthy = (now - state.LastWorkerUtc) < TimeSpan.FromSeconds(10);
                bool uiLate = (now - state.LastUiUtc.Value) > TimeSpan.FromSeconds(18);
                // Avoid creating a hang report if ALL heartbeats stopped; classify as uncertain instead.
                staleSamples = workerHealthy && uiLate ? staleSamples + 1 : 0;
                if (staleSamples >= 2 && hangReport == null)
                {
                    var report = DiagnosticSchema.Build("suspected_ui_hang", "suspected", "ui", state, now: now);
                    DiagnosticStore.Save(report);
                    hangReport = report.ReportId;
                    lastSeenUi = state.LastUiUtc.Value;
                }
                if (hangReport != null && state.LastUiUtc.Value > lastSeenUi)
                {
                    DiagnosticStore.MarkRecovered(hangReport);
                    hangReport = null;
                    staleSamples = 0;
                    lastSeenUi = state.LastUiUtc.Value;
                }
            }
        }
        catch { /* Independent supervisor must not affect the main application. */ }
        finally
        {
            try { File.Delete(heartbeatFile); } catch { }
        }
    }

    private static DiagnosticHeartbeat? ReadHeartbeat(string path, string expectedSession)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 8192) return null;
            var h = JsonSerializer.Deserialize<DiagnosticHeartbeat>(File.ReadAllBytes(path), DiagnosticSchema.Json);
            if (h == null || h.SessionId != expectedSession || h.Breadcrumbs.Count > 32 ||
                h.Breadcrumbs.Any(x => !DiagnosticSchema.AllowedEvents.Contains(x.Code))) return null;
            return h;
        }
        catch { return null; }
    }
}
