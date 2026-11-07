using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

/// <summary>Offline visual HUD layout editor. Does not query Phaseward or combat events.</summary>
public partial class OverlayStudioWindow : Window
{
    private const double PreviewWidth = 640;
    private const double PreviewHeight = 360;
    private PixelRect _screen = new(0, 0, 1920, 1080);
    private double _screenScale = 1;
    private StudioLayout _stage = new();
    private readonly Dictionary<string, Border> _previews = new(StringComparer.Ordinal);
    private Canvas _canvas = null!;
    private string _selectedKey = OverlayStudioStore.Dps;
    private bool _updatingUi;
    private readonly Dictionary<string, OverlayStudioPlacementWindow> _screenEditors = new(StringComparer.Ordinal);
    private bool _syncingScreenEditors;

    private string? _dragKey;
    private bool _resizing;
    private Point _startPointer;
    private StudioWidget? _dragStart;

    public OverlayStudioWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _canvas = this.FindControl<Canvas>("PreviewCanvas")!;
        _canvas.PointerMoved += PreviewMoved;
        _canvas.PointerReleased += PreviewReleased;
        _canvas.PointerCaptureLost += (_, _) => EndDrag();
        Opened += (_, _) => Initialize();
    }

    private void Initialize()
    {
        var primary = Screens.Primary;
        if (primary is not null)
        {
            _screen = primary.WorkingArea;
            _screenScale = Math.Max(.25, primary.Scaling);
        }
        this.FindControl<TextBlock>("ScreenLabel")!.Text =
            $"{_screen.Width} × {_screen.Height} · {_screenScale:0.##}x";
        _stage = OverlayStudioStore.Snapshot();
        foreach (string key in OverlayStudioStore.Keys)
        {
            if (!_stage.Widgets.TryGetValue(key, out var widget))
            {
                OverlayPlacement? last = CompanionSettings.GetOverlayPlacement(key);
                widget = Default(key);
                if (last is not null)
                {
                    widget.X = last.X;
                    widget.Y = last.Y;
                    widget.Width = last.Width;
                    widget.Height = last.Height;
                }
                _stage.Widgets[key] = widget;
            }
            // Live repositioning wins over stale saved Studio geometry.
            if (key == OverlayStudioStore.BossStatus &&
                CompanionSettings.GetOverlayPlacement(key) is OverlayPlacement currentPlace)
            {
                widget.X = currentPlace.X; widget.Y = currentPlace.Y;
                widget.Width = currentPlace.Width; widget.Height = currentPlace.Height;
            }
            FitToScreen(widget, key);
        }
        DrawPreviews();
        this.FindControl<ComboBox>("WidgetSelect")!.SelectedIndex = 0;
        UpdateControls();
        OpenScreenEditors();
    }

    private void OpenScreenEditors()
    {
        foreach (string key in OverlayStudioStore.Keys)
        {
            if (_screenEditors.ContainsKey(key)) continue;
            var editor = new OverlayStudioPlacementWindow(key, ScreenEditorChanged);
            _screenEditors.Add(key, editor);
            editor.Apply(_stage.Widgets[key]);
            editor.Show();
        }
        // The control window stays above the editable outlines, not behind them.
        Topmost = true;
        Activate();
    }

    private void ScreenEditorChanged(string key, PixelPoint position, double width, double height)
    {
        if (_syncingScreenEditors || !_stage.Widgets.TryGetValue(key, out var widget)) return;
        widget.X = position.X;
        widget.Y = position.Y;
        widget.Width = width;
        widget.Height = height;
        FitToScreen(widget, key);
        RefreshCard(key);
        UpdatePositionLabel();
        // Snap the real editor back inside the usable screen on out-of-bounds drags.
        SyncScreenEditors();
    }

    private void SyncScreenEditors()
    {
        _syncingScreenEditors = true;
        try
        {
            foreach (var pair in _screenEditors)
                if (_stage.Widgets.TryGetValue(pair.Key, out var widget)) pair.Value.Apply(widget);
        }
        finally { _syncingScreenEditors = false; }
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var editor in _screenEditors.Values) editor.Close();
        _screenEditors.Clear();
        base.OnClosed(e);
    }

    private StudioWidget Default(string key)
    {
        bool dps = key == OverlayStudioStore.Dps;
        bool phaseward = key == OverlayStudioStore.Phaseward;
        bool bossStatus = key == OverlayStudioStore.BossStatus;
        double width = bossStatus ? 328 : dps ? 328 : phaseward ? 520 : 360;
        double height = bossStatus ? 168 : dps ? 408 : phaseward ? 340 : 310;
        return new StudioWidget
        {
            X = _screen.X + (dps ? 28 : bossStatus ? 28 : phaseward ? (_screen.Width - 550) : 28),
            Y = _screen.Y + (dps ? 45 : bossStatus ? 470 : phaseward ? 60 : 500),
            Width = width, Height = height,
        };
    }

    private static string Label(string key) => key switch
    {
        OverlayStudioStore.Dps => "DPS METER",
        OverlayStudioStore.BossStatus => "BOSS STATUS",
        OverlayStudioStore.Phaseward => "PHASEWARD",
        _ => "EVENT TIMERS",
    };

    private StudioWidget Current => _stage.Widgets[_selectedKey];

    private void DrawPreviews()
    {
        _canvas.Children.Clear();
        _previews.Clear();
        foreach (string key in OverlayStudioStore.Keys)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1.4),
                BorderBrush = Brush.Parse(key == OverlayStudioStore.Phaseward ? "#C579FF" : "#45DFFF"),
                Background = Brush.Parse(key == OverlayStudioStore.Phaseward ? "#B432203B" : "#BA17304A"),
                ClipToBounds = true,
            };
            var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            body.Children.Add(new TextBlock
            {
                Text = Label(key), FontWeight = FontWeight.SemiBold, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                IsHitTestVisible = false,
            });
            var handle = new Border
            {
                Width = 18, Height = 18,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brush.Parse("#AE45DFFF"),
                CornerRadius = new CornerRadius(3),
                Cursor = new Cursor(StandardCursorType.BottomRightCorner),
                Child = new TextBlock { Text = "↘", Foreground = Brushes.Black, FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center },
            };
            Grid.SetRow(handle, 1);
            body.Children.Add(handle);
            card.Child = body;
            string captured = key;
            card.PointerPressed += (_, e) => BeginDrag(captured, e, resize: false);
            handle.PointerPressed += (_, e) => BeginDrag(captured, e, resize: true);
            _previews[key] = card;
            _canvas.Children.Add(card);
            RefreshCard(key);
        }
    }

    private void RefreshCard(string key)
    {
        var card = _previews[key];
        var widget = _stage.Widgets[key];
        FitToScreen(widget, key);
        double x = (widget.X - _screen.X) * PreviewWidth / Math.Max(1, _screen.Width);
        double y = (widget.Y - _screen.Y) * PreviewHeight / Math.Max(1, _screen.Height);
        double w = widget.Width * _screenScale * PreviewWidth / Math.Max(1, _screen.Width);
        double h = widget.Height * _screenScale * PreviewHeight / Math.Max(1, _screen.Height);
        card.Width = Math.Max(35, w);
        card.Height = Math.Max(26, h);
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
        card.Opacity = widget.Visible ? widget.Opacity : .23;
        card.BorderThickness = new Thickness(key == _selectedKey ? 2.8 : 1.4);
    }

    private void BeginDrag(string key, PointerPressedEventArgs e, bool resize)
    {
        if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
        _selectedKey = key;
        var select = this.FindControl<ComboBox>("WidgetSelect")!;
        foreach (ComboBoxItem item in select.Items.OfType<ComboBoxItem>())
            if ((string?)item.Tag == key) select.SelectedItem = item;
        UpdateControls();
        if (Current.Locked) return;
        _dragKey = key;
        _resizing = resize;
        _startPointer = e.GetPosition(_canvas);
        _dragStart = Current.Copy();
        e.Pointer.Capture(_canvas);
        e.Handled = true;
    }

    private void PreviewMoved(object? sender, PointerEventArgs e)
    {
        if (_dragKey is null || _dragStart is null) return;
        Point point = e.GetPosition(_canvas);
        double dx = (point.X - _startPointer.X) * _screen.Width / PreviewWidth;
        double dy = (point.Y - _startPointer.Y) * _screen.Height / PreviewHeight;
        var widget = _stage.Widgets[_dragKey];
        if (_resizing)
        {
            widget.Width = Math.Max(MinWidth(_dragKey), _dragStart.Width + dx / _screenScale);
            widget.Height = Math.Max(MinHeight(_dragKey), _dragStart.Height + dy / _screenScale);
        }
        else
        {
            widget.X = _dragStart.X + (int)Math.Round(dx);
            widget.Y = _dragStart.Y + (int)Math.Round(dy);
        }
        FitToScreen(widget, _dragKey);
        RefreshCard(_dragKey);
        UpdatePositionLabel();
        SyncScreenEditors();
        e.Handled = true;
    }

    private void PreviewReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndDrag();
        e.Pointer.Capture(null);
    }

    private void EndDrag()
    {
        _dragKey = null;
        _dragStart = null;
    }

    private static double MinWidth(string key) => key == OverlayStudioStore.Dps ? 280 : key == OverlayStudioStore.BossStatus ? 260 : 360;
    private static double MinHeight(string key) => key == OverlayStudioStore.Dps ? 120 : key == OverlayStudioStore.BossStatus ? 125 : 220;

    private void FitToScreen(StudioWidget w, string key)
    {
        w.Opacity = double.IsFinite(w.Opacity) ? Math.Clamp(w.Opacity, .35, 1) : 1;
        double maxW = key == OverlayStudioStore.Dps ? 440 : key == OverlayStudioStore.BossStatus ? 480 : key == OverlayStudioStore.Phaseward ? 760 : 650;
        double maxH = key == OverlayStudioStore.Dps ? 580 : key == OverlayStudioStore.BossStatus ? 260 : 650;
        w.Width = double.IsFinite(w.Width) ? Math.Clamp(w.Width, MinWidth(key), Math.Max(MinWidth(key), Math.Min(maxW, _screen.Width / _screenScale))) : MinWidth(key);
        w.Height = double.IsFinite(w.Height) ? Math.Clamp(w.Height, MinHeight(key), Math.Max(MinHeight(key), Math.Min(maxH, _screen.Height / _screenScale))) : MinHeight(key);
        w.X = Math.Clamp(w.X, _screen.X, _screen.X + Math.Max(0, _screen.Width - (int)Math.Ceiling(w.Width * _screenScale)));
        w.Y = Math.Clamp(w.Y, _screen.Y, _screen.Y + Math.Max(0, _screen.Height - (int)Math.Ceiling(w.Height * _screenScale)));
    }

    private void WidgetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is ComboBoxItem { Tag: string key } &&
            _stage.Widgets.ContainsKey(key))
        {
            _selectedKey = key;
            foreach (string item in OverlayStudioStore.Keys) RefreshCard(item);
            UpdateControls();
        }
    }

    private void UpdateControls()
    {
        if (!_stage.Widgets.TryGetValue(_selectedKey, out var w)) return;
        _updatingUi = true;
        this.FindControl<CheckBox>("WidgetVisible")!.IsChecked = w.Visible;
        this.FindControl<CheckBox>("WidgetLocked")!.IsChecked = w.Locked;
        this.FindControl<Slider>("WidgetOpacity")!.Value = w.Opacity;
        this.FindControl<TextBlock>("OpacityLabel")!.Text = $"{w.Opacity:P0}";
        _updatingUi = false;
        UpdatePositionLabel();
    }

    private void UpdatePositionLabel()
    {
        if (!_stage.Widgets.TryGetValue(_selectedKey, out var w)) return;
        this.FindControl<TextBlock>("PositionLabel")!.Text =
            $"X {w.X}, Y {w.Y} · {w.Width:0} × {w.Height:0} dp" +
            (w.Locked ? " · locked" : "");
    }

    private void SettingChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUi || !_stage.Widgets.ContainsKey(_selectedKey)) return;
        Current.Visible = this.FindControl<CheckBox>("WidgetVisible")!.IsChecked == true;
        Current.Locked = this.FindControl<CheckBox>("WidgetLocked")!.IsChecked == true;
        RefreshCard(_selectedKey);
        UpdatePositionLabel();
        SyncScreenEditors();
    }

    private void OpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingUi || !_stage.Widgets.ContainsKey(_selectedKey)) return;
        Current.Opacity = Math.Clamp(e.NewValue, .35, 1);
        this.FindControl<TextBlock>("OpacityLabel")!.Text = $"{Current.Opacity:P0}";
        RefreshCard(_selectedKey);
        SyncScreenEditors();
    }

    private void ApplyPreset(string type)
    {
        foreach (string key in OverlayStudioStore.Keys)
        {
            StudioWidget w = _stage.Widgets[key];
            var d = Default(key);
            w.X = d.X; w.Y = d.Y; w.Width = d.Width; w.Height = d.Height;
            w.Visible = type != "Minimal" || key == OverlayStudioStore.Dps;
            w.Locked = false;
            w.Opacity = type == "Compact" ? .85 : 1;
            if (type == "Compact")
            {
                w.Width *= .9;
                w.Height *= .85;
            }
            FitToScreen(w, key);
        }
        DrawPreviews();
        UpdateControls();
        SyncScreenEditors();
        Status($"{type} preset preview. Save to apply.");
    }

    private void MinimalClick(object? sender, RoutedEventArgs e) => ApplyPreset("Minimal");
    private void CombatClick(object? sender, RoutedEventArgs e) => ApplyPreset("Combat");
    private void CompactClick(object? sender, RoutedEventArgs e) => ApplyPreset("Compact");

    private void ResetClick(object? sender, RoutedEventArgs e) => ApplyPreset("Combat");

    private void SavePresetClick(object? sender, RoutedEventArgs e)
    {
        string name = this.FindControl<TextBox>("PresetName")!.Text?.Trim() ?? "";
        if (name.Length is < 1 or > 32) { Status("Please enter a preset name (1–32 characters)."); return; }
        try
        {
            _stage.Presets[name] = _stage.Widgets.ToDictionary(x => x.Key, x => x.Value.Copy(), StringComparer.Ordinal);
            OverlayStudioStore.SaveNamedPreset(name, _stage.Widgets);
            Status($"Saved preset '{name}'. The live HUD layout was not changed.");
        }
        catch (Exception ex) { Status($"Preset save failed: {ex.GetType().Name}"); }
    }

    private void LoadPresetClick(object? sender, RoutedEventArgs e)
    {
        string name = this.FindControl<TextBox>("PresetName")!.Text?.Trim() ?? "";
        var stored = OverlayStudioStore.Snapshot();
        if (!stored.Presets.TryGetValue(name, out var preset)) { Status("Preset not found."); return; }
        foreach (string key in OverlayStudioStore.Keys)
            if (preset.TryGetValue(key, out var value))
            {
                _stage.Widgets[key] = value.Copy();
                FitToScreen(_stage.Widgets[key], key);
            }
        DrawPreviews();
        UpdateControls();
        SyncScreenEditors();
        Status($"Loaded '{name}' for preview. Save to apply.");
    }

    private void SaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            foreach (string key in OverlayStudioStore.Keys)
                FitToScreen(_stage.Widgets[key], key);
            OverlayStudioStore.Commit(_stage);
            foreach (string key in OverlayStudioStore.Keys)
            {
                var w = _stage.Widgets[key];
                CompanionSettings.SetOverlayPlacement(key, new OverlayPlacement
                {
                    X = w.X, Y = w.Y, Width = w.Width, Height = w.Height,
                });
            }
            OverlayWindowManager.ApplyStudioLayout(_stage);
            Close();
        }
        catch (Exception ex)
        {
            Status($"Save failed ({ex.GetType().Name}). Layout may require repair.");
        }
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close();
    private void Status(string message) => this.FindControl<TextBlock>("StudioStatus")!.Text = message;
}
