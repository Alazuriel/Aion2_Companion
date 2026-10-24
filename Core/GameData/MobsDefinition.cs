namespace AION_2_Companion.Core.GameData;

using System.Text.Json.Serialization;

public sealed class MobDefinition
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("creature_type")]
    public string CreatureType { get; init; } = string.Empty;
}