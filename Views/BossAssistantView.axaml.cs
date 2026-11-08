using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Phaseward;

namespace AION_2_Companion.Views;

/// <summary>Phaseward dashboard. Never simulates combat or starts a pull countdown.</summary>
public partial class BossAssistantView : UserControl
{
    private readonly TextBlock _state = new() { FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#58E0B2") };
    private readonly TextBlock _boss = new() { FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _hint = new() { FontSize = 12, Foreground = Brush.Parse("#91B0C6"), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _mechanics = new() { Spacing = 8 };
    private readonly TextBlock _mechanicsHeading = new() { FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#45DFFF") };
    private readonly CheckBox _auto = new() { FontSize = 12 };
    private readonly Button _toggle = new() { Padding = new Thickness(16, 9) };
    private readonly Button _studio = new() { Padding = new Thickness(16, 9) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public BossAssistantView()
    {
        AvaloniaXamlLoader.Load(this);
        _auto.IsChecked = CompanionSettings.AutoPhasewardOverlay;
        _auto.IsCheckedChanged += (_, _) => CompanionSettings.AutoPhasewardOverlay = _auto.IsChecked == true;
        _toggle.Classes.Add("ui");
        _toggle.Classes.Add("primary");
        _toggle.Click += (_, _) => Overlays.OverlayWindowManager.ToggleDbm();
        _studio.Classes.Add("ui");
        _studio.Click += (_, _) => Overlays.OverlayWindowManager.OpenStudio();

        var root = new StackPanel { Margin = new Thickness(26, 24), Spacing = 16 };
        root.Children.Add(new TextBlock
        {
            Text = "NEXFORGE  /  PHASEWARD", FontSize = 12, FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#45DFFF")
        });
        root.Children.Add(new TextBlock
        {
            Text = "Know the fight. Before it strikes.", FontSize = 21,
            Foreground = Brushes.White, FontWeight = FontWeight.SemiBold
        });
        root.Children.Add(Card(new StackPanel { Spacing = 7, Children = { _state, _boss, _hint } }));
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { _toggle, _studio }
        };
        root.Children.Add(actions);
        root.Children.Add(new StackPanel { Spacing = 6, Children = { _auto } });
        root.Children.Add(_mechanicsHeading);
        root.Children.Add(new Border { Child = _mechanics });
        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { _timer.Start(); Refresh(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        Refresh();
    }

    private static Border Card(Control content) => new()
    {
        Background = Brush.Parse("#111E30"),
        CornerRadius = new CornerRadius(13), Padding = new Thickness(20), Child = content
    };

    private static Border Mechanic(string title, string detail, string time, string color)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var texts = new StackPanel { Spacing = 4 };
        texts.Children.Add(new TextBlock
        {
            Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(detail))
            texts.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 11, Foreground = Brush.Parse("#8FB1C8"),
                TextWrapping = TextWrapping.Wrap
            });
        grid.Children.Add(texts);
        var countdown = new TextBlock
        {
            Text = time, FontSize = 19, FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse(color), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(countdown, 1);
        grid.Children.Add(countdown);
        return new Border
        {
            Child = grid, Background = Brush.Parse("#10263A"),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12)
        };
    }

    private static string L(string key) => LocalizationService.Get(key);

    private void Refresh()
    {
        _auto.Content = L("PwAutoHud");
        _toggle.Content = L("PwToggleHud");
        _studio.Content = L("PwOpenStudio");
        _mechanicsHeading.Text = L("PwKnownMechanics");
        _mechanics.Children.Clear();
        PhasewardSnapshot snapshot = PhasewardHub.Snapshot();
        if (snapshot.State != "encounter")
        {
            _state.Text = snapshot.State == "unavailable" ? L("PwUnavailable") : L("PwStandby");
            _boss.Text = L("PwWaitingForBoss");
            _hint.Text = L("PwReadyHint");
            _mechanicsHeading.IsVisible = false;
            return;
        }

        _mechanicsHeading.IsVisible = Program.DeveloperMode;
        _state.Text = L("PwLive");
        _boss.Text = snapshot.BossName;
        _hint.Text = string.IsNullOrWhiteSpace(snapshot.Instance) ? L("PwDetected") : snapshot.Instance;
        // Experimental casts/cooldowns/cues must not leak into normal UI.
        // They are still processed by PhasewardEngine and available in Developer.
        if (!Program.DeveloperMode)
            return;

        // Only real, active countdowns from PhasewardHub; no fabricated time.
        foreach (var timer in snapshot.Timers.Where(x => !x.Elapsed && x.SecondsRemaining > 0)
                     .OrderBy(x => x.SecondsRemaining).Take(3))
            _mechanics.Children.Add(Mechanic(L("PwFrenzy"), "", TimeText(timer.SecondsRemaining), "#FF8E96"));
        foreach (var mechanic in snapshot.Mechanics.Where(x => x.SecondsUntilPredicted is > 0)
                     .OrderBy(x => x.SecondsUntilPredicted).Take(5))
            _mechanics.Children.Add(Mechanic(mechanic.Name, L("PwEstimate"),
                TimeText(mechanic.SecondsUntilPredicted!.Value), "#45DFFF"));
        foreach (var cue in snapshot.Cues.Where(x => !x.Reached && x.SecondsUntilEstimated is > 0)
                     .OrderBy(x => x.SecondsUntilEstimated).Take(3))
            _mechanics.Children.Add(Mechanic(cue.Name, L("PwHpEstimate"),
                TimeText(cue.SecondsUntilEstimated!.Value), "#FFBC67"));

        // Known but not predictable events: show names without invented seconds.
        if (_mechanics.Children.Count == 0)
        {
            foreach (var mechanic in snapshot.Mechanics.Where(x => x.VerifiedTrigger).Take(5))
                _mechanics.Children.Add(Mechanic(mechanic.Name, L("PwAwaitingTrigger"), "", "#45DFFF"));
        }
        if (_mechanics.Children.Count == 0)
            _mechanics.Children.Add(Mechanic(L("PwNoPrediction"), L("PwAwaitingTrigger"), "", "#45DFFF"));
    }

    private static string TimeText(double value)
    {
        int seconds = Math.Max(0, (int)Math.Ceiling(value));
        return seconds >= 60 ? $"{seconds / 60}:{seconds % 60:00}" : $"{seconds}s";
    }
}
