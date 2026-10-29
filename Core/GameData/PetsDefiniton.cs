using System.Text.Json.Serialization;

namespace AION_2_Companion.Core.GameData;

public class PetsDefiniton
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("map")]
    public string Map { get; init; } = string.Empty;
    
    [JsonPropertyName("monster_id")]
    public int MonsterId { get; init; }
    
    [JsonPropertyName("pet_id")]
    public int PetId { get; init; }
    
    [JsonPropertyName("name")]
    public string PetName { get; init; } = string.Empty;
    
    [JsonPropertyName("soul_item_ids")]
    public string[] SoulItemIds { get; init; } = [];

    [JsonPropertyName("x")]
    public int X { get; init; }
    
    [JsonPropertyName("y")]
    public int Y { get; init; }
    
    [JsonPropertyName("z")]
    public int Z { get; init; }
}
