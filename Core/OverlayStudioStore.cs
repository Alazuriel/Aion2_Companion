using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AION_2_Companion.Core;

// Editor preferences are local, optional, and never contain combat or identity data.
public sealed class StudioWidget
{
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1.0;
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public StudioWidget Copy() => (StudioWidget)MemberwiseClone();
}

public sealed class StudioLayout
{
    public Dictionary<string, StudioWidget> Widgets { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, StudioWidget>> Presets { get; set; } = new(StringComparer.Ordinal);

    public StudioLayout Copy() => new()
    {
        Widgets = Widgets.ToDictionary(x => x.Key, x => x.Value.Copy(), StringComparer.Ordinal),
        Presets = Presets.ToDictionary(x => x.Key,
            x => x.Value.ToDictionary(y => y.Key, y => y.Value.Copy(), StringComparer.Ordinal),
            StringComparer.Ordinal),
    };
}

public static class OverlayStudioStore
{
    public const string Dps = "dps-meter-compact-v2";
    public const string Phaseward = "dbm-assistant";
    public const string Timers = "event-timers-overlay";
    public const string BossStatus = "boss-status-detached-v1";
    public static readonly string[] Keys = { Dps, BossStatus, Phaseward, Timers };
    private static StudioLayout? _current;
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(CompanionSettings.SettingsDirectory, "overlay-studio.json");

    private static StudioLayout Read()
    {
        if (_current is not null) return _current;
        try
        {
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length < 65536)
            {
                var data = JsonSerializer.Deserialize<StudioLayout>(File.ReadAllText(FilePath));
                if (data?.Widgets is not null && data.Presets is not null)
                    return _current = data;
            }
        }
        catch (Exception) { /* A bad local preference is not a reason to block game startup. */ }
        return _current = new StudioLayout();
    }

    public static StudioLayout Snapshot()
    {
        lock (Gate) return Read().Copy();
    }

    public static bool IsVisible(string key)
    {
        lock (Gate) return !Read().Widgets.TryGetValue(key, out var widget) || widget.Visible;
    }
    public static bool IsLocked(string key)
    {
        lock (Gate) return Read().Widgets.TryGetValue(key, out var widget) && widget.Locked;
    }
    public static double Opacity(string key)
    {
        lock (Gate)
            return Read().Widgets.TryGetValue(key, out var widget) && double.IsFinite(widget.Opacity)
                ? Math.Clamp(widget.Opacity, .35, 1.0) : 1.0;
    }

    public static void SaveNamedPreset(string name, Dictionary<string, StudioWidget> widgets)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 32)
            throw new ArgumentException("Invalid preset name", nameof(name));
        var saved = Snapshot();
        saved.Presets[name.Trim()] = widgets.ToDictionary(x => x.Key, x => x.Value.Copy(), StringComparer.Ordinal);
        Commit(saved);
    }

    public static void Commit(StudioLayout layout)
    {
        // No unexpected widget keys, unbounded presets, or invalid values from disk/editor.
        var clean = new StudioLayout();
        foreach (string key in Keys)
            if (layout.Widgets.TryGetValue(key, out var item))
                clean.Widgets[key] = Sanitize(item);

        foreach (var preset in layout.Presets.Take(12))
        {
            string name = preset.Key.Trim();
            if (name.Length is < 1 or > 32) continue;
            var widgets = new Dictionary<string, StudioWidget>(StringComparer.Ordinal);
            foreach (string key in Keys)
                if (preset.Value.TryGetValue(key, out var item)) widgets[key] = Sanitize(item);
            clean.Presets[name] = widgets;
        }

        lock (Gate)
        {
            Directory.CreateDirectory(CompanionSettings.SettingsDirectory);
            string staging = FilePath + ".tmp";
            try
            {
                File.WriteAllText(staging, JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(staging, FilePath, true);
                _current = clean;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
            }
        }
    }

    private static StudioWidget Sanitize(StudioWidget item) => new()
    {
        Visible = item.Visible,
        Locked = item.Locked,
        Opacity = double.IsFinite(item.Opacity) ? Math.Clamp(item.Opacity, .35, 1) : 1,
        X = item.X, Y = item.Y,
        Width = double.IsFinite(item.Width) ? Math.Clamp(item.Width, 200, 1000) : 328,
        Height = double.IsFinite(item.Height) ? Math.Clamp(item.Height, 120, 800) : 400,
    };
}
