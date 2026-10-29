using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using Avalonia.Threading;

namespace AION_2_Companion.ViewModels;

public sealed class AccountPageViewModel : INotifyPropertyChanged, IDisposable
{
    public AccountPageViewModel()
    {
        AccountService.Shared.Changed += AccountChanged;
        LocalizationService.LanguageChanged += LanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private AccountServiceSnapshot Snapshot => AccountService.Shared.Snapshot;

    public bool IsConnected => Snapshot.Profile is not null;
    public bool IsDisconnected => !IsConnected;
    public bool IsConnecting => Snapshot.State == AccountConnectionState.Connecting;
    public bool CanConnect => Snapshot.CanConnect && !IsConnecting;
    public bool CanDisconnect => Snapshot.CanDisconnect && !IsConnecting;
    public bool HasPairingCode => !string.IsNullOrWhiteSpace(PairingCode);
    public string Username => Snapshot.Profile?.Username ?? "—";
    public string AccountId => Snapshot.Profile?.AccountId ?? "—";
    public string StatusText => LocalizationService.Get(Snapshot.StatusKey);
    public string StatusBadge => LocalizationService.Get(Snapshot.State switch
    {
        AccountConnectionState.Connected => "AccountStatusConnected",
        AccountConnectionState.Connecting => "AccountStatusConnecting",
        AccountConnectionState.Offline => "AccountStatusOffline",
        AccountConnectionState.SessionExpired => "AccountStatusExpired",
        AccountConnectionState.Error => "AccountStatusError",
        AccountConnectionState.ContractPending => "AccountStatusPending",
        _ => "AccountStatusDisconnected",
    });
    public string PairingCode => AccountService.Shared.PendingDevice?.UserCode ?? string.Empty;

    public Task ConnectAsync() => AccountService.Shared.ConnectAsync();
    public Task DisconnectAsync() => AccountService.Shared.DisconnectAsync();
    public void CancelConnect() => AccountService.Shared.CancelConnect();

    private void AccountChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(NotifyAll);

    private void LanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(NotifyAll);

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(HasPairingCode));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(AccountId));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(PairingCode));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        AccountService.Shared.Changed -= AccountChanged;
        LocalizationService.LanguageChanged -= LanguageChanged;
    }
}
