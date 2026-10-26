namespace AION_2_Companion.Views;

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AION_2_Companion.Core;
using AION_2_Companion.Core.EventTimers;
using Avalonia.Media;

public sealed class EventTimerCardViewModel : INotifyPropertyChanged
{
    private static readonly int[] AlertValues = [-1, 1, 5, 10, 15, 30];
    private EventTimerSnapshot _snapshot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EventTimerCardViewModel(EventTimerSnapshot snapshot) => _snapshot = snapshot;

    public string Id => _snapshot.Definition.Id;
    public string Icon => _snapshot.Definition.Icon;
    public string Name => _snapshot.Definition.LocalizedName(LocalizationService.CurrentLanguageCode);
    public string Description => _snapshot.Definition.LocalizedDescription(LocalizationService.CurrentLanguageCode);
    public string Category => LocalizationService.Get("EventCategory_" + _snapshot.Definition.Category);
    public string Countdown => FormatCountdown(_snapshot.Remaining);
    public string CountdownLabel => LocalizationService.Get(
        _snapshot.State == EventTimerState.Active ? "EventTimerEndsIn" : "EventTimerStartsIn");
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
    public string NextLocalTime => string.Format(
        LocalizationService.Get("EventTimerNextLocal"),
        _snapshot.NextStartUtc.ToLocalTime().ToString("ddd HH:mm"));
    public string EntryText => _snapshot.EntryOpen
        ? LocalizationService.Get("EventTimerEntryOpen")
        : string.Empty;
    public bool HasEntryText => _snapshot.EntryOpen;

    public int AlertIndex
    {
        get
        {
            int index = Array.IndexOf(AlertValues, _snapshot.Preference.AlertMinutes);
            return index < 0 ? 3 : index;
        }
        set
        {
            if (value < 0 || value >= AlertValues.Length || AlertValues[value] == _snapshot.Preference.AlertMinutes)
                return;
            Save(preference => preference.AlertMinutes = AlertValues[value]);
        }
    }

    public bool AlertAtStart
    {
        get => _snapshot.Preference.AlertAtStart;
        set
        {
            if (value == _snapshot.Preference.AlertAtStart) return;
            Save(preference => preference.AlertAtStart = value);
        }
    }

    public bool SoundEnabled
    {
        get => _snapshot.Preference.SoundEnabled;
        set
        {
            if (value == _snapshot.Preference.SoundEnabled) return;
            Save(preference => preference.SoundEnabled = value);
        }
    }

    public bool VisualEnabled
    {
        get => _snapshot.Preference.VisualEnabled;
        set
        {
            if (value == _snapshot.Preference.VisualEnabled) return;
            Save(preference => preference.VisualEnabled = value);
        }
    }

    public bool ShowInOverlay
    {
        get => _snapshot.Preference.ShowInOverlay;
        set
        {
            if (value == _snapshot.Preference.ShowInOverlay) return;
            Save(preference => preference.ShowInOverlay = value);
        }
    }

    public void Update(EventTimerSnapshot snapshot)
    {
        _snapshot = snapshot;
        RaiseAll();
    }

    private void Save(Action<EventTimerPreference> change)
    {
        EventTimerPreference current = _snapshot.Preference;
        var preference = new EventTimerPreference
        {
            Visible = current.Visible,
            ShowInOverlay = current.ShowInOverlay,
            AlertMinutes = current.AlertMinutes,
            AlertAtStart = current.AlertAtStart,
            SoundEnabled = current.SoundEnabled,
            VisualEnabled = current.VisualEnabled,
        };
        change(preference);
        CompanionSettings.SetEventTimerPreference(Id, preference);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatCountdown(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            value = TimeSpan.Zero;
        return value.TotalHours >= 24
            ? $"{(int)value.TotalDays}d {value.Hours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
