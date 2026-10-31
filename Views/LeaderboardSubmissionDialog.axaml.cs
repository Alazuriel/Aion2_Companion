using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Leaderboards;
using AION_2_Companion.Views.Overlays;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AION_2_Companion.Views;

public sealed record LeaderboardClassOption(string Key, string Label)
{
    public override string ToString() => Label;
}

public sealed record LeaderboardPlayerOption(PlayerStat Player)
{
    public override string ToString() => Player.Name;
}

/// <summary>
/// Submission review, not an editable data-entry form. Character/class/server are
/// determined from the saved fight, capture identity and the NFG server registry.
/// The API independently decides verification and moderation status.
/// </summary>
public partial class LeaderboardSubmissionDialog : Window
{
    private static readonly LeaderboardClassOption[] ClassOptions =
    {
        new("gladiator", "Gladiator"),
        new("templar", "Templar"),
        new("assassin", "Assassin"),
        new("ranger", "Ranger"),
        new("sorcerer", "Sorcerer"),
        new("spiritmaster", "Spiritmaster / Elementalist"),
        new("cleric", "Cleric"),
        new("chanter", "Chanter"),
    };

    private readonly CombatLogEntry _entry = new();
    private readonly MeterSnapshot _snapshot = new();
    private readonly string _serverName = string.Empty;
    private readonly string _region = string.Empty;
    private bool _submitted;
    private bool _submitting;

    public LeaderboardSubmissionDialog()
    {
        InitializeComponent();
    }

    public LeaderboardSubmissionDialog(CombatLogEntry entry) : this()
    {
        _entry = entry;
        _snapshot = entry.ToSnapshot(bossOnly: true);
        (_serverName, _region) = ResolveServerInfo(entry);

        this.FindControl<TextBlock>("BossText")!.Text = entry.MobName;
        this.FindControl<TextBlock>("DateText")!.Text =
            entry.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            + " · saved encounter";
        this.FindControl<TextBlock>("DurationText")!.Text = FormatDuration(EffectiveDuration);
        this.FindControl<TextBlock>("ServerText")!.Text =
            string.IsNullOrEmpty(_serverName) ? "Server unavailable" : _serverName;
        this.FindControl<TextBlock>("ServerMetaText")!.Text =
            entry.ServerId != 0
                ? $"Server ID {entry.ServerId}  ·  {(string.IsNullOrWhiteSpace(_region) ? "Region unknown" : _region.ToUpperInvariant())}"
                : "No numeric server ID saved in this fight";

        var players = _snapshot.Players
            .Where(player => player.Damage > 0)
            .OrderByDescending(player => player.Damage)
            .Select(player => new LeaderboardPlayerOption(player))
            .ToList();
        ComboBox playerBox = this.FindControl<ComboBox>("PlayerBox")!;
        playerBox.ItemsSource = players;

        // Do not preselect the highest-DPS group member as though it were the account owner's character.
        string? ownName = CharacterCenterService.Shared.CurrentSelf is { } self &&
            self.ServerId == entry.ServerId ? self.Name : null;
        if (string.IsNullOrWhiteSpace(ownName) &&
            DetectedCharacterService.Shared.Current is { } detected &&
            detected.ServerId == entry.ServerId)
            ownName = detected.Name;
        LeaderboardPlayerOption? own = players.FirstOrDefault(p =>
            string.Equals(p.Player.Name, ownName, StringComparison.OrdinalIgnoreCase));
        playerBox.SelectedItem = own;

        UpdateSelectedPlayer();
        UpdateFormState();
    }

    private double EffectiveDuration => _entry.BossDuration > 0 ? _entry.BossDuration : _entry.Duration;
    private PlayerStat? SelectedPlayer =>
        (this.FindControl<ComboBox>("PlayerBox")!.SelectedItem as LeaderboardPlayerOption)?.Player;

    private LeaderboardClassOption? SelectedClass =>
        ClassOptions.FirstOrDefault(option => option.Key == NormalizeClass(SelectedPlayer?.ClassName ?? string.Empty));

    private bool IsValidIdentity => _entry.ServerId != 0 &&
        _serverName.Length is >= 2 and <= 50 &&
        _region is "eu" or "nae" or "naw" or "la" or "as";

