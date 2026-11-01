using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.Characters.Verification;
using AION_2_Companion.Core.GameData;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

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
    private readonly DispatcherTimer _cooldownTimer;
    private bool _isRefreshing;
    private string _refreshStatus = string.Empty;

    public CharacterDetailsViewModel()
    {
        DetectedCharacterService.Shared.Changed += CharacterChanged;
        CharacterProfileService.Shared.Changed += CharacterChanged;
        CharacterVerificationService.Shared.Changed += VerificationChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _profile is not null && !IsRefreshing && RefreshRemaining <= TimeSpan.Zero);
        _cooldownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => NotifyCooldown());
        _cooldownTimer.Start();
        RebuildCollections();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IAsyncRelayCommand RefreshCommand { get; }

    public CharacterIdentity? CurrentIdentity => _profile is { ServerId: > 0 } profile &&
        !string.IsNullOrWhiteSpace(profile.Name)
        ? new CharacterIdentity(profile.Region, profile.ServerId, profile.Name)
        : _detected is { ServerId: > 0 } detected && !string.IsNullOrWhiteSpace(detected.Name)
            ? new CharacterIdentity(detected.Region, detected.ServerId.Value, detected.Name)
            : null;
    public bool CanVerify => CurrentIdentity is not null;
    public bool IsVerified => CurrentIdentity is { } identity && CharacterVerificationService.Shared.IsVerified(identity);
    public string VerifyButtonText => IsVerified ? LocalizationService.Get("VerificationVerifiedBadge") : LocalizationService.Get("VerificationVerifyButton");
    public bool IsDeveloperMode => Program.DeveloperMode;
    public bool ShowUnderForge => !IsDeveloperMode;
    public bool HasCharacter => _detected is not null || _profile is not null;
    public bool HasNoCharacter => !HasCharacter;
    public string CharacterName => _profile?.Name ?? _detected?.Name ?? "—";
    public string CharacterClass => _profile?.ClassName ?? _detected?.ClassName ?? "—";
    public string? ClassIconSource => _profile?.ImageUri?.ToString() ?? _detected?.ClassIconPath;
    public string? ProfileImageUrl => _profile?.ImageUri?.ToString();
    public string LevelText => (_profile?.Level ?? _detected?.Level) is int level && level > 0
        ? level.ToString()
        : "—";
    public string GearScoreText => (_profile?.ItemLevel is int itemLevel && itemLevel > 0)
        ? itemLevel.ToString()
        : _detected?.GearScore?.ToString() ?? "—";
    public string ServerText => !string.IsNullOrWhiteSpace(_profile?.ServerName)
        ? _profile!.ServerName
        : !string.IsNullOrWhiteSpace(_detected?.ServerName)
            ? _detected!.ServerName
            : _detected?.ServerId is ushort serverId
                ? LocalizationService.Format("ServerIdUnconfirmed", serverId)
                : LocalizationService.Get("UnknownServer");
    public string RegionText => string.IsNullOrWhiteSpace(_profile?.Region) ? "—" : _profile!.Region;
    public string VerificationText => IsVerified ? LocalizationService.Get("VerificationVerifiedBadge")
        : LocalizationService.Get("Unverified");
    public string VerificationHintText => IsVerified ? LocalizationService.Get("VerificationTrustNote") : LocalizationService.Get("UnverifiedHint");
    public string UpdatedText => (_profile?.UpdatedAtUtc ?? _detected?.DetectedAtUtc)?.ToLocalTime().ToString("g") ?? "—";
    public string ClassTooltip => $"{CharacterName}\n{CharacterClass}\n{ServerText}";
    public string CombatPowerText => _profile?.CombatPower is int cp ? cp.ToString("N0") : "—";
    public string DataStateText => _profile?.IsOffline == true ? LocalizationService.Get("CharacterCachedOffline") : _profile?.IsCached == true ? LocalizationService.Get("CharacterCached") : _profile?.IsPartial == true ? LocalizationService.Get("CharacterPartial") : LocalizationService.Get("CharacterLiveData");
    public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRefresh)); RefreshCommand.NotifyCanExecuteChanged(); } }
    public string RefreshStatus { get => _refreshStatus; private set { _refreshStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRefreshStatus)); } }
    public bool HasRefreshStatus => !string.IsNullOrWhiteSpace(RefreshStatus);
    private string RefreshKey => _profile is null ? "profile:none" : $"profile:{_profile.Region.ToLowerInvariant()}|{_profile.ServerId}|{_profile.Name.ToLowerInvariant()}";
    public TimeSpan RefreshRemaining => CharacterCenterService.Shared.Cooldown.Remaining(RefreshKey);
    public bool CanRefresh => _profile is not null && !IsRefreshing && RefreshRemaining <= TimeSpan.Zero;
    public string RefreshText => RefreshRemaining > TimeSpan.Zero ? $"{LocalizationService.Get("CharacterRefresh")} ({Math.Ceiling(RefreshRemaining.TotalSeconds):0})" : LocalizationService.Get("CharacterRefresh");

    public IReadOnlyList<CharacterVisualItem> EquipmentSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> SkillItems { get; private set; } = [];
    public string SkillCatalogStatus { get; private set; } = string.Empty;
    public IReadOnlyList<CharacterVisualItem> ArcanaItems { get; private set; } = [];
    public bool HasArcanaData => ArcanaItems.Count > 0;
    public bool HasNoArcanaData => !HasArcanaData;
    public IReadOnlyList<CharacterVisualItem> EmptySkillSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> EmptyStigmaSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> EmptyArcanaSlots { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> TalentItems { get; private set; } = [];
    public IReadOnlyList<CharacterVisualItem> DaevanionItems { get; private set; } = [];
    public IReadOnlyList<CharacterStatCard> StatCards { get; private set; } = [];

    public bool HasEquipmentData => _profile?.Gear.Any(item => !IsArcanaSlot(item)) == true;
    public bool HasSkillData => SkillItems.Count > 0;
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

        IReadOnlyList<CharacterGearItem> gear = _profile?.Gear ?? [];
        CharacterGearItem[] normalGear = gear.Where(item => !IsArcanaSlot(item)).ToArray();
        CharacterGearItem[] arcanaGear = gear.Where(IsArcanaSlot).ToArray();
        EquipmentSlots = normalGear.Length > 0 ? normalGear.Select(item => new CharacterVisualItem(
            item.Name, item.Rarity, item.IconUri?.ToString(), RarityColor(item.Rarity),
            BuildGearTooltip(item), item.EnchantLevel is int enchant ? $"+{enchant}" : null)).ToArray()
            : slotDefinitions.Select(definition => new CharacterVisualItem(
                LocalizationService.Get(definition.ResourceKey), null, null, "#315069",
                LocalizationService.Get(definition.ResourceKey))).ToArray();

        ArcanaItems = arcanaGear.Select(item => new CharacterVisualItem(
            item.Name, item.Rarity, item.IconUri?.ToString(), RarityColor(item.Rarity),
            BuildGearTooltip(item), item.EnchantLevel is int enchant ? $"+{enchant}" : null)).ToArray();

        // Display ONLY skills actually present in the character API payload.
        // NFG is a supplementary catalogue: it must never generate additional UI entries
        // or invent a learned level for the currently selected character.
        string? cls = _profile?.ClassName ?? _detected?.ClassName;
        IReadOnlyList<SkillDefinition> definitions = CharacterSkillCatalog.ForClass(cls, out string catalogStatus);
        SkillCatalogStatus = catalogStatus;
        IReadOnlyList<CharacterSkill> apiSkills = _profile?.Skills ?? [];
        SkillItems = apiSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill.SkillId) ||
                            !string.IsNullOrWhiteSpace(skill.Name))
            .GroupBy(skill => !string.IsNullOrWhiteSpace(skill.SkillId) ? skill.SkillId : skill.Name,
                     StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(skill => skill.Level).First())
            .Select(skill =>
            {
                SkillDefinition? definition = MatchSkillDefinition(definitions, skill);
                string? localIcon = CharacterSkillCatalog.LocalIcon(definition?.Icon)
                                    ?? CharacterSkillCatalog.LocalIcon(skill.IconUri?.AbsolutePath);
                string name = definition?.Name is { Length: > 0 } nameFromNfg ? nameFromNfg : skill.Name;
                string tooltip = BuildSkillTooltip(name, skill, definition);
                // A missing or zero API level is unknown, not a real level 0 or a reason
                // to show "max level" as if the skill had been learned.
                string? levelBadge = skill.Level > 0 ? $"Lv. {skill.Level}" : null;
                return new CharacterVisualItem(name, skill.Category ?? skill.Grade,
                    localIcon, "#45DFFF", tooltip, levelBadge);
            }).ToArray();

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
            node.CompletionPercent is int percent ? $"{percent}%" : node.Rank.ToString())).ToArray();

        var cards = new List<CharacterStatCard>
        {
            new(LocalizationService.Get("CharacterLevel"), LevelText, "#45DFFF"),
            new(LocalizationService.Get("CharacterCombatPower"), CombatPowerText, "#FFB86B"),
            new(LocalizationService.Get("CharacterItemLevel"), GearScoreText, "#D8B96A"),
            new(LocalizationService.Get("LeaderboardServer"), ServerText, "#58E0B2"),
            new(LocalizationService.Get("CharacterUpdated"), UpdatedText, "#A783D8"),
        };
        if (!string.IsNullOrWhiteSpace(_profile?.GuildName)) cards.Add(new(LocalizationService.Get("CharacterGuild"), _profile.GuildName!, "#58E0B2"));
        if (!string.IsNullOrWhiteSpace(_profile?.TitleName)) cards.Add(new(LocalizationService.Get("CharacterTitle"), _profile.TitleName!, "#D8B96A"));
        if (!string.IsNullOrWhiteSpace(_profile?.PetName)) cards.Add(new(LocalizationService.Get("CharacterPet"), _profile.PetName!, "#A783D8"));
        if (!string.IsNullOrWhiteSpace(_profile?.WingName)) cards.Add(new(LocalizationService.Get("CharacterWing"), _profile.WingName!, "#45DFFF"));
        StatCards = cards;
    }

    private async Task RefreshAsync()
    {
        CharacterProfile? current = _profile;
        if (current is null || !CharacterCenterService.Shared.Cooldown.TryBegin(RefreshKey, out IDisposable? lease, out _)) return;
        CharacterCenterService.Shared.Cooldown.StartCooldown(RefreshKey);
        IsRefreshing = true;
        RefreshStatus = LocalizationService.Get("CharacterLoading");
        try
        {
            var identity = new CharacterIdentity(current.Region, current.ServerId, current.Name);
            CharacterLoadResult result = await CharacterCenterService.Shared.LoadProfileAsync(identity, "full", true, CancellationToken.None);
            // A slow refresh must not overwrite a newly selected character.
            if (CharacterCenterService.Shared.CurrentSelf?.CacheKey != identity.CacheKey) return;
            CharacterProfileService.Shared.Replace(CharacterProfileMapper.Map(result));
            RefreshStatus = result.Offline ? LocalizationService.Get("CharacterCachedOffline") : string.Empty;
        }
        catch (CharacterApiException ex)
        {
            if (ex.RetryAfter is { } retry) CharacterCenterService.Shared.Cooldown.StartCooldown(RefreshKey, retry);
            RefreshStatus = ex.Message;
        }
        catch { RefreshStatus = LocalizationService.Get("CharacterUnavailable"); }
        finally { lease?.Dispose(); IsRefreshing = false; }
    }

    private void NotifyCooldown()
    {
        OnPropertyChanged(nameof(RefreshRemaining));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(CanRefresh));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private static SkillDefinition? MatchSkillDefinition(
        IReadOnlyList<SkillDefinition> definitions, CharacterSkill skill)
    {
        SkillDefinition? byId = definitions.FirstOrDefault(def =>
            !string.IsNullOrWhiteSpace(skill.SkillId) &&
            def.Id.Equals(skill.SkillId, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;

        // Name lookup is an optional fallback when the official API uses a different ID.
        if (string.IsNullOrWhiteSpace(skill.Name)) return null;
        return definitions.FirstOrDefault(def =>
            def.Name.Equals(skill.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSkillTooltip(string name, CharacterSkill skill, SkillDefinition? definition)
    {
        var lines = new List<string> { name };
        if (skill.Level > 0)
        {
            lines.Add(definition?.MaxLevel is > 0
                ? $"Lv. {skill.Level} / {definition.MaxLevel}"
                : $"Lv. {skill.Level}");
        }
        else
        {
            lines.Add("Szint: nincs megbízható API-adat");
            if (definition?.MaxLevel is > 0) lines.Add($"Max. szint: {definition.MaxLevel}");
        }

        if (!string.IsNullOrWhiteSpace(definition?.Description))
        {
            lines.Add(string.Empty);
            lines.Add(WrapTooltipText(definition.Description));
        }

        if (definition?.Specialties is { Count: > 0 } specialties)
        {
            lines.Add(string.Empty);
            lines.Add("SPECIALIZATIONS");
            foreach (SkillSpecialty specialty in specialties)
            {
                string requirement = specialty.Level is > 0 ? $"Lv. {specialty.Level}: " : "";
                string description = string.IsNullOrWhiteSpace(specialty.Description)
                    ? "Részletes leírás nincs az NFG-ben"
                    : specialty.Description;
                lines.Add(WrapTooltipText($"• {requirement}{description}", 62));
            }
        }

        // The example Dodge has specialties: []: do not show a misleading
        // empty-specializations warning for a skill where none is defined.
        return string.Join(Environment.NewLine, lines);
    }

    private static string WrapTooltipText(string text, int lineWidth = 68)
    {
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        string current = string.Empty;
        foreach (string word in words)
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > lineWidth)
            {
                lines.Add(current);
                current = word;
            }
            else current = current.Length == 0 ? word : current + " " + word;
        }
        if (current.Length > 0) lines.Add(current);
        return string.Join(Environment.NewLine, lines);
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

    private static bool IsArcanaSlot(CharacterGearItem item) =>
        (item.Slot?.Contains("arcana", StringComparison.OrdinalIgnoreCase) == true) ||
        (item.Slot?.Contains("card", StringComparison.OrdinalIgnoreCase) == true) ||
        (item.Slot?.Contains("deck", StringComparison.OrdinalIgnoreCase) == true) ||
        (item.IconUri?.AbsoluteUri.Contains("Arcana_Card", StringComparison.OrdinalIgnoreCase) == true);

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

    private void VerificationChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(NotifyAll);

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
            nameof(RegionText), nameof(VerificationText), nameof(VerificationHintText), nameof(UpdatedText), nameof(ClassTooltip),
            nameof(CurrentIdentity), nameof(CanVerify), nameof(IsVerified), nameof(VerifyButtonText), nameof(ProfileImageUrl),
            nameof(CombatPowerText), nameof(DataStateText), nameof(RefreshText), nameof(CanRefresh),
            nameof(EquipmentSlots), nameof(SkillItems), nameof(SkillCatalogStatus),
            nameof(ArcanaItems), nameof(HasArcanaData), nameof(HasNoArcanaData), nameof(EmptySkillSlots), nameof(EmptyStigmaSlots),
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
        _cooldownTimer.Stop();
        DetectedCharacterService.Shared.Changed -= CharacterChanged;
        CharacterProfileService.Shared.Changed -= CharacterChanged;
        CharacterVerificationService.Shared.Changed -= VerificationChanged;
        LocalizationService.LanguageChanged -= LanguageChanged;
    }
}
