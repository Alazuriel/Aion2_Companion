using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Accounts;

/// <summary>
/// Normalized desktop model for the future character API response. The final
/// contract adapter maps its JSON DTOs into this model, keeping wire details out
/// of the Avalonia UI.
/// </summary>
public sealed record CharacterProfile(
    string CharacterId,
    string Name,
    ushort ServerId,
    string ServerName,
    string Region,
    string ClassName,
    int Level,
    int? CombatPower,
    int? ItemLevel,
    string? Race,
    string? GuildName,
    string? TitleName,
    Uri? ImageUri,
    string VerificationStatus,
    DateTimeOffset UpdatedAtUtc,
    bool IsCached,
    bool IsOffline,
    bool IsPartial,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<CharacterStat> Stats,
    IReadOnlyList<CharacterGearItem> Gear,
    IReadOnlyList<CharacterTalent> Talents,
    IReadOnlyList<CharacterDevotion> Devotions,
    IReadOnlyList<CharacterSkill> Skills,
    IReadOnlyList<string> Titles,
    string? PetName,
    string? WingName);

public sealed record CharacterStat(string Key, string Name, string DisplayValue);

public sealed record CharacterGearItem(
    string Slot,
    string ItemId,
    string Name,
    string? Rarity,
    int? ItemLevel,
    int? EnchantLevel,
    Uri? IconUri,
    string? Details = null);

public sealed record CharacterTalent(
    string TalentId,
    string Name,
    string? Tree,
    int Rank,
    int? MaxRank,
    Uri? IconUri);

public sealed record CharacterDevotion(
    string DevotionId,
    string Name,
    string? Category,
    int Rank,
    Uri? IconUri,
    int? CompletionPercent = null);

public sealed record CharacterSkill(
    string SkillId,
    string Name,
    int Level,
    string? Grade,
    Uri? IconUri,
    string? Category = null,
    bool? Equipped = null);

public sealed class CharacterProfileService
{
    public static CharacterProfileService Shared { get; } = new();

    public event EventHandler? Changed;

    public CharacterProfile? Current { get; private set; }

    private CharacterProfileService()
    {
    }

    public void Replace(CharacterProfile? profile)
    {
        Current = profile;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => Replace(null);
}
