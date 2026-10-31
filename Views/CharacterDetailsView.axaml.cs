using System;
using AION_2_Companion.ViewModels;
using AION_2_Companion.Core.Characters;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public partial class CharacterDetailsView : UserControl
{
    private readonly CharacterDetailsViewModel _viewModel = new();

    public CharacterDetailsView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        DetachedFromVisualTree += (_, _) => _viewModel.Dispose();
    }

    public event EventHandler? BackRequested;

    private void BackClick(object? sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void VerifyCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentIdentity is not CharacterIdentity identity ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;
        var dialog = new CharacterVerificationWindow(identity, _viewModel.ServerText, _viewModel.ProfileImageUrl);
        _ = dialog.ShowDialog(owner);
    }
}
