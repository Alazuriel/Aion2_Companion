using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using Avalonia.Threading;

namespace AION_2_Companion.ViewModels;

public sealed record CharacterVisualItem(
    string Name,
    string? Subtitle,
    string? IconSource,
    string AccentColor,
    string Tooltip,
    string? CornerText = null)
{
    public bool HasCornerText => !string.IsNullOrWhiteSpace(CornerText);
}

public sealed record CharacterStatCard(string Name, string Value, string AccentColor);

public sealed class CharacterDetailsViewModel : INotifyPropertyChanged, IDisposable
{
    private DetectedCharacterSnapshot? _detected = DetectedCharacterService.Shared.Current;
    private CharacterProfile? _profile = CharacterProfileService.Shared.Current;

    public CharacterDetailsViewModel()
    {
        DetectedCharacterService.Shared.Changed += CharacterChanged;
        CharacterProfileService.Shared.Changed += CharacterChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        RebuildCollections();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDeveloperMode => Program.DeveloperMode;
    public bool ShowUnderForge => !IsDeveloperMode;
    public bool HasCharacter => _detected is not null || _profile is not null;
    public bool HasNoCharacter => !HasCharacter;
    public string CharacterName => _profile?.Name ?? _detected?.Name ?? "—";
    public string CharacterClass => _profile?.ClassName ?? _detected?.ClassName ?? "—";
    public string? ClassIconSource => _detected?.ClassIconPath;
    public string LevelText => (_profile?.Level ?? _detected?.Level) is int level && level > 0
        ? level.ToString()
        : "—";
    public string GearScoreText => (_profile?.ItemLevel is int itemLevel && itemLevel > 0)
        ? itemLevel.ToString()
        : _detected?.GearScore?.ToString() ?? "—";
    public string ServerText => !string.IsNullOrWhiteSpace(_profile?.ServerName)
        ? _profile!.ServerName
        : _detected?.ServerId is ushort serverId
            ? LocalizationService.Format("ServerIdUnconfirmed", serverId)
            : LocalizationService.Get("UnknownServer");
    public string RegionText => string.IsNullOrWhiteSpace(_profile?.Region) ? "—" : _profile!.Region;
    public string VerificationText => string.IsNullOrWhiteSpace(_profile?.VerificationStatus)
        ? LocalizationService.Get("Unverified")
        : _profile!.VerificationStatus;
    public string UpdatedText => (_profile?.UpdatedAtUtc ?? _detected?.DetectedAtUtc)?.ToLocalTime().ToString("g") ?? "—";
    public string ClassTooltip => $"{CharacterName}\n{CharacterClass}\n{ServerText}";

    public IReadOnlyList<CharacterVisualItem> EquipmentSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> SkillItems { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> EmptySkillSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> EmptyStigmaSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> EmptyArcanaSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> TalentItems { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> DaevanionItems { get; private set; } = [];
    public IReadOnlyList<CharacterStatCard> StatCards { get; private set; } = [];

    public bool HasEquipmentData => _profile?.Gear.Count > 0;
    public bool HasSkillData => _profile?.Skills.Count > 0;
    public bool HasTalentData => _profile?.Talents.Count > 0;
    public bool HasDaevanionData => _profile?.Devotions.Count > 0;
    public bool HasStatsData => _profile?.Stats.Count > 0;
    public bool HasNoEquipmentData => !HasEquipmentData;
    public bool HasNoSkillData => !HasSkillData;
    public bool HasNoTalentData => !HasTalentData;
    public bool HasNoDaevanionData => !HasDaevanionData;
    public bool HasNoStatsData => !HasStatsData;
    public IReadOnlyList<CharacterTalent> Talents => _profile?.Talents ?? [];
    public IReadOnlyList<CharacterDevotion> Daevanion => _profile?.Devotions ?? [];
    public IReadOnlyList<CharacterStat> AdditionalStats => _profile?.Stats ?? [];

    private void RebuildCollections()
    {
        (string ResourceKey, string[] Aliases)[] slotDefinitions =
        [
            ("EquipmentSlotWeapon", ["weapon", "mainhand", "mainweapon"]),
            ("EquipmentSlotOffhand", ["offhand", "secondaryweapon"]),
            ("EquipmentSlotHead", ["head", "helmet"]),
            ("EquipmentSlotChest", ["chest", "torso", "armor"]),
            ("EquipmentSlotHands", ["hands", "gloves"]),
            ("EquipmentSlotLegs", ["legs", "pants"]),
            ("EquipmentSlotFeet", ["feet", "boots"]),
            ("EquipmentSlotShoulders", ["shoulders", "pauldrons"]),
            ("EquipmentSlotNecklace", ["necklace", "neck"]),
            ("EquipmentSlotEarring1", ["earring1", "leftearring"]),
            ("EquipmentSlotEarring2", ["earring2", "rightearring"]),
            ("EquipmentSlotRing1", ["ring1", "leftring"]),
            ("EquipmentSlotRing2", ["ring2", "rightring"]),
            ("EquipmentSlotBelt", ["belt", "waist"]),
        ];

        EquipmentSlots = slotDefinitions.Select(definition =>
        {
            CharacterGearItem? item = _profile?.Gear.FirstOrDefault(gear =>
                definition.Aliases.Contains(NormalizeSlot(gear.Slot), StringComparer.OrdinalIgnoreCase));
            string slotName = LocalizationService.Get(definition.ResourceKey);
            return new CharacterVisualItem(
                item?.Name ?? slotName,
                item is null ? null : item.Rarity,
                item?.IconUri?.ToString(),
                RarityColor(item?.Rarity),
                item is null ? slotName : BuildGearTooltip(item),
                item?.EnchantLevel is int enchant ? $"+{enchant}" : null);
        }).ToArray();

        SkillItems = (_profile?.Skills ?? []).Select(skill => new CharacterVisualItem(
            skill.Name,
            skill.Grade,
            skill.IconUri?.ToString(),
            "#45DFFF",
            $"{skill.Name}\n{LocalizationService.Get("CharacterLevel")}: {skill.Level}",
            skill.Level > 0 ? skill.Level.ToString() : null)).ToArray();

        EmptySkillSlots = CreateEmptySlots(16, "NoSkillsLoaded", "#315069");
        EmptyStigmaSlots = CreateEmptySlots(10, "NoStigmasLoaded", "#A783D8");
        EmptyArcanaSlots = CreateEmptySlots(8, "NoArcanaLoaded", "#D8B96A");
        TalentItems = (_profile?.Talents ?? []).Select(talent => new CharacterVisualItem(
            talent.Name,
            talent.Tree,
            talent.IconUri?.ToString(),
            "#45DFFF",
            $"{talent.Name}\n{talent.Rank}/{talent.MaxRank?.ToString() ?? "—"}",
            talent.Rank.ToString())).ToArray();
        DaevanionItems = (_profile?.Devotions ?? []).Select(node => new CharacterVisualItem(
            node.Name,
            node.Category,
            node.IconUri?.ToString(),
            "#D8B96A",
            $"{node.Name}\n{node.Category}",
            node.Rank.ToString())).ToArray();

        var cards = new List<CharacterStatCard>
        {
            new(LocalizationService.Get("CharacterLevel"), LevelText, "#45DFFF"),
            new(LocalizationService.Get("CharacterItemLevel"), GearScoreText, "#D8B96A"),
            new(LocalizationService.Get("LeaderboardServer"), ServerText, "#58E0B2"),
            new(LocalizationService.Get("CharacterUpdated"), UpdatedText, "#A783D8"),
        };
        StatCards = cards;
    }

    private static IReadOnlyList<CharacterVisualItem> CreateEmptySlots(int count, string tooltipKey, string color) =>
        Enumerable.Range(0, count)
            .Select(_ => new CharacterVisualItem(string.Empty, null, null, color,
                LocalizationService.Get(tooltipKey)))
            .ToArray();

    private static string BuildGearTooltip(CharacterGearItem item)
    {
        var lines = new List<string> { item.Name };
        if (!string.IsNullOrWhiteSpace(item.Rarity)) lines.Add(item.Rarity);
        if (item.ItemLevel is int level)
            lines.Add($"{LocalizationService.Get("CharacterItemLevel")}: {level}");
        if (item.EnchantLevel is int enchant) lines.Add($"+{enchant}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string RarityColor(string? rarity) => rarity?.Trim().ToLowerInvariant() switch
    {
        "legendary" or "eternal" => "#D8B96A",
        "epic" or "heroic" => "#A783D8",
        "rare" => "#4C9CFF",
        "uncommon" => "#58E0B2",
        _ => "#3B5874",
    };

    private static string NormalizeSlot(string? slot) =>
        new((slot ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());

    private void CharacterChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        _detected = DetectedCharacterService.Shared.Current;
        _profile = CharacterProfileService.Shared.Current;
        RebuildCollections();
        NotifyAll();
    });

    private void LanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RebuildCollections();
        NotifyAll();
    });

    private void NotifyAll()
    {
        foreach (string property in new[]
        {
            nameof(HasCharacter), nameof(HasNoCharacter), nameof(CharacterName), nameof(CharacterClass),
            nameof(ClassIconSource), nameof(LevelText), nameof(GearScoreText), nameof(ServerText),
            nameof(RegionText), nameof(VerificationText), nameof(UpdatedText), nameof(ClassTooltip),
            nameof(EquipmentSlots), nameof(SkillItems), nameof(EmptySkillSlots), nameof(EmptyStigmaSlots),
            nameof(EmptyArcanaSlots), nameof(StatCards), nameof(HasEquipmentData), nameof(HasSkillData),
            nameof(TalentItems), nameof(DaevanionItems),
            nameof(HasTalentData), nameof(HasDaevanionData), nameof(HasStatsData),
            nameof(HasNoEquipmentData), nameof(HasNoSkillData), nameof(HasNoTalentData),
            nameof(HasNoDaevanionData), nameof(HasNoStatsData), nameof(Talents), nameof(Daevanion),
            nameof(AdditionalStats),
        }) OnPropertyChanged(property);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        DetectedCharacterService.Shared.Changed -= CharacterChanged;
        CharacterProfileService.Shared.Changed -= CharacterChanged;
        LocalizationService.LanguageChanged -= LanguageChanged;
    }
}
