using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Leaderboards;
using AION_2_Companion.Views.Overlays;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace AION_2_Companion.Views;

public sealed record LeaderboardFilterOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class LeaderboardEntryRow
{
    public required LeaderboardEntry Entry { get; init; }
    public string RankText => Entry.Rank > 0 ? $"#{Entry.Rank}" : "—";
    public string CharacterName => Entry.CharacterName;
    public string AccountText => string.IsNullOrWhiteSpace(Entry.Username) ? "—" : "@" + Entry.Username;
    public string ClassName => Humanize(Entry.ClassKey);
    public string ServerName => Entry.ServerName;
    public string BossName => Entry.BossName;
    public string DateText => Entry.FightStartedAt.ToLocalTime().ToString("yyyy.MM.dd. HH:mm");
    public string DpsText => Meter.Num(Entry.Dps) + "/s";
    public string DamageText => Meter.Num(Entry.DamageTotal);
    public string StatusText => LocalizationService.Get(Entry.Status?.ToLowerInvariant() switch
    {
        "approved" => "LeaderboardStatusApproved",
        "rejected" => "LeaderboardStatusRejected",
        "withdrawn" => "LeaderboardStatusWithdrawn",
        _ => "LeaderboardStatusPending",
    });
    public IBrush StatusColor => Brush.Parse(Entry.Status?.ToLowerInvariant() switch
    {
        "approved" => "#58E0B2",
        "rejected" => "#E0786A",
        "withdrawn" => "#71879B",
        _ => "#F5B54C",
    });
    public string ReviewText => string.IsNullOrWhiteSpace(Entry.ReviewNote)
        ? Entry.SubmittedAt.ToLocalTime().ToString("yyyy.MM.dd. HH:mm")
        : LocalizationService.Format("LeaderboardReviewNote", Entry.ReviewNote);
    public bool CanWithdraw => Entry.Status is "pending" or "approved" or "rejected";

    private static string Humanize(string value) => string.IsNullOrWhiteSpace(value)
        ? "—"
        : char.ToUpperInvariant(value[0]) + value[1..];
}

