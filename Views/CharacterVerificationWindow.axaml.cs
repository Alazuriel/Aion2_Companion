using AION_2_Companion.Core.Characters;
using AION_2_Companion.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public partial class CharacterVerificationWindow : Window
{
    public CharacterVerificationWindow()
    {
        InitializeComponent();
    }

    public CharacterVerificationWindow(CharacterIdentity identity, string serverName, string? imageUrl) : this()
    {
        var viewModel = new CharacterVerificationViewModel(identity, serverName, imageUrl);
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.InitializeAsync();
        Closed += (_, _) => viewModel.Dispose();
    }

    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
    private void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
