using Avalonia.Controls;

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

    public static DpsOverlayWindow? DpsWindow => _dps;

    public static void ToggleDps(MeterSnapshot? snapshot = null)
    {
        DpsOverlayWindow window = EnsureDps(snapshot);
        Toggle(window);
    }

    public static void ShowDps(MeterSnapshot snapshot)
    {
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

    public static void ToggleDbm()
    {
        if (_dbm is null)
        {
            var created = new DbmOverlayWindow();
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_dbm, created))
                    _dbm = null;
            };
            _dbm = created;
        }

        Toggle(_dbm);
    }

    public static void ToggleEventTimers()
    {
        if (_eventTimers is null)
        {
            var created = new EventTimerOverlayWindow();
            created.Closed += (_, _) =>
            {
                if (ReferenceEquals(_eventTimers, created))
                    _eventTimers = null;
            };
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
