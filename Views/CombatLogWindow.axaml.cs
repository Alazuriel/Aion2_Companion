using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace AION_2_Companion.Views;

public sealed class CombatLogListRow
{
    public required CombatLogEntry Entry { get; init; }
    public bool BossOnly { get; init; }
    public string Title => Entry.MobName;
    public string DateText => Entry.EndedAtUtc.ToLocalTime().ToString("yyyy.MM.dd.  HH:mm:ss");
    public string DurationText => FormatDuration(Entry.Duration);
    public string TotalsText
    {
        get
        {
            MeterSnapshot snapshot = Entry.ToSnapshot(BossOnly);
            string baseText = $"DMG {Meter.Num(snapshot.TotalDamage)}  ·  HEAL {Meter.Num(snapshot.TotalHealing)}";
            return snapshot.HasDamageTakenData
                ? baseText + $"  ·  TAKEN {Meter.Num(snapshot.TotalDamageTaken)}"
                : baseText;
        }
    }
    public string StateText => Entry.Completed ? string.Empty : LocalizationService.Get("InterruptedFight");
    public string FavoriteText => Entry.IsFavorite ? "★" : string.Empty;

    private static string FormatDuration(double seconds)
    {
        int total = Math.Max(0, (int)Math.Round(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}

public sealed class CombatLogPlayerRow
{
    public required PlayerStat Player { get; init; }
    public string Name => Player.Name;
    public string ClassName => string.IsNullOrWhiteSpace(Player.ClassName)
        ? LocalizationService.Get("UnknownClass")
        : Player.ClassName;
    public string DamageText => $"{Meter.Num(Player.Damage)} · {Meter.Format(Player.Dps, MeterMode.Dps)}";
    public string HealingText =>
        $"{LocalizationService.Get("Healing")} {Meter.Num(Player.Healing)} · {Meter.Format(Player.Hps, MeterMode.Hps)}" +
        (Player.DamageTaken.HasValue ? $"  ·  TAKEN {Meter.Num(Player.DamageTaken.Value)}" : "");
    public string DeathText => $"💀 {(Player.DeathCount.HasValue ? Player.DeathCount.Value.ToString() : "?")}";
    public string PositionText => PositionDetails.Format(Player, compact: true);
    public string DpsShortText => Meter.Format(Player.Dps, MeterMode.Dps);
    public string HealingShortText => Meter.Format(Player.Hps, MeterMode.Hps);
    public IBrush ClassBrush
    {
        get
        {
            try { return Brush.Parse(Player.ClassColor); }
            catch (FormatException) { return Brushes.Gray; }
        }
    }
    // Visual relative scale of real saved damage, not an independent encounter metric.
    public double DamageShare { get; init; }

}

public sealed class CombatLogSkillRow
{
    public string Name { get; init; } = string.Empty;
    public string TypeText { get; init; } = string.Empty;
    public IBrush TypeColor { get; init; } = Brushes.Gray;
    public string TotalText { get; init; } = string.Empty;
    public string HitsText { get; init; } = string.Empty;
    public string CritText { get; init; } = string.Empty;
    public double SharePercent { get; init; }
}

public partial class CombatLogWindow : UserControl
{
    private const int LogPageSize = 20;
    private readonly CombatLogStore _store = CombatLogStore.Shared;
    private readonly List<CombatLogEntry> _allEntries = new();
    private CombatLogEntry? _selectedEntry;
    private MeterSnapshot? _selectedSnapshot;
    private PlayerStat? _selectedPlayer;
    private CombatLogEntry? _comparisonBase;
    private bool _deleteArmed;
    private bool _isLoadingLogs;
    private int _nextLogOffset;
    private int _totalLogCount;
    private int _loadGeneration;
    private bool _readmeDemo;
    private string _sortMode = "newest";
    private bool _viewInitialized;

    public ObservableCollection<CombatLogListRow> Fights { get; } = new();
    public ObservableCollection<CombatLogPlayerRow> Players { get; } = new();
    public ObservableCollection<CombatLogSkillRow> Skills { get; } = new();

    public CombatLogWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;

        this.FindControl<TextBlock>("StorageText")!.Text = CombatLogStore.LogDirectory;
        // XAML may raise SortModeChanged before the control name scope is ready.
        _viewInitialized = true;
        _store.Changed += StoreChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        CompanionSettings.DpsEncounterModeChanged += EncounterModeChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            _store.Changed -= StoreChanged;
            LocalizationService.LanguageChanged -= LanguageChanged;
            CompanionSettings.DpsEncounterModeChanged -= EncounterModeChanged;
        };
        RefreshLogs();
    }

    internal void LoadReadmeDemo(IReadOnlyList<CombatLogEntry> entries)
    {
        _readmeDemo = true;
        _loadGeneration++;
        this.FindControl<TextBlock>("StorageText")!.Text = "LOCAL STORAGE";
        _allEntries.Clear();
        _allEntries.AddRange(entries);
        _nextLogOffset = entries.Count;
        _totalLogCount = entries.Count;
        SetLogLoading(false);
        ApplyFilter(entries.FirstOrDefault()?.Id);
        UpdateSessionSummary();
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        ApplyFilter(_selectedEntry?.Id);
        UpdateSessionSummary();
    }

    private void StoreChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RefreshLogs);

