
namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SkillDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonIgnore]
    public uint IdValue =>
        uint.TryParse(
            Id,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out uint value)
            ? value
            : 0;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("class")]
    public string ClassName { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("mastery")]
    public string Mastery { get; init; } = string.Empty;

    [JsonPropertyName("required_level")]
    public int? RequiredLevel { get; init; }

    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("max_level")]
    public int? MaxLevel { get; init; }

    [JsonPropertyName("damage")]
    public string Damage { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public List<string> Notes { get; init; } = [];

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("chain")]
    public List<SkillReference> Chain { get; init; } = [];

    [JsonPropertyName("specialties")]
    public List<SkillSpecialty> Specialties { get; init; } = [];

    [JsonPropertyName("details")]
    public Dictionary<string, JsonElement> Details { get; init; } = [];
}

public sealed class SkillReference
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonIgnore]
    public uint IdValue =>
        uint.TryParse(
            Id,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out uint value)
            ? value
            : 0;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public sealed class SkillSpecialty
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("icon_url")]
    public string IconUrl { get; init; } = string.Empty;

    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("linked_skills")]
    public List<SkillReference> LinkedSkills { get; init; } = [];
}
