using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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
    public string Title => Entry.MobName;
    public string DateText => Entry.EndedAtUtc.ToLocalTime().ToString("yyyy.MM.dd.  HH:mm:ss");
    public string DurationText => FormatDuration(Entry.Duration);
    public string TotalsText =>
        $"DMG {Meter.Num(Entry.TotalDamage)}  ·  HEAL {Meter.Num(Entry.TotalHealing)}";
    public string StateText => Entry.Completed ? string.Empty : "Megszakított fight";
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
        ? "Ismeretlen class"
        : Player.ClassName;
    public string DamageText => $"{Meter.Num(Player.Damage)} · {Meter.Format(Player.Dps, MeterMode.Dps)}";
    public string HealingText => $"Heal {Meter.Num(Player.Healing)} · {Meter.Format(Player.Hps, MeterMode.Hps)}";
}

public sealed class CombatLogSkillRow
{
    public string Name { get; init; } = string.Empty;
    public string TypeText { get; init; } = string.Empty;
    public IBrush TypeColor { get; init; } = Brushes.Gray;
    public string TotalText { get; init; } = string.Empty;
    public string HitsText { get; init; } = string.Empty;
    public string CritText { get; init; } = string.Empty;
}

public partial class CombatLogWindow : UserControl
{
    private readonly CombatLogStore _store = CombatLogStore.Shared;
    private readonly List<CombatLogEntry> _allEntries = new();
    private CombatLogEntry? _selectedEntry;
    private MeterSnapshot? _selectedSnapshot;
    private PlayerStat? _selectedPlayer;
    private CombatLogEntry? _comparisonBase;
    private bool _deleteArmed;

    public ObservableCollection<CombatLogListRow> Fights { get; } = new();
    public ObservableCollection<CombatLogPlayerRow> Players { get; } = new();
    public ObservableCollection<CombatLogSkillRow> Skills { get; } = new();

    public CombatLogWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;

