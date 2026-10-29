using System;
using System.Diagnostics;
using AION_2_Companion.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public partial class AccountView : UserControl
{
    private readonly AccountPageViewModel _viewModel = new();

    public AccountView()
    {
        InitializeComponent();
        DataContext = _viewModel;
        DetachedFromVisualTree += (_, _) => _viewModel.Dispose();
    }

    public event EventHandler? OpenCharactersRequested;

    private async void ConnectClick(object? sender, RoutedEventArgs e) => await _viewModel.ConnectAsync();
    private void CancelClick(object? sender, RoutedEventArgs e) => _viewModel.CancelConnect();
    private async void DisconnectClick(object? sender, RoutedEventArgs e) => await _viewModel.DisconnectAsync();
    private void OpenCharactersClick(object? sender, RoutedEventArgs e) => OpenCharactersRequested?.Invoke(this, EventArgs.Empty);

    private static void OpenAccountCenterClick(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://companion.nexforge.hu") { UseShellExecute = true });
}
