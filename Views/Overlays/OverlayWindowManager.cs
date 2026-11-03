using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using AION_2_Companion.Core.Phaseward;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

/// <summary>
/// Az overlay ablakok egyetlen elo peldanyat kezeli. A felulet barmely
/// megnyitasi pontja ugyanazt az ablakot hasznalja, igy nem gyulnek a
/// taskbarban es a jatek folott a masolatok.
/// </summary>
public static class OverlayWindowManager
{
    private static DpsOverlayWindow? _dps;
    private static DbmOverlayWindow? _dbm;
    private static PlayerDetailsWindow? _playerDetails;
    private static TimelineWindow? _timeline;
    private static EventTimerOverlayWindow? _eventTimers;
    private static OverlayStudioWindow? _studio;
    private static DispatcherTimer? _phasewardMonitor;
    private static bool _phasewardOpenedAutomatically;
    private static uint _dismissedRuntimeId;
    private static readonly HashSet<string> HiddenForStudio = new(StringComparer.Ordinal);


    public static DpsOverlayWindow? DpsWindow => _dps;

    public static void ToggleDps(MeterSnapshot? snapshot = null)
    {
        if (!OverlayStudioStore.IsVisible(OverlayStudioStore.Dps)) return;
        DpsOverlayWindow window = EnsureDps(snapshot);
        Toggle(window);
    }

    public static void ShowDps(MeterSnapshot snapshot)
    {
        if (!OverlayStudioStore.IsVisible(OverlayStudioStore.Dps)) return;
        DpsOverlayWindow window = EnsureDps(snapshot);
        ShowWithoutActivation(window);
    }

    public static void UpdateDps(MeterSnapshot snapshot)
    {
        _dps?.UpdateSnapshot(snapshot);
    }

    public static void HideDps()
    {
        if (_dps is { IsVisible: true })
            _dps.Hide();
    }

