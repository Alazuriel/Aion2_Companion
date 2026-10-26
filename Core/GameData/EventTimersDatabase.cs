namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class EventTimersDatabase
{
    private static readonly HashSet<string> SupportedRules =
        new(StringComparer.OrdinalIgnoreCase) { "hourly", "interval", "daily", "weekly" };

    public IReadOnlyList<EventTimerDefinition> Events { get; }
    public IReadOnlyList<EventTimerRegionDefinition> Regions { get; }

    public EventTimersDatabase(
        IEnumerable<EventTimerDefinition> events,
        IEnumerable<EventTimerRegionDefinition> regions)
    {
        Regions = regions
            .Where(region => !string.IsNullOrWhiteSpace(region.Id))
            .GroupBy(region => region.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var regionIds = Regions.Select(region => region.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Events = events
            .Where(ValidateEvent)
            .Where(timer => timer.Regions.Keys.Any(regionIds.Contains))
            .GroupBy(timer => timer.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (Regions.Count == 0 || Events.Count == 0)
            throw new InvalidDataException("The event timer data pack is empty or invalid.");
    }

    public static EventTimersDatabase Load(string path)
    {
        ForgeDataDocument document = ForgeDataReader.Load(path);
        return new EventTimersDatabase(document.EventTimers, document.EventTimerRegions);
    }

    private static bool ValidateEvent(EventTimerDefinition timer)
    {
        if (!timer.Enabled || string.IsNullOrWhiteSpace(timer.Id) || timer.Names.Count == 0)
            return false;
        if (timer.DurationMinutes < 0 || timer.EntryWindowMinutes is < 0)
            return false;

        foreach (EventTimerRegionRule rule in timer.Regions.Values)
        {
            if (!SupportedRules.Contains(rule.Rule) || string.IsNullOrWhiteSpace(rule.TimeZoneId))
                return false;
            if (rule.Rule.Equals("interval", StringComparison.OrdinalIgnoreCase) &&
                rule.IntervalMinutes is not (> 0 and <= 10080))
                return false;
            if (rule.Rule.Equals("weekly", StringComparison.OrdinalIgnoreCase) &&
                (rule.Weekdays.Length == 0 || rule.Weekdays.Any(day => day is < 1 or > 7)))
                return false;
            if ((rule.Rule.Equals("daily", StringComparison.OrdinalIgnoreCase) ||
                 rule.Rule.Equals("weekly", StringComparison.OrdinalIgnoreCase)) &&
                EffectiveTimes(rule).Length == 0)
                return false;
        }

        return timer.Regions.Count > 0;
    }

    internal static string[] EffectiveTimes(EventTimerRegionRule rule) =>
        rule.StartTimes.Length > 0
            ? rule.StartTimes
            : string.IsNullOrWhiteSpace(rule.StartTime) ? [] : [rule.StartTime];
}
