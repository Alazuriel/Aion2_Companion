using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

public partial class TimelineWindow : Window
{
    private const double LabelWidth = 170;
    private const double LaneHeight = 26;
    private const double RulerHeight = 22;

    // Egyezzen a skill-név oszlop szélességével, hogy a 0s egy vonalban legyen
    private const double ChartLeft = 170;
    private const double ChartRight = 24;
    private const double ChartTop = 16;
    private const double ChartBottom = 28;
    private const double ChartHeight = 220;

    private const double MinPxPerSecond = 8;
    private const double MaxPxPerSecond = 120;
    private const double DefaultVisibleSeconds = 12;

    private PlayerStat _player = null!;
    private MeterSnapshot _snapshot = null!;
    private int _selected;
    private bool[] _active = Array.Empty<bool>();
    private MeterMode _mode = MeterMode.Dps;

    private double _pxPerSecond = 40;
    private double _cellSeconds = 1;
    private double _zoom = 1.0;
    private double? _cursorTime;
    private Line? _cursorMarker;
    private bool _syncingScroll;
    // Mouse wheel events may arrive many times before one rendered frame.
    // Coalesce zoom work instead of re-creating thousands of Avalonia controls per tick.
    private readonly DispatcherTimer _zoomDebounce = new() { Interval = TimeSpan.FromMilliseconds(115) };
    private double _zoomAnchorTime;
    private double _zoomAnchorViewportX;
    private CancellationTokenSource? _prepareCts;
    private int _prepareVersion;
    private bool _isPreparing;
    private int _visibleSkillCount = 18;
    private const int SkillPageSize = 18;
    private SkillEvent[] _sortedEvents = Array.Empty<SkillEvent>();
    private readonly Dictionary<string, int[]> _laneBuckets = new(StringComparer.Ordinal);
    private static readonly IBrush[] CurveColors =
    {
        Brush.Parse("#42E6FA"), Brush.Parse("#F4AD61"), Brush.Parse("#CE90F0"),
        Brush.Parse("#8CE1A6"), Brush.Parse("#FF8585"), Brush.Parse("#8CB8FA"),
        Brush.Parse("#E4D17E"), Brush.Parse("#E98ECC"), Brush.Parse("#7AC9A6"),
    };
    private static IBrush CurveBrush(int index) => CurveColors[index % CurveColors.Length];

    // UI-only cached series; invalidated on fight/metric change, not on mouse movement.
    private readonly Dictionary<(int Player, MeterMode Mode), double[]> _seriesCache = new();
    private readonly Dictionary<string, List<SkillEvent>> _skillCache = new(StringComparer.Ordinal);
    private double _lastTooltipTime = double.NaN;
    private int _lastTooltipActiveMask = -1;


    public TimelineWindow() : this(new PlayerStat(), new MeterSnapshot()) { }

    public TimelineWindow(PlayerStat player, MeterSnapshot snapshot)
    {
        AvaloniaXamlLoader.Load(this);
        LocalizationService.LanguageChanged += LanguageChanged;
        _zoomDebounce.Tick += (_, _) =>
        {
            _zoomDebounce.Stop();
            RenderPendingZoom();
        };
        Closed += (_, _) =>
        {
            _prepareCts?.Cancel();
            _zoomDebounce.Stop();
            LocalizationService.LanguageChanged -= LanguageChanged;
        };

        // Vízszintes scroll szinkron chart ↔ sávok
        var chart = this.FindControl<Canvas>("ChartCanvas")!;
        chart.PointerPressed += ChartPointerPressed;
        chart.PointerMoved += ChartPointerMoved;
        chart.PointerWheelChanged += OnWheel;

        var chartScroll = this.FindControl<ScrollViewer>("ChartScroll");
        if (chartScroll is not null)
            chartScroll.PointerWheelChanged += OnWheel;

        var laneTicks = this.FindControl<StackPanel>("LaneTicks");
        var laneScroll = this.FindControl<ScrollViewer>("LaneScroll")
                         ?? laneTicks?.Parent as ScrollViewer;

        if (laneScroll is not null)
            laneScroll.PointerWheelChanged += OnWheel;

        // Scroll szinkron
        if (chartScroll is not null && laneScroll is not null)
        {
            chartScroll.ScrollChanged += (_, _) =>
            {
                if (_syncingScroll) return;
                _syncingScroll = true;
                try
                {
                    if (Math.Abs(laneScroll.Offset.X - chartScroll.Offset.X) > 0.5)
                        laneScroll.Offset = laneScroll.Offset.WithX(chartScroll.Offset.X);
                }
                finally { _syncingScroll = false; }
            };

            laneScroll.ScrollChanged += (_, _) =>
            {
                if (_syncingScroll) return;
                _syncingScroll = true;
                try
                {
                    if (Math.Abs(chartScroll.Offset.X - laneScroll.Offset.X) > 0.5)
                        chartScroll.Offset = chartScroll.Offset.WithX(laneScroll.Offset.X);
                }
                finally { _syncingScroll = false; }
            };
        }

        UpdatePlayer(player, snapshot);
    }

