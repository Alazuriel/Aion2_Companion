using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AION_2_Companion.ViewModels;

public sealed class CharacterSearchViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CharacterCenterService _center = CharacterCenterService.Shared;
    private readonly DispatcherTimer _cooldownTimer;
    private CancellationTokenSource? _cancellation;
    private string _query = string.Empty;
    private string _region = "eu";
    private bool _isBusy;
    private string _statusText = string.Empty;

    public CharacterSearchViewModel()
    {
        SearchCommand = new AsyncRelayCommand(() => SearchAsync(false), CanSearch);
        RefreshCommand = new AsyncRelayCommand(() => SearchAsync(true), () => Results.Count > 0 && !IsBusy && RefreshRemaining <= TimeSpan.Zero);
        OpenCommand = new AsyncRelayCommand<CharacterSearchResult>(OpenAsync);
        SelectAsSelfCommand = new RelayCommand<CharacterSearchResult>(SelectAsSelf);
        _cooldownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => NotifyCooldown());
        _cooldownTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? OpenProfileRequested;
    public ObservableCollection<CharacterSearchResult> Results { get; } = [];
    public string[] Regions { get; } = ["eu", "nae", "naw", "la", "as"];
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<CharacterSearchResult> OpenCommand { get; }
    public IRelayCommand<CharacterSearchResult> SelectAsSelfCommand { get; }
    public string Query { get => _query; set { if (_query == value) return; _query = value; OnPropertyChanged(); SearchCommand.NotifyCanExecuteChanged(); } }
    public string Region { get => _region; set { if (_region == value) return; _region = value; OnPropertyChanged(); SearchCommand.NotifyCanExecuteChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRefresh)); SearchCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool HasResults => Results.Count > 0;
    public bool HasNoResults => !IsBusy && Results.Count == 0;
    public TimeSpan RefreshRemaining => _center.Cooldown.Remaining(SearchCooldownKey);
    public bool CanRefresh => Results.Count > 0 && !IsBusy && RefreshRemaining <= TimeSpan.Zero;
    public string RefreshText => RefreshRemaining > TimeSpan.Zero ? $"{LocalizationService.Get("CharacterRefresh")} ({Math.Ceiling(RefreshRemaining.TotalSeconds):0})" : LocalizationService.Get("CharacterRefresh");
    private string SearchCooldownKey => $"search:{CharacterApiClient.NormalizeRegion(Region)}:{Query.Trim().ToLowerInvariant()}";

    private bool CanSearch() => !IsBusy && Query.Trim().Length >= 2;

    private async Task SearchAsync(bool forceRefresh)
    {
        string name = Query.Trim();
        if (name.Length < 2) return;
        string key = SearchCooldownKey;
        IDisposable? lease = null;
        if (forceRefresh && !_center.Cooldown.TryBegin(key, out lease, out _)) return;
        if (forceRefresh) _center.Cooldown.StartCooldown(key);
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = LocalizationService.Get("CharacterSearching");
        try
        {
            CharacterSearchLoadResult result = await _center.SearchAsync(name, Region, forceRefresh, _cancellation.Token);
            Results.Clear();
            foreach (CharacterSearchResult item in result.Response.Results) Results.Add(item);
            StatusText = result.Offline ? LocalizationService.Get("CharacterCachedOffline") : Results.Count == 0 ? LocalizationService.Get("CharacterNoResults") : result.FromLocalCache ? LocalizationService.Get("CharacterCached") : string.Empty;
            NotifyResults();
        }
        catch (CharacterApiException ex)
        {
            if (ex.RetryAfter is { } retry) _center.Cooldown.StartCooldown(key, retry);
            StatusText = ex.Message;
        }
        catch (OperationCanceledException) { }
        catch { StatusText = LocalizationService.Get("CharacterUnavailable"); }
        finally { lease?.Dispose(); IsBusy = false; }
    }

    private async Task OpenAsync(CharacterSearchResult? result)
    {
        if (result is null || IsBusy) return;
        IsBusy = true;
        StatusText = LocalizationService.Get("CharacterLoading");
        try
        {
            CharacterLoadResult standard = await _center.LoadProfileAsync(result.Identity, "standard", false, _cancellation?.Token ?? default);
            CharacterProfileService.Shared.Replace(CharacterProfileMapper.Map(standard));
            OpenProfileRequested?.Invoke(this, EventArgs.Empty);
            _ = LoadFullAsync(result.Identity);
        }
        catch { StatusText = LocalizationService.Get("CharacterUnavailable"); }
        finally { IsBusy = false; }
    }

    private async Task LoadFullAsync(CharacterIdentity identity)
    {
        try { CharacterLoadResult full = await _center.LoadProfileAsync(identity, "full", false, default); CharacterProfileService.Shared.Replace(CharacterProfileMapper.Map(full)); }
        catch { }
    }

    private void SelectAsSelf(CharacterSearchResult? result) { if (result is null) return; _center.SelectSelf(result.Identity); StatusText = LocalizationService.Get("CharacterSelectedAsSelf"); }
    private void NotifyCooldown() { OnPropertyChanged(nameof(RefreshRemaining)); OnPropertyChanged(nameof(RefreshText)); OnPropertyChanged(nameof(CanRefresh)); RefreshCommand.NotifyCanExecuteChanged(); }
    private void NotifyResults() { OnPropertyChanged(nameof(HasResults)); OnPropertyChanged(nameof(HasNoResults)); RefreshCommand.NotifyCanExecuteChanged(); }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public void Dispose() { _cooldownTimer.Stop(); _cancellation?.Cancel(); _cancellation?.Dispose(); }
}
