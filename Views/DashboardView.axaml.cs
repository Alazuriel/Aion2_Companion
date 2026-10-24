using System;
using System.Linq;
using AION_2_Companion.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AION_2_Companion.ViewModels;

namespace AION_2_Companion.Views;

public partial class DashboardView : UserControl
{
    private bool _editing;
    private Border? _dragCard;

    public DashboardView()
    {
        InitializeComponent();
    }

    public DashboardView(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void EditLayoutClick(object? sender, RoutedEventArgs e)
    {
        _editing = !_editing;
        this.FindControl<Button>("EditLayoutButton")!.Content = _editing ? "Kész" : "Layout szerkesztése";
        this.FindControl<TextBlock>("LayoutHint")!.Text = _editing
            ? "Kártyák áthelyezése drag & drop módszerrel."
            : "A kártyák sorrendje szerkesztési módban módosítható.";
    }

    private async void CardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_editing || sender is not Border card)
            return;

        _dragCard = card;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DataFormat.Text, card.Tag?.ToString() ?? ""));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private void CardDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = _editing && e.DataTransfer.Contains(DataFormat.Text)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void CardDrop(object? sender, DragEventArgs e)
    {
        if (!_editing || sender is not Border target || _dragCard is null || target == _dragCard)
            return;

        var panel = this.FindControl<WrapPanel>("QuickCards")!;
        var children = panel.Children.ToList();
        int from = children.IndexOf(_dragCard);
        int to = children.IndexOf(target);
        if (from < 0 || to < 0)
            return;

        panel.Children.Remove(_dragCard);
        panel.Children.Insert(Math.Min(to, panel.Children.Count), _dragCard);
        _dragCard = null;
    }
    
    private void OpenDpsClick(object? sender, RoutedEventArgs e)
    {
        MeterSnapshot? snapshot =
            (DataContext as MainWindowViewModel)?.BuildSnapshot();

        OverlayWindowManager.ToggleDps(snapshot);
    }

    private void OpenDbmClick(object? sender, RoutedEventArgs e) => OverlayWindowManager.ToggleDbm();
}
