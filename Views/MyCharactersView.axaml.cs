using System;
using AION_2_Companion.ViewModels;
using AION_2_Companion.Core.Characters;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public partial class MyCharactersView : UserControl
{
    private readonly MyCharactersViewModel _viewModel = new();

    public MyCharactersView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        DetachedFromVisualTree += (_, _) => _viewModel.Dispose();
    }

    public event EventHandler? OpenCharacterRequested;
    public event EventHandler? OpenSearchRequested;

    private void OpenCharacterClick(object? sender, RoutedEventArgs e) =>
        OpenCharacterRequested?.Invoke(this, EventArgs.Empty);

    private void SelectVerifiedCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VerifiedCharacterSummary character })
            _viewModel.SelectVerifiedCharacter(character);
    }

    private void OpenSearchClick(object? sender, RoutedEventArgs e) =>
        OpenSearchRequested?.Invoke(this, EventArgs.Empty);

    private void VerifyCharacterClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentIdentity is not CharacterIdentity identity ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;

        // A server selects the item. The desktop only opens the API-driven wizard.
        var dialog = new CharacterVerificationWindow(identity, _viewModel.ServerText, _viewModel.ProfileImageUrl);
        _ = dialog.ShowDialog(owner);
    }
}
