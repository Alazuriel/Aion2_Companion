using System;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Accounts;

public enum AccountConnectionState
{
    ContractPending,
    Disconnected,
    Connecting,
    Connected,
    Offline,
    SessionExpired,
    Error,
}

public sealed record NexforgeAccountProfile(
    string AccountId,
    string Username,
    string Status,
    string? Email,
    bool EmailVerified);

public sealed record AccountTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAtUtc);

public sealed record AccountSession(
    AccountTokenSet Tokens,
    NexforgeAccountProfile Profile);

public sealed record AccountServiceSnapshot(
    AccountConnectionState State,
    NexforgeAccountProfile? Profile,
    string StatusKey,
    bool CanConnect,
    bool CanDisconnect);

public sealed record DeviceAuthorization(
    string DeviceCode,
    string UserCode,
    Uri VerificationUriComplete,
    DateTimeOffset ExpiresAtUtc,
    TimeSpan Interval);

public interface IDeviceAccountAuthProtocol
{
    Task<DeviceAuthorization> StartDeviceAsync(string deviceName, CancellationToken cancellationToken);
    Task<DevicePollResult> PollDeviceAsync(string deviceCode, CancellationToken cancellationToken);
    Task CancelDeviceAsync(string deviceCode, CancellationToken cancellationToken);
}

public enum DevicePollState { Pending, SlowDown, Approved, Denied, Expired, Invalid }
public sealed record DevicePollResult(DevicePollState State, AccountTokenSet? Tokens = null, TimeSpan? RetryAfter = null);

public sealed record AccountAuthorizationContext(
    Uri RedirectUri,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod);

/// <summary>
/// Adapter boundary for Nexforge desktop authentication. The Companion owns
/// browser launch, loopback callback, PKCE, state validation and secure storage;
/// the protocol implementation owns the documented HTTP contract.
/// </summary>
public interface IAccountAuthProtocol
{
    bool IsConfigured { get; }

    ValueTask<Uri> CreateAuthorizationUriAsync(
        AccountAuthorizationContext context,
        CancellationToken cancellationToken);

    Task<AccountTokenSet> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        string codeVerifier,
        Uri redirectUri,
        CancellationToken cancellationToken);

    Task<NexforgeAccountProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken);

    Task<AccountTokenSet?> TryRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task SignOutAsync(AccountTokenSet tokens, CancellationToken cancellationToken);
}

public sealed class AccountSessionExpiredException : Exception
{
    public AccountSessionExpiredException(string? message = null)
        : base(message ?? "The Nexforge account session has expired.")
    {
    }
}

internal sealed class UnconfiguredAccountAuthProtocol : IAccountAuthProtocol
{
    public bool IsConfigured => false;

    public ValueTask<Uri> CreateAuthorizationUriAsync(
        AccountAuthorizationContext context,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Nexforge Auth API contract is not configured.");

    public Task<AccountTokenSet> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        string codeVerifier,
        Uri redirectUri,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Nexforge Auth API contract is not configured.");

    public Task<NexforgeAccountProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Nexforge Auth API contract is not configured.");

    public Task<AccountTokenSet?> TryRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken) =>
        Task.FromResult<AccountTokenSet?>(null);

    public Task SignOutAsync(AccountTokenSet tokens, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
