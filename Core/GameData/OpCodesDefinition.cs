namespace AION_2_Companion.Core.GameData;

using System.Text.Json.Serialization;

public static class OpCodesDefinition
{
    public sealed class OpCodes
    {
        [JsonPropertyName("key")]
        public string Key { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; init; } = string.Empty;

        [JsonPropertyName("confidence")]
        public string Confidence { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;
    }
}
