using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.Characters.Verification;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AION_2_Companion.ViewModels;

public sealed partial class MyCharactersViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CharacterCenterService _center = CharacterCenterService.Shared;
    private readonly DispatcherTimer _cooldownTimer;
    private DetectedCharacterSnapshot? _character = DetectedCharacterService.Shared.Current;
    private bool _isLoading;
    private string _statusText = string.Empty;
    private CancellationTokenSource? _loadCancellation;
    private long _loadGeneration;

    private CharacterProfile? SelectedProfile
    {
        get
        {
            CharacterIdentity? identity = _center.CurrentSelf;
            CharacterProfile? profile = CharacterProfileService.Shared.Current;
            return identity is not null && profile is not null && profile.ServerId == identity.ServerId &&
                string.Equals(profile.Region, identity.Region, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.Name, identity.Name, StringComparison.OrdinalIgnoreCase) ? profile : null;
        }
    }
    private DetectedCharacterSnapshot? SelectedDetected
    {
        get
        {
            CharacterIdentity? identity = _center.CurrentSelf;
            return identity is not null && _character?.ServerId == identity.ServerId &&
                string.Equals(_character.Name, identity.Name, StringComparison.OrdinalIgnoreCase) ? _character : null;
        }
    }

    public MyCharactersViewModel()
    {
        DetectedCharacterService.Shared.Changed += CharacterChanged;
        _center.SelfChanged += SelfChanged;
        _center.SelfProfileUpdated += SelfProfileUpdated;
        CharacterProfileService.Shared.Changed += ProfileChanged;
        CharacterVerificationService.Shared.Changed += VerificationChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(true), () => HasCharacter && !IsLoading && RefreshRemaining <= TimeSpan.Zero);
        _cooldownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => NotifyCooldown());
        _cooldownTimer.Start();
        InitializeVerifiedCharacters();
        _ = LoadAsync(false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IAsyncRelayCommand RefreshCommand { get; }
    public bool HasCharacter => _center.CurrentSelf is not null || _character is not null;
    public bool HasNoCharacter => !HasCharacter;
    public string Name => _center.CurrentSelf?.Name ?? _character?.Name ?? "—";
    public string ClassName => SelectedProfile?.ClassName ?? SelectedDetected?.ClassName ?? "—";
    public string? ClassIconSource => SelectedDetected?.ClassIconPath;
    public string LevelText => SelectedProfile?.Level is > 0 ? LocalizationService.Format("LevelValue", SelectedProfile.Level) : SelectedDetected?.Level is byte level ? LocalizationService.Format("LevelValue", level) : LocalizationService.Get("UnknownLevel");
    public string GearScoreText => SelectedProfile?.CombatPower is int cp ? $"CP {cp:N0}" : SelectedDetected?.GearScore is uint score ? LocalizationService.Format("GearScoreValue", score) : LocalizationService.Get("UnknownGearScore");
    public string ServerText => SelectedProfile?.ServerName ?? (!string.IsNullOrWhiteSpace(SelectedDetected?.ServerName) ? SelectedDetected!.ServerName : _center.CurrentSelf?.ServerId.ToString() ?? LocalizationService.Get("UnknownServer"));
    public string DetectedText => _character is null ? string.Empty : LocalizationService.Format("DetectedAt", _character.DetectedAtUtc.ToLocalTime().ToString("g"));
    public string CharacterTooltip => HasCharacter ? $"{Name}\n{ClassName}\n{LevelText}\n{GearScoreText}" : LocalizationService.Get("NoCharacterText");
    public CharacterIdentity? CurrentIdentity => _center.CurrentSelf;
    public string? ProfileImageUrl => SelectedProfile?.ImageUri?.ToString();
    public bool CanVerify => CurrentIdentity is not null;
    public bool IsVerified => CurrentIdentity is { } identity && IsServerVerified(identity);
    public string VerificationBadge => IsVerified ? LocalizationService.Get("VerificationVerifiedBadge") : LocalizationService.Get("VerificationVerifyButton");
    public bool IsLoading { get => _isLoading; private set { if (_isLoading == value) return; _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRefresh)); RefreshCommand.NotifyCanExecuteChanged(); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public TimeSpan RefreshRemaining => _center.CurrentSelf is { } self ? _center.Cooldown.Remaining("profile:" + self.CacheKey) : TimeSpan.Zero;
    public bool CanRefresh => HasCharacter && !IsLoading && RefreshRemaining <= TimeSpan.Zero;
    public string RefreshText => RefreshRemaining > TimeSpan.Zero ? $"{LocalizationService.Get("CharacterRefresh")} ({Math.Ceiling(RefreshRemaining.TotalSeconds):0})" : LocalizationService.Get("CharacterRefresh");

    private async Task LoadAsync(bool forceRefresh)
    {
        CharacterIdentity? identity = _center.CurrentSelf;
        if (identity is null) return;
        string cooldownKey = "profile:" + identity.CacheKey;
        IDisposable? lease = null;
        if (forceRefresh && !_center.Cooldown.TryBegin(cooldownKey, out lease, out _)) return;
        if (forceRefresh) _center.Cooldown.StartCooldown(cooldownKey);
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        long generation = Interlocked.Increment(ref _loadGeneration);
        CancellationToken token = _loadCancellation.Token;
        IsLoading = true;
        StatusText = LocalizationService.Get("CharacterLoading");
        try
        {
            CharacterLoadResult result = await _center.LoadProfileAsync(identity, "standard", forceRefresh, token);
            if (generation != Interlocked.Read(ref _loadGeneration) || _center.CurrentSelf?.CacheKey != identity.CacheKey) return;
            CharacterProfileService.Shared.Replace(CharacterProfileMapper.Map(result));
            StatusText = result.Offline ? LocalizationService.Get("CharacterCachedOffline") : result.FromLocalCache ? LocalizationService.Get("CharacterCached") : string.Empty;
            _ = LoadFullAsync(identity, generation, token);
        }
        catch (CharacterApiException ex)
        {
            if (ex.RetryAfter is { } retry) _center.Cooldown.StartCooldown(cooldownKey, retry);
            StatusText = ex.Message;
        }
        catch (OperationCanceledException) { }
        catch { StatusText = LocalizationService.Get("CharacterUnavailable"); }
        finally { lease?.Dispose(); if (generation == Interlocked.Read(ref _loadGeneration)) IsLoading = false; }
    }

    private async Task LoadFullAsync(CharacterIdentity identity, long generation, CancellationToken token)
    {
        try
        {
            CharacterLoadResult full = await _center.LoadProfileAsync(identity, "full", false, token);
            if (generation == Interlocked.Read(ref _loadGeneration) && _center.CurrentSelf?.CacheKey == identity.CacheKey)
                CharacterProfileService.Shared.Replace(CharacterProfileMapper.Map(full));
        }
        catch { }
    }

    private void SelfProfileUpdated(object? sender, CharacterLoadResult result) => Dispatcher.UIThread.Post(() =>
    {
        CharacterApiHeader? actual = result.Response.Data?.Character;
        if (actual is null || _center.CurrentSelf is not { } selected || selected.ServerId != actual.ServerId ||
            !string.Equals(selected.Region, actual.Region, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selected.Name, actual.Name, StringComparison.OrdinalIgnoreCase)) return;
        CharacterProfileService.Shared.Replace(CharacterProfileMapper.Map(result));
    });
    private void CharacterChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { _character = DetectedCharacterService.Shared.Current; NotifyAll(); });
    private void SelfChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        Interlocked.Increment(ref _loadGeneration);
        _loadCancellation?.Cancel();
        CharacterProfileService.Shared.Clear(); // Never keep the previous character's stats on screen.
        StatusText = string.Empty;
        NotifyAll();
        _ = LoadAsync(false);
    });
    private void LanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { NotifyAll(); RefreshVerifiedStatusLocalization(); });
    private void ProfileChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(NotifyAll);
    private void VerificationChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => { NotifyAll(); VerifiedVerificationChanged(); });
    private void NotifyCooldown() { OnPropertyChanged(nameof(RefreshRemaining)); OnPropertyChanged(nameof(RefreshText)); OnPropertyChanged(nameof(CanRefresh)); RefreshCommand.NotifyCanExecuteChanged(); }
    private void NotifyAll() { foreach (string property in new[] { nameof(HasCharacter), nameof(HasNoCharacter), nameof(Name), nameof(ClassName), nameof(ClassIconSource), nameof(LevelText), nameof(GearScoreText), nameof(ServerText), nameof(DetectedText), nameof(CharacterTooltip), nameof(CurrentIdentity), nameof(ProfileImageUrl), nameof(CanVerify), nameof(IsVerified), nameof(VerificationBadge), nameof(RefreshText), nameof(CanRefresh) }) OnPropertyChanged(property); }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _cooldownTimer.Stop();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        DetectedCharacterService.Shared.Changed -= CharacterChanged;
        _center.SelfChanged -= SelfChanged;
        _center.SelfProfileUpdated -= SelfProfileUpdated;
        CharacterProfileService.Shared.Changed -= ProfileChanged;
        CharacterVerificationService.Shared.Changed -= VerificationChanged;
        DisposeVerifiedCharacters();
        LocalizationService.LanguageChanged -= LanguageChanged;
    }
}