public partial class LeaderboardsView : UserControl
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<LeaderboardBoss> _bosses = new();
    private bool _ready;
    private bool _mineSelected;
    private bool _busy;
    private int _page = 1;
    private int _pages;
    private string? _withdrawArmedId;
    private string? _bossLoadError;

    public ObservableCollection<LeaderboardEntryRow> PublicEntries { get; } = new();
    public ObservableCollection<LeaderboardEntryRow> MineEntries { get; } = new();

    public LeaderboardsView()
    {
        InitializeComponent();
        DataContext = this;
        DetachedFromVisualTree += (_, _) =>
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            LocalizationService.LanguageChanged -= LanguageChanged;
            AccountService.Shared.Changed -= AccountChanged;
        };
        LocalizationService.LanguageChanged += LanguageChanged;
        AccountService.Shared.Changed += AccountChanged;
        Dispatcher.UIThread.Post(async () => await InitializeAsync(), DispatcherPriority.Loaded);
    }

    private async Task InitializeAsync()
    {
        SetupFilters();
        await LoadBossesAsync();
        _ready = true;
        await LoadCurrentTabAsync();
    }

    private void SetupFilters()
    {
        ComboBox metric = this.FindControl<ComboBox>("MetricFilter")!;
        string selectedMetric = (metric.SelectedItem as LeaderboardFilterOption)?.Value ?? "dps";
        var metrics = new[]
        {
            new LeaderboardFilterOption("dps", "DPS"),
            new LeaderboardFilterOption("damage", LocalizationService.Get("LeaderboardDamage")),
        };
        metric.ItemsSource = metrics;
        metric.SelectedItem = metrics.First(item => item.Value == selectedMetric);

        ComboBox classFilter = this.FindControl<ComboBox>("ClassFilter")!;
        string selectedClass = (classFilter.SelectedItem as LeaderboardFilterOption)?.Value ?? string.Empty;
        var classes = new List<LeaderboardFilterOption>
        {
            new(string.Empty, LocalizationService.Get("LeaderboardAllClasses")),
        };
        classes.AddRange(new[] { "gladiator", "templar", "assassin", "ranger", "sorcerer", "spiritmaster", "cleric", "chanter" }
            .Select(key => new LeaderboardFilterOption(key, char.ToUpperInvariant(key[0]) + key[1..])));
        classFilter.ItemsSource = classes;
        classFilter.SelectedItem = classes.FirstOrDefault(item => item.Value == selectedClass) ?? classes[0];
    }

    private async Task LoadBossesAsync()
    {
        try
        {
            IReadOnlyList<LeaderboardBoss> bosses = await LeaderboardService.Shared.GetBossesAsync(_lifetime.Token);
            _bosses.Clear();
            _bosses.AddRange(bosses);
            ComboBox combo = this.FindControl<ComboBox>("BossFilter")!;
            string? selected = (combo.SelectedItem as LeaderboardBoss)?.Key;
            combo.ItemsSource = null;
            combo.ItemsSource = _bosses;
            combo.SelectedItem = _bosses.FirstOrDefault(boss => boss.Key == selected) ?? _bosses.FirstOrDefault();
            _bossLoadError = null;
            SetPublicEmpty(_bosses.Count == 0, "LeaderboardNoBosses");
            ClearLeaderboardError();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Keep previously fetched boss choices on a transient transport error.
            _bossLoadError = FormatLeaderboardError(ex);
            SetLeaderboardError(ex);
            if (_bosses.Count == 0)
                SetPublicEmpty(true, "LeaderboardUnavailable", FormatLeaderboardError(ex));
        }
    }

    private Task LoadCurrentTabAsync() => _mineSelected ? LoadMineAsync() : LoadPublicAsync();

    private async Task LoadPublicAsync()
    {
        if (_busy)
            return;
        if (this.FindControl<ComboBox>("BossFilter")!.SelectedItem is not LeaderboardBoss boss)
        {
            PublicEntries.Clear();
            SetPublicEmpty(true, _bosses.Count == 0 ? "LeaderboardNoBosses" : "LeaderboardNoEntries",
                _bosses.Count == 0 ? _bossLoadError : null);
            UpdatePaging();
            return;
        }

        _busy = true;
        try
        {
            string metric = (this.FindControl<ComboBox>("MetricFilter")!.SelectedItem as LeaderboardFilterOption)?.Value ?? "dps";
            string? classKey = (this.FindControl<ComboBox>("ClassFilter")!.SelectedItem as LeaderboardFilterOption)?.Value;
            string? search = this.FindControl<TextBox>("SearchBox")!.Text;
            LeaderboardPage result = await LeaderboardService.Shared.GetLeaderboardAsync(
                boss.Key, metric, classKey, search, _page, 20, _lifetime.Token);
            PublicEntries.Clear();
            foreach (LeaderboardEntry entry in result.Entries)
                PublicEntries.Add(new LeaderboardEntryRow { Entry = entry });
            _pages = result.Pages;
            SetPublicEmpty(PublicEntries.Count == 0, "LeaderboardNoEntries");
            ClearLeaderboardError();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Do not discard valid data when a refresh fails.
            SetLeaderboardError(ex);
            if (PublicEntries.Count == 0)
                SetPublicEmpty(true, "LeaderboardUnavailable", FormatLeaderboardError(ex));
        }
        finally
        {
            _busy = false;
            UpdatePaging();
        }
    }

    private async Task LoadMineAsync()
    {
        if (_busy)
            return;
        if (AccountService.Shared.Snapshot.State != AccountConnectionState.Connected)
        {
            MineEntries.Clear();
            SetMineEmpty(true, "LeaderboardSignIn");
            return;
        }

        _busy = true;
        try
        {
            IReadOnlyList<LeaderboardEntry> entries = await LeaderboardService.Shared.GetMineAsync(_lifetime.Token);
            MineEntries.Clear();
            foreach (LeaderboardEntry entry in entries)
                MineEntries.Add(new LeaderboardEntryRow { Entry = entry });
            SetMineEmpty(MineEntries.Count == 0, "LeaderboardNoEntries");
            ClearLeaderboardError();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetLeaderboardError(ex);
            if (MineEntries.Count == 0)
                SetMineEmpty(true, "LeaderboardUnavailable", FormatLeaderboardError(ex));
        }
        finally
        {
            _busy = false;
        }
    }

    private void SetPublicEmpty(bool visible, string key, string? detail = null)
    {
        this.FindControl<Border>("PublicEmptyState")!.IsVisible = visible;
        this.FindControl<TextBlock>("PublicEmptyText")!.Text = detail ?? LocalizationService.Get(key);
    }

    private void SetMineEmpty(bool visible, string key, string? detail = null)
    {
        this.FindControl<Border>("MineEmptyState")!.IsVisible = visible;
        this.FindControl<TextBlock>("MineEmptyText")!.Text = detail ?? LocalizationService.Get(key);
    }

    // The old client swallowed HTTP/JSON errors into one generic unavailable string.
    // Show a safe diagnostic (HTTP status + API error code), not tokens or response bodies.
    private static string FormatLeaderboardError(Exception ex) => ex switch
    {
        LeaderboardApiException api =>
            $"{LocalizationService.Get("LeaderboardUnavailable")} (HTTP {api.StatusCode}, {api.Code})",
        System.Net.Http.HttpRequestException =>
            $"{LocalizationService.Get("LeaderboardUnavailable")} (network connection)",
        TaskCanceledException =>
            $"{LocalizationService.Get("LeaderboardUnavailable")} (request timed out)",
        System.Text.Json.JsonException =>
            $"{LocalizationService.Get("LeaderboardUnavailable")} (invalid API response)",
        _ => $"{LocalizationService.Get("LeaderboardUnavailable")} ({ex.GetType().Name})",
    };

    private void SetLeaderboardError(Exception ex)
    {
        Border panel = this.FindControl<Border>("LeaderboardNetworkBanner")!;
        panel.IsVisible = true;
        this.FindControl<TextBlock>("LeaderboardNetworkStatus")!.Text = FormatLeaderboardError(ex);
    }

    private void ClearLeaderboardError()
    {
        this.FindControl<Border>("LeaderboardNetworkBanner")!.IsVisible = false;
        this.FindControl<TextBlock>("LeaderboardNetworkStatus")!.Text = string.Empty;
    }

    private void UpdatePaging()
    {
        int shownPages = Math.Max(1, _pages);
        this.FindControl<TextBlock>("PageText")!.Text = LocalizationService.Format(
            "LeaderboardPage", Math.Min(_page, shownPages), shownPages);
        this.FindControl<Button>("PreviousButton")!.IsEnabled = _page > 1;
        this.FindControl<Button>("NextButton")!.IsEnabled = _pages > 0 && _page < _pages;
    }

    private async void PublicTabClick(object? sender, RoutedEventArgs e)
    {
        _mineSelected = false;
        this.FindControl<Grid>("PublicPanel")!.IsVisible = true;
        this.FindControl<Grid>("MinePanel")!.IsVisible = false;
        this.FindControl<Button>("PublicTabButton")!.Classes.Set("primary", true);
        this.FindControl<Button>("MineTabButton")!.Classes.Set("primary", false);
        await LoadPublicAsync();
    }

    private async void MineTabClick(object? sender, RoutedEventArgs e)
    {
        _mineSelected = true;
        this.FindControl<Grid>("PublicPanel")!.IsVisible = false;
        this.FindControl<Grid>("MinePanel")!.IsVisible = true;
        this.FindControl<Button>("PublicTabButton")!.Classes.Set("primary", false);
        this.FindControl<Button>("MineTabButton")!.Classes.Set("primary", true);
        await LoadMineAsync();
    }

    private async void FilterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _mineSelected)
            return;
        _page = 1;
        await LoadPublicAsync();
    }

    private async void SearchKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _mineSelected)
            return;
        _page = 1;
        await LoadPublicAsync();
    }

    private async void RefreshClick(object? sender, RoutedEventArgs e)
    {
        if (!_mineSelected)
            await LoadBossesAsync();
        await LoadCurrentTabAsync();
    }

    private async void PreviousClick(object? sender, RoutedEventArgs e)
    {
        if (_page <= 1)
            return;
        _page--;
        await LoadPublicAsync();
    }

    private async void NextClick(object? sender, RoutedEventArgs e)
    {
        if (_pages <= 0 || _page >= _pages)
            return;
        _page++;
        await LoadPublicAsync();
    }

    private async void WithdrawClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LeaderboardEntry entry })
            return;
        TextBlock status = this.FindControl<TextBlock>("ActionStatusText")!;
        if (!string.Equals(_withdrawArmedId, entry.Id, StringComparison.Ordinal))
        {
            _withdrawArmedId = entry.Id;
            status.Text = LocalizationService.Get("LeaderboardWithdrawConfirm");
            return;
        }

        _withdrawArmedId = null;
        try
        {
            await LeaderboardService.Shared.WithdrawAsync(entry.Id, _lifetime.Token);
            status.Text = LocalizationService.Get("LeaderboardWithdrawn");
            await LoadMineAsync();
        }
        catch (Exception ex)
        {
            status.Text = FormatLeaderboardError(ex);
        }
    }

    private void LanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            SetupFilters();
            UpdatePaging();
            _ = LoadCurrentTabAsync();
        });

    private void AccountChanged(object? sender, EventArgs e)
    {
        if (_mineSelected)
            Dispatcher.UIThread.Post(async () => await LoadMineAsync());
    }
}
