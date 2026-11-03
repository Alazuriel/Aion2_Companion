using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Phaseward;
using AION_2_Companion.Core.Leaderboards;
using AION_2_Companion.Views.Overlays;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public sealed record SubmissionParticipantRow(
    string Name, string ClassName, string Damage, string Dps, string Healing, string Hps);

/// <summary>
/// Desktop-only Leaderboard review. The server is the authority for ownership and approval.
/// Group mode uses VYREN's versioned group-v2 contract. The server keeps its own
/// feature flag, moderation and independent ownership validation.
/// </summary>
public partial class LeaderboardSubmissionDialog : Window
{
    private static readonly Dictionary<string, string> ClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gladiator"] = "Gladiator", ["templar"] = "Templar", ["assassin"] = "Assassin",
        ["ranger"] = "Ranger", ["sorcerer"] = "Sorcerer", ["spiritmaster"] = "Spiritmaster",
        ["cleric"] = "Cleric", ["chanter"] = "Chanter",
    };

    private readonly CancellationTokenSource _lifetime = new();
    private CombatLogEntry _entry = new();
    private MeterSnapshot _snapshot = new();
    private string _serverName = string.Empty;
    private string _region = string.Empty;
    private PlayerStat? _matchedPlayer;
    private VerifiedCharacterSummary? _matchedCharacter;
    private bool _checking;
    private bool _checkedVerification;
    private bool _submitting;
    private bool _submitted;
    private bool _isGroup;
    private string _verificationMessage = string.Empty;

    public LeaderboardSubmissionDialog()
    {
        InitializeComponent();
        Opened += async (_, _) => await RefreshVerifiedAsync();
        Closed += (_, _) => _lifetime.Cancel();
    }

    public LeaderboardSubmissionDialog(CombatLogEntry entry) : this()
    {
        _entry = entry;
        _snapshot = entry.ToSnapshot(bossOnly: true);
        (_serverName, _region) = ResolveServerInfo(entry);

        this.FindControl<TextBlock>("BossText")!.Text =
            string.IsNullOrWhiteSpace(entry.MobName) ? "—" : entry.MobName;
        this.FindControl<TextBlock>("DateText")!.Text =
            entry.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("DurationText")!.Text = FormatDuration(EffectiveDuration);
        this.FindControl<TextBlock>("ParticipantCountText")!.Text = _snapshot.Players.Count.ToString(CultureInfo.CurrentCulture);
        this.FindControl<TextBlock>("ServerText")!.Text =
            string.IsNullOrWhiteSpace(_serverName) ? L("UiSubmitServerUnavailable") : _serverName;
        this.FindControl<TextBlock>("ServerMetaText")!.Text = _entry.ServerId > 0
            ? $"{_region.ToUpperInvariant()} · ID {_entry.ServerId}"
            : L("UiSubmitNoNumericServerIdSavedThisFight");
        this.FindControl<ItemsControl>("ParticipantList")!.ItemsSource = GetParticipantRows();

        var players = _snapshot.Players;
        long damage = players.Sum(p => p.Damage);
        long healing = players.Sum(p => p.Healing);
        bool measuredDamage = players.Any(p => p.Damage > 0 || p.DamageEventCount > 0);
        bool measuredHealing = players.Any(p => p.Healing > 0 || p.HealingEventCount > 0);
        this.FindControl<TextBlock>("TeamDamageText")!.Text = measuredDamage ? Meter.Num(damage) : "—";
        this.FindControl<TextBlock>("TeamDpsText")!.Text = measuredDamage ? Meter.Num(damage / Math.Max(1, EffectiveDuration)) : "—";
        this.FindControl<TextBlock>("TeamHealingText")!.Text = measuredHealing ? Meter.Num(healing) : "—";
        this.FindControl<TextBlock>("TeamHpsText")!.Text = measuredHealing ? Meter.Num(healing / Math.Max(1, EffectiveDuration)) : "—";
        UpdatePersonalPreview();
        UpdateFormState();
    }

    private double EffectiveDuration => _entry.BossDuration > 0 ? _entry.BossDuration : _entry.Duration;
    private bool ValidFight => _entry.Completed && _entry.IsBossEncounter && EffectiveDuration is >= 3 and <= 7200;
    private bool ValidIdentity => _entry.ServerId != 0 && _serverName.Length is >= 2 and <= 50 &&
        _region is "eu" or "nae" or "naw" or "la" or "as";

    private List<SubmissionParticipantRow> GetParticipantRows()
    {
        return _snapshot.Players.OrderByDescending(p => p.Damage).ThenBy(p => p.Name)
            .Select((p, index) => new SubmissionParticipantRow(
                string.IsNullOrWhiteSpace(p.Name) ? $"Participant {index + 1:D2}" : p.Name,
                string.IsNullOrWhiteSpace(p.ClassName) ? "—" : p.ClassName,
                p.Damage > 0 || p.DamageEventCount > 0 ? Meter.Num(p.Damage) : "—",
                p.Damage > 0 || p.DamageEventCount > 0 ? Meter.Num(p.Damage / Math.Max(1, EffectiveDuration)) : "—",
                p.Healing > 0 || p.HealingEventCount > 0 ? Meter.Num(p.Healing) : "—",
                p.Healing > 0 || p.HealingEventCount > 0 ? Meter.Num(p.Healing / Math.Max(1, EffectiveDuration)) : "—"
            )).ToList();
    }

    private void ModeClick(object? sender, RoutedEventArgs e)
    {
        _isGroup = sender is Button { Tag: string tag } && tag == "group";
        this.FindControl<StackPanel>("IndividualPanel")!.IsVisible = !_isGroup;
        this.FindControl<StackPanel>("GroupPanel")!.IsVisible = _isGroup;
        this.FindControl<Button>("IndividualModeButton")!.Classes.Set("primary", !_isGroup);
        this.FindControl<Button>("GroupModeButton")!.Classes.Set("primary", _isGroup);
        this.FindControl<CheckBox>("ConsentBox")!.IsChecked = false;
        this.FindControl<CheckBox>("GroupPrivateNamesBox")!.IsChecked = false;
        this.FindControl<CheckBox>("ConsentBox")!.Content = L(
            _isGroup ? "UiSubmissionGroupPublicConsent" : "UiSubmissionConsent");
        UpdateFormState();
    }

    private void ConsentChanged(object? sender, RoutedEventArgs e) => UpdateFormState();

    private async void RefreshVerifiedClick(object? sender, RoutedEventArgs e) => await RefreshVerifiedAsync();

    private async Task RefreshVerifiedAsync()
    {
        if (_checking || _submitting || _submitted || _lifetime.IsCancellationRequested) return;
        _checking = true;
        _checkedVerification = false;
        _matchedPlayer = null;
        _matchedCharacter = null;
        _verificationMessage = L("UiSubmissionCheckingOwner");
        UpdatePersonalPreview();
        UpdateFormState();
        try
        {
            if (AccountService.Shared.Snapshot.State != AccountConnectionState.Connected)
            {
                _verificationMessage = L("UiSubmitConnectNexforgeAccountBeforeSubmitting");
                return;
            }
            if (!ValidIdentity)
            {
                _verificationMessage = L("UiSubmitVerifiedServerIdNameRegionAreMissingFromThisEncounterR");
                return;
            }
            using var api = new VerifiedCharactersApiClient();
            IReadOnlyList<VerifiedCharacterSummary> verified = await api.GetVerifiedAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _checkedVerification = true;
            (_matchedPlayer, _matchedCharacter) = MatchVerifiedFightParticipant(
                verified, _snapshot.Players, _entry.ServerId, _region);
            _verificationMessage = _matchedCharacter is null
                ? L("UiSubmissionNoMatchingVerified")
                : L("UiSubmissionOwnerMatched");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            // Never fall back to a locally selected or cached character after API failure.
            _checkedVerification = false;
            _verificationMessage = L("UiSubmissionVerificationUnavailable");
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _checking = false;
                UpdatePersonalPreview();
                UpdateFormState();
            }
        }
    }

    /// <summary>
    /// Only a server-verified AION2 identity in the same region and numeric server,
    /// with exactly one saved fight row, is accepted. No CurrentSelf, name-only, or top-DPS fallback.
    /// The backend must independently enforce verified ownership and the evidence-to-row mapping.
    /// </summary>
    private static (PlayerStat?, VerifiedCharacterSummary?) MatchVerifiedFightParticipant(
        IReadOnlyList<VerifiedCharacterSummary> verified, IReadOnlyList<PlayerStat> players,
        ushort serverId, string region)
    {
        var matches = (from owner in verified
                       where owner.Identity.ServerId == serverId &&
                             string.Equals(owner.Identity.Region, region, StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(owner.CharacterId)
                       from player in players
                       where !string.IsNullOrWhiteSpace(player.Name) &&
                             string.Equals(owner.Identity.Name.Trim(), player.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                       select (Player: player, Owner: owner)).ToList();
        return matches.Count == 1 ? (matches[0].Player, matches[0].Owner) : (null, null);
    }

    private void UpdatePersonalPreview()
    {
        var p = _matchedPlayer;
        this.FindControl<TextBlock>("VerifiedCharacterText")!.Text = _matchedCharacter?.Name ?? "—";
        this.FindControl<TextBlock>("MatchedLocationText")!.Text =
            _matchedCharacter?.DisplayLocation ?? "—";
        this.FindControl<TextBlock>("VerificationStatusText")!.Text = _verificationMessage;
        this.FindControl<TextBlock>("ClassText")!.Text = p is null ? "—" : ClassLabel(p.ClassName);
        this.FindControl<TextBlock>("DamageText")!.Text = p is null ? "—" : Meter.Num(p.Damage);
        this.FindControl<TextBlock>("DpsText")!.Text = p is null ? "—" : Meter.Num(p.Damage / Math.Max(1, EffectiveDuration));
        this.FindControl<TextBlock>("HealingText")!.Text = p is null ? "—" : Meter.Num(p.Healing);
        this.FindControl<TextBlock>("HpsText")!.Text = p is null ? "—" : Meter.Num(p.Healing / Math.Max(1, EffectiveDuration));
        this.FindControl<TextBlock>("EvidenceText")!.Text = p is null ? "—" :
            BuildEvidence(p) is { } evidence
                ? L("UiSubmissionRecordedHits").Replace("{0}", evidence.Hits.Count.ToString("N0", CultureInfo.CurrentCulture))
                : L("UiSubmissionSummaryOnly");
    }

    private void UpdateFormState()
    {
        if (_lifetime.IsCancellationRequested) return;
        var button = this.FindControl<Button>("SubmitButton")!;
        this.FindControl<Button>("RefreshVerifiedButton")!.IsEnabled = !_checking && !_submitting && !_submitted;
        if (_submitted || _submitting)
        {
            button.IsEnabled = false;
            return;
        }
        bool connected = AccountService.Shared.Snapshot.State == AccountConnectionState.Connected;
        bool validPlayer = _matchedPlayer is { Damage: > 0 and <= 5_000_000_000 };
        bool validClass = _matchedPlayer is not null && ClassKey(_matchedPlayer.ClassName).Length > 0;
        bool consent = this.FindControl<CheckBox>("ConsentBox")!.IsChecked == true;
        bool privateNotice = this.FindControl<CheckBox>("GroupPrivateNamesBox")!.IsChecked == true;
        bool groupStructureValid = TryBuildGroupRequest(_matchedPlayer, _matchedCharacter,
            Guid.Empty.ToString(), out _, out string groupError);
        bool ready = connected && ValidFight && ValidIdentity && _checkedVerification &&
            _matchedCharacter is not null && _matchedPlayer is not null && consent &&
            (_isGroup ? privateNotice && groupStructureValid : validPlayer && validClass);
        button.IsEnabled = ready;
        button.Content = L(_isGroup ? "UiSubmissionSendGroup" : "UiSubmissionSendIndividual");
        this.FindControl<TextBlock>("StatusText")!.Text = !connected
            ? L("UiSubmitConnectNexforgeAccountBeforeSubmitting")
            : !ValidFight ? L("UiSubmitOnlyCompletedBossEncounters3Seconds2HoursCanBeSubmitte")
            : !ValidIdentity ? L("UiSubmitVerifiedServerIdNameRegionAreMissingFromThisEncounterR")
            : _checking ? L("UiSubmissionCheckingOwner")
            : !_checkedVerification ? _verificationMessage
            : _matchedCharacter is null ? L("UiSubmissionNoMatchingVerified")
            : _isGroup && !groupStructureValid ? L(groupError)
            : !_isGroup && !validPlayer ? L("UiSubmissionOwnDamageUnavailable")
            : !_isGroup && !validClass ? L("UiSubmitSelectedPlayerSClassWasNotIdentifiedThisFight")
            : !consent ? L("UiSubmissionConsentRequired")
            : _isGroup && !privateNotice ? L("UiSubmissionPrivateNamesConsentRequired")
            : L(_isGroup ? "UiSubmissionGroupReady" : "UiSubmissionReady");
    }

    /// <summary>
    /// This app does not store a game-server kill verdict or capture completeness proof.
    /// 'unknown' and false are deliberately sent instead of guessed values.
    /// </summary>
    private bool TryBuildGroupRequest(
        PlayerStat? submitter, VerifiedCharacterSummary? verified, string submissionId,
        out GroupFightSubmitRequest? request, out string errorKey)
    {
        request = null;
        errorKey = "UiSubmissionGroupInvalidFight";
        if (!ValidFight || !ValidIdentity || submitter is null || verified is null) return false;

        var players = _snapshot.Players;
        if (players.Count is < 2 or > 24)
        {
            errorKey = "UiSubmissionGroupPartySize";
            return false;
        }
        if (_entry.MobTemplateId <= 0) { errorKey = "UiSubmissionGroupBossCatalogMissing"; return false; }
        // Never resolve by displayed boss name: approved NPC + canonical instance identity required.
        BossDefinition? boss = BossCatalog.Find(_entry.MobTemplateId);
        if (boss is null || !boss.TemplateIds.Contains(_entry.MobTemplateId) ||
            (!string.IsNullOrWhiteSpace(_entry.BossId) &&
             !string.Equals(_entry.BossId, boss.Id, StringComparison.OrdinalIgnoreCase)))
        {
            errorKey = "UiSubmissionGroupBossCatalogMissing";
            return false;
        }
        // An instance ID is optional under backend contract #35. Only pass
        // already-canonical local values; the server resolves null by NPC ID.
        // Never generate dungeon slugs from the boss's display/location name.
        string? instance = GroupInstanceResolver.Resolve(
            boss, _entry.MobTemplateId,
            PhasewardCatalog.Find(_entry.MobTemplateId)?.Instance);
        if (string.IsNullOrWhiteSpace(boss.Id) || boss.Id.Length > 100)
        {
            errorKey = "UiSubmissionGroupBossCatalogMissing";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<GroupFightParticipant>(players.Count);
        string? matchedId = null;
        long damageSum = 0, healingSum = 0;
        bool hasAllDamage = true, hasAllHealing = true;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerStat player = players[i];
            string name = player.Name.Trim();
            if (name.Length is < 2 or > 64 || !seen.Add(name) || player.Damage < 0 || player.Healing < 0)
            {
                errorKey = "UiSubmissionGroupParticipantUnreliable";
                return false;
            }
            long? damage = player.Damage > 0 || player.DamageEventCount > 0 ? player.Damage : null;
            long? healing = player.Healing > 0 || player.HealingEventCount > 0 ? player.Healing : null;
            hasAllDamage &= damage.HasValue;
            hasAllHealing &= healing.HasValue;
            if (damage.HasValue) damageSum = checked(damageSum + damage.Value);
            if (healing.HasValue) healingSum = checked(healingSum + healing.Value);
            string id = $"p{i + 1}";
            if (ReferenceEquals(submitter, player)) matchedId = id;
            rows.Add(new GroupFightParticipant(id, name,
                ReferenceEquals(submitter, player) && !string.IsNullOrWhiteSpace(verified.CharacterId)
                    ? verified.CharacterId : null,
                ClassKey(player.ClassName) is { Length: > 0 } cls ? cls : null,
                damage, healing));
        }
        if (matchedId is null)
        {
            errorKey = "UiSubmissionNoMatchingVerified";
            return false;
        }
        if (_entry.StartedAtUtc == default || EffectiveDuration <= 0 ||
            double.IsNaN(EffectiveDuration) || double.IsInfinity(EffectiveDuration))
            return false;
        long durationMs = (long)Math.Round(EffectiveDuration * 1000, MidpointRounding.AwayFromZero);
        if (durationMs is < 3000 or > 7_200_000) return false;
        DateTimeOffset start = _entry.StartedAtUtc;
        if (_entry.BossStartOffset > 0 && _entry.BossStartOffset < _entry.Duration)
            start = start.AddSeconds(_entry.BossStartOffset);

        request = new GroupFightSubmitRequest(submissionId, ChangelogService.GetCurrentVersion(),
            true, true, "group-v2", matchedId,
            new GroupFightData(_entry.MobName, boss.Id, instance, _entry.MobTemplateId,
                _region, _entry.ServerId,
                start.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                durationMs, "unknown", false,
                hasAllDamage ? damageSum : null, hasAllHealing ? healingSum : null), rows);
        return true;
    }

    // Idempotency across UI retries/restarts: persist one UUID for the same exact request data.
    // If an encounter or client version changes, use a new UUID rather than replaying different bytes.
    private string GetOrCreateGroupSubmissionId(GroupFightSubmitRequest candidate)
    {
        GroupFightSubmitRequest unsigned = candidate with { ClientSubmissionId = string.Empty };
        byte[] source = JsonSerializer.SerializeToUtf8Bytes(unsigned);
        string fingerprint = Convert.ToHexString(SHA256.HashData(source));
        string key = "group-v2:" + fingerprint;
        if (_entry.LeaderboardSubmissionIds.TryGetValue(key, out string? existing) &&
            Guid.TryParse(existing, out _))
            return existing;
        string submissionId = Guid.NewGuid().ToString();
        _entry.LeaderboardSubmissionIds[key] = submissionId;
        if (!CombatLogStore.Shared.UpdateMetadata(_entry))
        {
            _entry.LeaderboardSubmissionIds.Remove(key);
            throw new IOException("Unable to persist the group submission ID; nothing was uploaded.");
        }
        return submissionId;
    }

    private async void SubmitClick(object? sender, RoutedEventArgs e)
    {
        if (_submitted || _submitting || this.FindControl<Button>("SubmitButton")?.IsEnabled != true)
            return;
        if (_isGroup)
        {
            await SubmitGroupAsync();
            return;
        }
        PlayerStat? player = _matchedPlayer;
        VerifiedCharacterSummary? verified = _matchedCharacter;
        if (player is null || verified is null || !ValidFight || !ValidIdentity)
            return;
        string classKey = ClassKey(player.ClassName);
        if (classKey.Length == 0) return;
        _submitting = true;
        UpdateFormState();
        var button = this.FindControl<Button>("SubmitButton")!;
        var status = this.FindControl<TextBlock>("StatusText")!;
        button.Content = L("UiSubmitSubmitting");
        status.Text = L("UiSubmissionCheckingOwner");
        try
        {
            // Recheck server authority immediately before uploading. Session and ownership may have changed.
            using (var api = new VerifiedCharactersApiClient())
            {
                var fresh = await api.GetVerifiedAsync(_lifetime.Token);
                if (_lifetime.IsCancellationRequested) return;
                var (freshPlayer, freshOwner) = MatchVerifiedFightParticipant(
                    fresh, _snapshot.Players, _entry.ServerId, _region);
                if (!ReferenceEquals(freshPlayer, player) || freshOwner is null ||
                    freshOwner.Identity.CacheKey != verified.Identity.CacheKey ||
                    freshOwner.CharacterId != verified.CharacterId)
                {
                    _matchedPlayer = null;
                    _matchedCharacter = null;
                    _verificationMessage = L("UiSubmissionOwnershipChanged");
                    status.Text = _verificationMessage;
                    return;
                }
            }

            long durationMs = (long)Math.Round(EffectiveDuration * 1000, MidpointRounding.AwayFromZero);
            string localKey = string.Join('\u001f', "individual", verified.Identity.CacheKey,
                _entry.Id, classKey, _entry.MobName,
                player.Damage.ToString(CultureInfo.InvariantCulture),
                durationMs.ToString(CultureInfo.InvariantCulture));
            if (!_entry.LeaderboardSubmissionIds.TryGetValue(localKey, out string? submissionId) ||
                !Guid.TryParse(submissionId, out _))
            {
                submissionId = Guid.NewGuid().ToString();
                _entry.LeaderboardSubmissionIds[localKey] = submissionId;
                CombatLogStore.Shared.UpdateMetadata(_entry);
            }
            var request = new LeaderboardSubmitRequest(
                submissionId, _entry.MobName, player.Name, classKey, _serverName,
                player.Damage, durationMs, ChangelogService.GetCurrentVersion(),
                _entry.StartedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                PublicConsent: true, Region: _region, ServerId: _entry.ServerId,
                CombatEvidence: BuildEvidence(player));
            LeaderboardSubmitResult result = await LeaderboardService.Shared.SubmitAsync(request, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _submitted = true;
            string serverStatus = result.Entry.Status?.ToLowerInvariant() ?? "pending";
            status.Text = result.Duplicate
                ? L("UiSubmissionAlreadyReceived")
                : serverStatus switch
                {
                    "approved" => L("UiSubmitApprovedServerAcceptedThisResultPublicLeaderboard"),
                    "pending" => L("UiSubmitSubmittedSuccessfullyPendingModeratorReview"),
                    "rejected" => L("UiSubmitServerRejectedThisResultSeeSubmissionsReviewNote"),
                    _ => $"{L("UiSubmissionServerStatus")}: {serverStatus}",
                };
            button.IsVisible = false;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (LeaderboardApiException ex)
        {
            status.Text = $"HTTP {ex.StatusCode} · {ex.Code}: {ex.Message}";
        }
        catch (Exception)
        {
            status.Text = L("UiSubmissionRequestFailed");
        }
        finally
        {
            _submitting = false;
            if (!_lifetime.IsCancellationRequested && !_submitted)
            {
                string message = status.Text ?? string.Empty;
                UpdatePersonalPreview();
                UpdateFormState();
                if (!string.IsNullOrWhiteSpace(message)) status.Text = message;
            }
        }
    }

    private async Task SubmitGroupAsync()
    {
        if (_submitting || _submitted || !_isGroup ||
            this.FindControl<CheckBox>("ConsentBox")!.IsChecked != true ||
            this.FindControl<CheckBox>("GroupPrivateNamesBox")!.IsChecked != true)
            return;
        PlayerStat? originalPlayer = _matchedPlayer;
        VerifiedCharacterSummary? originalVerified = _matchedCharacter;
        if (originalPlayer is null || originalVerified is null) return;
        var button = this.FindControl<Button>("SubmitButton")!;
        var status = this.FindControl<TextBlock>("StatusText")!;
        _submitting = true;
        UpdateFormState();
        button.Content = L("UiSubmitSubmitting");
        status.Text = L("UiSubmissionCheckingOwner");
        try
        {
            using (var verifier = new VerifiedCharactersApiClient())
            {
                IReadOnlyList<VerifiedCharacterSummary> verified =
                    await verifier.GetVerifiedAsync(_lifetime.Token);
                if (_lifetime.IsCancellationRequested) return;
                var (player, owner) = MatchVerifiedFightParticipant(
                    verified, _snapshot.Players, _entry.ServerId, _region);
                if (!ReferenceEquals(player, originalPlayer) || owner is null ||
                    owner.Identity.CacheKey != originalVerified.Identity.CacheKey ||
                    owner.CharacterId != originalVerified.CharacterId)
                {
                    _matchedPlayer = null;
                    _matchedCharacter = null;
                    _verificationMessage = L("UiSubmissionOwnershipChanged");
                    status.Text = _verificationMessage;
                    return;
                }
            }
            if (!TryBuildGroupRequest(originalPlayer, originalVerified,
                    Guid.Empty.ToString(), out var candidate, out string error) || candidate is null)
            {
                status.Text = L(error);
                return;
            }
            string id = GetOrCreateGroupSubmissionId(candidate);
            GroupFightSubmitRequest request = candidate with { ClientSubmissionId = id };
            using var groupApi = new GroupFightApiClient();
            GroupFightSubmitResult result = await groupApi.SubmitAsync(request, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _submitted = true;
            status.Text = result.Duplicate ? L("UiSubmissionAlreadyReceived")
                : result.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)
                    ? L("UiSubmitSubmittedSuccessfullyPendingModeratorReview")
                    : L("UiSubmissionServerStatus") + ": " + result.Status;
            button.IsVisible = false;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (LeaderboardApiException ex)
        {
            status.Text = ex.Code switch
            {
                "feature_disabled" => L("UiSubmissionGroupFeatureDisabled"),
                "catalog_unknown" => L("UiSubmissionGroupCatalogUnknown"),
                "catalog_ambiguous" => L("UiSubmissionGroupCatalogAmbiguous"),
                "catalog_mismatch" => L("UiSubmissionGroupCatalogMismatch"),
                "instance_id_required" => L("UiSubmissionGroupServerUpdateRequired"),
                "character_not_verified" => L("UiSubmissionNoMatchingVerified"),
                "idempotency_conflict" => L("UiSubmissionGroupConflict"),
                "request_too_large" => L("UiSubmissionGroupTooLarge"),
                "submission_limit" => L("UiSubmissionGroupRateLimited"),
                _ => $"HTTP {ex.StatusCode} · {ex.Code}: {ex.Message}",
            };
        }
        catch (HttpRequestException)
        {
            status.Text = L("UiSubmissionGroupNetworkFailure");
        }
        catch (Exception)
        {
            status.Text = L("UiSubmissionRequestFailed");
        }
        finally
        {
            _submitting = false;
            if (!_lifetime.IsCancellationRequested && !_submitted)
            {
                string message = status.Text ?? string.Empty;
                UpdateFormState();
                if (!string.IsNullOrWhiteSpace(message)) status.Text = message;
            }
        }
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close(_submitted);

    private static LeaderboardCombatEvidence? BuildEvidence(PlayerStat player)
    {
        if (player.Events.Count < 3) return null;
        var events = player.Events.Where(hit => !hit.IsHeal).OrderBy(hit => hit.Time).ToList();
        if (events.Count is < 3 or > 10000) return null;
        var hits = new List<long[]>(events.Count);
        long damageTotal = 0;
        foreach (SkillEvent hit in events)
        {
            if (hit.Amount <= 0 || double.IsNaN(hit.Time) || double.IsInfinity(hit.Time) || hit.Time < 0)
                return null;
            long offsetMs = (long)Math.Round(hit.Time * 1000, MidpointRounding.AwayFromZero);
            long durationMs = (long)Math.Round(player.Duration * 1000, MidpointRounding.AwayFromZero);
            if (offsetMs > durationMs || hit.Amount > player.Damage - damageTotal) return null;
            damageTotal += hit.Amount;
            hits.Add(new[] { offsetMs, hit.Amount });
        }
        if (damageTotal != player.Damage) return null;
        var evidence = new LeaderboardCombatEvidence(1, hits);
        return JsonSerializer.SerializeToUtf8Bytes(evidence).Length <= 235_000 ? evidence : null;
    }

    private static (string Name, string Region) ResolveServerInfo(CombatLogEntry entry)
    {
        if (entry.ServerId == 0) return (string.Empty, string.Empty);
        string name = entry.ServerName?.Trim() ?? string.Empty;
        string region = string.Empty;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData", "AION2_Servers.nfg");
            if (File.Exists(path) && ServersDatabase.Load(path).GetById(entry.ServerId) is { } server)
            {
                if (!string.IsNullOrWhiteSpace(server.Name)) name = server.Name.Trim();
                region = NormalizeRegion(server.Region);
            }
        }
        catch (Exception) { /* Non-critical optional NFG local lookup. */ }
        if (DetectedCharacterService.Shared.Current is { } detected && detected.ServerId == entry.ServerId)
        {
            if (string.IsNullOrWhiteSpace(name)) name = detected.ServerName.Trim();
            if (string.IsNullOrWhiteSpace(region)) region = NormalizeRegion(detected.Region);
        }
        if (CharacterCenterService.Shared.CurrentSelf is { } self && self.ServerId == entry.ServerId &&
            string.IsNullOrWhiteSpace(region))
            region = NormalizeRegion(self.Region);
        return (name, region);
    }

    private static string L(string key) => LocalizationService.Get(key);

    private static string ClassLabel(string? name)
    {
        string key = ClassKey(name);
        return ClassNames.TryGetValue(key, out string? label) ? label : L("UiSubmitClassUnknown");
    }

    private static string ClassKey(string? value)
    {
        string name = new((value ?? string.Empty).Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant).ToArray());
        return name == "elementalist" ? "spiritmaster" : ClassNames.ContainsKey(name) ? name : string.Empty;
    }

    private static string NormalizeRegion(string? value)
    {
        string region = (value ?? string.Empty).Trim().ToLowerInvariant();
        return region is "eu" or "nae" or "naw" or "la" or "as" ? region : string.Empty;
    }

    private static string FormatDuration(double seconds)
    {
        int total = Math.Max(0, (int)Math.Round(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}
