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
    string ServerName,
    string Region,
    string ClassName,
    int Level,
    int? ItemLevel,
    string? Race,
    string VerificationStatus,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<CharacterStat> Stats,
    IReadOnlyList<CharacterGearItem> Gear,
    IReadOnlyList<CharacterTalent> Talents,
    IReadOnlyList<CharacterDevotion> Devotions,
    IReadOnlyList<CharacterSkill> Skills);

public sealed record CharacterStat(string Key, string Name, string DisplayValue);

public sealed record CharacterGearItem(
    string Slot,
    string ItemId,
    string Name,
    string? Rarity,
    int? ItemLevel,
    int? EnchantLevel,
    Uri? IconUri);

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
    Uri? IconUri);

public sealed record CharacterSkill(
    string SkillId,
    string Name,
    int Level,
    string? Grade,
    Uri? IconUri);

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
