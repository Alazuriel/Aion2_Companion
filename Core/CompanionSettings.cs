using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AION_2_Companion.Core;

public sealed class OverlayPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class EventTimerPreference
{
    public bool Visible { get; set; } = true;
    public bool ShowInOverlay { get; set; }
    public int AlertMinutes { get; set; } = 5;
    public bool AlertAtStart { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool VisualEnabled { get; set; } = true;
}

public enum MainWindowCloseAction
{
    Ask = 0,
    MinimizeToTray = 1,
    ExitApplication = 2,
}

public enum DpsEncounterMode
{
    All = 0,
    Bosses = 1,
}

public static class CompanionSettings
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static SettingsData? _data;

    public static event EventHandler? MapHelperEnabledChanged;
    public static event EventHandler? EventTimerSettingsChanged;
    public static event EventHandler? DpsEncounterModeChanged;

    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nexforge",
        "AION 2 Companion");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static bool MapHelperEnabled
    {
        get
        {
            lock (Sync)
                return Load().MapHelperEnabled;
        }
        set
        {
            bool changed = false;
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.MapHelperEnabled == value)
                    return;

                data.MapHelperEnabled = value;
                Save(data);
                changed = true;
            }

            if (changed)
                MapHelperEnabledChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static MainWindowCloseAction CloseAction
    {
        get
        {
            lock (Sync)
                return Load().CloseAction;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.CloseAction == value)
                    return;

                data.CloseAction = value;
                Save(data);
            }
        }
    }

    public static bool FileLoggingEnabled
    {
        get
        {
            lock (Sync)
                return Load().FileLoggingEnabled;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.FileLoggingEnabled == value)
                    return;

                data.FileLoggingEnabled = value;
                Save(data);
            }
        }
    }

    public static bool AutoManageDpsOverlay
    {
        get
        {
            lock (Sync)
                return Load().AutoManageDpsOverlay;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.AutoManageDpsOverlay == value)
                    return;

                data.AutoManageDpsOverlay = value;
                Save(data);
            }
        }
    }

    public static bool AutoCaptureEnabled
    {
        get
        {
            lock (Sync)
                return Load().AutoCaptureEnabled;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.AutoCaptureEnabled == value)
                    return;

                data.AutoCaptureEnabled = value;
                Save(data);
            }
        }
    }

    public static DpsEncounterMode DpsEncounterMode
    {
        get
        {
            lock (Sync)
                return Load().DpsEncounterMode;
        }
        set
        {
            bool changed;
            lock (Sync)
            {
                SettingsData data = Load();
                changed = data.DpsEncounterMode != value;
                if (!changed)
                    return;
                data.DpsEncounterMode = value;
                Save(data);
            }

            DpsEncounterModeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string LanguageCode
    {
        get
        {
            lock (Sync)
                return Load().LanguageCode;
        }
        set
        {
            string normalized = LocalizationService.NormalizeLanguageCode(value);
            lock (Sync)
            {
                SettingsData data = Load();
                if (string.Equals(data.LanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
                    return;

                data.LanguageCode = normalized;
                Save(data);
            }
        }
    }

    public static bool CheckUpdatesAutomatically
    {
        get
        {
            lock (Sync)
                return Load().CheckUpdatesAutomatically;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.CheckUpdatesAutomatically == value)
                    return;

                data.CheckUpdatesAutomatically = value;
                Save(data);
            }
        }
    }

    public static DateTimeOffset? LastUpdateCheckUtc
    {
        get
        {
            lock (Sync)
                return Load().LastUpdateCheckUtc;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                data.LastUpdateCheckUtc = value;
                Save(data);
            }
        }
    }

    public static string? LastSeenOnboardingVersion
    {
        get
        {
            lock (Sync)
                return Load().LastSeenOnboardingVersion;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (string.Equals(data.LastSeenOnboardingVersion, value, StringComparison.Ordinal))
                    return;

                data.LastSeenOnboardingVersion = value;
                Save(data);
            }
        }
    }

    public static int CombatLogLimit
    {
        get
        {
            lock (Sync)
                return Load().CombatLogLimit;
        }
        set
        {
            int normalized = value <= 0 ? 0 : Math.Max(10, value);
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.CombatLogLimit == normalized)
                    return;

                data.CombatLogLimit = normalized;
                Save(data);
            }
        }
    }

    public static string EventTimerRegion
    {
        get
        {
            lock (Sync)
                return Load().EventTimerRegion;
        }
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "europe" : value.Trim();
            bool changed;
            lock (Sync)
            {
                SettingsData data = Load();
                changed = !string.Equals(data.EventTimerRegion, normalized, StringComparison.OrdinalIgnoreCase);
                if (!changed)
                    return;
                data.EventTimerRegion = normalized;
                Save(data);
            }

            EventTimerSettingsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool EventTimerOverlayClickThrough
    {
        get
        {
            lock (Sync)
                return Load().EventTimerOverlayClickThrough;
        }
        set
        {
            bool changed;
            lock (Sync)
            {
                SettingsData data = Load();
                changed = data.EventTimerOverlayClickThrough != value;
                if (!changed)
                    return;
                data.EventTimerOverlayClickThrough = value;
                Save(data);
            }

            EventTimerSettingsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static EventTimerPreference GetEventTimerPreference(string eventId, bool defaultOverlay = false)
    {
        lock (Sync)
        {
            SettingsData data = Load();
            if (!data.EventTimerPreferences.TryGetValue(eventId, out EventTimerPreference? preference))
            {
                return new EventTimerPreference { ShowInOverlay = defaultOverlay };
            }

            return Clone(preference);
        }
    }

    public static void SetEventTimerPreference(string eventId, EventTimerPreference preference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(preference);

        var normalized = Clone(preference);
        normalized.AlertMinutes = normalized.AlertMinutes < 0
            ? -1
            : Math.Clamp(normalized.AlertMinutes, 0, 1440);

        lock (Sync)
        {
            SettingsData data = Load();
            data.EventTimerPreferences[eventId] = normalized;
            Save(data);
        }

        EventTimerSettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool HasEventTimerNotification(string key)
    {
        lock (Sync)
            return Load().EventTimerNotificationHistory.Contains(key, StringComparer.Ordinal);
    }

    public static void RememberEventTimerNotification(string key)
    {
        lock (Sync)
        {
            SettingsData data = Load();
            if (data.EventTimerNotificationHistory.Contains(key, StringComparer.Ordinal))
                return;

            data.EventTimerNotificationHistory.Add(key);
            if (data.EventTimerNotificationHistory.Count > 400)
                data.EventTimerNotificationHistory.RemoveRange(
                    0, data.EventTimerNotificationHistory.Count - 300);
            Save(data);
        }
    }

    private static EventTimerPreference Clone(EventTimerPreference value) => new()
    {
        Visible = value.Visible,
        ShowInOverlay = value.ShowInOverlay,
        AlertMinutes = value.AlertMinutes,
        AlertAtStart = value.AlertAtStart,
        SoundEnabled = value.SoundEnabled,
        VisualEnabled = value.VisualEnabled,
    };

    public static OverlayPlacement? GetOverlayPlacement(string key)
    {
        lock (Sync)
        {
            if (!Load().OverlayPlacements.TryGetValue(key, out OverlayPlacement? value))
                return null;

            return new OverlayPlacement
            {
                X = value.X,
                Y = value.Y,
                Width = value.Width,
                Height = value.Height,
            };
        }
    }

    public static void SetOverlayPlacement(string key, OverlayPlacement placement)
    {
        lock (Sync)
        {
            Load().OverlayPlacements[key] = placement;
            Save(_data!);
        }
    }

    private static SettingsData Load()
    {
        if (_data is not null)
            return _data;

        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                _data = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugConsole.Log("settings", $"A beállítások nem tölthetők be: {ex.Message}");
        }

        _data ??= new SettingsData();
        _data.OverlayPlacements ??= new Dictionary<string, OverlayPlacement>(StringComparer.OrdinalIgnoreCase);
        _data.EventTimerPreferences ??= new Dictionary<string, EventTimerPreference>(StringComparer.OrdinalIgnoreCase);
        _data.EventTimerNotificationHistory ??= [];
        return _data;
    }

    private static void Save(SettingsData data)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            string temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.Log("settings", $"A beállítások nem menthetők: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public SettingsData()
        {
        }

        public bool MapHelperEnabled { get; set; }
        public bool FileLoggingEnabled { get; set; }
        public bool AutoManageDpsOverlay { get; set; } = true;
        public bool AutoCaptureEnabled { get; set; } = true;
        public DpsEncounterMode DpsEncounterMode { get; set; } = DpsEncounterMode.All;
        public string LanguageCode { get; set; } = "en";
        public bool CheckUpdatesAutomatically { get; set; } = true;
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }
        public string? LastSeenOnboardingVersion { get; set; }
        public int CombatLogLimit { get; set; } = 500;
        public string EventTimerRegion { get; set; } = "europe";
        public bool EventTimerOverlayClickThrough { get; set; }
        public Dictionary<string, EventTimerPreference> EventTimerPreferences { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
        public List<string> EventTimerNotificationHistory { get; set; } = [];
        public MainWindowCloseAction CloseAction { get; set; } = MainWindowCloseAction.Ask;
        public Dictionary<string, OverlayPlacement> OverlayPlacements { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }
}
