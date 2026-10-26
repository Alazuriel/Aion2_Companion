namespace AION_2_Companion.Views.Overlays;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AION_2_Companion.Core;
using AION_2_Companion.Core.EventTimers;
using AION_2_Companion.Core.GameData;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

public sealed class EventTimerOverlayRow : INotifyPropertyChanged
{
    private EventTimerSnapshot _snapshot;
    public event PropertyChangedEventHandler? PropertyChanged;

    public EventTimerOverlayRow(EventTimerSnapshot snapshot) => _snapshot = snapshot;
    public string Id => _snapshot.Definition.Id;
    public string Icon => _snapshot.Definition.Icon;
    public string Name => _snapshot.Definition.LocalizedName(LocalizationService.CurrentLanguageCode);
    public string StateText => LocalizationService.Get(_snapshot.State switch
    {
        EventTimerState.Active => "EventTimerActive",
        EventTimerState.Soon => "EventTimerSoon",
        _ => "EventTimerScheduled",
    });
    public IBrush StateBrush => new SolidColorBrush(Color.Parse(_snapshot.State switch
    {
        EventTimerState.Active => "#58E0B2",
        EventTimerState.Soon => "#FFB454",
        _ => "#45DFFF",
    }));
    public string Countdown
    {
        get
        {
            TimeSpan value = _snapshot.Remaining < TimeSpan.Zero ? TimeSpan.Zero : _snapshot.Remaining;
            return value.TotalHours >= 24
                ? $"{(int)value.TotalDays}d {value.Hours:00}:{value.Minutes:00}"
                : $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }
    }

    public void Update(EventTimerSnapshot snapshot)
    {
        _snapshot = snapshot;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public partial class EventTimerOverlayWindow : Window
{
    private readonly EventTimerService _service = EventTimerService.Shared;
    public ObservableCollection<EventTimerOverlayRow> Rows { get; } = [];

    public EventTimerOverlayWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        Focusable = false;
        OverlayPlacementTracker.Attach(this, "event-timers-overlay");
        Opened += OverlayOpened;
        Closed += OverlayClosed;
    }

    private void OverlayOpened(object? sender, EventArgs e)
    {
        _service.Start();
        _service.Updated += ServiceUpdated;
        CompanionSettings.EventTimerSettingsChanged += SettingsChanged;
        Refresh();
        WindowClickThrough.Apply(this, CompanionSettings.EventTimerOverlayClickThrough);
    }

    private void OverlayClosed(object? sender, EventArgs e)
    {
        _service.Updated -= ServiceUpdated;
        CompanionSettings.EventTimerSettingsChanged -= SettingsChanged;
    }

    private void ServiceUpdated(object? sender, EventArgs e) => Refresh();

    private void SettingsChanged(object? sender, EventArgs e)
    {
        WindowClickThrough.Apply(this, CompanionSettings.EventTimerOverlayClickThrough);
        Refresh();
    }

    private void Refresh()
    {
        EventTimerSnapshot[] snapshots = _service.Snapshots
            .Where(snapshot => snapshot.Preference.ShowInOverlay)
            .Take(6)
            .ToArray();
        var existing = Rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = snapshots.Select(snapshot =>
        {
            if (!existing.TryGetValue(snapshot.Definition.Id, out EventTimerOverlayRow? row))
                return new EventTimerOverlayRow(snapshot);
            row.Update(snapshot);
            return row;
        }).ToArray();

        if (!Rows.Select(row => row.Id).SequenceEqual(ordered.Select(row => row.Id), StringComparer.OrdinalIgnoreCase))
        {
            Rows.Clear();
            foreach (EventTimerOverlayRow row in ordered)
                Rows.Add(row);
        }

        this.FindControl<TextBlock>("EmptyText")!.IsVisible = Rows.Count == 0;
        EventTimerRegionDefinition? region = _service.Regions.FirstOrDefault(value =>
            value.Id.Equals(CompanionSettings.EventTimerRegion, StringComparison.OrdinalIgnoreCase));
        this.FindControl<TextBlock>("RegionText")!.Text = region?.LocalizedName(LocalizationService.CurrentLanguageCode) ?? string.Empty;
    }

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!CompanionSettings.EventTimerOverlayClickThrough &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
