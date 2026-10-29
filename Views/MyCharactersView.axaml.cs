using System;
using AION_2_Companion.ViewModels;
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

    private void OpenCharacterClick(object? sender, RoutedEventArgs e) =>
        OpenCharacterRequested?.Invoke(this, EventArgs.Empty);
}