    private void EncounterModeChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyFilter(_selectedEntry?.Id);
            UpdateSessionSummary();
        });

    private bool BossOnly => !_readmeDemo &&
        CompanionSettings.DpsEncounterMode == DpsEncounterMode.Bosses;

    private async void RefreshLogs()
    {
        if (_readmeDemo)
            return;

        string? selectedId = _selectedEntry?.Id;
        int generation = ++_loadGeneration;
        int reloadCount = Math.Max(LogPageSize, _nextLogOffset);
        SetLogLoading(true);

        CombatLogPage page = await Task.Run(() => _store.LoadPage(0, reloadCount));
        if (generation != _loadGeneration)
            return;

        _allEntries.Clear();
        _allEntries.AddRange(page.Entries);
        _nextLogOffset = page.NextOffset;
        _totalLogCount = page.TotalCount;
        SetLogLoading(false);
        ApplyFilter(selectedId);
        UpdateSessionSummary();
    }

    private async void LoadOlderFightsClick(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingLogs || _nextLogOffset >= _totalLogCount)
            return;

        int generation = _loadGeneration;
        SetLogLoading(true);
        CombatLogPage page = await Task.Run(() =>
            _store.LoadPage(_nextLogOffset, LogPageSize));
        if (generation != _loadGeneration)
            return;

        var knownIds = _allEntries.Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        _allEntries.AddRange(page.Entries.Where(entry => knownIds.Add(entry.Id)));
        _nextLogOffset = page.NextOffset;
        _totalLogCount = page.TotalCount;
        SetLogLoading(false);
        ApplyFilter(_selectedEntry?.Id);
        UpdateSessionSummary();
    }

    private void SetLogLoading(bool loading)
    {
        _isLoadingLogs = loading;
        Button? button = this.FindControl<Button>("LoadOlderFightsButton");
        if (button is null)
            return;

        button.IsEnabled = !loading;
        button.Content = LocalizationService.Get(
            loading ? "LoadingFights" : "LoadOlderFights");
        button.IsVisible = loading || _nextLogOffset < _totalLogCount;
    }

    private void ApplyFilter(string? preferredId = null)
    {
        string query = this.FindControl<TextBox>("SearchBox")?.Text?.Trim() ?? string.Empty;
        bool favoritesOnly = this.FindControl<CheckBox>("FavoritesOnlyToggle")?.IsChecked == true;

        IEnumerable<CombatLogEntry> filtered = _allEntries;
        if (BossOnly)
            filtered = filtered.Where(entry => entry.IsBossEncounter);
        if (favoritesOnly)
            filtered = filtered.Where(entry => entry.IsFavorite);

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(entry => SearchableText(entry)
                .Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        // Sorting affects loaded history only; original page loading is unchanged.
        filtered = _sortMode switch
        {
            "oldest" => filtered.OrderBy(entry => entry.EndedAtUtc),
            "damage" => filtered.OrderByDescending(entry => entry.ToSnapshot(BossOnly).TotalDamage)
                                .ThenByDescending(entry => entry.EndedAtUtc),
            "duration" => filtered.OrderByDescending(entry => entry.Duration)
                                  .ThenByDescending(entry => entry.EndedAtUtc),
            _ => filtered.OrderByDescending(entry => entry.EndedAtUtc)
        };

        Fights.Clear();
        foreach (CombatLogEntry entry in filtered)
            Fights.Add(new CombatLogListRow { Entry = entry, BossOnly = BossOnly });

        this.FindControl<Button>("AllEncountersButton")!.Classes.Set("primary", !BossOnly);
        this.FindControl<Button>("BossEncountersButton")!.Classes.Set("primary", BossOnly);

        // Keep the badge concise. Filtering only covers loaded encounters, never claim a global match count.
        bool isFiltering = BossOnly || favoritesOnly || !string.IsNullOrWhiteSpace(query);
        this.FindControl<TextBlock>("FightCountText")!.Text = isFiltering
            ? $"{Fights.Count} / {_allEntries.Count}"
            : $"{_allEntries.Count} / {_totalLogCount}";
        this.FindControl<TextBlock>("EmptyLogText")!.IsVisible =
            Fights.Count == 0 && !_isLoadingLogs;
        SetLogLoading(_isLoadingLogs);

        string? wantedId = preferredId ?? _selectedEntry?.Id;
        CombatLogListRow? selection = Fights.FirstOrDefault(row => row.Entry.Id == wantedId)
                                      ?? Fights.FirstOrDefault();
        this.FindControl<ListBox>("FightList")!.SelectedItem = selection;

        if (selection is null)
            ClearDetails();
    }

    private static string SearchableText(CombatLogEntry entry)
    {
        string playerText = string.Join(' ', entry.Players.Select(player =>
            $"{player.Name} {player.ClassName}"));
        DateTimeOffset local = entry.EndedAtUtc.ToLocalTime();
        string mobText = string.Join(' ', entry.Mobs.Select(mob => mob.Name));
        return $"{entry.MobName} {entry.ServerName} {mobText} {entry.Notes} {playerText} " +
               $"{local:yyyy.MM.dd} {local:yyyy-MM-dd} {local:yyyy MMMM dd}";
    }

    private void DetailsTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section })
            return;

        bool breakdown = section == "Breakdown";
        bool notes = section == "Notes";
        bool enemies = section == "Enemies";
        this.FindControl<Grid>("DetailsBreakdownPanel")!.IsVisible = breakdown;
        this.FindControl<Border>("NotesContentPanel")!.IsVisible = notes;
        this.FindControl<Border>("EncounterEnemiesPanel")!.IsVisible = enemies;
        this.FindControl<Button>("BreakdownTabButton")!.Classes.Set("selected", breakdown);
        this.FindControl<Button>("NotesTabButton")!.Classes.Set("selected", notes);
        this.FindControl<Button>("EnemiesTabButton")!.Classes.Set("selected", enemies);
    }

    private void SortModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string mode } })
            return;
        _sortMode = mode;
        if (!_viewInitialized)
            return;
        ApplyFilter(_selectedEntry?.Id);
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void FavoritesFilterChanged(object? sender, RoutedEventArgs e) => ApplyFilter();

    private void EncounterModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode })
            return;
        CompanionSettings.DpsEncounterMode = mode.Equals("Bosses", StringComparison.OrdinalIgnoreCase)
            ? DpsEncounterMode.Bosses
            : DpsEncounterMode.All;
    }

    private void FightSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: CombatLogListRow row })
            return;

        _deleteArmed = false;
        this.FindControl<TextBlock>("ActionStatusText")!.Text = string.Empty;
        this.FindControl<Button>("DeleteButton")!.Content = LocalizationService.Get("Delete");
        _selectedEntry = row.Entry;
        _selectedSnapshot = row.Entry.ToSnapshot(BossOnly);

        this.FindControl<TextBlock>("EncounterName")!.Text = row.Entry.MobName;
        this.FindControl<TextBlock>("EncounterMeta")!.Text =
            $"{row.DateText}  ·  {row.DurationText}  ·  " +
            LocalizationService.Format("PlayerCount", row.Entry.Players.Count) +
            (string.IsNullOrWhiteSpace(row.Entry.ServerName)
                ? string.Empty
                : $"  ·  {row.Entry.ServerName}") +
            (row.Entry.Completed ? string.Empty : $"  ·  {LocalizationService.Get("Interrupted")}");
        this.FindControl<TextBlock>("EncounterMobsText")!.Text = FormatEncounterMobs(row.Entry);

        double duration = Math.Max(0.001, row.Entry.Duration);
        this.FindControl<TextBlock>("TotalDamageText")!.Text = Meter.Num(_selectedSnapshot.TotalDamage);
        this.FindControl<TextBlock>("TotalDpsText")!.Text = Meter.Format(_selectedSnapshot.TotalDamage / duration, MeterMode.Dps);
        this.FindControl<TextBlock>("TotalHealingText")!.Text = Meter.Num(_selectedSnapshot.TotalHealing);
        this.FindControl<TextBlock>("TotalHpsText")!.Text = Meter.Format(_selectedSnapshot.TotalHealing / duration, MeterMode.Hps);
        // Nexforge Damage Taken Log Timeline v0.2: Unknown != zero for legacy fight logs.
        this.FindControl<TextBlock>("TotalTakenText")!.Text = _selectedSnapshot.HasDamageTakenData
            ? Meter.Num(_selectedSnapshot.TotalDamageTaken) : "—";
        this.FindControl<Button>("FavoriteButton")!.Content =
            $"{(row.Entry.IsFavorite ? "★" : "☆")} {LocalizationService.Get("Favorite")}";
        this.FindControl<TextBox>("NotesBox")!.Text = row.Entry.Notes;

        Players.Clear();
        long highestDamage = _selectedSnapshot.Players.Select(player => player.Damage)
            .DefaultIfEmpty(0).Max();
        foreach (PlayerStat player in _selectedSnapshot.Players.OrderByDescending(player => player.Damage))
            Players.Add(new CombatLogPlayerRow
            {
                Player = player,
                DamageShare = highestDamage > 0
                    ? Math.Clamp(100d * player.Damage / highestDamage, 0, 100) : 0
            });

        SetFightButtonsEnabled(true);
        this.FindControl<Button>("ShareButton")!.IsEnabled =
            row.Entry.Completed && row.Entry.IsBossEncounter &&
            (row.Entry.BossDuration > 0 ? row.Entry.BossDuration : row.Entry.Duration) is >= 3 and <= 7200;
        this.FindControl<ListBox>("PlayerList")!.SelectedItem = Players.FirstOrDefault();
        if (Players.Count == 0)
            ClearPlayerDetails();
    }

    private void PlayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: CombatLogPlayerRow row })
            return;

        _selectedPlayer = row.Player;
        this.FindControl<TextBlock>("SelectedPlayerName")!.Text = row.Player.Name;
        this.FindControl<TextBlock>("SelectedPlayerMeta")!.Text = row.ClassName +
            (row.Player.DamageTaken.HasValue
                ? $" · Damage Taken {Meter.Num(row.Player.DamageTaken.Value)}"
                : " · Damage Taken — (older log)");
        this.FindControl<TextBlock>("PositionDetailsText")!.Text = PositionDetails.Format(row.Player, compact: true);
        this.FindControl<TextBlock>("LogDetailDeaths")!.Text = PositionDetails.DeathsValue(row.Player);
        this.FindControl<TextBlock>("LogDetailFront")!.Text = PositionDetails.FrontValue(row.Player);
        this.FindControl<TextBlock>("LogDetailBack")!.Text = PositionDetails.BackValue(row.Player);
        this.FindControl<TextBlock>("LogDetailUnknown")!.Text = PositionDetails.UnknownValue(row.Player);
        this.FindControl<TextBlock>("LogDetailCoverage")!.Text = PositionDetails.CoverageNote(row.Player);
        this.FindControl<TextBlock>("PlayerDpsText")!.Text = Meter.Format(row.Player.Dps, MeterMode.Dps);
        this.FindControl<TextBlock>("PlayerHpsText")!.Text = Meter.Format(row.Player.Hps, MeterMode.Hps);
        this.FindControl<TextBlock>("PlayerDamageText")!.Text = Meter.Num(row.Player.Damage);
        this.FindControl<TextBlock>("PlayerHealingText")!.Text = Meter.Num(row.Player.Healing);
        this.FindControl<TextBlock>("PlayerTakenText")!.Text = row.Player.DamageTaken.HasValue
            ? Meter.Num(row.Player.DamageTaken.Value) : "—";
        long skillTotal = row.Player.Events.Sum(skillEvent => Math.Max(0, skillEvent.Amount));

        Skills.Clear();
        foreach (var group in row.Player.Events
                     .GroupBy(skillEvent => new { skillEvent.Skill, skillEvent.IsHeal })
                     .Select(group => new
                     {
                         group.Key.Skill,
                         group.Key.IsHeal,
                         Total = group.Sum(skillEvent => skillEvent.Amount),
                         Hits = group.Count(),
                         Crits = group.Count(skillEvent => skillEvent.Crit),
                     })
                     .OrderByDescending(group => group.Total))
        {
            Skills.Add(new CombatLogSkillRow
            {
                Name = group.Skill,
                TypeText = group.IsHeal ? "HEAL" : "DMG",
                TypeColor = group.IsHeal ? Brush.Parse("#8FBFA8") : Brush.Parse("#C4A574"),
                TotalText = Meter.Num(group.Total),
                HitsText = group.Hits.ToString(),
                CritText = group.Hits > 0 ? $"{(double)group.Crits / group.Hits * 100:F0}%" : "—",
                SharePercent = skillTotal > 0
                    ? Math.Clamp(100d * group.Total / skillTotal, 0, 100) : 0,
            });
        }

        UpdateRecordText(row.Player);
        this.FindControl<Button>("PlayerDetailsButton")!.IsEnabled = true;
        this.FindControl<Button>("TimelineButton")!.IsEnabled = true;
    }

    private static string FormatEncounterMobs(CombatLogEntry entry)
    {
        List<StoredEncounterMob> mobs = entry.Mobs
            .Where(mob => mob.Count > 0)
            .ToList();
        int total = mobs.Sum(mob => mob.Count);
        if (total == 0)
            return LocalizationService.Get("EncounterMobsUnavailable");

        string names = string.Join(", ", mobs.Select(mob =>
        {
            string name = string.IsNullOrWhiteSpace(mob.Name)
                ? LocalizationService.Get("UnknownEnemy")
                : mob.Name;
            return mob.Count > 1 ? $"{name} ×{mob.Count}" : name;
        }));

        return $"{LocalizationService.Format("MobCount", total)}  ·  {names}";
    }

    private void UpdateRecordText(PlayerStat selectedPlayer)
    {
        if (_selectedEntry is null)
            return;

        var comparable = _allEntries
            .Where(entry => string.Equals(entry.MobName, _selectedEntry.MobName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Entry = entry,
                Player = entry.ToSnapshot(BossOnly).Players.FirstOrDefault(player =>
                    string.Equals(player.Name, selectedPlayer.Name, StringComparison.OrdinalIgnoreCase))
            })
            .Where(item => item.Player is not null)
            .Select(item => new
            {
                item.Entry,
                Player = item.Player!,
                BestHit = item.Player!.Events.Where(skillEvent => !skillEvent.IsHeal)
                    .Select(skillEvent => skillEvent.Amount).DefaultIfEmpty(0).Max()
            })
            .OrderByDescending(item => item.Player.Dps)
            .ToList();

        int rank = comparable.FindIndex(item => item.Entry.Id == _selectedEntry.Id) + 1;
        long bestHit = selectedPlayer.Events.Where(skillEvent => !skillEvent.IsHeal)
            .Select(skillEvent => skillEvent.Amount).DefaultIfEmpty(0).Max();
        long allTimeBestHit = comparable.Select(item => item.BestHit).DefaultIfEmpty(0).Max();
        double bestHps = comparable.Select(item => item.Player.Hps).DefaultIfEmpty(0).Max();

        string badge = rank == 1 && comparable.Count > 0
            ? LocalizationService.Get("DpsRecord")
            : LocalizationService.Format("DpsRank", rank, comparable.Count);
        string hitBadge = bestHit == allTimeBestHit && bestHit > 0
            ? LocalizationService.Get("BestHitRecord") : string.Empty;
        string healBadge = selectedPlayer.Hps >= bestHps && bestHps > 0
            ? LocalizationService.Get("HpsRecord") : string.Empty;
        this.FindControl<TextBlock>("RecordText")!.Text = badge + hitBadge + healBadge;
    }

    private void UpdateSessionSummary()
    {
        List<CombatLogEntry> session = _allEntries
            .Where(entry => entry.EndedAtUtc >= _store.SessionStartedAtUtc)
            .Where(entry => !BossOnly || entry.IsBossEncounter)
            .ToList();

        if (session.Count == 0)
        {
            this.FindControl<Border>("SessionSummaryPanel")!.IsVisible = false;
            this.FindControl<TextBlock>("SessionText")!.Text = string.Empty;
            return;
        }

        double seconds = session.Sum(entry => entry.Duration);
        var sessionSnapshots = session.Select(entry => (Entry: entry, Snapshot: entry.ToSnapshot(BossOnly))).ToList();
        long damage = sessionSnapshots.Sum(item => item.Snapshot.TotalDamage);
        long healing = sessionSnapshots.Sum(item => item.Snapshot.TotalHealing);
        var best = sessionSnapshots.OrderByDescending(item =>
            item.Snapshot.TotalDamage / Math.Max(0.001, item.Entry.Duration)).First();
        int total = Math.Max(0, (int)Math.Round(seconds));

        this.FindControl<TextBlock>("SessionText")!.Text = LocalizationService.Format(
            "SessionSummary", session.Count, $"{total / 60:00}:{total % 60:00}", Meter.Num(damage),
            Meter.Num(healing), best.Entry.MobName,
            Meter.Num(best.Snapshot.TotalDamage / Math.Max(0.001, best.Entry.Duration)));
        this.FindControl<Border>("SessionSummaryPanel")!.IsVisible = false; // tucked away: redundant session strip
    }

    private void ClearDetails()
    {
        _selectedEntry = null;
        _selectedSnapshot = null;
        Players.Clear();
        Skills.Clear();
        this.FindControl<TextBlock>("EncounterName")!.Text = LocalizationService.Get("NoSelectedFight");
        this.FindControl<TextBlock>("EncounterMeta")!.Text = "—";
        this.FindControl<TextBlock>("EncounterMobsText")!.Text = "—";
        this.FindControl<TextBlock>("TotalDamageText")!.Text = "—";
        this.FindControl<TextBlock>("TotalDpsText")!.Text = "—";
        this.FindControl<TextBlock>("TotalHealingText")!.Text = "—";
        this.FindControl<TextBlock>("TotalHpsText")!.Text = "—";
        this.FindControl<TextBlock>("TotalTakenText")!.Text = "—";
        this.FindControl<TextBox>("NotesBox")!.Text = string.Empty;
        SetFightButtonsEnabled(false);
        ClearPlayerDetails();
    }

    private void SetFightButtonsEnabled(bool enabled)
    {
        foreach (string name in new[]
                 {
                     "OpenDpsButton", "FavoriteButton", "CopyButton", "CompareButton",
                     "DeleteButton", "ShareButton", "SaveNotesButton"
                 })
            this.FindControl<Button>(name)!.IsEnabled = enabled;
    }

    private void ClearPlayerDetails()
    {
        _selectedPlayer = null;
        Skills.Clear();
        this.FindControl<TextBlock>("SelectedPlayerName")!.Text = LocalizationService.Get("PlayerDetails");
        this.FindControl<TextBlock>("SelectedPlayerMeta")!.Text = "—";
        this.FindControl<TextBlock>("PositionDetailsText")!.Text = string.Empty;
        this.FindControl<TextBlock>("LogDetailDeaths")!.Text = "—";
        this.FindControl<TextBlock>("LogDetailFront")!.Text = "N/A";
        this.FindControl<TextBlock>("LogDetailBack")!.Text = "N/A";
        this.FindControl<TextBlock>("LogDetailUnknown")!.Text = "?";
        this.FindControl<TextBlock>("LogDetailCoverage")!.Text = string.Empty;
        this.FindControl<TextBlock>("PlayerDpsText")!.Text = "—";
        this.FindControl<TextBlock>("PlayerHpsText")!.Text = "—";
        this.FindControl<TextBlock>("PlayerDamageText")!.Text = "—";
        this.FindControl<TextBlock>("PlayerHealingText")!.Text = "—";
        this.FindControl<TextBlock>("PlayerTakenText")!.Text = "—";
        this.FindControl<TextBlock>("RecordText")!.Text = string.Empty;
        this.FindControl<Button>("PlayerDetailsButton")!.IsEnabled = false;
        this.FindControl<Button>("TimelineButton")!.IsEnabled = false;
    }

    private void ToggleFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        _selectedEntry.IsFavorite = !_selectedEntry.IsFavorite;
        _store.UpdateMetadata(_selectedEntry);
    }

    private void SaveNotesClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        _selectedEntry.Notes = this.FindControl<TextBox>("NotesBox")!.Text?.Trim() ?? string.Empty;
        if (_store.UpdateMetadata(_selectedEntry))
            this.FindControl<TextBlock>("ActionStatusText")!.Text = LocalizationService.Get("NotesSaved");
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        Button button = this.FindControl<Button>("DeleteButton")!;
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            button.Content = LocalizationService.Get("ConfirmDelete");
            return;
        }
        string id = _selectedEntry.Id;
        _selectedEntry = null;
        _deleteArmed = false;
        button.Content = LocalizationService.Get("Delete");
        _store.Delete(id);
    }

    private async void CopyDiscordClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        MeterSnapshot snapshot = _selectedEntry.ToSnapshot(BossOnly);
        var text = new StringBuilder();
        text.AppendLine($"**{EscapeMarkdown(_selectedEntry.MobName)}** — {FormatDuration(_selectedEntry.Duration)}");
        int rank = 1;
        foreach (PlayerStat player in snapshot.Players.OrderByDescending(player => player.Damage))
        {
            text.Append(rank++).Append(". **").Append(EscapeMarkdown(player.Name)).Append("**");
            if (!string.IsNullOrWhiteSpace(player.ClassName))
                text.Append(" (").Append(EscapeMarkdown(player.ClassName)).Append(')');
            text.Append(" — ").Append(Meter.Format(player.Dps, MeterMode.Dps))
                .Append(" | ").Append(Meter.Num(player.Damage)).Append(" dmg");
            if (player.Healing > 0)
                text.Append(" | ").Append(Meter.Format(player.Hps, MeterMode.Hps)).Append(" heal");
            text.AppendLine();
        }
        text.Append(LocalizationService.Format(
            "DiscordTotalLine",
            Meter.Num(snapshot.TotalDamage),
            Meter.Num(snapshot.TotalHealing)));

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text.ToString());
            this.FindControl<TextBlock>("ActionStatusText")!.Text = LocalizationService.Get("DiscordCopied");
        }
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`");

    private void CompareClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        if (_comparisonBase is null)
        {
            _comparisonBase = _selectedEntry;
            this.FindControl<TextBlock>("ComparisonHint")!.Text =
                LocalizationService.Format("ComparisonBase", _comparisonBase.MobName,
                    _comparisonBase.EndedAtUtc.ToLocalTime().ToString("MM.dd HH:mm"));
            this.FindControl<Button>("CompareButton")!.Content = LocalizationService.Get("CompareWithThis");
            return;
        }
        if (_comparisonBase.Id == _selectedEntry.Id)
        {
            this.FindControl<TextBlock>("ComparisonHint")!.Text = LocalizationService.Get("SameComparisonFight");
            return;
        }
        var comparison = new CombatComparisonWindow(_comparisonBase, _selectedEntry, BossOnly);
        if (TopLevel.GetTopLevel(this) is Window owner)
            comparison.Show(owner);
        else
            comparison.Show();
        _comparisonBase = null;
        this.FindControl<TextBlock>("ComparisonHint")!.Text = string.Empty;
        this.FindControl<Button>("CompareButton")!.Content = LocalizationService.Get("Compare");
    }

    private void OpenDpsClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSnapshot is not null)
            OverlayWindowManager.ShowSavedFightDps(_selectedSnapshot);
    }

    private async void ShareResultClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;

        var dialog = new LeaderboardSubmissionDialog(_selectedEntry);
        if (TopLevel.GetTopLevel(this) is Window owner)
            await dialog.ShowDialog<bool>(owner);
        else
            dialog.Show();
    }

    private void OpenPlayerDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedPlayer is not null && _selectedSnapshot is not null)
            OverlayWindowManager.ShowPlayerDetails(_selectedPlayer, _selectedSnapshot);
    }

    private void OpenTimelineClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedPlayer is not null && _selectedSnapshot is not null)
            OverlayWindowManager.ShowTimeline(_selectedPlayer, _selectedSnapshot);
    }

    private static string FormatDuration(double seconds)
    {
        int total = Math.Max(0, (int)Math.Round(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}
