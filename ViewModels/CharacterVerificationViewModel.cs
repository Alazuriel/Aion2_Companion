using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.Characters.Verification;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AION_2_Companion.ViewModels;

public sealed class CharacterVerificationViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly CharacterVerificationService _service = CharacterVerificationService.Shared;
    private readonly DispatcherTimer _timer;
    private string _message = string.Empty;
    private bool _initialized;

    public CharacterVerificationViewModel(CharacterIdentity identity, string serverName, string? imageUrl)
    {
        Identity = identity;
        ServerName = string.IsNullOrWhiteSpace(serverName) ? $"Server {identity.ServerId}" : serverName;
        ImageUrl = imageUrl;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => CanCheck);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => CanCancel);
        StartAgainCommand = new AsyncRelayCommand(StartAgainAsync, () => CanStartAgain);
        _service.Changed += ServiceChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => NotifyAll());
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public CharacterIdentity Identity { get; }
    public string ServerName { get; }
    public string? ImageUrl { get; }
    public IAsyncRelayCommand CheckCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand StartAgainCommand { get; }
    public CharacterVerificationChallenge? Challenge => _service.IsCurrent(Identity) ? _service.Current : null;
    public bool IsBusy => _service.IsBusy;
    public bool IsConnected => AccountService.Shared.Snapshot.State == AccountConnectionState.Connected;
    public bool HasChallenge => Challenge is not null;
    public bool IsPending => Challenge?.IsActive == true;
    public bool IsChecking => IsBusy || Challenge?.Status == "checking";
    public bool IsVerified => _service.IsVerified(Identity);
    public bool IsUnconfirmedSavedVerification => Challenge?.Status == "verified" && !IsVerified;
    // Never mark a server-pending challenge expired from the desktop timer.
    // The API owns the state; a zero display only means a new status is needed.
    public bool IsExpired => Challenge?.Status == "expired";
    public bool IsCanceled => Challenge?.Status == "canceled";
    public string SlotName => LocalizationService.Get("VerificationSlot." + (Challenge?.Instruction.SlotKey ?? "unknown"));
    public string StatusTitle => IsVerified ? LocalizationService.Get("VerificationSuccessTitle") : IsUnconfirmedSavedVerification ? LocalizationService.Get("VerificationOfflineUnconfirmed") : IsExpired ? LocalizationService.Get("VerificationExpiredTitle") : IsCanceled ? LocalizationService.Get("VerificationCanceledTitle") : LocalizationService.Get("VerificationWaitingTitle");
    public string StatusText => IsVerified ? LocalizationService.Get("VerificationSuccessText") : IsUnconfirmedSavedVerification ? LocalizationService.Get("VerificationOfflineUnconfirmedDetails") : IsExpired ? LocalizationService.Get("VerificationExpiredText") : IsCanceled ? LocalizationService.Get("VerificationCanceledText") : LocalizationService.Get("VerificationWaitingText");
    public TimeSpan CheckRemaining => Challenge is null ? TimeSpan.Zero : _service.CheckRemaining;
    public TimeSpan ExpiryRemaining => Challenge is null ? TimeSpan.Zero : _service.ExpiryRemaining;
    public string CheckButtonText => CheckRemaining > TimeSpan.Zero
        ? LocalizationService.Format("VerificationCheckCountdown", Math.Ceiling(CheckRemaining.TotalSeconds))
        : LocalizationService.Get("VerificationCheckAgain");
    public string ExpiryText => LocalizationService.Format("VerificationExpiresIn", FormatDuration(ExpiryRemaining));
    // API is authoritative about expiration; don't permanently block Check
    // because a client clock/timer incorrectly reached zero.
    public bool CanCheck => IsConnected && IsPending && !IsBusy && CheckRemaining <= TimeSpan.Zero;
    public bool CanCancel => Challenge?.IsActive == true && !IsBusy;
    public bool CanStartAgain => IsConnected && !IsBusy &&
        (Challenge is null || IsExpired || IsCanceled);
    public string Message { get => _message; private set { _message = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMessage)); } }
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        Message = string.Empty;
        if (!IsConnected)
        {
            Message = LocalizationService.Get("VerificationSignInRequired");
            NotifyAll();
            return;
        }

        await _service.RestoreAsync();
        if (_service.Current?.IsActive == true && !_service.IsCurrent(Identity))
        {
            Message = LocalizationService.Get("VerificationOtherChallengeActive");
            NotifyAll();
            return;
        }
        if (_service.IsCurrent(Identity)) { NotifyAll(); return; }

        try { await _service.StartAsync(Identity); }
        catch (VerificationSignInRequiredException) { Message = LocalizationService.Get("VerificationSignInRequired"); }
        catch (CharacterVerificationApiException ex) { Message = LocalizeError(ex); }
        catch { Message = LocalizationService.Get("VerificationUnavailable"); }
        NotifyAll();
    }

    private async Task StartAgainAsync()
    {
        if (!CanStartAgain) return;
        Message = string.Empty;
        try
        {
            // Server creates a fresh challenge and chooses the new random slot.
            // Never select an item locally, and do not reuse an expired ID.
            await _service.StartAsync(Identity);
        }
        catch (VerificationSignInRequiredException) { Message = LocalizationService.Get("VerificationSignInRequired"); }
        catch (CharacterVerificationApiException ex) { Message = LocalizeError(ex); }
        catch { Message = LocalizationService.Get("VerificationUnavailable"); }
        NotifyAll();
    }

    private async Task CheckAsync()
    {
        Message = LocalizationService.Get("VerificationCheckingOfficialData");
        try
        {
            VerificationCheckResponse response = await _service.CheckAsync();
            Message = response.Verified ? LocalizationService.Get("VerificationSuccessText") : LocalizationService.Get("VerificationStillWaiting");
        }
        catch (CharacterVerificationApiException ex) { Message = LocalizeError(ex); }
        catch (VerificationSignInRequiredException) { Message = LocalizationService.Get("VerificationSignInRequired"); }
        catch { Message = LocalizationService.Get("VerificationUnavailable"); }
        NotifyAll();
    }

    private async Task CancelAsync()
    {
        try { await _service.CancelAsync(); Message = LocalizationService.Get("VerificationCanceledText"); }
        catch (CharacterVerificationApiException ex) { Message = LocalizeError(ex); }
        catch { Message = LocalizationService.Get("VerificationUnavailable"); }
        NotifyAll();
    }

    private static string LocalizeError(CharacterVerificationApiException exception) => exception.Code switch
    {
        "character_already_claimed" => LocalizationService.Get("VerificationAlreadyClaimed"),
        "already_verified" => LocalizationService.Get("VerificationAlreadyVerified"),
        "challenge_in_progress" => LocalizationService.Get("VerificationOtherChallengeActive"),
        "challenge_expired" => LocalizationService.Get("VerificationExpiredText"),
        "verification_cooldown" or "verification_in_progress" or "local_cooldown" => LocalizationService.Get("VerificationCooldown"),
        _ when exception.StatusCode == 401 => LocalizationService.Get("VerificationSignInRequired"),
        _ when exception.StatusCode is 502 or 503 => LocalizationService.Get("VerificationOfficialDataUnavailable"),
        _ => LocalizationService.Get("VerificationUnavailable"),
    };

    private void ServiceChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(NotifyAll);
    private void LanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(NotifyAll);
    private void NotifyAll()
    {
        foreach (string property in new[] { nameof(Challenge), nameof(IsBusy), nameof(IsConnected), nameof(HasChallenge), nameof(IsPending), nameof(IsChecking), nameof(IsVerified), nameof(IsUnconfirmedSavedVerification), nameof(IsExpired), nameof(IsCanceled), nameof(SlotName), nameof(StatusTitle), nameof(StatusText), nameof(CheckRemaining), nameof(ExpiryRemaining), nameof(CheckButtonText), nameof(ExpiryText), nameof(CanCheck), nameof(CanCancel), nameof(CanStartAgain) }) OnPropertyChanged(property);
        CheckCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        StartAgainCommand.NotifyCanExecuteChanged();
    }
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    public void Dispose() { _timer.Stop(); _service.Changed -= ServiceChanged; LocalizationService.LanguageChanged -= LanguageChanged; }
}
