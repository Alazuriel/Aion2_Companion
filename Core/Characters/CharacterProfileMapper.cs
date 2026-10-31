using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters.Verification;

namespace AION_2_Companion.Core.Characters;

public static class CharacterProfileMapper
{
    public static CharacterProfile Map(CharacterLoadResult load)
    {
        CharacterResolveResponse response = load.Response;
        CharacterApiHeader header = response.Data.Character;
        return new CharacterProfile(
            header.CharacterId,
            header.Name,
            header.ServerId,
            header.ServerName,
            header.Region,
            header.ClassName,
            header.Level,
            header.CombatPower,
            header.ItemLevel,
            header.RaceName,
            header.GuildName,
            header.TitleName,
            ToUri(CharacterApiClient.NormalizeImageUrl(header.ImageUrl)),
            CharacterVerificationService.Shared.IsVerified(new CharacterIdentity(header.Region, header.ServerId, header.Name))
                ? "Verified" : response.Partial ? "Partial" : "Ready",
            load.FetchedAtUtc,
            load.FromLocalCache,
            load.Offline,
            response.Partial,
            response.Warnings,
            MapStats(response.Data.Stats),
            MapGear(response.Data.Gear, response.Data.GearDetails),
            [],
            MapDaevanion(response.Data.Daevanion),
            MapSkills(response.Data.Skills),
            MapNames(response.Data.Titles),
            ReadName(response.Data.Pet),
            ReadName(response.Data.Wing));
    }

    private static IReadOnlyList<CharacterStat> MapStats(JsonElement? source) => Elements(source)
        .Select((entry, index) => new CharacterStat(
            ReadString(entry, "type", "id") ?? index.ToString(CultureInfo.InvariantCulture),
            ReadString(entry, "name", "statName") ?? "—",
            BuildStatValue(entry)))
        .ToArray();

    private static IReadOnlyList<CharacterGearItem> MapGear(JsonElement? source, JsonElement? details)
    {
        var result = new List<CharacterGearItem>();
        foreach (JsonElement entry in Elements(source))
        {
            string slot = ReadString(entry, "slotPosName", "slot", "slotName") ?? "Unknown";
            string id = ReadString(entry, "id", "itemId") ?? string.Empty;
            JsonElement? detail = FindDetail(details, slot, id);
            result.Add(new CharacterGearItem(
                slot,
                id,
                ReadString(entry, "name", "itemName") ?? "—",
                ReadString(entry, "grade", "rarity"),
                ReadInt(entry, "itemLevel", "level"),
                ReadInt(entry, "enchantLevel", "enchant"),
                ToUri(ReadString(entry, "icon", "iconUrl")),
                detail is null || detail.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : detail.Value.GetRawText()));
        }
        return result;
    }

    private static IReadOnlyList<CharacterSkill> MapSkills(JsonElement? source) => Elements(source)
        .Select(entry => new CharacterSkill(
            ReadString(entry, "id", "skillId") ?? string.Empty,
            ReadString(entry, "name", "skillName") ?? "—",
            ReadInt(entry, "skillLevel", "level") ?? 0,
            ReadString(entry, "grade"),
            ToUri(ReadString(entry, "icon", "iconUrl")),
            ReadString(entry, "category", "type"),
            ReadInt(entry, "equip", "equipped") is int equipped ? equipped != 0 : null))
        .ToArray();

    private static IReadOnlyList<CharacterDevotion> MapDaevanion(CharacterApiDaevanion? source)
    {
        if (source is null) return [];
        return Elements(source.Boards).Select(entry => new CharacterDevotion(
            ReadString(entry, "id", "boardId") ?? string.Empty,
            ReadString(entry, "name", "boardName") ?? "—",
            "Board",
            ReadInt(entry, "openNodeCount") ?? 0,
            ToUri(ReadString(entry, "icon", "iconUrl")),
            ReadInt(entry, "openPercent"))).ToArray();
    }

    private static IReadOnlyList<string> MapNames(JsonElement? source) => Elements(source)
        .Select(entry => ReadName(entry)).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();

    private static string BuildStatValue(JsonElement entry)
    {
        string baseValue = ReadString(entry, "value", "displayValue") ?? "—";
        if (entry.TryGetProperty("statSecondList", out JsonElement secondary) && secondary.ValueKind == JsonValueKind.Array)
        {
            string extras = string.Join(", ", secondary.EnumerateArray().Select(value => value.ToString()).Where(value => value.Length > 0));
            if (extras.Length > 0) return $"{baseValue} · {extras}";
        }
        return baseValue;
    }

    private static IEnumerable<JsonElement> Elements(JsonElement? value) => value is { ValueKind: JsonValueKind.Array } element ? element.EnumerateArray() : [];
    private static JsonElement? FindDetail(JsonElement? details, string slot, string id)
    {
        if (details is not { ValueKind: JsonValueKind.Object } element) return null;
        foreach (JsonProperty property in element.EnumerateObject())
            if (property.Name.Equals(slot, StringComparison.OrdinalIgnoreCase) || property.Name.Equals(id, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }
    private static string? ReadName(JsonElement? entry) => entry is { } value ? ReadString(value, "name", "titleName", "petName", "wingName") : null;
    private static string? ReadString(JsonElement entry, params string[] names)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        foreach (string name in names)
            if (entry.TryGetProperty(name, out JsonElement value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                return value.ToString();
        return null;
    }
    private static int? ReadInt(JsonElement entry, params string[] names) => int.TryParse(ReadString(entry, names), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    private static Uri? ToUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
}
