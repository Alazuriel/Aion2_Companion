namespace AION_2_Companion.Views.Overlays;

using System;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Integrity;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

public partial class IntegrityToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public IntegrityToastWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _closeTimer.Tick += (_, _) => Close();
        Opened += (_, _) => PositionAtTopRight();
        Closed += (_, _) => _closeTimer.Stop();
    }

    public void ShowStatus(IntegrityMonitorState state)
    {
        (string title, string message) = state switch
        {
            IntegrityMonitorState.RepairReady => ("IntegrityRepairReady", "IntegrityRepairReadyText"),
            IntegrityMonitorState.RepairFailed => ("IntegrityRepairFailed", "IntegrityRepairFailedText"),
            _ => ("IntegrityProblem", "IntegrityProblemText"),
        };
        this.FindControl<TextBlock>("TitleText")!.Text = LocalizationService.Get(title);
        this.FindControl<TextBlock>("MessageText")!.Text = LocalizationService.Get(message);
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