    public void UpdatePlayer(PlayerStat player, MeterSnapshot snapshot)
    {
        _zoomDebounce.Stop();
        _player = player;
        _snapshot = snapshot;
        _selected = Math.Max(0, snapshot.Players.ToList().IndexOf(player));
        _active = new bool[snapshot.Players.Count];
        if (_active.Length > 0)
            _active[Math.Min(_selected, _active.Length - 1)] = true;

        _mode = MeterMode.Dps;
        _seriesCache.Clear();
        _skillCache.Clear();
        _laneBuckets.Clear();
        _visibleSkillCount = SkillPageSize;
        _lastTooltipTime = double.NaN;
        _lastTooltipActiveMask = -1;
        _zoom = 1.0;
        _cursorTime = null;
        ComputeScale();
        BuildChips();
        UpdateSelectionHeader();
        UpdateModeUi();
        _ = PrepareAndDrawAsync();
    }

    private void UpdateSelectionHeader()
    {
        this.FindControl<TextBlock>("TimelineTitle")!.Text = _player.Name;
        // Preserve the user's existing saved-fight death/positional hit indicators.
        // The header is refreshed for the newly inspected player on every chip switch.
        var summary = LocalizationService.Format(
            "TimelineSubtitle", FormatTime(_snapshot.Duration), _snapshot.Players.Count) +
            $"  ·  💀 {(_player.DeathCount.HasValue ? _player.DeathCount.Value.ToString() : "?")}" +
            $"  ·  {PositionDetails.Format(_player, compact: true)}";
        this.FindControl<TextBlock>("TimelineSub")!.Text = summary;
        int selectedCount = _active.Count(x => x);
        this.FindControl<TextBlock>("SelectedCountText")!.Text = selectedCount > 1
            ? $"{selectedCount} players compared" : "Select more players to compare";
    }

    private sealed class PreparedData
    {
        public Dictionary<int, double[]> Series { get; init; } = new();
        public Dictionary<string, List<SkillEvent>> SkillGroups { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, int[]> LaneBuckets { get; init; } = new(StringComparer.Ordinal);
        public SkillEvent[] SortedEvents { get; set; } = Array.Empty<SkillEvent>();
    }

    private async Task PrepareAndDrawAsync()
    {
        _zoomDebounce.Stop();
        _prepareCts?.Cancel();
        _prepareCts?.Dispose();
        var cts = new CancellationTokenSource();
        _prepareCts = cts;
        int version = ++_prepareVersion;
        CancellationToken token = cts.Token;
        var snapshot = _snapshot;
        var selectedPlayer = _player;
        var mode = _mode;
        double cellSeconds = _cellSeconds;
        double duration = Duration;

        _isPreparing = true;
        var overlay = this.FindControl<Border>("LoadingOverlay");
        if (overlay is not null) overlay.IsVisible = true;
        var loading = this.FindControl<TextBlock>("LoadingText");
        if (loading is not null) loading.Text = "Processing saved combat data…";

        try
        {
            // Allow one frame for the overlay/spinner to paint before expensive work.
            // Covers fast jobs too; older combat-file deserialization happens in the caller.
            await Task.Delay(140, token);
            // The costly part (5 s rolling series, event grouping and lane bucketing)
            // runs away from the Avalonia UI thread. Never access controls in Task.Run.
            var data = await Task.Run(() =>
            {
                var output = new PreparedData();
                for (int i = 0; i < snapshot.Players.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    output.Series[i] = Meter.Series(snapshot.Players[i], mode, snapshot.Duration);
                }
                token.ThrowIfCancellationRequested();
                int cellCount = Math.Max(1, (int)Math.Ceiling(duration / cellSeconds));
                // Nexforge Damage Taken Log Timeline v0.2: generic incoming hit samples, never fabricated boss skills.
                SkillEvent[] sourceEvents = mode == MeterMode.DamageTaken
                    ? selectedPlayer.IncomingDamageEvents.Select(e => new SkillEvent
                    {
                        Time = e.Time, Amount = e.Amount, Skill = "Incoming hit", IsHeal = false
                    }).ToArray()
                    : selectedPlayer.Events.ToArray();
                foreach (var group in sourceEvents.GroupBy(ev => ev.Skill, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    var events = group.ToList();
                    output.SkillGroups[group.Key] = events;
                    var buckets = new int[cellCount];
                    foreach (var ev in events)
                    {
                        int cell = Math.Clamp((int)(ev.Time / cellSeconds), 0, cellCount - 1);
                        buckets[cell]++;
                    }
                    output.LaneBuckets[group.Key] = buckets;
                }
                output.SortedEvents = sourceEvents.OrderBy(ev => ev.Time).ToArray();
                token.ThrowIfCancellationRequested();
                return output;
            }, token);

            if (token.IsCancellationRequested || version != _prepareVersion) return;
            // All Avalonia changes, including bitmap loading, stay on the UI thread.
            _seriesCache.Clear();
            foreach (var item in data.Series)
                _seriesCache[(item.Key, mode)] = item.Value;
            _skillCache.Clear();
            foreach (var item in data.SkillGroups)
                _skillCache[item.Key] = item.Value;
            _laneBuckets.Clear();
            foreach (var item in data.LaneBuckets)
                _laneBuckets[item.Key] = item.Value;
            _sortedEvents = data.SortedEvents;
            _visibleSkillCount = SkillPageSize;
            BuildLanes();
            UpdateModeUi();
            DrawChart();
            HideEventDetail();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version != _prepareVersion) return;
            var time = this.FindControl<TextBlock>("EventDetailTime");
            var detail = this.FindControl<TextBlock>("EventDetailList");
            if (time is not null) time.Text = "Timeline unavailable";
            if (detail is not null) detail.Text = ex.Message;
        }
        finally
        {
            if (version == _prepareVersion)
            {
                _isPreparing = false;
                if (overlay is not null) overlay.IsVisible = false;
            }
        }
    }

