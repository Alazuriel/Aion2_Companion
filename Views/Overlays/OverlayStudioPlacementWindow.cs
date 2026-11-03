using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

/// <summary>Editable screen-space outline. Never starts an overlay/Phaseward engine.</summary>
internal sealed class OverlayStudioPlacementWindow : Window
{
    private readonly string _key;
    private readonly Action<string, PixelPoint, double, double> _changed;
    private readonly Border _frame;
    private readonly DispatcherTimer? _demoTimer;
    private StackPanel? _demoRows;
    private TextBlock? _demoBadge;
    private double _demoSeconds = 17;
    private bool _applying;
    private bool _resizing;
    private Point _resizeOrigin;
    private double _startWidth, _startHeight;

    public OverlayStudioPlacementWindow(string key, Action<string, PixelPoint, double, double> changed)
    {
        _key = key;
        _changed = changed;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _frame = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = Brush.Parse(key == OverlayStudioStore.Phaseward ? "#DF99FF" : "#45DFFF"),
            Background = Brush.Parse("#AD0A1929"),
            Padding = new Thickness(14),
        };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var label = new TextBlock
        {
            Text = key switch
            {
                OverlayStudioStore.Dps => "DPS METER",
                OverlayStudioStore.Phaseward => "PHASEWARD · live HUD location",
                _ => "EVENT TIMERS"
            },
            FontSize = 15, FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            IsHitTestVisible = false,
        };
        body.Children.Add(label);
        var hint = new TextBlock
        {
            Text = "HÚZD IDE · DRAG TO POSITION",
            FontSize = 11, Foreground = Brush.Parse("#B0E4F7"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        Grid.SetRow(hint, 1);
        if (key == OverlayStudioStore.Phaseward)
        {
            // Editor-only visual sample; not a PhasewardEngine event or real alert.
            var demo = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0), Spacing = 6,
                IsHitTestVisible = false
            };
            _demoBadge = new TextBlock
            {
                FontSize = 10, FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#FFBD72"), TextAlignment = TextAlignment.Left,
            };
            _demoRows = new StackPanel { Spacing = 7, IsHitTestVisible = false };
            demo.Children.Add(_demoBadge);
            demo.Children.Add(_demoRows);
            Grid.SetRow(demo, 1);
            body.Children.Add(demo);
            _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _demoTimer.Tick += (_, _) => UpdateDemo();
            Opened += (_, _) => { UpdateDemo(); _demoTimer.Start(); };
            Closed += (_, _) => _demoTimer.Stop();
        }
        else
        {
            hint.Text = LocalizationService.Get("PwStudioDrag");
            body.Children.Add(hint);
        }
        var handle = new Border
        {
            Width = 38, Height = 34, CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brush.Parse("#DD36C9ED"),
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Child = new TextBlock
            {
                Text = "↘", FontSize = 22, Foreground = Brush.Parse("#091725"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            },
        };
        handle.PointerPressed += ResizePressed;
        Grid.SetRow(handle, 2);
        body.Children.Add(handle);
        _frame.Child = body;
        Content = _frame;
        _frame.PointerPressed += DragPressed;
        PointerMoved += ResizeMoved;
        PointerReleased += ResizeReleased;
        PointerCaptureLost += (_, _) => _resizing = false;
        PositionChanged += (_, _) => Publish();
        Resized += (_, _) => Publish();
    }

    private void UpdateDemo()
    {
        if (_demoRows is null || _demoBadge is null) return;
        _demoSeconds -= 0.25;
        if (_demoSeconds <= 0) _demoSeconds = 17;
        _demoBadge.Text = LocalizationService.Get("PwDemo");
        _demoRows.Children.Clear();
        // Real HUD row component, sample values only; never touches actual combat data.
        var examples = new[]
        {
            new PhasewardHudBar(LocalizationService.Get("PwDemoMechanic"), _demoSeconds,
                17, Color.Parse("#3CD8FF")),
            new PhasewardHudBar(LocalizationService.Get("PwDemoSkillTwo"), _demoSeconds + 10,
                38, Color.Parse("#FFC76E")),
            new PhasewardHudBar(LocalizationService.Get("PwDemoSkillThree"), _demoSeconds + 25,
                60, Color.Parse("#3CD8FF")),
            new PhasewardHudBar(LocalizationService.Get("PwDemoSkillFour"), _demoSeconds + 48,
                90, Color.Parse("#B88CFF"))
        };
        int count = PhasewardHudVisuals.VisibleRows(Math.Max(220, Bounds.Height) - 35, false, 4);
        for (int i = 0; i < Math.Min(count, examples.Length); i++)
            _demoRows.Children.Add(PhasewardHudVisuals.Row(examples[i], i == 0));
    }

    public void Apply(StudioWidget widget)
    {
        _applying = true;
        try
        {
            Width = widget.Width;
            Height = widget.Height;
            Position = new PixelPoint(widget.X, widget.Y);
            Opacity = widget.Visible ? Math.Clamp(widget.Opacity, 0.35, 1) : 0.43;
            _frame.BorderBrush = Brush.Parse(widget.Visible
            ? _key == OverlayStudioStore.Phaseward ? "#D49CFF" : "#45DFFF"
            : "#8B919D");
            _frame.IsEnabled = !widget.Locked;
        }
        finally { _applying = false; }
    }

    private void DragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_resizing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void ResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _resizing = true;
        _resizeOrigin = e.GetPosition(this);
        _startWidth = Width;
        _startHeight = Height;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void ResizeMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizing) return;
        var point = e.GetPosition(this);
        Width = Math.Clamp(_startWidth + point.X - _resizeOrigin.X, _key == OverlayStudioStore.Dps ? 280 : 360,
            _key == OverlayStudioStore.Dps ? 440 : _key == OverlayStudioStore.Phaseward ? 760 : 650);
        Height = Math.Clamp(_startHeight + point.Y - _resizeOrigin.Y, _key == OverlayStudioStore.Dps ? 120 : 220,
            _key == OverlayStudioStore.Dps ? 580 : 650);
        e.Handled = true;
    }

    private void ResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        e.Pointer.Capture(null);
        Publish();
        e.Handled = true;
    }

    private void Publish()
    {
        if (!_applying && IsVisible)
            _changed(_key, Position, Bounds.Width, Bounds.Height);
    }
}
