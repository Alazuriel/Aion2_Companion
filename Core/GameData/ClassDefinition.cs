namespace AION_2_Companion.Core.GameData;

using System.Collections.Generic;
using System.Text.Json.Serialization;

public sealed class ClassDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("job_values")]
    public List<byte> JobValues { get; init; } = [];

    [JsonPropertyName("color")]
    public string Color { get; init; } = "#FFFFFF";

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = string.Empty;

    [JsonPropertyName("skill_prefix")]
    public uint SkillPrefix { get; init; }
}