    // ---------- Skála ----------

    private double Duration => Math.Max(0.25, _snapshot.Duration);

    private void ComputeScale()
    {
        double basePx = Math.Clamp(
            600.0 / Math.Min(Duration, DefaultVisibleSeconds),
            MinPxPerSecond,
            MaxPxPerSecond);

        _pxPerSecond = Math.Clamp(basePx * _zoom, MinPxPerSecond, MaxPxPerSecond);

        if (Duration <= 5) _cellSeconds = 0.25;
        else if (Duration <= 15) _cellSeconds = 0.5;
        else if (Duration <= 45) _cellSeconds = 1.0;
        else if (Duration <= 120) _cellSeconds = 2.0;
        else _cellSeconds = 5.0;
        // At most 240 visual buckets per skill; raw combat events stay untouched.
        _cellSeconds = Math.Max(_cellSeconds, Math.Ceiling(Duration / 240.0 * 4) / 4.0);
    }

    private double TimeToX(double t) => ChartLeft + t * _pxPerSecond;
    private double ChartContentWidth => ChartLeft + Duration * _pxPerSecond + ChartRight;

    // ---------- Zoom ----------

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        e.Handled = true; // Do not also vertically scroll the parent or fire nested zoom handlers.
        if (_isPreparing) return;

        double factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
        double newZoom = Math.Clamp(_zoom * factor, 0.35, 10.0);
        if (Math.Abs(newZoom - _zoom) < 0.001) return;

        var viewport = this.FindControl<ScrollViewer>("ChartScroll")!;
        var canvas = this.FindControl<Canvas>("ChartCanvas")!;
        var lanesViewport = this.FindControl<ScrollViewer>("LaneScroll");
        double viewportX;
        double anchorTime;
        if (_zoomDebounce.IsEnabled)
        {
            // Geometry still depicts the previous scale until redraw: reuse the original anchor.
            anchorTime = _zoomAnchorTime;
            viewportX = _zoomAnchorViewportX;
        }
        else if (ReferenceEquals(sender, lanesViewport) && lanesViewport is not null)
        {
            double laneX = Math.Clamp(e.GetPosition(lanesViewport).X, 0, lanesViewport.Viewport.Width);
            anchorTime = Math.Clamp((lanesViewport.Offset.X + laneX) / _pxPerSecond, 0, Duration);
            viewportX = laneX + ChartLeft;
        }
        else
        {
            viewportX = e.GetPosition(viewport).X;
            double canvasX = e.GetPosition(canvas).X;
            if (!double.IsFinite(canvasX)) canvasX = viewport.Offset.X + viewportX;
            anchorTime = Math.Clamp((canvasX - ChartLeft) / _pxPerSecond, 0, Duration);
        }

        double oldScale = _pxPerSecond;
        _zoom = newZoom;
        ComputeScale();
        if (Math.Abs(oldScale - _pxPerSecond) < 0.001) return;
        UpdateModeUi();
        ScheduleZoomRefresh(anchorTime, viewportX);
    }

    private void ScheduleZoomRefresh(double anchorTime, double viewportX)
    {
        _zoomAnchorTime = anchorTime;
        _zoomAnchorViewportX = viewportX;
        _zoomDebounce.Stop();
        _zoomDebounce.Start();
    }

