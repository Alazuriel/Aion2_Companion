using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AION_2_Companion.Core.Phaseward;

namespace AION_2_Companion.Views;

public partial class CaptureMonitorView : UserControl
{
    private readonly DispatcherTimer _phasewardResearchTimer =
        new() { Interval = TimeSpan.FromMilliseconds(500) };

    public CaptureMonitorView()
    {
        AvaloniaXamlLoader.Load(this);
        _phasewardResearchTimer.Tick += (_, _) => RefreshPhasewardResearch();
        AttachedToVisualTree += (_, _) =>
        {
            RefreshPhasewardResearch();
            _phasewardResearchTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => _phasewardResearchTimer.Stop();
    }

    private void RefreshPhasewardResearch()
    {
        // This UserControl is only navigable when Program.DeveloperMode is true.
        // Never broadcast research cues to the live HUD.
        if (!Program.DeveloperMode) return;
        var state = this.FindControl<TextBlock>("PhasewardResearchStateText");
        var boss = this.FindControl<TextBlock>("PhasewardResearchBossText");
        var signals = this.FindControl<TextBlock>("PhasewardResearchSignalsText");
        if (state is null || boss is null || signals is null) return;

        state.Text = PhasewardHub.LoadStatus;
        PhasewardSnapshot snapshot = PhasewardHub.Snapshot();
        if (snapshot.State != "encounter")
        {
            boss.Text = "No active Phaseward boss";
            signals.Text = "Awaiting decoded boss casts/hits...";
            return;
        }

        boss.Text = $"{snapshot.BossName} · NPC {snapshot.BossNpcId} · " +
                    $"Runtime {snapshot.BossRuntimeId} · " +
                    $"{snapshot.Mechanics.Count} loaded mechanics · {snapshot.Instance}";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var seen = snapshot.Mechanics
            .Where(m => m.LastObserved is DateTimeOffset time && time <= now)
            .OrderByDescending(m => m.LastObserved)
            .Take(10)
            .Select(m => $"{m.LastObserved!.Value.ToLocalTime():HH:mm:ss}  {m.Name}  " +
                         $"({Math.Max(0, (now - m.LastObserved.Value).TotalSeconds):0.0}s ago)")
            .ToArray();
        signals.Text = seen.Length == 0
            ? "No matched mechanic signal in this encounter yet."
            : string.Join(Environment.NewLine, seen);
    }
}
