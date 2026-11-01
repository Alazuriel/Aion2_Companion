using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AION_2_Companion.Views.Controls;

/// <summary>
/// Displays full text, slowly scrolling horizontally only when it exceeds its available width.
/// Hover pauses movement; tooltip always contains the complete text.
/// </summary>
public sealed class MarqueeText : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarqueeText, string?>(nameof(Text));
    public static readonly StyledProperty<string> TextColorProperty =
        AvaloniaProperty.Register<MarqueeText, string>(nameof(TextColor), "#EAF6FF");
    public static readonly StyledProperty<double> TextSizeProperty =
        AvaloniaProperty.Register<MarqueeText, double>(nameof(TextSize), 12);
    public static readonly StyledProperty<bool> IsStrongProperty =
        AvaloniaProperty.Register<MarqueeText, bool>(nameof(IsStrong));
    public static readonly StyledProperty<bool> CenterTextProperty =
        AvaloniaProperty.Register<MarqueeText, bool>(nameof(CenterText));

    private readonly TextBlock _label;
    private readonly ScrollViewer _scroller;
    private readonly DispatcherTimer _timer;
    private bool _hovering;
    private int _holdTicks;

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string TextColor { get => GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    public double TextSize { get => GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }
    public bool IsStrong { get => GetValue(IsStrongProperty); set => SetValue(IsStrongProperty, value); }
    public bool CenterText { get => GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }

    static MarqueeText()
    {
        TextProperty.Changed.AddClassHandler<MarqueeText>((control, _) => control.UpdateText());
        TextColorProperty.Changed.AddClassHandler<MarqueeText>((control, _) => control.UpdateAppearance());
        TextSizeProperty.Changed.AddClassHandler<MarqueeText>((control, _) => control.UpdateAppearance());
        IsStrongProperty.Changed.AddClassHandler<MarqueeText>((control, _) => control.UpdateAppearance());
        CenterTextProperty.Changed.AddClassHandler<MarqueeText>((control, _) => control.UpdateAppearance());
    }

    public MarqueeText()
    {
        _label = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ClipToBounds = true,
            Content = _label
        };
        Content = _scroller;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Advance();
        _scroller.SizeChanged += (_, _) => ScheduleCheck();
        AttachedToVisualTree += (_, _) => ScheduleCheck();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        PointerEntered += (_, _) => _hovering = true;
        PointerExited += (_, _) => _hovering = false;
        UpdateText();
        UpdateAppearance();
    }

    private void UpdateText()
    {
        if (_label is null) return;
        _label.Text = Text ?? string.Empty;
        ToolTip.SetTip(this, Text ?? string.Empty);
        _scroller.Offset = default;
        _holdTicks = 20;
        ScheduleCheck();
    }

    private void UpdateAppearance()
    {
        if (_label is null) return;
        _label.FontSize = TextSize;
        _label.FontWeight = IsStrong ? FontWeight.Bold : FontWeight.Normal;
        _label.HorizontalAlignment = CenterText ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        try { _label.Foreground = new SolidColorBrush(Color.Parse(TextColor)); }
        catch (FormatException) { _label.Foreground = Brushes.White; }
        ScheduleCheck();
    }

    private void ScheduleCheck() => Dispatcher.UIThread.Post(CheckOverflow, DispatcherPriority.Loaded);

    private void CheckOverflow()
    {
        if (_scroller is null) return;
        if (_scroller.Extent.Width - _scroller.Viewport.Width > 1.5 && IsVisible)
            _timer.Start();
        else
        {
            _timer.Stop();
            _scroller.Offset = default;
        }
    }

    private void Advance()
    {
        if (_hovering || !IsVisible) return;
        double distance = _scroller.Extent.Width - _scroller.Viewport.Width;
        if (distance <= 1.5) { _timer.Stop(); _scroller.Offset = default; return; }
        if (_holdTicks > 0) { _holdTicks--; return; }
        double next = _scroller.Offset.X + 1;
        if (next >= distance)
        {
            // Show the end long enough to read it before returning to the beginning.
            if (_scroller.Offset.X >= distance - 0.5)
            {
                _scroller.Offset = default;
                _holdTicks = 24;
                return;
            }
            _holdTicks = 26;
            next = distance;
        }
        _scroller.Offset = new Vector(next, 0);
    }
}