    private void PlayerChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedPlayer();
        UpdateFormState();
    }

    private void ConsentChanged(object? sender, RoutedEventArgs e) => UpdateFormState();

    private void UpdateSelectedPlayer()
    {
        PlayerStat? player = SelectedPlayer;
        this.FindControl<TextBlock>("DamageText")!.Text =
            player is null ? "—" : Meter.Num(player.Damage);
        this.FindControl<TextBlock>("DpsText")!.Text =
            player is null ? "—" : Meter.Num(player.Damage / Math.Max(0.001, EffectiveDuration)) + "/s";
        this.FindControl<TextBlock>("ClassText")!.Text = SelectedClass?.Label ?? "Class unknown";

        LeaderboardCombatEvidence? evidence = player is null ? null : BuildEvidence(player);
        this.FindControl<TextBlock>("EvidenceText")!.Text = evidence is null
            ? "Summary only · manual review"
            : $"{evidence.Hits.Count:N0} recorded damage hits";
        this.FindControl<TextBlock>("EvidenceHintText")!.Text = evidence is null
            ? "The stored hits do not fully reconcile with this player’s damage. No synthetic evidence will be generated; the server can review the summary."
            : "Compact hit timestamps and amounts will be sent. Verified auto-accept is possible only if the SERVER enables it and all checks pass.";
        this.FindControl<TextBlock>("ReviewText")!.Text = "Server decides · pending / auto-check";
    }

    private void UpdateFormState()
    {
        if (_submitted || _submitting)
            return;
        bool connected = AccountService.Shared.Snapshot.State == AccountConnectionState.Connected;
        bool validFight = _entry.Completed && _entry.IsBossEncounter && EffectiveDuration is >= 3 and <= 7200;
        bool validPlayer = SelectedPlayer is { Damage: > 0 and <= 5_000_000_000 };
        bool validClass = SelectedClass is not null;
        bool consent = this.FindControl<CheckBox>("ConsentBox")!.IsChecked == true;
        this.FindControl<Button>("SubmitButton")!.IsEnabled =
            connected && validFight && validPlayer && validClass && IsValidIdentity && consent;

        string warning = !connected ? "Connect your Nexforge Account before submitting."
            : !validFight ? "Only completed boss encounters (3 seconds to 2 hours) can be submitted."
            : !validPlayer ? "Choose your own character from this encounter."
            : !validClass ? "The selected player's class was not identified in this fight."
            : !IsValidIdentity ? "A verified server ID, name and region are missing from this encounter. Refresh capture metadata; the server cannot be edited here."
            : string.Empty;
        this.FindControl<TextBlock>("StatusText")!.Text = warning;
    }

    private async void SubmitClick(object? sender, RoutedEventArgs e)
    {
        if (_submitted || _submitting || this.FindControl<Button>("SubmitButton")?.IsEnabled != true)
            return;
        PlayerStat? player = SelectedPlayer;
        LeaderboardClassOption? playerClass = SelectedClass;
        if (player is null || playerClass is null || !IsValidIdentity)
            return;

        _submitting = true;
        Button button = this.FindControl<Button>("SubmitButton")!;
        ComboBox playerBox = this.FindControl<ComboBox>("PlayerBox")!;
        TextBlock status = this.FindControl<TextBlock>("StatusText")!;
        button.IsEnabled = false;
        playerBox.IsEnabled = false;
        button.Content = "Submitting…";
        status.Text = string.Empty;

        try
        {
            long durationMs = (long)Math.Round(EffectiveDuration * 1000, MidpointRounding.AwayFromZero);
            LeaderboardCombatEvidence? evidence = BuildEvidence(player);
            string localKey = string.Join('\u001f', player.Name, playerClass.Key,
                _region, _entry.ServerId.ToString(CultureInfo.InvariantCulture), _serverName,
                _entry.MobName, player.Damage.ToString(CultureInfo.InvariantCulture),
                durationMs.ToString(CultureInfo.InvariantCulture));
            if (!_entry.LeaderboardSubmissionIds.TryGetValue(localKey, out string? submissionId) ||
                !Guid.TryParse(submissionId, out _))
            {
                submissionId = Guid.NewGuid().ToString();
                _entry.LeaderboardSubmissionIds[localKey] = submissionId;
                CombatLogStore.Shared.UpdateMetadata(_entry);
            }

            var request = new LeaderboardSubmitRequest(
                submissionId,
                _entry.MobName,
                player.Name,
                playerClass.Key,
                _serverName,
                player.Damage,
                durationMs,
                ChangelogService.GetCurrentVersion(),
                _entry.StartedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                PublicConsent: true,
                Region: _region,
                ServerId: _entry.ServerId,
                CombatEvidence: evidence);

            LeaderboardSubmitResult result = await LeaderboardService.Shared.SubmitAsync(request);
            _submitted = true;
            string actualStatus = result.Entry.Status?.ToLowerInvariant() ?? "pending";
            status.Text = result.Duplicate
                ? $"This result already exists. Server status: {actualStatus}."
                : actualStatus switch
                {
                    "approved" => "Approved! The server accepted this result for the public leaderboard.",
                    "pending" => "Submitted successfully. Pending moderator review.",
                    "rejected" => "The server rejected this result; see your submissions for the review note.",
                    _ => $"Submission recorded. Server status: {actualStatus}."
                };
            this.FindControl<TextBlock>("ReviewText")!.Text = actualStatus.ToUpperInvariant();
            button.IsVisible = false;
        }
        catch (LeaderboardApiException ex)
        {
            // Keep the service's real error code instead of losing everything behind "unavailable".
            status.Text = $"Submission failed ({ex.StatusCode}, {ex.Code}): {ex.Message}";
        }
        catch (Exception ex)
        {
            status.Text = $"Could not submit result: {ex.Message}";
        }
        finally
        {
            _submitting = false;
            if (!_submitted)
            {
                string errorMessage = status.Text ?? string.Empty;
                playerBox.IsEnabled = true;
                button.Content = "Submit result";
                UpdateFormState();
                if (!string.IsNullOrWhiteSpace(errorMessage))
                    status.Text = errorMessage;
            }
        }
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close(_submitted);

    /// <summary>
    /// V14 expects actual boss damage events, sorted by their fight-relative timestamp.
    /// If the saved history cannot reproduce the submitted total EXACTLY, upload summary
    /// only and let the backend leave the submission pending. Never invent missing hits.
    /// </summary>
    private static LeaderboardCombatEvidence? BuildEvidence(PlayerStat player)
    {
        if (player.Events.Count < 3)
            return null;
        var events = player.Events.Where(hit => !hit.IsHeal).OrderBy(hit => hit.Time).ToList();
        if (events.Count is < 3 or > 10000)
            return null;

        var hits = new List<long[]>(events.Count);
        long damageTotal = 0;
        foreach (SkillEvent hit in events)
        {
            if (hit.Amount <= 0 || double.IsNaN(hit.Time) || double.IsInfinity(hit.Time) || hit.Time < 0)
                return null;
            long offsetMs = (long)Math.Round(hit.Time * 1000, MidpointRounding.AwayFromZero);
            // Keep events outside the actual saved fight out of the evidence entirely.
            long durationMs = (long)Math.Round(player.Duration * 1000, MidpointRounding.AwayFromZero);
            if (offsetMs > durationMs)
                return null;
            if (hit.Amount > player.Damage - damageTotal)
                return null;
            damageTotal += hit.Amount;
            hits.Add(new[] { offsetMs, hit.Amount });
        }
        if (damageTotal != player.Damage)
            return null;

        var evidence = new LeaderboardCombatEvidence(1, hits);
        // V14 endpoint caps the total JSON body at 256KiB; allow room for metadata.
        return JsonSerializer.SerializeToUtf8Bytes(evidence).Length <= 235_000 ? evidence : null;
    }

    private static (string Name, string Region) ResolveServerInfo(CombatLogEntry entry)
    {
        if (entry.ServerId == 0)
            return (string.Empty, string.Empty);

        string name = entry.ServerName?.Trim() ?? string.Empty;
        string region = string.Empty;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData", "AION2_Servers.nfg");
            if (File.Exists(path) && ServersDatabase.Load(path).GetById(entry.ServerId) is { } server)
            {
                // The numeric NFG ID is authoritative for the server's display name.
                if (!string.IsNullOrWhiteSpace(server.Name)) name = server.Name.Trim();
                region = NormalizeRegion(server.Region);
            }
        }
        catch (Exception)
        {
            // Some installations have no local NFG key; use the captured identity instead.
        }

        if (DetectedCharacterService.Shared.Current is { } detected &&
            detected.ServerId == entry.ServerId)
        {
            if (string.IsNullOrWhiteSpace(name)) name = detected.ServerName.Trim();
            if (string.IsNullOrEmpty(region)) region = NormalizeRegion(detected.Region);
        }
        if (CharacterCenterService.Shared.CurrentSelf is { } self && self.ServerId == entry.ServerId &&
            string.IsNullOrEmpty(region))
            region = NormalizeRegion(self.Region);
        return (name, region);
    }

    private static string NormalizeRegion(string? value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "eu" or "nae" or "naw" or "la" or "as" ? normalized : string.Empty;
    }

    private static string NormalizeClass(string value)
    {
        string normalized = new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized switch
        {
            "elementalist" or "spiritmaster" => "spiritmaster",
            "gladiator" => "gladiator",
            "templar" => "templar",
            "assassin" => "assassin",
            "ranger" => "ranger",
            "sorcerer" => "sorcerer",
            "cleric" => "cleric",
            "chanter" => "chanter",
            _ => string.Empty,
        };
    }

    private static string FormatDuration(double seconds)
    {
        int total = Math.Max(0, (int)Math.Round(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}
