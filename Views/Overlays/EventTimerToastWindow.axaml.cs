namespace AION_2_Companion.Views.Overlays;

using System;
using AION_2_Companion.Core;
using AION_2_Companion.Core.EventTimers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

public partial class EventTimerToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public EventTimerToastWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _closeTimer.Tick += (_, _) => Close();
        Opened += (_, _) => PositionAtTopRight();
        Closed += (_, _) => _closeTimer.Stop();
    }

    public void ShowNotification(EventTimerNotification notification)
    {
        this.FindControl<TextBlock>("IconText")!.Text = notification.Snapshot.Definition.Icon;
        this.FindControl<TextBlock>("TitleText")!.Text =
            notification.Snapshot.Definition.LocalizedName(LocalizationService.CurrentLanguageCode);
        this.FindControl<TextBlock>("MessageText")!.Text = notification.AtStart
            ? LocalizationService.Get("EventTimerNotificationNow")
            : string.Format(LocalizationService.Get("EventTimerNotificationSoon"), notification.ThresholdMinutes);
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void PositionAtTopRight()
    {
        Screen? screen = Screens.Primary;
        if (screen is null)
            return;
        PixelRect area = screen.WorkingArea;
        Position = new PixelPoint(
            area.Right - (int)Math.Ceiling(Width * screen.Scaling) - 20,
            area.Y + 20);
    }

    private void CloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
