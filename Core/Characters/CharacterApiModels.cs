using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AION_2_Companion.Core.Characters;

public sealed record CharacterIdentity(string Region, ushort ServerId, string Name)
{
    public string CacheKey => $"{Region.Trim().ToLowerInvariant()}|{ServerId}|{Name.Trim().ToLowerInvariant()}";
}

public sealed class CharacterSearchResponse
{
    [JsonPropertyName("results")] public List<CharacterSearchResult> Results { get; set; } = [];
    [JsonPropertyName("cache")] public CharacterApiCacheInfo? Cache { get; set; }
    [JsonPropertyName("refresh")] public CharacterApiRefreshInfo? Refresh { get; set; }
}

public sealed class CharacterSearchResult
{
    [JsonPropertyName("character_id")] public string CharacterId { get; set; } = string.Empty;
    [JsonPropertyName("character_name")] public string CharacterName { get; set; } = string.Empty;
    [JsonPropertyName("server_id")] public ushort ServerId { get; set; }
    [JsonPropertyName("server_name")] public string ServerName { get; set; } = string.Empty;
    [JsonPropertyName("region")] public string Region { get; set; } = "eu";
    [JsonPropertyName("class_name")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("race")] public int? Race { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }

    [JsonIgnore] public CharacterIdentity Identity => new(Region, ServerId, CharacterName);
    [JsonIgnore] public string DisplayImageUrl => CharacterApiClient.NormalizeImageUrl(ImageUrl);
}

public sealed class CharacterResolveResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("detail_level")] public string DetailLevel { get; set; } = "standard";
    [JsonPropertyName("partial")] public bool Partial { get; set; }
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
    [JsonPropertyName("stale")] public bool Stale { get; set; }
    [JsonPropertyName("refresh")] public CharacterApiRefreshInfo? Refresh { get; set; }
    [JsonPropertyName("data")] public CharacterResolveData Data { get; set; } = new();
}

public sealed class CharacterResolveData
{
    [JsonPropertyName("character")] public CharacterApiHeader Character { get; set; } = new();
    [JsonPropertyName("stats")] public JsonElement? Stats { get; set; }
    [JsonPropertyName("stats_extra")] public JsonElement? StatsExtra { get; set; }
    [JsonPropertyName("gear")] public JsonElement? Gear { get; set; }
    [JsonPropertyName("gear_details")] public JsonElement? GearDetails { get; set; }
    [JsonPropertyName("skins")] public JsonElement? Skins { get; set; }
    [JsonPropertyName("skills")] public JsonElement? Skills { get; set; }
    [JsonPropertyName("daevanion")] public CharacterApiDaevanion? Daevanion { get; set; }
    [JsonPropertyName("titles")] public JsonElement? Titles { get; set; }
    [JsonPropertyName("pet")] public JsonElement? Pet { get; set; }
    [JsonPropertyName("wing")] public JsonElement? Wing { get; set; }
    [JsonPropertyName("wing_skin")] public JsonElement? WingSkin { get; set; }
    [JsonPropertyName("petwing")] public JsonElement? PetWing { get; set; }
}

public sealed class CharacterApiHeader
{
    [JsonPropertyName("character_id")] public string CharacterId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("server_id")] public ushort ServerId { get; set; }
    [JsonPropertyName("server_name")] public string ServerName { get; set; } = string.Empty;
    [JsonPropertyName("region")] public string Region { get; set; } = "eu";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("class_name")] public string ClassName { get; set; } = string.Empty;
    [JsonPropertyName("race_name")] public string? RaceName { get; set; }
    [JsonPropertyName("combat_power")] public int? CombatPower { get; set; }
    [JsonPropertyName("item_level")] public int? ItemLevel { get; set; }
    [JsonPropertyName("guild_name")] public string? GuildName { get; set; }
    [JsonPropertyName("title_name")] public string? TitleName { get; set; }
    [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
}

public sealed class CharacterApiDaevanion
{
    [JsonPropertyName("boards")] public JsonElement? Boards { get; set; }
    [JsonPropertyName("details")] public JsonElement? Details { get; set; }
    [JsonPropertyName("raw_summary")] public JsonElement? RawSummary { get; set; }
}

public sealed class CharacterApiCacheInfo
{
    [JsonPropertyName("cached")] public bool Cached { get; set; }
    [JsonPropertyName("stale")] public bool Stale { get; set; }
    [JsonPropertyName("fetched_at")] public string? FetchedAt { get; set; }
}

public sealed class CharacterApiRefreshInfo
{
    [JsonPropertyName("requested")] public bool Requested { get; set; }
    [JsonPropertyName("cooldown_seconds")] public int CooldownSeconds { get; set; } = 10;
}

public sealed class CharacterApiException : Exception
{
    public CharacterApiException(string message, int? statusCode = null, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public int? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}
