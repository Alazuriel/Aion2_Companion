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
    private bool _syncingScroll;

    public TimelineWindow() : this(new PlayerStat(), new MeterSnapshot()) { }

    public TimelineWindow(PlayerStat player, MeterSnapshot snapshot)
    {
        AvaloniaXamlLoader.Load(this);
        LocalizationService.LanguageChanged += LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LanguageChanged;

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
        _player = player;
        _snapshot = snapshot;
        _selected = Math.Max(0, snapshot.Players.ToList().IndexOf(player));
        _active = new bool[snapshot.Players.Count];
        if (_active.Length > 0)
            _active[Math.Min(_selected, _active.Length - 1)] = true;

        _mode = MeterMode.Dps;
        _zoom = 1.0;
        _cursorTime = null;

        this.FindControl<TextBlock>("TimelineTitle")!.Text = LocalizationService.Format(
            "TimelineTitleFormat", player.Name);
        this.FindControl<TextBlock>("TimelineSub")!.Text = LocalizationService.Format(
            "TimelineSubtitle", FormatTime(snapshot.Duration), snapshot.Players.Count);

        ComputeScale();
        BuildChips();
        BuildLanes();
        UpdateModeUi();
        DrawChart();
        HideEventDetail();
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
    }

    private double TimeToX(double t) => ChartLeft + t * _pxPerSecond;
    private double ChartContentWidth => ChartLeft + Duration * _pxPerSecond + ChartRight;

    // ---------- Zoom ----------

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        double factor = e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2;
        double newZoom = Math.Clamp(_zoom * factor, 0.35, 10.0);
        if (Math.Abs(newZoom - _zoom) < 0.001)
            return;

        _zoom = newZoom;
        ComputeScale();
        BuildLanes();
        UpdateModeUi();
        DrawChart();

        this.FindControl<TextBlock>("LanesTitle")!.Text = LocalizationService.Format(
            "SkillsTimeline", _player.Name, _zoom, _pxPerSecond);

        e.Handled = true;
    }

    // ---------- Vezérlők ----------

    private void ModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<MeterMode>(tag, out var mode))
        {
            _mode = mode;
            UpdateModeUi();
            DrawChart();
        }
    }

    private void UpdateModeUi()
    {
        SetModeActive("ModeDps", MeterMode.Dps);
        SetModeActive("ModeDamage", MeterMode.Damage);
        SetModeActive("ModeHps", MeterMode.Hps);
        SetModeActive("ModeHeal", MeterMode.Heal);

        this.FindControl<TextBlock>("ChartTitle")!.Text = _mode switch
        {
            MeterMode.Dps => LocalizationService.Format("TimelineChartDps", _zoom),
            MeterMode.Hps => LocalizationService.Format("TimelineChartHps", _zoom),
            MeterMode.Damage => LocalizationService.Format("TimelineChartDamage", _zoom),
            _ => LocalizationService.Format("TimelineChartHealing", _zoom),
        };
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
                Background = Visuals.ForId(p.ClassId),
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
            chip.Classes.Add("ui");
            chip.Click += ChipClick;
            host.Children.Add(chip);
        }

        UpdateChips();
    }

    private void ChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int i })
            return;

        bool othersOn = _active.Where((_, k) => k != i).Any(x => x);
        if (_active[i] && !othersOn)
            return;

        _active[i] = !_active[i];
        UpdateChips();
        DrawChart();
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

    // ---------- Hover / kattintás ----------

    private void ChartPointerMoved(object? sender, PointerEventArgs e)
    {
        var c = this.FindControl<Canvas>("ChartCanvas");
        if (c is null) return;

        var pos = e.GetPosition(c);
        if (pos.X < ChartLeft || pos.X > ChartLeft + Duration * _pxPerSecond)
        {
            ToolTip.SetTip(c, null);
            return;
        }

        double t = (pos.X - ChartLeft) / _pxPerSecond;
        var lines = new System.Text.StringBuilder();
        lines.AppendLine(LocalizationService.Format("TimeValue", FormatTime(t)));

        foreach (int i in Enumerable.Range(0, _snapshot.Players.Count).Where(idx => _active[idx]))
        {
            var series = Meter.Series(_snapshot.Players[i], _mode, _snapshot.Duration);
            int idx = Math.Clamp((int)Math.Round(t), 0, Math.Max(0, series.Length - 1));
            double val = series.Length > 0 ? series[idx] : 0;
            lines.AppendLine($"{_snapshot.Players[i].Name}: {Meter.Format(val, _mode)}");
        }

        ToolTip.SetTip(c, lines.ToString().TrimEnd());
    }

    private void ChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var c = this.FindControl<Canvas>("ChartCanvas");
        if (c is null) return;

        var pos = e.GetPosition(c);
        if (pos.X < ChartLeft || pos.X > ChartLeft + Duration * _pxPerSecond)
            return;

        double t = (pos.X - ChartLeft) / _pxPerSecond;
        ShowEventsAt(t);
    }

    private void ShowEventsAt(double timeSeconds)
    {
        _cursorTime = timeSeconds;
        DrawChart();

        double window = Math.Max(0.3, _cellSeconds);
        var hits = _player.Events
            .Where(ev => Math.Abs(ev.Time - timeSeconds) <= window / 2)
            .OrderBy(ev => ev.Time)
            .ToList();

        var panel = this.FindControl<Border>("EventDetailPanel");
        var timeTb = this.FindControl<TextBlock>("EventDetailTime");
        var listTb = this.FindControl<TextBlock>("EventDetailList");
        if (panel is null || timeTb is null || listTb is null)
            return;

        panel.IsVisible = true;

        if (hits.Count == 0)
        {
            timeTb.Text = LocalizationService.Format("TimeValue", FormatTime(timeSeconds));
            listTb.Text = LocalizationService.Get("NoEventsWindow");
            return;
        }

        timeTb.Text = LocalizationService.Format(
            "TimelineEventHeader", FormatTime(timeSeconds), window / 2, hits.Count);
        listTb.Text = string.Join("\n", hits.Select(ev =>
        {
            string crit = ev.Crit ? "  CRIT" : "";
            string heal = ev.IsHeal ? "  (heal)" : "";
            return $"{FormatTime(ev.Time)}  ·  {ev.Skill}  ·  {Meter.Num(ev.Amount)}{crit}{heal}";
        }));
    }

    private void HideEventDetail()
    {
        var panel = this.FindControl<Border>("EventDetailPanel");
        if (panel is not null)
            panel.IsVisible = false;
    }

    // ---------- Grafikon ----------

    private void DrawChart()
    {
        var c = this.FindControl<Canvas>("ChartCanvas");
        if (c is null) return;

        c.Children.Clear();
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
            i => Meter.Series(_snapshot.Players[i], _mode, _snapshot.Duration));

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
        double step = Duration <= 5 ? 0.5
            : Duration <= 15 ? 1
            : Duration <= 60 ? 5
            : 30;

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
                Stroke = Visuals.ForId(p.ClassId),
                StrokeThickness = isSel ? 2.6 : 1.5,
                Opacity = isSel ? 1.0 : 0.75,
            };

            for (int k = 0; k < data.Length; k++)
            {
                double t = k;
                if (t > Duration) break;
                double x = TimeToX(t);
                double y = ChartTop + plotH - data[k] / yMax * plotH;
                line.Points.Add(new Point(x, y));
            }

            c.Children.Add(line);
        }

        // Kurzor
        if (_cursorTime is double ct)
        {
            double cx = TimeToX(Math.Clamp(ct, 0, Duration));
            c.Children.Add(new Line
            {
                StartPoint = new Point(cx, ChartTop),
                EndPoint = new Point(cx, ChartTop + plotH),
                Stroke = Brush.Parse("#45DFFF"),
                StrokeThickness = 1.5,
                Opacity = 0.8,
            });
        }

        // Csúcs
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

        bool cumulative = _mode is MeterMode.Damage or MeterMode.Heal;
        string peakText = cumulative
            ? LocalizationService.Format("FinalTotal", Meter.Format(selMax, _mode))
            : LocalizationService.Format("PeakAt", Meter.Format(selMax, _mode), FormatTime(peak));

        var peakLabel = new TextBlock
        {
            Text = peakText,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#EAF7FF"),
        };
        Canvas.SetLeft(peakLabel, px + 8);
        Canvas.SetTop(peakLabel, Math.Max(0, py - 22));
        c.Children.Add(peakLabel);
    }

    // ---------- Skill sávok ----------

    private void BuildLanes()
    {
        var labels = this.FindControl<StackPanel>("LaneLabels")!;
        var ticks = this.FindControl<StackPanel>("LaneTicks")!;
        labels.Children.Clear();
        ticks.Children.Clear();

        this.FindControl<TextBlock>("LanesTitle")!.Text = LocalizationService.Format(
            "SkillsTimeline", _player.Name, _zoom, _pxPerSecond);

        double cellWidth = _cellSeconds * _pxPerSecond;
        int cellCount = Math.Max(1, (int)Math.Ceiling(Duration / _cellSeconds));
        double trackWidth = cellCount * cellWidth;

        labels.Children.Add(new Border { Height = RulerHeight, Width = LabelWidth });
        ticks.Children.Add(BuildRuler(trackWidth));

        var lanes = _player.Events
            .GroupBy(e => e.Skill)
            .OrderBy(g => g.Min(e => e.Time))
            .ToList();

        if (lanes.Count == 0)
        {
            labels.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("NoRecordedEvents"),
                Height = LaneHeight,
                Foreground = Brush.Parse("#71879B"),
            });
            ticks.Children.Add(new Canvas { Width = trackWidth, Height = LaneHeight });
            return;
        }

        foreach (var lane in lanes)
        {
            bool isHeal = lane.First().IsHeal;
            IBrush brush = isHeal ? Visuals.Heal : Visuals.ForText(lane.Key);
            string? icon = lane
                .Select(e => e.SkillIcon)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            labels.Children.Add(BuildLabel(lane.Key, brush, icon));
            ticks.Children.Add(BuildLaneTrack(lane, cellCount, cellWidth, brush));
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

    private Control BuildLaneTrack(
        IGrouping<string, SkillEvent> lane,
        int cellCount,
        double cellWidth,
        IBrush brush)
    {
        var counts = new int[cellCount];
        foreach (var ev in lane)
        {
            int cell = Math.Clamp((int)(ev.Time / _cellSeconds), 0, cellCount - 1);
            counts[cell]++;
        }

        int maxCount = Math.Max(1, counts.Max());
        double trackWidth = cellCount * cellWidth;

        var track = new Canvas { Width = trackWidth, Height = LaneHeight };

        track.Children.Add(new Rectangle
        {
            Width = trackWidth,
            Height = LaneHeight,
            Fill = Brushes.Transparent,
        });

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

        for (int i = 0; i < cellCount; i++)
        {
            if (counts[i] == 0) continue;

            double strength = (double)counts[i] / maxCount;
            double cellStart = i * _cellSeconds;

            var block = new Border
            {
                Width = Math.Max(3, cellWidth - 1),
                Height = LaneHeight - 6,
                CornerRadius = new CornerRadius(3),
                Background = brush,
                Opacity = 0.35 + 0.65 * strength,
                Cursor = new Cursor(StandardCursorType.Hand),
            };

            ToolTip.SetTip(block, LocalizationService.Format(
                "TimelineHitTooltip",
                FormatTime(cellStart),
                FormatTime(cellStart + _cellSeconds),
                counts[i]));

            int captured = i;
            block.PointerPressed += (_, e) =>
            {
                ShowEventsAt(captured * _cellSeconds + _cellSeconds / 2);
                e.Handled = true;
            };

            Canvas.SetLeft(block, cellStart * _pxPerSecond);
            Canvas.SetTop(block, 3);
            track.Children.Add(block);
        }

        return track;
    }

    private Control BuildRuler(double trackWidth)
    {
        var ruler = new Canvas { Width = trackWidth, Height = RulerHeight };
        var tickBrush = Brush.Parse("#35536B");

        double step = Duration <= 5 ? 0.5
            : Duration <= 15 ? 1
            : Duration <= 60 ? 5
            : 30;

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
        if (_player is null || _snapshot is null)
            return;

        this.FindControl<TextBlock>("TimelineTitle")!.Text = LocalizationService.Format(
            "TimelineTitleFormat", _player.Name);
        this.FindControl<TextBlock>("TimelineSub")!.Text = LocalizationService.Format(
            "TimelineSubtitle", FormatTime(_snapshot.Duration), _snapshot.Players.Count);
        BuildLanes();
        UpdateModeUi();
        DrawChart();
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
