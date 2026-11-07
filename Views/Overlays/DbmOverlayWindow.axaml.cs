using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Phaseward;

namespace AION_2_Companion.Views.Overlays;

public partial class DbmOverlayWindow : Window
{
    // Windows 11 DWM can draw a border on undecorated transparent windows.
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private const int DwmwaBorderColor = 34;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    private readonly StackPanel _surface = new() { Spacing = 7, Margin = new Thickness(8, 6) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public DbmOverlayWindow()
    {
        AvaloniaXamlLoader.Load(this);
        OverlayPlacementTracker.Attach(this, "dbm-assistant");
        ShowActivated = false;
        Focusable = false;
        Opened += (_, _) =>
        {
            WindowClickThrough.Apply(this, enabled: true);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        int noBorder = DwmColorNone;
                        DwmSetWindowAttribute(handle, DwmwaBorderColor, ref noBorder, sizeof(int));
                    }
                    catch (DllNotFoundException) { }
                    catch (EntryPointNotFoundException) { }
                }
            }
        };

        _surface.IsHitTestVisible = false;
        Content = _surface;
        _timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        Opened += (_, _) => { _timer.Start(); Refresh(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        PhasewardSnapshot snapshot = PhasewardHub.Snapshot();
        _surface.Children.Clear();
        if (snapshot.State != "encounter")
        {
            _surface.IsVisible = false;
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var alert = ActiveAlert(snapshot, now);
        if (alert is not null)
            _surface.Children.Add(PhasewardHudVisuals.Alert(alert.Value.Title, alert.Value.Detail, alert.Value.Tint));

        // Multiple known mechanics stay visible simultaneously, nearest first.
        // No fake timers: only verified cooldowns, measurable HP estimates or reference events.
        List<PhasewardHudBar> upcoming = BuildBars(snapshot)
            .Where(x => double.IsFinite(x.Remaining) && x.Remaining > 0 &&
                        double.IsFinite(x.Duration) && x.Duration > 0)
            .OrderBy(x => x.Remaining)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        double height = Bounds.Height > 0 ? Bounds.Height : Height;
        int capacity = PhasewardHudVisuals.VisibleRows(height, alert is not null);
        for (int index = 0; index < Math.Min(capacity, upcoming.Count); index++)
            _surface.Children.Add(PhasewardHudVisuals.Row(upcoming[index], index == 0));

        _surface.IsVisible = _surface.Children.Count > 0;
    }

    private static (string Title, string Detail, Color Tint)? ActiveAlert(PhasewardSnapshot snapshot, DateTimeOffset now)
    {
        PhasewardCueStatus? hpAlert = snapshot.Cues
            .Where(cue => cue.TriggeredAt is not null &&
                          (now - cue.TriggeredAt.Value).TotalSeconds is >= 0 and <= 6)
            .OrderByDescending(cue => cue.TriggeredAt)
            .FirstOrDefault();
        if (hpAlert is not null)
            return (hpAlert.Name, hpAlert.Detail, Color.Parse("#FFB84D"));

        PhasewardMechanicStatus? cast = snapshot.Mechanics
            .Where(mechanic => mechanic.LastObserved is not null &&
                (now - mechanic.LastObserved.Value).TotalSeconds >= 0 &&
                (now - mechanic.LastObserved.Value).TotalSeconds <=
                    Math.Max(2.5, (mechanic.CastSeconds ?? 0) + 0.8))
            .OrderByDescending(mechanic => mechanic.LastObserved)
            .FirstOrDefault();
        if (cast is not null)
            return (UsefulName(cast), cast.Notes.FirstOrDefault() ?? "", Color.Parse("#FF657A"));

        return null;
    }

    private static IEnumerable<PhasewardHudBar> BuildBars(PhasewardSnapshot snapshot)
    {
        // Predictive rules come only from cross-validated previous pulls.
        // They are conspicuously labelled estimates; actual observed casts remain authoritative.
        foreach (PhasewardLearnedPrediction prediction in snapshot.LearnedPredictions)
            if (prediction.SecondsRemaining is > 0 and <= 90)
                yield return new PhasewardHudBar(
                    $"{prediction.Name} · {LocalizationService.Get("PwLearnedShort")} " +
                    $"({prediction.SupportingPulls})",
                    prediction.SecondsRemaining, prediction.WindowSeconds,
                    Color.Parse("#72D9AE"), Estimated: true);

        foreach (PhasewardTimerStatus timer in snapshot.Timers)
            if (!timer.Elapsed && timer.SecondsRemaining > 0 && timer.SecondsRemaining <= 300)
                yield return new PhasewardHudBar(LocalizationService.Get("PwFrenzy"),
                    timer.SecondsRemaining, timer.AtSeconds, Color.Parse("#FF6475"));

        foreach (PhasewardCueStatus cue in snapshot.Cues)
            if (!cue.Reached && cue.SecondsUntilEstimated is > 0 and <= 180)
                yield return new PhasewardHudBar(
                    $"{cue.Name} · {cue.HpThreshold:0.#}% HP", cue.SecondsUntilEstimated.Value,
                    180, Color.Parse("#FFC76E"), Estimated: true);

        foreach (PhasewardMechanicStatus mechanic in snapshot.Mechanics)
            if (mechanic.VerifiedTrigger && mechanic.CooldownSeconds is >= 3 &&
                mechanic.SecondsUntilPredicted is > 0 and <= 120)
                yield return new PhasewardHudBar(UsefulName(mechanic),
                    mechanic.SecondsUntilPredicted.Value, mechanic.CooldownSeconds.Value,
                    Color.Parse("#3CD8FF"), Estimated: true);
    }

    private static string UsefulName(PhasewardMechanicStatus mechanic)
    {
        if (!IsGeneric(mechanic.Name)) return mechanic.Name;
        string? line = mechanic.BossLines.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(line)) return LocalizationService.Get("PwIncomingMechanic");
        int left = line.IndexOf('“');
        int right = line.LastIndexOf('”');
        return left >= 0 && right > left ? line[(left + 1)..right] : line;
    }

    private static bool IsGeneric(string name) =>
        name.Equals("Cast", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Skill", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Attack", StringComparison.OrdinalIgnoreCase);
}
