namespace AION_2_Companion.Core.EventTimers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AION_2_Companion.Core.GameData;
using Avalonia.Threading;

public enum EventTimerState
{
    Inactive,
    Soon,
    Active,
}

public sealed record EventTimerSnapshot(
    EventTimerDefinition Definition,
    EventTimerRegionRule Rule,
    DateTimeOffset OccurrenceStartUtc,
    DateTimeOffset OccurrenceEndUtc,
    DateTimeOffset PreviousStartUtc,
    DateTimeOffset NextStartUtc,
    EventTimerState State,
    bool EntryOpen,
    TimeSpan Remaining,
    EventTimerPreference Preference);

public sealed record EventTimerNotification(
    EventTimerSnapshot Snapshot,
    int ThresholdMinutes,
    bool AtStart,
    bool SoundEnabled,
    bool VisualEnabled);

public sealed class EventTimerNotificationEventArgs(EventTimerNotification notification) : EventArgs
{
    public EventTimerNotification Notification { get; } = notification;
}

public sealed class EventTimerService
{
    private static readonly IReadOnlyDictionary<string, string> WindowsTimeZoneFallbacks =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["America/New_York"] = "Eastern Standard Time",
            ["America/Los_Angeles"] = "Pacific Standard Time",
            ["America/Sao_Paulo"] = "E. South America Standard Time",
            ["Europe/Berlin"] = "W. Europe Standard Time",
            ["Asia/Seoul"] = "Korea Standard Time",
            ["Asia/Taipei"] = "Taipei Standard Time",
        };

    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _timer;
    private EventTimersDatabase? _database;
    private IReadOnlyList<EventTimerSnapshot> _snapshots = [];
    private bool _started;

    public static EventTimerService Shared { get; } = new(TimeProvider.System);

    public event EventHandler? Updated;
    public event EventHandler<EventTimerNotificationEventArgs>? NotificationRaised;

    public string? LoadError { get; private set; }
    public IReadOnlyList<EventTimerSnapshot> Snapshots => _snapshots;
    public IReadOnlyList<EventTimerRegionDefinition> Regions => _database?.Regions ?? [];

    public EventTimerService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh(checkNotifications: true);
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        LoadDatabase();
        CompanionSettings.EventTimerSettingsChanged += SettingsChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        Refresh(checkNotifications: false);
        _timer.Start();
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _timer.Stop();
        CompanionSettings.EventTimerSettingsChanged -= SettingsChanged;
        LocalizationService.LanguageChanged -= LanguageChanged;
    }

    public void Reload()
    {
        LoadDatabase();
        Refresh(checkNotifications: false);
    }

    private void LoadDatabase()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "AION2_EventTimers.nfg");
            _database = EventTimersDatabase.Load(path);
            LoadError = null;

            if (!_database.Regions.Any(region =>
                    region.Id.Equals(CompanionSettings.EventTimerRegion, StringComparison.OrdinalIgnoreCase)))
            {
                CompanionSettings.EventTimerRegion = _database.Regions[0].Id;
            }
        }
        catch (Exception ex)
        {
            _database = null;
            _snapshots = [];
            LoadError = ex.Message;
            DebugConsole.Log("event-timers", $"Event timer data could not be loaded: {ex.Message}");
        }
    }

    private void SettingsChanged(object? sender, EventArgs e) => Refresh(checkNotifications: false);
    private void LanguageChanged(object? sender, EventArgs e) => Updated?.Invoke(this, EventArgs.Empty);

    private void Refresh(bool checkNotifications)
    {
        if (_database is null)
        {
            Updated?.Invoke(this, EventArgs.Empty);
            return;
        }

        DateTimeOffset nowUtc = _timeProvider.GetUtcNow();
        string region = CompanionSettings.EventTimerRegion;
        var snapshots = new List<EventTimerSnapshot>();
        int supportedIndex = 0;

        foreach (EventTimerDefinition definition in _database.Events)
        {
            if (!definition.Regions.TryGetValue(region, out EventTimerRegionRule? rule))
                continue;

            EventTimerPreference preference = CompanionSettings.GetEventTimerPreference(
                definition.Id,
                defaultOverlay: supportedIndex < 6);
            supportedIndex++;
            if (!preference.Visible)
                continue;

            EventTimerSnapshot? snapshot = Calculate(definition, rule, nowUtc, preference);
            if (snapshot is null)
                continue;

            snapshots.Add(snapshot);
            if (checkNotifications)
                CheckNotifications(snapshot, nowUtc);
        }

        _snapshots = snapshots
            .OrderByDescending(snapshot => snapshot.State == EventTimerState.Active)
            .ThenBy(snapshot => snapshot.State == EventTimerState.Active
                ? snapshot.OccurrenceEndUtc
                : snapshot.OccurrenceStartUtc)
            .ThenBy(snapshot => snapshot.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private static EventTimerSnapshot? Calculate(
        EventTimerDefinition definition,
        EventTimerRegionRule rule,
        DateTimeOffset nowUtc,
        EventTimerPreference preference)
    {
        TimeZoneInfo zone;
        try
        {
            zone = ResolveTimeZone(rule.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }

        DateTime localDate = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        List<DateTimeOffset> occurrences = GenerateOccurrences(rule, zone, localDate)
            .Distinct()
            .OrderBy(value => value)
            .ToList();
        if (occurrences.Count == 0)
            return null;

        DateTimeOffset? activeStart = null;
        if (definition.DurationMinutes > 0)
        {
            DateTimeOffset activeCandidate = occurrences.LastOrDefault(start =>
                start <= nowUtc && start.AddMinutes(definition.DurationMinutes) > nowUtc);
            if (activeCandidate != default(DateTimeOffset))
                activeStart = activeCandidate;
        }

        DateTimeOffset next = occurrences.FirstOrDefault(start => start > nowUtc);
        if (next == default)
            return null;
        DateTimeOffset previous = occurrences.LastOrDefault(start => start <= nowUtc);

        if (activeStart is { } active)
        {
            DateTimeOffset end = active.AddMinutes(definition.DurationMinutes);
            int entryMinutes = definition.EntryWindowMinutes ?? definition.DurationMinutes;
            return new EventTimerSnapshot(
                definition,
                rule,
                active,
                end,
                previous,
                next,
                EventTimerState.Active,
                entryMinutes > 0 && nowUtc < active.AddMinutes(entryMinutes),
                end - nowUtc,
                preference);
        }

        TimeSpan remaining = next - nowUtc;
        EventTimerState state = remaining <= TimeSpan.FromMinutes(10)
            ? EventTimerState.Soon
            : EventTimerState.Inactive;
        return new EventTimerSnapshot(
            definition,
            rule,
            next,
            next.AddMinutes(definition.DurationMinutes),
            previous,
            next,
            state,
            false,
            remaining,
            preference);
    }

    private static IEnumerable<DateTimeOffset> GenerateOccurrences(
        EventTimerRegionRule rule,
        TimeZoneInfo zone,
        DateTime localDate)
    {
        for (int dayOffset = -2; dayOffset <= 9; dayOffset++)
        {
            DateTime day = localDate.AddDays(dayOffset);
            IEnumerable<int> minutes = rule.Rule.ToLowerInvariant() switch
            {
                "hourly" => Enumerable.Range(0, 24)
                    .Select(hour => hour * 60 + Math.Clamp(rule.OffsetMinutes ?? 0, 0, 59)),
                "interval" => IntervalMinutes(rule),
                "daily" => ParseTimes(EventTimersDatabase.EffectiveTimes(rule)),
                "weekly" when rule.Weekdays.Contains(IsoWeekday(day.DayOfWeek)) =>
                    ParseTimes(EventTimersDatabase.EffectiveTimes(rule)),
                _ => [],
            };

            foreach (int minuteOfDay in minutes)
            {
                DateTime local = DateTime.SpecifyKind(day.AddMinutes(minuteOfDay), DateTimeKind.Unspecified);
                if (zone.IsInvalidTime(local))
                    continue;

                DateTime utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                yield return new DateTimeOffset(utc, TimeSpan.Zero);
            }
        }
    }

    private static IEnumerable<int> IntervalMinutes(EventTimerRegionRule rule)
    {
        int interval = rule.IntervalMinutes ?? 60;
        int offset = Math.Clamp(rule.OffsetMinutes ?? 0, 0, 1439);
        for (int minute = offset; minute < 1440; minute += interval)
            yield return minute;
    }

    private static IEnumerable<int> ParseTimes(IEnumerable<string> values)
    {
        foreach (string value in values)
        {
            if (TimeOnly.TryParse(value, out TimeOnly time))
                yield return time.Hour * 60 + time.Minute;
        }
    }

    private void CheckNotifications(EventTimerSnapshot snapshot, DateTimeOffset nowUtc)
    {
        EventTimerPreference preference = snapshot.Preference;
        if (!preference.SoundEnabled && !preference.VisualEnabled)
            return;

        if (preference.AlertMinutes >= 0)
        {
            DateTimeOffset alertAt = snapshot.NextStartUtc.AddMinutes(-preference.AlertMinutes);
            TryNotify(snapshot, nowUtc, alertAt, preference.AlertMinutes, atStart: false);
        }

        if (preference.AlertAtStart)
        {
            DateTimeOffset start = snapshot.State == EventTimerState.Active
                ? snapshot.OccurrenceStartUtc
                : snapshot.PreviousStartUtc;
            TryNotify(snapshot, nowUtc, start, 0, atStart: true);
        }
    }

    private void TryNotify(
        EventTimerSnapshot snapshot,
        DateTimeOffset nowUtc,
        DateTimeOffset alertAt,
        int thresholdMinutes,
        bool atStart)
    {
        if (nowUtc < alertAt)
            return;
        if (atStart && nowUtc - alertAt > TimeSpan.FromSeconds(75))
            return;
        if (!atStart && nowUtc >= snapshot.NextStartUtc)
            return;

        long occurrence = (atStart ? alertAt : snapshot.NextStartUtc).UtcTicks;
        string key = string.Join('|',
            CompanionSettings.EventTimerRegion,
            snapshot.Definition.Id,
            occurrence,
            atStart ? "start" : thresholdMinutes.ToString());
        if (CompanionSettings.HasEventTimerNotification(key))
            return;

        CompanionSettings.RememberEventTimerNotification(key);
        NotificationRaised?.Invoke(this, new EventTimerNotificationEventArgs(
            new EventTimerNotification(
                snapshot,
                thresholdMinutes,
                atStart,
                snapshot.Preference.SoundEnabled,
                snapshot.Preference.VisualEnabled)));
    }

    private static int IsoWeekday(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (WindowsTimeZoneFallbacks.TryGetValue(id, out string? windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
