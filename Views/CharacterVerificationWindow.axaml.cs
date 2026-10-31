using AION_2_Companion.Core.Characters;
using AION_2_Companion.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public partial class CharacterVerificationWindow : Window
{
    private readonly CharacterVerificationViewModel _viewModel;

    public CharacterVerificationWindow(CharacterIdentity identity, string serverName, string? imageUrl)
    {
        InitializeComponent();
        _viewModel = new CharacterVerificationViewModel(identity, serverName, imageUrl);
        DataContext = _viewModel;
        Opened += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void TitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
    private void CloseClick(object? sender, RoutedEventArgs e) => Close();
}
