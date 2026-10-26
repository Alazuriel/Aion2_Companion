namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed class EventTimerDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("names")]
    public Dictionary<string, string> Names { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("description")]
    public Dictionary<string, string> Description { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = "◇";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "event";

    [JsonPropertyName("regions")]
    public Dictionary<string, EventTimerRegionRule> Regions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; init; }

    [JsonPropertyName("entryWindowMinutes")]
    public int? EntryWindowMinutes { get; init; }

    [JsonPropertyName("alertMinutes")]
    public int[] AlertMinutes { get; init; } = [10, 5, 1];

    [JsonPropertyName("verifiedAt")]
    public string VerifiedAt { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "verified";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    public string LocalizedName(string language) => Localized(Names, language, Id);

    public string LocalizedDescription(string language) => Localized(Description, language, string.Empty);

    private static string Localized(
        IReadOnlyDictionary<string, string> values,
        string language,
        string fallback)
    {
        if (values.TryGetValue(language, out string? localized) && !string.IsNullOrWhiteSpace(localized))
            return localized;
        if (values.TryGetValue("en", out string? english) && !string.IsNullOrWhiteSpace(english))
            return english;
        return fallback;
    }
}

public sealed class EventTimerRegionRule
{
    [JsonPropertyName("rule")]
    public string Rule { get; init; } = string.Empty;

    [JsonPropertyName("intervalMinutes")]
    public int? IntervalMinutes { get; init; }

    [JsonPropertyName("offsetMinutes")]
    public int? OffsetMinutes { get; init; }

    [JsonPropertyName("weekdays")]
    public int[] Weekdays { get; init; } = [];

    [JsonPropertyName("startTime")]
    public string? StartTime { get; init; }

    [JsonPropertyName("startTimes")]
    public string[] StartTimes { get; init; } = [];

    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; init; } = "UTC";
}

public sealed class EventTimerRegionDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("names")]
    public Dictionary<string, string> Names { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("timeZoneId")]
    public string TimeZoneId { get; init; } = "UTC";

    public string LocalizedName(string language)
    {
        if (Names.TryGetValue(language, out string? localized) && !string.IsNullOrWhiteSpace(localized))
            return localized;
        return Names.TryGetValue("en", out string? english) ? english : Id;
    }
}
