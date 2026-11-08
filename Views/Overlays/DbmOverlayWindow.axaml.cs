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
        // One-time compatibility with the former 520x340 overlay size.
        // Run after both placement-restoration event handlers for this opening.
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Width is >= 500 and <= 550 && Height is >= 320 and <= 360)
            {
                Width = 260;
                Height = 170;
            }
        }, DispatcherPriority.Background);
        _timer.Tick += (_, _) => { if (IsVisible) Refresh(); };
        Opened += (_, _) => { _timer.Start(); Refresh(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        // The live HUD is deliberately Stagger-only, even in --developer mode.
        // Research, cooldowns, cast cues, boss banners and other Phaseward alerts
        // remain collected but are visible only in the Developer screen.
        PhasewardSnapshot snapshot = PhasewardHub.Snapshot();
        _surface.Children.Clear();
        if (snapshot.State != "encounter" || snapshot.BossRuntimeId == 0)
        {
            _surface.IsVisible = false;
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        PhasewardStaggerResearchState staggerResearch =
            PhasewardStaggerResearch.Snapshot(snapshot.BossRuntimeId, now);
        if (staggerResearch.Active &&
            staggerResearch.SecondsRemaining is double remaining &&
            double.IsFinite(remaining) && remaining > 0)
        {
            _surface.Children.Add(PhasewardHudVisuals.Row(
                new PhasewardHudBar("STAGGER", remaining, 5.0, Color.Parse("#58D8EE")),
                nearest: true));
        }
        _surface.IsVisible = _surface.Children.Count > 0;
    }

    private void RenderBossesResearchStatus(PhasewardSnapshot snapshot, DateTimeOffset now)
    {
        PhasewardMechanicStatus? observed = snapshot.Mechanics
            .Where(mechanic => mechanic.LastObserved is DateTimeOffset at &&
                (now - at).TotalSeconds is >= 0 and <= 6)
            .OrderByDescending(mechanic => mechanic.LastObserved)
            .FirstOrDefault();
        string origin = snapshot.IsBorrowedDifficulty
            ? $"Variant {snapshot.BossNpcId}, source {snapshot.MechanicsSourceNpcId}"
            : $"NPC {snapshot.BossNpcId}";
        string observation = observed is null ? "waiting for boss cast" : $"cast: {observed.Name}";
        _surface.Children.Add(PhasewardHudVisuals.Alert(
            "RESEARCH TEST · UNVERIFIED", origin + " · " + observation,
            Color.Parse("#EFC071")));
    }

    private void RenderBossesEmptyStatus(PhasewardSnapshot snapshot)
    {
        _surface.Children.Add(PhasewardHudVisuals.Alert(
            snapshot.BossName + " · BOSS RECOGNIZED",
            "No Phaseward mechanics available for this NPC",
            Color.Parse("#78BFD2")));
    }

    private static (string Title, string Detail, Color Tint)? ActiveAlert(PhasewardSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Stagger.Active)
        {
            string title = snapshot.Stagger.EstimatedFromOpcode
                ? "STAGGER · ESTIMATED" : "BOSS STAGGERED";
            string detail = snapshot.Stagger.ActiveRemainingSeconds is double seconds
                ? $"~{seconds:0.0}s left · +5s estimated timing shift"
                : "Boss control state · timing estimate";
            return (title, detail, Color.Parse("#58D8EE"));
        }

        if (snapshot.Stagger.EstimatedEndFromOpcode &&
            snapshot.Stagger.LastEndedAt is DateTimeOffset staggerEnd &&
            now >= staggerEnd && (now - staggerEnd).TotalSeconds <= 2.5)
            return ("STAGGER ENDED · ESTIMATED",
                snapshot.Stagger.EstimatedLastDurationSeconds is double seconds
                    ? $"Control-value zero after {seconds:0.0}s · provisional"
                    : "Boss control-value zero · provisional",
                Color.Parse("#72D9AE"));

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
        // Estimate the next Stagger only from repeated, consistent observations.
        if (snapshot.Stagger.NextEstimatedSeconds is double next &&
            !snapshot.Stagger.Active && next is > 0 and <= 180)
            yield return new PhasewardHudBar(
                $"Stagger · estimated ({snapshot.Stagger.SignalCount} signals)",
                next, Math.Max(12, snapshot.Stagger.EstimatedCycleSeconds ?? 180),
                Color.Parse("#58D8EE"), Estimated: true);

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
            if (!snapshot.IsResearchPreview && !snapshot.IsResearchMapping && !cue.Reached &&
                cue.SecondsUntilEstimated is > 0 and <= 180)
                yield return new PhasewardHudBar(
                    $"{cue.Name} · {cue.HpThreshold:0.#}% HP", cue.SecondsUntilEstimated.Value,
                    180, Color.Parse("#FFC76E"), Estimated: true);

        foreach (PhasewardMechanicStatus mechanic in snapshot.Mechanics)
            if ((mechanic.VerifiedTrigger || snapshot.IsResearchPreview || snapshot.IsResearchMapping) &&
                mechanic.CooldownSeconds is >= 3 &&
                mechanic.SecondsUntilPredicted is > 0 and <= 120)
                yield return new PhasewardHudBar(
                    snapshot.IsResearchPreview || snapshot.IsResearchMapping ? $"Research · {UsefulName(mechanic)}" : UsefulName(mechanic),
                    mechanic.SecondsUntilPredicted.Value, mechanic.CooldownSeconds.Value,
                    Color.Parse(snapshot.IsResearchPreview || snapshot.IsResearchMapping ? "#EFC071" : "#3CD8FF"), Estimated: true);
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
