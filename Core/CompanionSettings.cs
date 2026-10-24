using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AION_2_Companion.Core;

public sealed class OverlayPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public enum MainWindowCloseAction
{
    Ask = 0,
    MinimizeToTray = 1,
    ExitApplication = 2,
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
        public int CombatLogLimit { get; set; } = 500;
        public MainWindowCloseAction CloseAction { get; set; } = MainWindowCloseAction.Ask;
        public Dictionary<string, OverlayPlacement> OverlayPlacements { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }
}
