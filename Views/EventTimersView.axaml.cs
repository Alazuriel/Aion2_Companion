namespace AION_2_Companion.Views;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using AION_2_Companion.Core;
using AION_2_Companion.Core.EventTimers;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Views.Overlays;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

public partial class EventTimersView : UserControl
{
    private readonly EventTimerService _service = EventTimerService.Shared;
    private readonly ComboBox _regionCombo;
    private readonly CheckBox _clickThroughToggle;
    private bool _updatingRegion;

    public ObservableCollection<EventTimerCardViewModel> Rows { get; } = [];

    public EventTimersView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        _regionCombo = this.FindControl<ComboBox>("RegionCombo")!;
        _clickThroughToggle = this.FindControl<CheckBox>("ClickThroughToggle")!;
        _clickThroughToggle.IsChecked = CompanionSettings.EventTimerOverlayClickThrough;

        AttachedToVisualTree += (_, _) =>
        {
            _service.Updated -= ServiceUpdated;
            _service.Updated += ServiceUpdated;
            _service.Start();
            RebuildRegions();
            SyncRows();
        };
        DetachedFromVisualTree += (_, _) => _service.Updated -= ServiceUpdated;
    }

    private void ServiceUpdated(object? sender, EventArgs e)
    {
        RebuildRegions();
        SyncRows();
    }

    private void RebuildRegions()
    {
        string selected = CompanionSettings.EventTimerRegion;
        string[] existing = _regionCombo.Items
            .OfType<ComboBoxItem>()
            .Select(item => item.Tag as string ?? string.Empty)
            .ToArray();
        string[] current = _service.Regions.Select(region => region.Id).ToArray();

        _updatingRegion = true;
        try
        {
            if (!existing.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
            {
                _regionCombo.Items.Clear();
                foreach (EventTimerRegionDefinition region in _service.Regions)
                {
                    _regionCombo.Items.Add(new ComboBoxItem
                    {
                        Tag = region.Id,
                        Content = region.LocalizedName(LocalizationService.CurrentLanguageCode),
                    });
                }
            }
            else
            {
                foreach (ComboBoxItem item in _regionCombo.Items.OfType<ComboBoxItem>())
                {
                    EventTimerRegionDefinition? region = _service.Regions.FirstOrDefault(value =>
                        value.Id.Equals(item.Tag as string, StringComparison.OrdinalIgnoreCase));
                    if (region is not null)
                        item.Content = region.LocalizedName(LocalizationService.CurrentLanguageCode);
                }
            }

            _regionCombo.SelectedItem = _regionCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _updatingRegion = false;
        }
    }

    private void SyncRows()
    {
        TextBlock title = this.FindControl<TextBlock>("DataStatusTitle")!;
        TextBlock text = this.FindControl<TextBlock>("DataStatusText")!;
        if (_service.LoadError is not null)
        {
            title.Text = LocalizationService.Get("EventTimerDataError");
            title.Foreground = new SolidColorBrush(Color.Parse("#F87171"));
            text.Text = LocalizationService.Get("EventTimerDataErrorHint");
            Rows.Clear();
            return;
        }

        title.Text = LocalizationService.Get("EventTimerDataReady");
        title.Foreground = new SolidColorBrush(Color.Parse("#58E0B2"));
        text.Text = LocalizationService.Get("EventTimerDataHint");

        var byId = Rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new ObservableCollection<EventTimerCardViewModel>();
        foreach (EventTimerSnapshot snapshot in _service.Snapshots)
        {
            if (!byId.TryGetValue(snapshot.Definition.Id, out EventTimerCardViewModel? row))
                row = new EventTimerCardViewModel(snapshot);
            else
                row.Update(snapshot);
            ordered.Add(row);
        }

        if (!Rows.Select(row => row.Id).SequenceEqual(ordered.Select(row => row.Id), StringComparer.OrdinalIgnoreCase))
        {
            Rows.Clear();
            foreach (EventTimerCardViewModel row in ordered)
                Rows.Add(row);
        }
    }

    private void RegionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingRegion || _regionCombo.SelectedItem is not ComboBoxItem { Tag: string region })
            return;
        CompanionSettings.EventTimerRegion = region;
    }

    private void ClickThroughChanged(object? sender, RoutedEventArgs e) =>
        CompanionSettings.EventTimerOverlayClickThrough = _clickThroughToggle.IsChecked == true;

    private void OverlayClick(object? sender, RoutedEventArgs e) => OverlayWindowManager.ToggleEventTimers();
}
