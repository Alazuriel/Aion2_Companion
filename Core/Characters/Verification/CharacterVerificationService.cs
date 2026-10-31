using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion.Core.Characters.Verification;

public sealed class CharacterVerificationService : IDisposable
{
    public static CharacterVerificationService Shared { get; } = new(
        new CharacterVerificationApiClient(), new CharacterVerificationStateStore());

    private readonly ICharacterVerificationApi _api;
    private readonly ICharacterVerificationStateStore _store;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _started;
    private StoredVerificationState? _stored;
    private bool _serverConfirmed;
    // Durations received from the API are based on SERVER time. Never compare
    // the API's timezone-less SQL timestamps against the desktop PC clock.
    private long _timingAnchor = Stopwatch.GetTimestamp();
    private TimeSpan _expiryAtAnchor = TimeSpan.Zero;
    private TimeSpan _checkAtAnchor = TimeSpan.Zero;

    public TimeSpan ExpiryRemaining => Remaining(_expiryAtAnchor);
    public TimeSpan CheckRemaining => Remaining(_checkAtAnchor);

    private TimeSpan Remaining(TimeSpan initial)
    {
        TimeSpan remaining = initial - Stopwatch.GetElapsedTime(_timingAnchor);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void SetCheckCooldown(TimeSpan cooldown)
    {
        // Re-anchor both countdowns without extending the server's expiry.
        _expiryAtAnchor = ExpiryRemaining;
        _checkAtAnchor = cooldown > TimeSpan.Zero ? cooldown : TimeSpan.Zero;
        _timingAnchor = Stopwatch.GetTimestamp();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExpiresAtUtc = now + _expiryAtAnchor; // diagnostics / old consumers
        NextCheckAtUtc = now + _checkAtAnchor;
    }

    public CharacterVerificationService(ICharacterVerificationApi api, ICharacterVerificationStateStore store)
    {
        _api = api;
        _store = store;
    }

    public event EventHandler? Changed;
    public CharacterVerificationChallenge? Current { get; private set; }
    public bool IsBusy { get; private set; }
    public string? LastResult { get; private set; }
    public DateTimeOffset NextCheckAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public void Start()
    {
        if (_started) return;
        _started = true;
        AccountService.Shared.Changed += AccountChanged;
        _ = RestoreAsync();
    }

    public bool IsVerified(CharacterIdentity identity)
    {
        if (!_serverConfirmed || _stored?.Challenge.Verified != true || _stored.Challenge.Identity.CacheKey != identity.CacheKey) return false;
        string? accountId = AccountService.Shared.Snapshot.Profile?.AccountId;
        return !string.IsNullOrWhiteSpace(accountId) && string.Equals(accountId, _stored.AccountId, StringComparison.Ordinal);
    }

    public bool IsCurrent(CharacterIdentity identity) => Current?.Identity.CacheKey == identity.CacheKey;

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            _stored ??= await _store.LoadAsync(cancellationToken);
            if (_stored is null) return;
            string? accountId = AccountService.Shared.Snapshot.Profile?.AccountId;
            if (string.IsNullOrWhiteSpace(accountId) || !string.Equals(accountId, _stored.AccountId, StringComparison.Ordinal)) return;

            // DPAPI state is for recovery only. A fresh server status is required
            // before showing the Verified badge in a new desktop session.
            _serverConfirmed = false;
            SetBusy(true);
            VerificationStatusResponse response = await _api.GetStatusAsync(_stored.Challenge.ChallengeId, cancellationToken);
            if (response.Challenge.ChallengeId != _stored.Challenge.ChallengeId ||
                response.Challenge.Identity.CacheKey != _stored.Challenge.Identity.CacheKey)
                throw new InvalidOperationException("Verification status does not match the saved challenge.");
            _serverConfirmed = response.Challenge.Verified && response.Challenge.Status == "verified";
            Apply(response.Challenge, null);
            await PersistAsync(response.Challenge, cancellationToken);
            if (_serverConfirmed) MarkVisibleProfileVerified(response.Challenge.Identity);
        }
        catch (VerificationSignInRequiredException) { }
        catch (CharacterVerificationApiException ex) when (ex.StatusCode is 404 or 410)
        {
            await _store.DeleteAsync();
            _stored = null;
            Current = null;
            _serverConfirmed = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CharacterVerificationApiException)
        {
            if (_stored is not null) Apply(_stored.Challenge, null);
        }
        finally { SetBusy(false); _operationLock.Release(); }
    }

    public async Task<CharacterVerificationChallenge> StartAsync(CharacterIdentity identity, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            SetBusy(true);
            VerificationStartResponse response = await _api.StartAsync(identity, cancellationToken);
            if (response.Challenge.Identity.CacheKey != identity.CacheKey)
                throw new InvalidOperationException("Verification API returned a different character.");
            _serverConfirmed = response.Challenge.Verified && response.Challenge.Status == "verified";
            Apply(response.Challenge, response.MessageCode);
            await PersistAsync(response.Challenge, cancellationToken);
            return response.Challenge;
        }
        finally { SetBusy(false); _operationLock.Release(); }
    }

    public async Task<VerificationCheckResponse> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (Current is null || !Current.IsActive) throw new InvalidOperationException("There is no active verification.");
            TimeSpan wait = CheckRemaining;
            if (wait > TimeSpan.Zero)
                throw new CharacterVerificationApiException("Wait for the countdown before checking again.", "local_cooldown", 429, wait);

            SetBusy(true);
            // Conservative local retry guard even if the network request times out:
            // the API may already have reserved the check slot.
            SetCheckCooldown(TimeSpan.FromSeconds(30));
            Changed?.Invoke(this, EventArgs.Empty);
            VerificationCheckResponse response;
            try { response = await _api.CheckAsync(Current.ChallengeId, cancellationToken); }
            catch (CharacterVerificationApiException ex) when (ex.StatusCode == 429)
            {
                SetCheckCooldown(ex.RetryAfter ?? TimeSpan.FromSeconds(30));
                Changed?.Invoke(this, EventArgs.Empty);
                throw;
            }
            catch (CharacterVerificationApiException ex) when (ex.StatusCode == 410 && ex.Code == "challenge_expired")
            {
                // The server explicitly rejected this expired challenge. Allow
                // Start Again immediately without relying on local clock time.
                Current.Status = "expired";
                await PersistAsync(Current, cancellationToken);
                Changed?.Invoke(this, EventArgs.Empty);
                throw;
            }
            if (response.Challenge.ChallengeId != Current.ChallengeId ||
                response.Challenge.Identity.CacheKey != Current.Identity.CacheKey)
                throw new InvalidOperationException("Verification API returned a different challenge.");
            _serverConfirmed = response.Verified && response.Challenge.Verified && response.Challenge.Status == "verified";
            Apply(response.Challenge, response.Result);
            await PersistAsync(response.Challenge, cancellationToken);
            if (response.Verified && response.Challenge.Verified) MarkVisibleProfileVerified(response.Challenge.Identity);
            return response;
        }
        finally { SetBusy(false); _operationLock.Release(); }
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (Current is null || !Current.IsActive) return;
            SetBusy(true);
            VerificationStatusResponse response = await _api.CancelAsync(Current.ChallengeId, cancellationToken);
            _serverConfirmed = false;
            Apply(response.Challenge, null);
            await _store.DeleteAsync();
            _stored = null;
        }
        finally { SetBusy(false); _operationLock.Release(); }
    }

    private async Task PersistAsync(CharacterVerificationChallenge challenge, CancellationToken cancellationToken)
    {
        string accountId = AccountService.Shared.Snapshot.Profile?.AccountId ?? string.Empty;
        _stored = new StoredVerificationState(accountId, challenge, DateTimeOffset.UtcNow);
        await _store.SaveAsync(_stored, cancellationToken);
    }

    private void Apply(CharacterVerificationChallenge challenge, string? result)
    {
        Current = challenge;
        LastResult = result;
        // The PHP V13 response already includes authoritative seconds remaining,
        // computed on the server. Use ONLY these relative values for UI timers.
        // This works even if the Windows PC clock/timezone differs from the VPS.
        _expiryAtAnchor = TimeSpan.FromSeconds(Math.Max(0, challenge.ExpiresInSeconds));
        _checkAtAnchor = TimeSpan.FromSeconds(Math.Max(0, challenge.CheckAfterSeconds));
        _timingAnchor = Stopwatch.GetTimestamp();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ExpiresAtUtc = now + _expiryAtAnchor; // compatibility / diagnostics
        NextCheckAtUtc = now + _checkAtAnchor;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void MarkVisibleProfileVerified(CharacterIdentity identity)
    {
        CharacterProfile? profile = CharacterProfileService.Shared.Current;
        if (profile is not null && profile.ServerId == identity.ServerId &&
            profile.Region.Equals(identity.Region, StringComparison.OrdinalIgnoreCase) &&
            profile.Name.Equals(identity.Name, StringComparison.OrdinalIgnoreCase))
            CharacterProfileService.Shared.Replace(profile with { VerificationStatus = "Verified" });
    }

    private void AccountChanged(object? sender, EventArgs e)
    {
        if (AccountService.Shared.Snapshot.State == AccountConnectionState.Connected) _ = RestoreAsync();
        else if (AccountService.Shared.Snapshot.State is AccountConnectionState.Disconnected or AccountConnectionState.SessionExpired)
        {
            Current = null;
            LastResult = null;
            _serverConfirmed = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        AccountService.Shared.Changed -= AccountChanged;
        _operationLock.Dispose();
        if (_api is IDisposable disposable) disposable.Dispose();
    }
}