    private static DbmOverlayWindow EnsureDbm()
    {
        if (_dbm is null)
        {
            var created = new DbmOverlayWindow();
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_dbm, created)) _dbm = null;
            };
            created.Opened += (_, _) => ApplySavedWidget(created, OverlayStudioStore.Phaseward);
            _dbm = created;
        }
        return _dbm;
    }

    public static void ToggleDbm()
    {
        if (!OverlayStudioStore.IsVisible(OverlayStudioStore.Phaseward)) return;
        var window = EnsureDbm();
        if (window.IsVisible)
        {
            _dismissedRuntimeId = PhasewardHub.Snapshot().BossRuntimeId;
            _phasewardOpenedAutomatically = false;
            window.Hide();
        }
        else
        {
            _dismissedRuntimeId = 0;
            _phasewardOpenedAutomatically = false;
            ShowWithoutActivation(window);
        }
    }

    public static void StartAutomaticPhaseward()
    {
        if (_phasewardMonitor is not null) return;
        _phasewardMonitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _phasewardMonitor.Tick += (_, _) => PollAutomaticPhaseward();
        _phasewardMonitor.Start();
    }

    private static void PollAutomaticPhaseward()
    {
        if (_studio is not null) return;
        bool enabled = CompanionSettings.AutoPhasewardOverlay &&
                       OverlayStudioStore.IsVisible(OverlayStudioStore.Phaseward);
        var snapshot = PhasewardHub.Snapshot();
        bool active = snapshot.State == "encounter" && snapshot.BossRuntimeId != 0;
        if (!active) _dismissedRuntimeId = 0;
        if (enabled && active && snapshot.BossRuntimeId != _dismissedRuntimeId)
        {
            var window = EnsureDbm();
            if (!window.IsVisible)
            {
                ShowWithoutActivation(window);
                _phasewardOpenedAutomatically = true;
            }
        }
        else if (_phasewardOpenedAutomatically && _dbm is { IsVisible: true })
        {
            _dbm.Hide();
            _phasewardOpenedAutomatically = false;
        }
    }

    public static void ToggleEventTimers()
    {
        if (!OverlayStudioStore.IsVisible(OverlayStudioStore.Timers)) return;
        if (_eventTimers is null)
        {
            var created = new EventTimerOverlayWindow();
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_eventTimers, created))
                    _eventTimers = null;
            };
            created.Opened += (_, _) => ApplySavedWidget(created, OverlayStudioStore.Timers);
            _eventTimers = created;
        }

        Toggle(_eventTimers);
    }

    public static void ShowPlayerDetails(PlayerStat player, MeterSnapshot snapshot)
    {
        if (_playerDetails is null)
        {
            var created = new PlayerDetailsWindow(player, snapshot);
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_playerDetails, created))
                    _playerDetails = null;
            };
            _playerDetails = created;
        }
        else
        {
            _playerDetails.UpdatePlayer(player, snapshot);
        }

        ShowAndActivate(_playerDetails);
    }

    public static void ShowTimeline(PlayerStat player, MeterSnapshot snapshot)
    {
        if (_timeline is null)
        {
            var created = new TimelineWindow(player, snapshot);
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_timeline, created))
                    _timeline = null;
            };
            _timeline = created;
        }
        else
        {
            _timeline.UpdatePlayer(player, snapshot);
        }

        ShowAndActivate(_timeline);
    }

    // Editor is a normal app window. It never opens Phaseward or starts capture.
    public static void OpenStudio()
    {
        if (_studio is null)
        {
            // Preview windows are temporary. Hide real overlays without closing their engines.
            HiddenForStudio.Clear();
            HideForStudio(_dps, OverlayStudioStore.Dps);
            HideForStudio(_dbm, OverlayStudioStore.Phaseward);
            HideForStudio(_eventTimers, OverlayStudioStore.Timers);
            var created = new OverlayStudioWindow();
            created.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_studio, created)) return;
                _studio = null;
                RestoreFromStudio(_dps, OverlayStudioStore.Dps);
                RestoreFromStudio(_dbm, OverlayStudioStore.Phaseward);
                RestoreFromStudio(_eventTimers, OverlayStudioStore.Timers);
                HiddenForStudio.Clear();
                PollAutomaticPhaseward();
            };
            _studio = created;
        }
        ShowAndActivate(_studio);
    }

    private static void HideForStudio(Window? window, string key)
    {
        if (window is not { IsVisible: true }) return;
        HiddenForStudio.Add(key);
        window.Hide();
    }
    private static void RestoreFromStudio(Window? window, string key)
    {
        if (window is null || !HiddenForStudio.Contains(key) || !OverlayStudioStore.IsVisible(key)) return;
        // Automatically activated Phaseward only returns for a currently recognized encounter.
        if (key == OverlayStudioStore.Phaseward && _phasewardOpenedAutomatically &&
            PhasewardHub.Snapshot().State != "encounter") return;
        var layout = OverlayStudioStore.Snapshot();
        ApplyWindow(window, key, layout);
        ShowWithoutActivation(window);
    }

    public static void ApplyStudioLayout(StudioLayout layout)
    {
        ApplyWindow(_dps, OverlayStudioStore.Dps, layout);
        ApplyWindow(_dbm, OverlayStudioStore.Phaseward, layout);
        ApplyWindow(_eventTimers, OverlayStudioStore.Timers, layout);
    }

    private static void ApplyWindow(Window? window, string key, StudioLayout layout)
    {
        if (window is null || !layout.Widgets.TryGetValue(key, out var w)) return;
        window.Opacity = w.Opacity;
        if (!w.Visible) { window.Hide(); return; }
        // Do not auto-show a previously hidden HUD widget, especially Phaseward.
        window.Width = w.Width;
        window.Height = w.Height;
        if (window.IsVisible)
            window.Position = new PixelPoint(w.X, w.Y);
    }

    private static void ApplySavedWidget(Window window, string key)
    {
        var saved = OverlayStudioStore.Snapshot();
        if (!saved.Widgets.TryGetValue(key, out var item)) return;
        window.Opacity = item.Opacity;
        window.Width = Math.Clamp(item.Width, window.MinWidth, window.MaxWidth);
        window.Height = Math.Clamp(item.Height, window.MinHeight, window.MaxHeight);
        var position = new PixelPoint(item.X, item.Y);
        if (window.Screens.ScreenFromPoint(position) is not null)
            window.Position = position;
    }

    private static DpsOverlayWindow EnsureDps(MeterSnapshot? snapshot)
    {
        if (_dps is null)
        {
            var created = snapshot is null
                ? new DpsOverlayWindow()
                : new DpsOverlayWindow(snapshot);

            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_dps, created))
                    _dps = null;
            };
            created.Opened += (_, _) => ApplySavedWidget(created, OverlayStudioStore.Dps);
            _dps = created;
        }
        else if (snapshot is not null)
        {
            _dps.UpdateSnapshot(snapshot);
        }

        return _dps;
    }

    private static void Toggle(Window window)
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        ShowAndActivate(window);
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible)
            window.Show();

        window.Activate();
    }

    private static void ShowWithoutActivation(Window window)
    {
        if (!window.IsVisible)
            window.Show();
    }
}