        this.FindControl<TextBlock>("StorageText")!.Text = CombatLogStore.LogDirectory;
        _store.Changed += StoreChanged;
        DetachedFromVisualTree += (_, _) => _store.Changed -= StoreChanged;
        RefreshLogs();
    }

    private void StoreChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RefreshLogs);

    private void RefreshLogs()
    {
        string? selectedId = _selectedEntry?.Id;
        _allEntries.Clear();
        _allEntries.AddRange(_store.LoadAll());
        ApplyFilter(selectedId);
        UpdateSessionSummary();
    }

    private void ApplyFilter(string? preferredId = null)
    {
        string query = this.FindControl<TextBox>("SearchBox")?.Text?.Trim() ?? string.Empty;
        bool favoritesOnly = this.FindControl<CheckBox>("FavoritesOnlyToggle")?.IsChecked == true;

        IEnumerable<CombatLogEntry> filtered = _allEntries;
        if (favoritesOnly)
            filtered = filtered.Where(entry => entry.IsFavorite);

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(entry => SearchableText(entry)
                .Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        Fights.Clear();
        foreach (CombatLogEntry entry in filtered)
            Fights.Add(new CombatLogListRow { Entry = entry });

        this.FindControl<TextBlock>("FightCountText")!.Text =
            Fights.Count == _allEntries.Count ? $"{Fights.Count} fight" : $"{Fights.Count} / {_allEntries.Count}";
        this.FindControl<TextBlock>("EmptyLogText")!.IsVisible = Fights.Count == 0;

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
        return $"{entry.MobName} {entry.Notes} {playerText} " +
               $"{local:yyyy.MM.dd} {local:yyyy-MM-dd} {local:yyyy MMMM dd}";
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void FavoritesFilterChanged(object? sender, RoutedEventArgs e) => ApplyFilter();

    private void FightSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: CombatLogListRow row })
            return;

        _deleteArmed = false;
        this.FindControl<Button>("DeleteButton")!.Content = "Törlés";
        _selectedEntry = row.Entry;
        _selectedSnapshot = row.Entry.ToSnapshot();

        this.FindControl<TextBlock>("EncounterName")!.Text = row.Entry.MobName;
        this.FindControl<TextBlock>("EncounterMeta")!.Text =
            $"{row.DateText}  ·  {row.DurationText}  ·  {row.Entry.Players.Count} játékos" +
            (row.Entry.Completed ? string.Empty : "  ·  megszakított");

        double duration = Math.Max(0.001, row.Entry.Duration);
        this.FindControl<TextBlock>("TotalDamageText")!.Text = Meter.Num(row.Entry.TotalDamage);
        this.FindControl<TextBlock>("TotalDpsText")!.Text = Meter.Format(row.Entry.TotalDamage / duration, MeterMode.Dps);
        this.FindControl<TextBlock>("TotalHealingText")!.Text = Meter.Num(row.Entry.TotalHealing);
        this.FindControl<TextBlock>("TotalHpsText")!.Text = Meter.Format(row.Entry.TotalHealing / duration, MeterMode.Hps);
        this.FindControl<Button>("FavoriteButton")!.Content = row.Entry.IsFavorite ? "★ Kedvenc" : "☆ Kedvenc";
        this.FindControl<TextBox>("NotesBox")!.Text = row.Entry.Notes;

        Players.Clear();
        foreach (PlayerStat player in _selectedSnapshot.Players.OrderByDescending(player => player.Damage))
            Players.Add(new CombatLogPlayerRow { Player = player });

        SetFightButtonsEnabled(true);
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
        this.FindControl<TextBlock>("SelectedPlayerMeta")!.Text =
            $"{row.ClassName}  ·  {row.Player.Events.Count} esemény  ·  " +
            $"DPS {Meter.Num(row.Player.Dps)}  ·  HPS {Meter.Num(row.Player.Hps)}";

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
            });
        }

        UpdateRecordText(row.Player);
        this.FindControl<Button>("PlayerDetailsButton")!.IsEnabled = true;
        this.FindControl<Button>("TimelineButton")!.IsEnabled = true;
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
                Player = entry.ToSnapshot().Players.FirstOrDefault(player =>
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

        string badge = rank == 1 && comparable.Count > 0 ? "🏆 DPS rekord" : $"DPS #{rank}/{comparable.Count}";
        string hitBadge = bestHit == allTimeBestHit && bestHit > 0 ? " · legnagyobb hit rekord" : string.Empty;
        string healBadge = selectedPlayer.Hps >= bestHps && bestHps > 0 ? " · HPS rekord" : string.Empty;
        this.FindControl<TextBlock>("RecordText")!.Text = badge + hitBadge + healBadge;
    }

    private void UpdateSessionSummary()
    {
        List<CombatLogEntry> session = _allEntries
            .Where(entry => entry.EndedAtUtc >= _store.SessionStartedAtUtc)
            .ToList();

        if (session.Count == 0)
        {
            this.FindControl<TextBlock>("SessionText")!.Text = "Ebben a munkamenetben még nincs lezárt fight.";
            return;
        }

        double seconds = session.Sum(entry => entry.Duration);
        long damage = session.Sum(entry => entry.TotalDamage);
        long healing = session.Sum(entry => entry.TotalHealing);
        CombatLogEntry best = session.OrderByDescending(entry =>
            entry.TotalDamage / Math.Max(0.001, entry.Duration)).First();
        int total = Math.Max(0, (int)Math.Round(seconds));

        this.FindControl<TextBlock>("SessionText")!.Text =
            $"{session.Count} fight  ·  {total / 60:00}:{total % 60:00} harcidő  ·  " +
            $"{Meter.Num(damage)} damage  ·  {Meter.Num(healing)} heal  ·  " +
            $"legjobb group DPS: {best.MobName} ({Meter.Num(best.TotalDamage / Math.Max(0.001, best.Duration))}/s)";
    }

    private void ClearDetails()
    {
        _selectedEntry = null;
        _selectedSnapshot = null;
        Players.Clear();
        Skills.Clear();
        this.FindControl<TextBlock>("EncounterName")!.Text = "Nincs kijelölt fight";
        this.FindControl<TextBlock>("EncounterMeta")!.Text = "—";
        this.FindControl<TextBlock>("TotalDamageText")!.Text = "—";
        this.FindControl<TextBlock>("TotalDpsText")!.Text = "—";
        this.FindControl<TextBlock>("TotalHealingText")!.Text = "—";
        this.FindControl<TextBlock>("TotalHpsText")!.Text = "—";
        this.FindControl<TextBox>("NotesBox")!.Text = string.Empty;
        SetFightButtonsEnabled(false);
        ClearPlayerDetails();
    }

    private void SetFightButtonsEnabled(bool enabled)
    {
        foreach (string name in new[]
                 {
                     "OpenDpsButton", "FavoriteButton", "CopyButton", "CompareButton",
                     "DeleteButton", "SaveNotesButton"
                 })
            this.FindControl<Button>(name)!.IsEnabled = enabled;
    }

    private void ClearPlayerDetails()
    {
        _selectedPlayer = null;
        Skills.Clear();
        this.FindControl<TextBlock>("SelectedPlayerName")!.Text = "Játékos részletei";
        this.FindControl<TextBlock>("SelectedPlayerMeta")!.Text = "—";
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
            this.FindControl<TextBlock>("ActionStatusText")!.Text = "Jegyzet elmentve.";
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        Button button = this.FindControl<Button>("DeleteButton")!;
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            button.Content = "Törlés megerősítése";
            return;
        }
        string id = _selectedEntry.Id;
        _selectedEntry = null;
        _deleteArmed = false;
        button.Content = "Törlés";
        _store.Delete(id);
    }

    private async void CopyDiscordClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
            return;
        MeterSnapshot snapshot = _selectedEntry.ToSnapshot();
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
        text.Append("**Összesen:** ").Append(Meter.Num(_selectedEntry.TotalDamage))
            .Append(" damage · ").Append(Meter.Num(_selectedEntry.TotalHealing)).Append(" heal");

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text.ToString());
            this.FindControl<TextBlock>("ActionStatusText")!.Text = "Discord-formátum a vágólapra másolva.";
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
                $"Alap: {_comparisonBase.MobName} · {_comparisonBase.EndedAtUtc.ToLocalTime():MM.dd HH:mm} · " +
                "második fight nincs kijelölve";
            this.FindControl<Button>("CompareButton")!.Content = "Összehasonlítás ezzel";
            return;
        }
        if (_comparisonBase.Id == _selectedEntry.Id)
        {
            this.FindControl<TextBlock>("ComparisonHint")!.Text = "A második fight megegyezik az alappal.";
            return;
        }
        var comparison = new CombatComparisonWindow(_comparisonBase, _selectedEntry);
        if (TopLevel.GetTopLevel(this) is Window owner)
            comparison.Show(owner);
        else
            comparison.Show();
        _comparisonBase = null;
        this.FindControl<TextBlock>("ComparisonHint")!.Text = string.Empty;
        this.FindControl<Button>("CompareButton")!.Content = "Összehasonlítás";
    }

    private void OpenDpsClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedSnapshot is not null)
            OverlayWindowManager.ToggleDps(_selectedSnapshot);
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
