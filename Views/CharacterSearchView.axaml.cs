using System;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public partial class CharacterSearchView : UserControl
{
    private readonly CharacterSearchViewModel _viewModel = new();

    public CharacterSearchView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.OpenProfileRequested += ViewModelOpenProfileRequested;
        DetachedFromVisualTree += (_, _) =>
        {
            _viewModel.OpenProfileRequested -= ViewModelOpenProfileRequested;
            _viewModel.Dispose();
        };
    }

    public event EventHandler? OpenSelfRequested;
    public event EventHandler? OpenProfileRequested;

    private void ViewModelOpenProfileRequested(object? sender, EventArgs e) => OpenProfileRequested?.Invoke(this, EventArgs.Empty);
    private void OpenSelfClick(object? sender, RoutedEventArgs e) => OpenSelfRequested?.Invoke(this, EventArgs.Empty);
    private void OpenProfileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CharacterSearchResult result }) _viewModel.OpenCommand.Execute(result);
    }
    private void SelectSelfClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CharacterSearchResult result }) _viewModel.SelectAsSelfCommand.Execute(result);
    }
}
