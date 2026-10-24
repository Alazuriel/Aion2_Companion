using AION_2_Companion.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AION_2_Companion.Views;

public partial class CloseBehaviorDialog : Window
{
    public CloseBehaviorDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void TrayClick(object? sender, RoutedEventArgs e) =>
        Close(MainWindowCloseAction.MinimizeToTray);

    private void ExitClick(object? sender, RoutedEventArgs e) =>
        Close(MainWindowCloseAction.ExitApplication);

    private void CancelClick(object? sender, RoutedEventArgs e) =>
        Close(null);
}
