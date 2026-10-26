namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed class BossDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("template_ids")]
    public int[] TemplateIds { get; init; } = [];

    [JsonPropertyName("names")]
    public Dictionary<string, string> Names { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("aliases")]
    public string[] Aliases { get; init; } = [];

    [JsonPropertyName("instance")]
    public string Instance { get; init; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public string Difficulty { get; init; } = string.Empty;

    [JsonPropertyName("max_hp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long MaxHp { get; init; }

    [JsonPropertyName("enrage_seconds")]
    public int? EnrageSeconds { get; init; }

    [JsonPropertyName("enrage_warning_seconds")]
    public int? EnrageWarningSeconds { get; init; }

    [JsonPropertyName("verifiedAt")]
    public string VerifiedAt { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "verified";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    public string LocalizedName(string language, string fallback = "")
    {
        if (Names.TryGetValue(language, out string? localized) &&
            !string.IsNullOrWhiteSpace(localized))
            return localized;
        if (Names.TryGetValue("en", out string? english) &&
            !string.IsNullOrWhiteSpace(english))
            return english;
        return string.IsNullOrWhiteSpace(fallback) ? Id : fallback;
    }
}
