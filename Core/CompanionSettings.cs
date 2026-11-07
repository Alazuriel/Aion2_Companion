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

    // Opt-in: only show the Phaseward HUD when a recognized encounter is active.
    public static bool AutoPhasewardOverlay
    {
        get { lock (Sync) return Load().AutoPhasewardOverlay; }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.AutoPhasewardOverlay == value) return;
                data.AutoPhasewardOverlay = value;
                Save(data);
            }
        }
    }

    // Local-only opt-in recorder for repeatable boss cast/HP patterns.
    // Separate opt-in for future community observations. Default OFF; no live POST in DEV.
    // Separate consent for internet upload; existing local queue consent does NOT grant it.
    public static bool PhasewardCommunityUploadConsentConfirmed
    {
        get { lock (Sync) return Load().PhasewardCommunityUploadConsentConfirmed; }
        set { lock (Sync) { var data = Load(); if (data.PhasewardCommunityUploadConsentConfirmed == value) return;
            data.PhasewardCommunityUploadConsentConfirmed = value; Save(data); } }
    }

    public static string PhasewardCommunityUploadAccountHash
    {
        get { lock (Sync) return Load().PhasewardCommunityUploadAccountHash; }
        set { lock (Sync) { var data = Load(); if (data.PhasewardCommunityUploadAccountHash == value) return;
            data.PhasewardCommunityUploadAccountHash = value; Save(data); } }
    }

    public static bool PhasewardCommunityEnablePending
    {
        get { lock (Sync) return Load().PhasewardCommunityEnablePending; }
        set { lock (Sync) { var data = Load(); if (data.PhasewardCommunityEnablePending == value) return;
            data.PhasewardCommunityEnablePending = value; Save(data); } }
    }

    public static bool PhasewardCommunityEnableOnNextLogin
    {
        get { lock (Sync) return Load().PhasewardCommunityEnableOnNextLogin; }
        set { lock (Sync) { var data = Load(); if (data.PhasewardCommunityEnableOnNextLogin == value) return;
            data.PhasewardCommunityEnableOnNextLogin = value; Save(data); } }
    }

    public static bool PhasewardCommunityRevokePending
    {
        get { lock (Sync) return Load().PhasewardCommunityRevokePending; }
        set { lock (Sync) { var data = Load(); if (data.PhasewardCommunityRevokePending == value) return;
            data.PhasewardCommunityRevokePending = value; Save(data); } }
    }

    public static bool PhasewardCommunityQueueEnabled
    {
        get { lock (Sync) return Load().PhasewardCommunityQueueEnabled; }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.PhasewardCommunityQueueEnabled == value) return;
                data.PhasewardCommunityQueueEnabled = value;
                Save(data);
            }
        }
    }

    // Separate local-only consent: the trace may contain raw, potentially personal packet fields.
    // This flag does NOT authorize sending it to any endpoint.
    // Raw Research V2 is a SEPARATE consent from community-v1. Defaults OFF.
    // Set ONLY after the separate, explicit raw-packet disclosure is accepted.
    public static bool PhasewardTraceV2RawUploadAcknowledged
    {
        get { lock (Sync) return Load().PhasewardTraceV2RawUploadAcknowledged; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2RawUploadAcknowledged==value) return;
            d.PhasewardTraceV2RawUploadAcknowledged=value; Save(d); } }
    }
    public static bool PhasewardTraceV2UploadEnabled
    {
        get { lock (Sync) return Load().PhasewardTraceV2UploadEnabled; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2UploadEnabled==value) return;
            d.PhasewardTraceV2UploadEnabled=value; Save(d); } }
    }
    public static string PhasewardTraceV2AccountHash
    {
        get { lock (Sync) return Load().PhasewardTraceV2AccountHash; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2AccountHash==value) return;
            d.PhasewardTraceV2AccountHash=value; Save(d); } }
    }
    public static bool PhasewardTraceV2EnablePending
    {
        get { lock (Sync) return Load().PhasewardTraceV2EnablePending; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2EnablePending==value) return;
            d.PhasewardTraceV2EnablePending=value; Save(d); } }
    }
    public static bool PhasewardTraceV2RevokePending
    {
        get { lock (Sync) return Load().PhasewardTraceV2RevokePending; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2RevokePending==value) return;
            d.PhasewardTraceV2RevokePending=value; Save(d); } }
    }
    public static string PhasewardTraceV2WelcomeVersion
    {
        get { lock (Sync) return Load().PhasewardTraceV2WelcomeVersion; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2WelcomeVersion==value) return;
            d.PhasewardTraceV2WelcomeVersion=value; Save(d); } }
    }
    public static bool PhasewardTraceV2EnableOnNextLogin
    {
        get { lock (Sync) return Load().PhasewardTraceV2EnableOnNextLogin; }
        set { lock (Sync) { var d=Load(); if (d.PhasewardTraceV2EnableOnNextLogin==value) return;
            d.PhasewardTraceV2EnableOnNextLogin=value; Save(d); } }
    }

    public static bool PhasewardDetailedTraceEnabled
    {
        get { lock (Sync) return Load().PhasewardDetailedTraceEnabled; }
        set { lock (Sync) { var data = Load();
            if (data.PhasewardDetailedTraceEnabled == value) return;
            data.PhasewardDetailedTraceEnabled = value; Save(data); } }
    }

    public static bool PhasewardLearningEnabled
    {
        get { lock (Sync) return Load().PhasewardLearningEnabled; }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.PhasewardLearningEnabled == value) return;
                data.PhasewardLearningEnabled = value;
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

    // Compact player-death badge for boss encounters.
    public static bool ShowPlayerDeathCounts
    {
        get { lock (Sync) return Load().ShowPlayerDeathCounts; }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.ShowPlayerDeathCounts == value) return;
                data.ShowPlayerDeathCounts = value;
                Save(data);
            }
        }
    }

    // The boss status overlay can be controlled independently from the DPS overlay.
    public static bool ShowBossStatusOverlay
    {
        get { lock (Sync) return Load().ShowBossStatusOverlay; }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.ShowBossStatusOverlay == value) return;
                data.ShowBossStatusOverlay = value;
                Save(data);
            }
        }
    }

    public static bool ShowBossDeathPrediction
    {
        get
        {
            lock (Sync)
                return Load().ShowBossDeathPrediction;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.ShowBossDeathPrediction == value)
                    return;

                data.ShowBossDeathPrediction = value;
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

    public static bool DownloadUpdatesInBackground
    {
        get
        {
            lock (Sync)
                return Load().DownloadUpdatesInBackground;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.DownloadUpdatesInBackground == value)
                    return;

                data.DownloadUpdatesInBackground = value;
                Save(data);
            }
        }
    }

    public static bool IntegrityMonitoringEnabled
    {
        get
        {
            lock (Sync)
                return Load().IntegrityMonitoringEnabled;
        }
        set
        {
            lock (Sync)
            {
                SettingsData data = Load();
                if (data.IntegrityMonitoringEnabled == value)
                    return;
                data.IntegrityMonitoringEnabled = value;
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

    public static string LeaderboardServerName
    {
        get => Load().LeaderboardServerName ?? string.Empty;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            var data = Load();
            if (string.Equals(data.LeaderboardServerName, normalized, StringComparison.Ordinal))
                return;
            data.LeaderboardServerName = normalized;
            Save(data);
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
        public bool AutoPhasewardOverlay { get; set; } = false;
        public bool PhasewardTraceV2RawUploadAcknowledged { get; set; } = false;
        public bool PhasewardTraceV2UploadEnabled { get; set; } = false;
        public string PhasewardTraceV2AccountHash { get; set; } = string.Empty;
        public bool PhasewardTraceV2EnablePending { get; set; } = false;
        public bool PhasewardTraceV2RevokePending { get; set; } = false;
        public string PhasewardTraceV2WelcomeVersion { get; set; } = string.Empty;
        public bool PhasewardTraceV2EnableOnNextLogin { get; set; } = false;
        public bool PhasewardDetailedTraceEnabled { get; set; } = false;
        public bool PhasewardLearningEnabled { get; set; } = false;
        public bool PhasewardCommunityQueueEnabled { get; set; } = false;
        public bool PhasewardCommunityUploadConsentConfirmed { get; set; } = false;
        public string PhasewardCommunityUploadAccountHash { get; set; } = string.Empty;
        public bool PhasewardCommunityRevokePending { get; set; } = false;
        public bool PhasewardCommunityEnablePending { get; set; } = false;
        public bool PhasewardCommunityEnableOnNextLogin { get; set; } = false;
        public bool AutoCaptureEnabled { get; set; } = true;
        public bool ShowBossStatusOverlay { get; set; } = true;
        public bool ShowBossDeathPrediction { get; set; } = true;
        public bool ShowPlayerDeathCounts { get; set; } = true;
        public DpsEncounterMode DpsEncounterMode { get; set; } = DpsEncounterMode.All;
        public string LanguageCode { get; set; } = "en";
        public bool CheckUpdatesAutomatically { get; set; } = true;
        public bool DownloadUpdatesInBackground { get; set; } = false;
        public bool IntegrityMonitoringEnabled { get; set; } = true;
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }
        public string? LastSeenOnboardingVersion { get; set; }
        public int CombatLogLimit { get; set; } = 500;
        public string LeaderboardServerName { get; set; } = string.Empty;
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
