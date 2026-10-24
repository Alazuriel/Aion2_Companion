using System.Text.Json.Serialization;

namespace AION_2_Companion.Core.GameData;

public class PetsDefiniton
{
    
    /*
    "category": "Feral",
    "map": "World/World_L/World_L_A/InstanceLayer/World_L_A_Ins_FE_Defence_01",
    "monster_id": 2340102,
    "pet_id": 1004,
    "pet_name": "Fossa",
    "soul_item_ids": [
      "534660003"
    ],
    "x": -101073,
    "y": 4065,
    "z": 17798
    */
    
    [JsonPropertyName("category")]
    public string Category { get; init; }

    [JsonPropertyName("map")]
    public string Map { get; init; } = string.Empty;
    
    [JsonPropertyName("monster_id")]
    public int MonsterId { get; init; }
    
    [JsonPropertyName("pet_id")]
    public int PetId { get; init; }
    
    [JsonPropertyName("name")]
    public string PetName { get; init; } = string.Empty;
    
    [JsonPropertyName("soul_item_ids")]
    public string[] SoulItemIds { get; init; }

    [JsonPropertyName("x")]
    public int x { get; init; }
    
    [JsonPropertyName("y")]
    public int y { get; init; }
    
    [JsonPropertyName("z")]
    public int z { get; init; }
}