    private void RenderPendingZoom()
    {
        if (_isPreparing || !IsVisible) return;
        BuildLanes();
        DrawChart();
        double anchorTime = _zoomAnchorTime;
        double viewportX = _zoomAnchorViewportX;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible) return;
            var chartScroll = this.FindControl<ScrollViewer>("ChartScroll")!;
            double max = Math.Max(0, chartScroll.Extent.Width - chartScroll.Viewport.Width);
            double next = Math.Clamp(TimeToX(anchorTime) - viewportX, 0, max);
            chartScroll.Offset = chartScroll.Offset.WithX(next);
            var lanes = this.FindControl<ScrollViewer>("LaneScroll");
            if (lanes is not null)
                lanes.Offset = lanes.Offset.WithX(Math.Clamp(next, 0,
                    Math.Max(0, lanes.Extent.Width - lanes.Viewport.Width)));
        }, DispatcherPriority.Loaded);
    }

    private double TickStep()
    {
        double requested = Math.Max(0.25, 78.0 / _pxPerSecond);
        // Avoid generating thousands of labels for long fights.
        requested = Math.Max(requested, Duration / 400.0);
        foreach (double step in new[] { .25, .5, 1.0, 2.0, 5.0, 10.0, 15.0, 30.0, 60.0, 120.0, 300.0, 600.0, 1200.0, 3600.0 })
            if (step >= requested) return step;
        return Math.Ceiling(requested / 3600) * 3600;
    }

    private double[] Series(int playerIndex)
    {
        var key = (playerIndex, _mode);
        if (!_seriesCache.TryGetValue(key, out var values))
            _seriesCache[key] = values = Meter.Series(_snapshot.Players[playerIndex], _mode, _snapshot.Duration);
        return values;
    }

    // ---------- Vezérlők ----------

    private void ModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<MeterMode>(tag, out var mode) && mode != _mode)
        {
            _mode = mode;
            _lastTooltipTime = double.NaN;
            UpdateModeUi();
            _ = PrepareAndDrawAsync();
        }
    }

    private void UpdateModeUi()
    {
        SetModeActive("ModeDps", MeterMode.Dps);
        SetModeActive("ModeDamage", MeterMode.Damage);
        SetModeActive("ModeHps", MeterMode.Hps);
        SetModeActive("ModeHeal", MeterMode.Heal);
        SetModeActive("ModeTaken", MeterMode.DamageTaken);
        this.FindControl<TextBlock>("ChartTitle")!.Text = _mode switch
        {
            MeterMode.Dps => "DPS Over Time",
            MeterMode.Hps => "HPS Over Time",
            MeterMode.Damage => "Total Damage Over Time",
            MeterMode.DamageTaken => "Damage Taken Over Time",
            _ => "Total Healing Over Time",
        };
        this.FindControl<TextBlock>("ZoomText")!.Text = $"Zoom ×{_zoom:0.0}";
        this.FindControl<TextBlock>("ChartMeta")!.Text =
            _mode == MeterMode.DamageTaken &&
            !_snapshot.Players.Any(p => p.IncomingDamageEvents.Count > 0)
                ? "No incoming-hit timeline in this saved fight (old log / totals only)"
                : $"{_active.Count(x => x)} players · wheel to zoom · click for events";
    }

    private void SetModeActive(string name, MeterMode m) =>
        this.FindControl<Button>(name)!.Classes.Set("primary", m == _mode);

    private void BuildChips()
    {
        var host = this.FindControl<WrapPanel>("PlayerChips")!;
        host.Children.Clear();

        for (int i = 0; i < _snapshot.Players.Count; i++)
        {
            var p = _snapshot.Players[i];
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
            };
            content.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = CurveBrush(i),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = p.Name,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var chip = new Button
            {
                Content = content,
                Tag = i,
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Classes.Add("tacticalTab");
            chip.Click += ChipClick;
            host.Children.Add(chip);
        }

        UpdateChips();
    }

    private void ChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int i } || i < 0 || i >= _active.Length)
            return;
        // Never leave the comparison plot empty.
        if (_active[i] && _active.Count(x => x) == 1)
            return;
        bool wasActive = _active[i];
        _active[i] = !wasActive;
        if (!wasActive)
            _selected = i; // Newly selected chip becomes the skill/event inspector.
        else if (_selected == i)
            _selected = Array.FindIndex(_active, x => x);
        if (_selected >= 0 && _selected < _snapshot.Players.Count)
            _player = _snapshot.Players[_selected];
        _cursorTime = null;
        _lastTooltipTime = double.NaN;
        UpdateChips();
        UpdateSelectionHeader();
        UpdateModeUi();
        _ = PrepareAndDrawAsync();
    }

    private void UpdateChips()
    {
        var host = this.FindControl<WrapPanel>("PlayerChips")!;
        foreach (var child in host.Children)
        {
            if (child is Button { Tag: int i } b)
                b.Classes.Set("primary", _active[i]);
        }
    }

    private void SoloPlayerClick(object? sender, RoutedEventArgs e)
    {
        if (_selected < 0 || _selected >= _active.Length) return;
        for (int i = 0; i < _active.Length; i++) _active[i] = i == _selected;
        UpdateChips();
        UpdateSelectionHeader();
        UpdateModeUi();
        _ = PrepareAndDrawAsync();
    }

    private void ZoomInClick(object? sender, RoutedEventArgs e) => ChangeZoom(1.25);
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => ChangeZoom(0.8);
    private void ZoomResetClick(object? sender, RoutedEventArgs e) => ChangeZoom(1.0, reset: true);

    private void ChangeZoom(double factor, bool reset = false)
    {
        if (_isPreparing) return;
        double nextZoom = Math.Clamp(reset ? 1.0 : _zoom * factor, 0.35, 10.0);
        if (Math.Abs(nextZoom - _zoom) < 0.001) return;
        var viewport = this.FindControl<ScrollViewer>("ChartScroll")!;
        double middleX = viewport.Viewport.Width / 2.0;
        double focusTime = Math.Clamp((viewport.Offset.X + middleX - ChartLeft) / _pxPerSecond, 0, Duration);
        _zoom = nextZoom;
        ComputeScale();
        UpdateModeUi();
        ScheduleZoomRefresh(focusTime, middleX);
    }

    private void LoadMoreSkillsClick(object? sender, RoutedEventArgs e)
    {
        if (_isPreparing) return;
        _visibleSkillCount += SkillPageSize;
        BuildLanes();
    }

    // ---------- Hover / kattintás ----------

    private void ChartPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPreparing) return;
        var c = this.FindControl<Canvas>("ChartCanvas");
        if (c is null) return;

        var pos = e.GetPosition(c);
        if (pos.X < ChartLeft || pos.X > ChartLeft + Duration * _pxPerSecond)
        {
            ToolTip.SetTip(c, null);
            return;
        }

        double t = (pos.X - ChartLeft) / _pxPerSecond;
        // Tooltips change only when their underlying one-second sample changes.
        int sampleTime = Math.Clamp((int)Math.Round(t), 0, (int)Math.Ceiling(Duration));
        int mask = 0;
        for (int i = 0; i < Math.Min(30, _active.Length); i++)
            if (_active[i]) mask |= 1 << i;
        if (Math.Abs(sampleTime - _lastTooltipTime) < .5 && mask == _lastTooltipActiveMask) return;
        _lastTooltipTime = sampleTime;
        _lastTooltipActiveMask = mask;
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(LocalizationService.Format("TimeValue", FormatTime(t)));

        foreach (int i in Enumerable.Range(0, _snapshot.Players.Count).Where(idx => _active[idx]))
        {
            var series = Series(i);
            int idx = Math.Clamp((int)Math.Round(t), 0, Math.Max(0, series.Length - 1));
            double val = series.Length > 0 ? series[idx] : 0;
            lines.AppendLine($"{_snapshot.Players[i].Name}: {Meter.Format(val, _mode)}");
        }

        ToolTip.SetTip(c, lines.ToString().TrimEnd());
    }

    private void ChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isPreparing) return;
        var c = this.FindControl<Canvas>("ChartCanvas");
        if (c is null) return;

        var pos = e.GetPosition(c);
        if (pos.X < ChartLeft || pos.X > ChartLeft + Duration * _pxPerSecond)
            return;

        double t = (pos.X - ChartLeft) / _pxPerSecond;
        ShowEventsAt(t);
        e.Handled = true;
    }

    private void ShowEventsAt(double timeSeconds)
    {
        _cursorTime = Math.Clamp(timeSeconds, 0, Duration);
        MoveCursorMarker();
        double window = Math.Max(0.3, _cellSeconds);
        double start = timeSeconds - window / 2;
        double end = timeSeconds + window / 2;
        // Binary search the prepared time-sorted index, avoiding a full replay scan
        // whenever the user clicks on a long combat timeline.
        int lo = 0, hi = _sortedEvents.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_sortedEvents[mid].Time < start) lo = mid + 1;
            else hi = mid;
        }
        int first = lo;
        while (lo < _sortedEvents.Length && _sortedEvents[lo].Time <= end) lo++;
        int total = lo - first;
        var timeTb = this.FindControl<TextBlock>("EventDetailTime");
        var listTb = this.FindControl<TextBlock>("EventDetailList");
        if (timeTb is null || listTb is null) return;
        timeTb.Text = total == 0
            ? LocalizationService.Format("TimeValue", FormatTime(timeSeconds))
            : LocalizationService.Format("TimelineEventHeader", FormatTime(timeSeconds), window / 2, total);
        if (total == 0) { listTb.Text = LocalizationService.Get("NoEventsWindow"); return; }
        const int cap = 100;
        var lines = new List<string>();
        for (int i = first; i < Math.Min(lo, first + cap); i++)
        {
            var ev = _sortedEvents[i];
            if (_mode == MeterMode.DamageTaken)
                lines.Add($"{FormatTime(ev.Time)}  ·  Damage received  ·  {Meter.Num(ev.Amount)}");
            else
            {
                string suffix = ev.Crit ? "  CRIT" : ev.IsHeal ? "  HEAL" : "";
                lines.Add($"{FormatTime(ev.Time)}  ·  {ev.Skill}  ·  {Meter.Num(ev.Amount)}{suffix}");
            }
        }
        if (total > cap) lines.Add($"… +{total - cap} events");
        listTb.Text = string.Join("\n", lines);
    }

    private void HideEventDetail()
    {
        this.FindControl<TextBlock>("EventDetailTime")!.Text = _mode == MeterMode.DamageTaken
            ? "Select an incoming hit" : "Select a skill event";
        this.FindControl<TextBlock>("EventDetailList")!.Text = string.Empty;
    }

    // ---------- Grafikon ----------

    private void DrawChart()
    {
        var c = this.FindControl<Canvas>("ChartCanvas");
        if (c is null) return;

        c.Children.Clear();
        _cursorMarker = null;
        c.Width = ChartContentWidth;
        c.Height = ChartHeight;

        // Üres terület is fogadjon kattintást / hovert
        c.Children.Add(new Rectangle
        {
            Width = ChartContentWidth,
            Height = ChartHeight,
            Fill = Brushes.Transparent,
        });

        double plotH = ChartHeight - ChartTop - ChartBottom;
        double plotW = Duration * _pxPerSecond;

        var activeIdx = Enumerable.Range(0, _snapshot.Players.Count)
            .Where(i => _active[i])
            .ToList();

        var seriesMap = activeIdx.ToDictionary(
            i => i,
            i => Series(i));

        double dataMax = seriesMap.Values
            .Where(v => v.Length > 0)
            .Select(v => v.Max())
            .DefaultIfEmpty(0)
            .Max();
        double yMax = Math.Max(1, dataMax * 1.1);

        var gridBrush = Brush.Parse("#29445A");
        var textBrush = Brush.Parse("#8EA7BA");

        // Y rács
        for (int g = 0; g <= 4; g++)
        {
            double y = ChartTop + plotH - plotH * g / 4.0;
            c.Children.Add(new Line
            {
                StartPoint = new Point(ChartLeft, y),
                EndPoint = new Point(ChartLeft + plotW, y),
                Stroke = gridBrush,
                StrokeThickness = 1,
            });

            var yLabel = new TextBlock
            {
                Text = Meter.Format(yMax * g / 4.0, _mode),
                FontSize = 11,
                Foreground = textBrush,
            };
            Canvas.SetLeft(yLabel, 4);
            Canvas.SetTop(yLabel, y - 8);
            c.Children.Add(yLabel);
        }

        // X tengely
        double step = TickStep();

        for (double s = 0; s <= Duration + 0.001; s += step)
        {
            double x = TimeToX(s);
            c.Children.Add(new Line
            {
                StartPoint = new Point(x, ChartTop + plotH),
                EndPoint = new Point(x, ChartTop + plotH + 4),
                Stroke = gridBrush,
                StrokeThickness = 1,
            });

            var xLabel = new TextBlock
            {
                Text = FormatTime(s),
                FontSize = 10,
                Foreground = textBrush,
            };
            Canvas.SetLeft(xLabel, x - 12);
            Canvas.SetTop(xLabel, ChartTop + plotH + 6);
            c.Children.Add(xLabel);
        }

        // Görbék
        foreach (var i in activeIdx.OrderBy(i => i == _selected ? 1 : 0))
        {
            var data = seriesMap[i];
            if (data.Length < 2) continue;

            var p = _snapshot.Players[i];
            bool isSel = i == _selected;

            var line = new Polyline
            {
                Stroke = CurveBrush(i),
                StrokeThickness = isSel ? 2.6 : 1.5,
                Opacity = isSel ? 1.0 : 0.75,
            };

            // Display-only min/max envelope. Never modifies Meter.Series or combat history.
            int limit = Math.Min(data.Length, (int)Math.Ceiling(Duration) + 1);
            int stride = Math.Max(1, (int)Math.Ceiling(limit / 1400.0));
            for (int first = 0; first < limit; first += stride)
            {
                int last = Math.Min(limit, first + stride);
                int minAt = first, maxAt = first;
                for (int k = first + 1; k < last; k++)
                {
                    if (data[k] < data[minAt]) minAt = k;
                    if (data[k] > data[maxAt]) maxAt = k;
                }
                void Add(int k)
                {
                    line.Points.Add(new Point(TimeToX(k),
                        ChartTop + plotH - data[k] / yMax * plotH));
                }
                if (minAt < maxAt) { Add(minAt); if (maxAt != minAt) Add(maxAt); }
                else { Add(maxAt); if (minAt != maxAt) Add(minAt); }
                if (last - 1 != minAt && last - 1 != maxAt) Add(last - 1);
            }

            c.Children.Add(line);
        }

        // Create once on every full chart redraw; subsequent clicks only move it.
        _cursorMarker = new Line
        {
            Stroke = Brush.Parse("#45DFFF"),
            StrokeThickness = 1.5,
            Opacity = 0.8,
            IsVisible = _cursorTime is not null,
            IsHitTestVisible = false,
        };
        c.Children.Add(_cursorMarker);
        MoveCursorMarker();

        // Show peak annotation only for a single plotted player, never for comparisons.
        if (activeIdx.Count != 1) return;
        if (!seriesMap.TryGetValue(_selected, out var selData) || selData.Length < 2)
            return;

        double selMax = selData.Max();
        if (selMax <= 0) return;

        int peak = Array.IndexOf(selData, selMax);
        double px = TimeToX(peak);
        double py = ChartTop + plotH - selData[peak] / yMax * plotH;

        var marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brush.Parse("#FF9B52"),
        };
        Canvas.SetLeft(marker, px - 4);
        Canvas.SetTop(marker, py - 4);
        c.Children.Add(marker);

        bool cumulative = _mode is MeterMode.Damage or MeterMode.Heal or MeterMode.DamageTaken;
        string peakText = cumulative
            ? LocalizationService.Format("FinalTotal", Meter.Format(selMax, _mode))
            : LocalizationService.Format("PeakAt", Meter.Format(selMax, _mode), FormatTime(peak));

        var peakLabel = new TextBlock
        {
            Text = peakText,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#EAF7FF"),
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(peakLabel, px + 8);
        Canvas.SetTop(peakLabel, Math.Max(0, py - 22));
        c.Children.Add(peakLabel);
    }

    private void MoveCursorMarker()
    {
        if (_cursorMarker is null) return;
        _cursorMarker.IsVisible = _cursorTime is not null;
        if (_cursorTime is not double time) return;
        double x = TimeToX(Math.Clamp(time, 0, Duration));
        _cursorMarker.StartPoint = new Point(x, ChartTop);
        _cursorMarker.EndPoint = new Point(x, ChartHeight - ChartBottom);
    }

    // ---------- Skill sávok ----------

    private void BuildLanes()
    {
        var labels = this.FindControl<StackPanel>("LaneLabels")!;
        var ticks = this.FindControl<StackPanel>("LaneTicks")!;
        labels.Children.Clear();
        ticks.Children.Clear();

        this.FindControl<TextBlock>("LanesTitle")!.Text = $"{_player.Name} · ×{_zoom:0.0}";
        this.FindControl<TextBlock>("LaneHeadingText")!.Text =
            _mode == MeterMode.DamageTaken ? "Incoming hit timeline" : "Skill Timeline";

        double cellWidth = _cellSeconds * _pxPerSecond;
        int cellCount = Math.Max(1, (int)Math.Ceiling(Duration / _cellSeconds));
        double trackWidth = cellCount * cellWidth;

        labels.Children.Add(new Border { Height = RulerHeight, Width = LabelWidth });
        ticks.Children.Add(BuildRuler(trackWidth));

        var lanes = _skillCache
            .OrderBy(g => g.Value.Count == 0 ? double.PositiveInfinity : g.Value[0].Time)
            .ToList();

        if (lanes.Count == 0)
        {
            this.FindControl<TextBlock>("LaneCountLabel")!.Text = _mode == MeterMode.DamageTaken
                ? "No incoming hit events" : "0 skills";
            this.FindControl<Button>("LoadMoreSkillsButton")!.IsVisible = false;
            labels.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("NoRecordedEvents"),
                Height = LaneHeight,
                Foreground = Brush.Parse("#71879B"),
            });
            ticks.Children.Add(new Canvas { Width = trackWidth, Height = LaneHeight });
            return;
        }

        int visible = Math.Min(lanes.Count, _visibleSkillCount);
        this.FindControl<TextBlock>("LaneCountLabel")!.Text = _mode == MeterMode.DamageTaken
            ? $"{_player.IncomingDamageEvents.Count} hits" : $"{visible} / {lanes.Count} skills";
        this.FindControl<Button>("LoadMoreSkillsButton")!.IsVisible =
            _mode != MeterMode.DamageTaken && visible < lanes.Count;

        foreach (var lane in lanes.Take(visible))
        {
            bool isHeal = lane.Value[0].IsHeal;
            IBrush brush = isHeal ? Visuals.Heal : Visuals.ForText(lane.Key);
            string? icon = lane.Value
                .Select(e => e.SkillIcon)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            labels.Children.Add(BuildLabel(lane.Key, brush, icon));
            ticks.Children.Add(BuildLaneTrack(_laneBuckets[lane.Key], cellWidth, brush));
        }
    }

    private Control BuildLabel(string text, IBrush brush, string? iconName)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Height = LaneHeight,
            Width = LabelWidth,
        };

        var iconHost = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = brush,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text.Length > 0 ? text[..1].ToUpperInvariant() : "?",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var bitmap = SkillIconLoader.Load(iconName);
        if (bitmap is not null)
        {
            iconHost.Child = new Image
            {
                Source = bitmap,
                Width = 20,
                Height = 20,
                Stretch = Stretch.UniformToFill,
            };
        }

        panel.Children.Add(iconHost);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Width = LabelWidth - 36,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return panel;
    }

    private Control BuildLaneTrack(int[] counts, double cellWidth, IBrush brush)
    {
        int cellCount = counts.Length;
        double trackWidth = cellCount * cellWidth;
        var track = new Canvas { Width = trackWidth, Height = LaneHeight };
        track.Children.Add(new Rectangle
        {
            Width = trackWidth,
            Height = LaneHeight,
            Fill = Brushes.Transparent,
        });

        // A single hit test per skill row; each visual segment is inert.
        // Keeps precise event inspection while avoiding thousands of controls/handlers.
        track.PointerPressed += (_, e) =>
        {
            var pos = e.GetPosition(track);
            double t = pos.X / _pxPerSecond;
            if (t >= 0 && t <= Duration)
            {
                ShowEventsAt(t);
                e.Handled = true;
            }
        };

        // Preserve the old per-bucket hit-count tooltip without one Tooltip/handler per cell.
        int hoveredBucket = -1;
        track.PointerMoved += (_, e) =>
        {
            int bucket = Math.Clamp((int)(e.GetPosition(track).X / cellWidth), 0, cellCount - 1);
            if (bucket == hoveredBucket) return;
            hoveredBucket = bucket;
            double start = bucket * _cellSeconds;
            ToolTip.SetTip(track, LocalizationService.Format(
                "TimelineHitTooltip", FormatTime(start), FormatTime(start + _cellSeconds), counts[bucket]));
        };

        // The full source buckets are preserved; only render at most 48 groups.
        // On long fights this limits the UI to <= 18 * 48 visible cells, not 18 * 240.
        const int maxRenderedGroups = 48;
        int stride = Math.Max(1, (int)Math.Ceiling(cellCount / (double)maxRenderedGroups));
        int peak = Math.Max(1, counts.Max());
        for (int first = 0; first < cellCount; first += stride)
        {
            int last = Math.Min(cellCount, first + stride);
            int hits = 0;
            for (int i = first; i < last; i++) hits += counts[i];
            if (hits == 0) continue;

            double strength = Math.Min(1.0, hits / (double)(peak * (last - first)));
            var block = new Border
            {
                Width = Math.Max(2, (last - first) * cellWidth - 1),
                Height = LaneHeight - 6,
                CornerRadius = new CornerRadius(3),
                Background = brush,
                Opacity = 0.38 + 0.62 * strength,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(block, first * cellWidth);
            Canvas.SetTop(block, 3);
            track.Children.Add(block);
        }
        return track;
    }

    private Control BuildRuler(double trackWidth)
    {
        var ruler = new Canvas { Width = trackWidth, Height = RulerHeight };
        var tickBrush = Brush.Parse("#35536B");

        double step = TickStep();

        for (double s = 0; s <= Duration + 0.001; s += step)
        {
            double x = s * _pxPerSecond;

            ruler.Children.Add(new Line
            {
                StartPoint = new Point(x, RulerHeight - 5),
                EndPoint = new Point(x, RulerHeight),
                Stroke = tickBrush,
                StrokeThickness = 1,
            });

            var label = new TextBlock
            {
                Text = FormatTime(s),
                FontSize = 10,
                Foreground = Brush.Parse("#8EA7BA"),
            };
            Canvas.SetLeft(label, x - 10);
            Canvas.SetTop(label, 0);
            ruler.Children.Add(label);
        }

        return ruler;
    }

    // ---------- Segédek ----------

    private static string FormatTime(double seconds)
    {
        if (seconds < 60)
            return $"{seconds:0.##}s";
        int total = (int)seconds;
        return $"{total / 60:00}:{total % 60:00}";
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        if (_player is null || _snapshot is null) return;
        UpdateSelectionHeader();
        UpdateModeUi();
        if (!_isPreparing) { BuildLanes(); DrawChart(); }
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
