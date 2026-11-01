using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.Characters.Verification;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AION_2_Companion.ViewModels;

public sealed partial class MyCharactersViewModel
{
    private readonly VerifiedCharactersApiClient _verifiedApi = new();
    private CancellationTokenSource? _verifiedCancellation;
    private string? _verifiedAccountId;
    private string? _lastVerifiedChallengeId;
    private DateTimeOffset _nextAutomaticVerifiedSync = DateTimeOffset.MinValue;
    private int _verifiedGeneration;
    private bool _verifiedIsLoading;
    private string _verifiedStatusText = "Connect a Nexforge account to see your verified characters.";
    private bool _verifiedDisposed;

    public ObservableCollection<VerifiedCharacterSummary> VerifiedCharacters { get; } = new();
    public bool HasVerifiedCharacters => VerifiedCharacters.Count > 0;
    public bool VerifiedIsLoading
    {
        get => _verifiedIsLoading;
        private set
        {
            if (_verifiedIsLoading == value) return;
            _verifiedIsLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSyncVerifiedCharacters));
            SyncVerifiedCharactersCommand.NotifyCanExecuteChanged();
        }
    }
    public bool CanSyncVerifiedCharacters => !_verifiedDisposed && !VerifiedIsLoading &&
        AccountService.Shared.Snapshot.State == AccountConnectionState.Connected;
    public string VerifiedStatusText
    {
        get => _verifiedStatusText;
        private set { _verifiedStatusText = value; OnPropertyChanged(); }
    }
    public IAsyncRelayCommand SyncVerifiedCharactersCommand { get; private set; } = null!;

    private void InitializeVerifiedCharacters()
    {
        SyncVerifiedCharactersCommand = new AsyncRelayCommand(
            () => RefreshVerifiedCharactersAsync(manual: true), () => CanSyncVerifiedCharacters);
        AccountService.Shared.Changed += VerifiedAccountChanged;
        VerifiedAccountChanged(this, EventArgs.Empty);
    }

    private void VerifiedAccountChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_verifiedDisposed) return;
        AccountServiceSnapshot snapshot = AccountService.Shared.Snapshot;
        string? accountId = snapshot.State == AccountConnectionState.Connected
            ? snapshot.Profile?.AccountId : null;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            _verifiedAccountId = null;
            ClearVerifiedCharacters();
            VerifiedStatusText = snapshot.State == AccountConnectionState.Offline
                ? "Connect to Nexforge to confirm your verified characters."
                : "Connect a Nexforge account to see your verified characters.";
            SyncVerifiedCharactersCommand.NotifyCanExecuteChanged();
            return;
        }

        if (!string.Equals(_verifiedAccountId, accountId, StringComparison.Ordinal))
        {
            _verifiedAccountId = accountId;
            _lastVerifiedChallengeId = null;
            ClearVerifiedCharacters();
            _nextAutomaticVerifiedSync = DateTimeOffset.MinValue;
        }

        SyncVerifiedCharactersCommand.NotifyCanExecuteChanged();
        if (DateTimeOffset.UtcNow >= _nextAutomaticVerifiedSync)
            _ = RefreshVerifiedCharactersAsync(manual: false);
    });

    // Triggered after the server reports a completed equipment-change challenge.
    // Never trust the challenge/local cache as the *list* of verified characters.
    private void VerifiedVerificationChanged()
    {
        CharacterVerificationChallenge? challenge = CharacterVerificationService.Shared.Current;
        if (challenge?.Verified == true &&
            string.Equals(challenge.Status, "verified", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(challenge.ChallengeId, _lastVerifiedChallengeId, StringComparison.Ordinal))
        {
            _lastVerifiedChallengeId = challenge.ChallengeId;
            _ = RefreshVerifiedCharactersAsync(manual: true);
        }
    }

    private bool IsServerVerified(CharacterIdentity identity) =>
        VerifiedCharacters.Any(entry => entry.Identity.CacheKey == identity.CacheKey);

    public void SelectVerifiedCharacter(VerifiedCharacterSummary entry)
    {
        if (_verifiedDisposed || !VerifiedCharacters.Contains(entry)) return;
        _center.SelectSelf(entry.Identity);
    }

    private void ClearVerifiedCharacters()
    {
        ++_verifiedGeneration;
        _verifiedCancellation?.Cancel();
        _verifiedCancellation?.Dispose();
        _verifiedCancellation = null;
        VerifiedCharacters.Clear();
        VerifiedIsLoading = false;
        OnPropertyChanged(nameof(HasVerifiedCharacters));
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(VerificationBadge));
    }

    private async Task RefreshVerifiedCharactersAsync(bool manual)
    {
        if (_verifiedDisposed) return;
        AccountServiceSnapshot snapshot = AccountService.Shared.Snapshot;
        string? accountId = snapshot.State == AccountConnectionState.Connected
            ? snapshot.Profile?.AccountId : null;
        if (string.IsNullOrWhiteSpace(accountId)) return;
        if (!manual && DateTimeOffset.UtcNow < _nextAutomaticVerifiedSync) return;

        _verifiedAccountId = accountId;
        _nextAutomaticVerifiedSync = DateTimeOffset.UtcNow.AddMinutes(2);
        ++_verifiedGeneration;
        int generation = _verifiedGeneration;
        _verifiedCancellation?.Cancel();
        _verifiedCancellation?.Dispose();
        _verifiedCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _verifiedCancellation.Token;
        // Clear before every request so revoked ownership is never presented as confirmed.
        VerifiedCharacters.Clear();
        OnPropertyChanged(nameof(HasVerifiedCharacters));
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(VerificationBadge));
        VerifiedIsLoading = true;
        VerifiedStatusText = "Syncing verified characters...";

        try
        {
            IReadOnlyList<VerifiedCharacterSummary> result =
                await _verifiedApi.GetVerifiedAsync(cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentVerifiedRequest(generation, accountId)) return;
                foreach (VerifiedCharacterSummary entry in result) VerifiedCharacters.Add(entry);
                VerifiedStatusText = result.Count == 0
                    ? "No server-verified characters are linked to this account yet."
                    : $"{result.Count} Nexforge-verified character(s).";
                OnPropertyChanged(nameof(HasVerifiedCharacters));
                OnPropertyChanged(nameof(IsVerified));
                OnPropertyChanged(nameof(VerificationBadge));
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (VerifiedCharactersApiException ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentVerifiedRequest(generation, accountId)) VerifiedStatusText = ex.Message;
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentVerifiedRequest(generation, accountId))
                    VerifiedStatusText = "Could not confirm verified characters right now. Please retry the sync.";
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentVerifiedRequest(generation, accountId)) VerifiedIsLoading = false;
            });
        }
    }

    private bool IsCurrentVerifiedRequest(int generation, string accountId) =>
        !_verifiedDisposed && generation == _verifiedGeneration &&
        AccountService.Shared.Snapshot.State == AccountConnectionState.Connected &&
        string.Equals(AccountService.Shared.Snapshot.Profile?.AccountId, accountId, StringComparison.Ordinal);

    private void DisposeVerifiedCharacters()
    {
        _verifiedDisposed = true;
        AccountService.Shared.Changed -= VerifiedAccountChanged;
        ++_verifiedGeneration;
        _verifiedCancellation?.Cancel();
        _verifiedCancellation?.Dispose();
        _verifiedApi.Dispose();
    }
}
