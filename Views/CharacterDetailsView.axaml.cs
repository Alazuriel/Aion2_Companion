using System;
using AION_2_Companion.ViewModels;
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
}
