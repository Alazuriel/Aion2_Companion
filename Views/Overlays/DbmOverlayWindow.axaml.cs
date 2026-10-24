using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

public partial class DbmOverlayWindow : Window
{
    public DbmOverlayWindow()
    {
        AvaloniaXamlLoader.Load(this);
        OverlayPlacementTracker.Attach(this, "dbm-assistant");
    }

    private void CloseClick(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
