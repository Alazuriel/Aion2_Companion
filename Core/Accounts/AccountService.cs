using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Accounts;

public sealed class AccountService
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(3); // Legacy PKCE fallback only.
    private static readonly TimeSpan RefreshLeadTime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OfflineRetryDelay = TimeSpan.FromMinutes(1);

    private readonly WindowsAccountTokenStore _tokenStore = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private IAccountAuthProtocol _protocol = new UnconfiguredAccountAuthProtocol();
    private AccountSession? _session;
    private Timer? _refreshTimer;
    private CancellationTokenSource? _activeLoginCancellation;
    public DeviceAuthorization? PendingDevice { get; private set; }
    public string? DeviceError { get; private set; }

    public static AccountService Shared { get; } = new();

    public event EventHandler? Changed;

    public AccountServiceSnapshot Snapshot { get; private set; } = new(
        AccountConnectionState.ContractPending,
        null,
        "AccountContractPending",
        CanConnect: true,
        CanDisconnect: false);

    private AccountService()
    {
    }

    public void ConfigureProtocol(IAccountAuthProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _protocol = protocol;
        if (_session is null)
            SetSnapshot(AccountConnectionState.Disconnected, null, "AccountDisconnected", true, false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            WindowsAccountTokenStore.StoredAccountCredential? stored =
                await _tokenStore.LoadAsync(cancellationToken);
            if (stored is null)
            {
                SetSnapshot(
                    _protocol.IsConfigured
                        ? AccountConnectionState.Disconnected
                        : AccountConnectionState.ContractPending,
                    null,
                    _protocol.IsConfigured ? "AccountDisconnected" : "AccountContractPending",
                    true,
                    false);
                return;
            }

            // The access token is never persisted. A restart always rotates the
            // DPAPI-protected refresh token into a fresh in-memory access token.
            _session = new AccountSession(
                new AccountTokenSet(string.Empty, stored.RefreshToken, DateTimeOffset.MinValue),
                stored.Profile);

            RefreshSessionResult refreshResult = await TryRefreshSessionAsync(cancellationToken);
            if (refreshResult == RefreshSessionResult.Offline)
            {
                SetSnapshot(AccountConnectionState.Offline, _session.Profile,
                    "AccountOffline", false, true);
                ScheduleRefresh(OfflineRetryDelay);
                return;
            }

            if (refreshResult != RefreshSessionResult.Refreshed)
            {
                await _tokenStore.DeleteAsync();
                SetSnapshot(AccountConnectionState.SessionExpired, _session.Profile,
                    "AccountSessionExpired", true, true);
                return;
            }

            try
            {
                NexforgeAccountProfile profile = await _protocol.GetProfileAsync(
                    _session.Tokens.AccessToken,
                    cancellationToken);
                _session = _session with { Profile = profile };
                await _tokenStore.SaveAsync(_session, cancellationToken);
                SetSnapshot(AccountConnectionState.Connected, profile, "AccountConnected", false, true);
                ScheduleRefreshForCurrentSession();
            }
            catch (AccountSessionExpiredException)
            {
                await _tokenStore.DeleteAsync();
                SetSnapshot(AccountConnectionState.SessionExpired, _session.Profile,
                    "AccountSessionExpired", true, true);
            }
            catch (Exception ex) when (IsOfflineFailure(ex))
            {
                SetSnapshot(AccountConnectionState.Offline, _session.Profile,
                    "AccountOffline", false, true);
                ScheduleRefresh(OfflineRetryDelay);
            }
            catch (Exception)
            {
                SetSnapshot(AccountConnectionState.Error, _session.Profile,
                    "AccountLoginFailed", true, true);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _protocol is IDeviceAccountAuthProtocol device
            ? ConnectDeviceAsync(device, cancellationToken)
            : ConnectPkceFallbackAsync(cancellationToken);

    public async Task ConnectDeviceAsync(IDeviceAccountAuthProtocol device, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        CancellationTokenSource? cancel = null;
        DeviceAuthorization? authorization = null;
        bool completed = false;
        try
        {
            cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
            _activeLoginCancellation = cancel;
            PendingDevice = null;
            DeviceError = null;
            SetSnapshot(AccountConnectionState.Connecting, null, "AccountConnecting", false, false);
            authorization = await device.StartDeviceAsync(Environment.MachineName, cancel.Token);
            PendingDevice = authorization;
            Changed?.Invoke(this, EventArgs.Empty);
            Process.Start(new ProcessStartInfo(authorization.VerificationUriComplete.AbsoluteUri)
                { UseShellExecute = true });
            TimeSpan interval = authorization.Interval;
            while (DateTimeOffset.UtcNow < authorization.ExpiresAtUtc)
            {
                TimeSpan remaining = authorization.ExpiresAtUtc - DateTimeOffset.UtcNow;
                await Task.Delay(remaining < interval ? remaining : interval, cancel.Token);
                if (DateTimeOffset.UtcNow >= authorization.ExpiresAtUtc) break;
                DevicePollResult result = await device.PollDeviceAsync(authorization.DeviceCode, cancel.Token);
                if (result.State == DevicePollState.SlowDown)
                    interval += TimeSpan.FromSeconds(5);
                if (result.RetryAfter is TimeSpan retry && retry > interval)
                    interval = retry;
                switch (result.State)
                {
                    case DevicePollState.Pending:
                    case DevicePollState.SlowDown:
                        continue;
                    case DevicePollState.Denied:
                        DeviceError = "Account authorization was denied.";
                        break;
                    case DevicePollState.Expired:
                    case DevicePollState.Invalid:
                        DeviceError = "The pairing code has expired or was already used.";
                        break;
                    case DevicePollState.Approved when result.Tokens is not null:
                        NexforgeAccountProfile profile = await _protocol.GetProfileAsync(result.Tokens.AccessToken, cancel.Token);
                        var session = new AccountSession(result.Tokens, profile);
                        await _tokenStore.SaveAsync(session, cancel.Token);
                        _session = session;
                        completed = true;
                        SetSnapshot(AccountConnectionState.Connected, profile, "AccountConnected", false, true);
                        ScheduleRefreshForCurrentSession();
                        return;
                    default:
                        DeviceError = "Unexpected pairing response.";
                        break;
                }
                break;
            }
            DeviceError ??= "The pairing code expired. Please try again.";
            SetSnapshot(AccountConnectionState.Error, null, "AccountLoginFailed", true, false);
        }
        catch (OperationCanceledException)
        {
            bool userCancelled = cancel?.IsCancellationRequested == true &&
                !cancellationToken.IsCancellationRequested && !_lifetimeCts.IsCancellationRequested;
            DeviceError = userCancelled ? null : "Pairing timed out or was interrupted.";
            SetSnapshot(userCancelled ? AccountConnectionState.Disconnected : AccountConnectionState.Error,
                null, userCancelled ? "AccountLoginCancelled" : "AccountTimedOut", true, false);
        }
        catch (Exception ex) when (IsOfflineFailure(ex))
        {
            DeviceError = "The Nexforge server cannot be reached.";
            SetSnapshot(AccountConnectionState.Offline, null, "AccountOffline", true, false);
        }
        catch (Exception)
        {
            DeviceError = "Device pairing could not be completed.";
            SetSnapshot(AccountConnectionState.Error, null, "AccountLoginFailed", true, false);
        }
        finally
        {
            PendingDevice = null;
            Changed?.Invoke(this, EventArgs.Empty);
            if (!completed && authorization is not null)
            {
                try
                {
                    using var cancelTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await device.CancelDeviceAsync(authorization.DeviceCode, cancelTimeout.Token);
                }
                catch (Exception) { /* Best effort. */ }
            }
            if (ReferenceEquals(_activeLoginCancellation, cancel)) _activeLoginCancellation = null;
            cancel?.Dispose();
            _operationLock.Release();
        }
    }

    // Retained for rollback until the live Device Flow has been verified.
    public async Task ConnectPkceFallbackAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        CancellationTokenSource? loginCancellation = null;
        try
        {
            if (!_protocol.IsConfigured)
            {
                SetSnapshot(AccountConnectionState.ContractPending, null,
                    "AccountContractPending", true, false);
                return;
            }

            SetSnapshot(AccountConnectionState.Connecting, null,
                "AccountConnecting", false, false);

            loginCancellation = new CancellationTokenSource();
            _activeLoginCancellation = loginCancellation;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetimeCts.Token, loginCancellation.Token);
            timeout.CancelAfter(LoginTimeout);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = new Uri($"http://127.0.0.1:{port}/callback");
            string verifier = CreateRandomBase64Url(64);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = CreateRandomBase64Url(32);

            Uri authorizationUri = await _protocol.CreateAuthorizationUriAsync(
                new AccountAuthorizationContext(redirectUri, state, challenge, "S256"),
                timeout.Token);
            if (!authorizationUri.IsAbsoluteUri || authorizationUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("The authorization endpoint must use HTTPS.");

            Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });

            OAuthCallback callback = await WaitForCallbackAsync(listener, timeout.Token);
            if (!FixedTimeEquals(state, callback.State))
                throw new InvalidOperationException("The authorization state did not match.");
            if (!string.IsNullOrWhiteSpace(callback.Error))
                throw new InvalidOperationException("Authorization was not completed.");
            if (string.IsNullOrWhiteSpace(callback.Code))
                throw new InvalidOperationException("No authorization code was returned.");

            AccountTokenSet tokens = await _protocol.ExchangeAuthorizationCodeAsync(
                callback.Code,
                verifier,
                redirectUri,
                timeout.Token);
            NexforgeAccountProfile profile = await _protocol.GetProfileAsync(
                tokens.AccessToken,
                timeout.Token);

            _session = new AccountSession(tokens, profile);
            await _tokenStore.SaveAsync(_session, timeout.Token);
            SetSnapshot(AccountConnectionState.Connected, profile,
                "AccountConnected", false, true);
            ScheduleRefreshForCurrentSession();
        }
        catch (OperationCanceledException)
        {
            bool cancelledByUser = loginCancellation?.IsCancellationRequested == true &&
                                   !_lifetimeCts.IsCancellationRequested;
            SetSnapshot(
                cancelledByUser ? AccountConnectionState.Disconnected : AccountConnectionState.Error,
                _session?.Profile,
                cancelledByUser ? "AccountLoginCancelled" : "AccountTimedOut",
                true,
                _session is not null);
        }
        catch (AccountSessionExpiredException)
        {
            SetSnapshot(AccountConnectionState.SessionExpired, _session?.Profile,
                "AccountSessionExpired", true, _session is not null);
        }
        catch (Exception ex) when (IsOfflineFailure(ex) || ex is SocketException)
        {
            SetSnapshot(AccountConnectionState.Offline, _session?.Profile,
                "AccountOffline", true, _session is not null);
        }
        catch (Exception)
        {
            SetSnapshot(AccountConnectionState.Error, _session?.Profile,
                "AccountLoginFailed", true, _session is not null);
        }
        finally
        {
            if (ReferenceEquals(_activeLoginCancellation, loginCancellation))
                _activeLoginCancellation = null;
            loginCancellation?.Dispose();
            _operationLock.Release();
        }
    }

    public void CancelConnect()
    {
        try
        {
            _activeLoginCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancelConnect();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            StopRefreshTimer();
            AccountSession? session = _session;
            _session = null;
            await _tokenStore.DeleteAsync();
            CharacterProfileService.Shared.Clear();

            if (session is not null && _protocol.IsConfigured)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await _protocol.SignOutAsync(session.Tokens, timeout.Token);
                }
                catch (Exception)
                {
                    // Revocation is best-effort. Local sign-out is authoritative even
                    // when the server is offline or rejects an already-invalid token.
                }
            }

            SetSnapshot(
                _protocol.IsConfigured
                    ? AccountConnectionState.Disconnected
                    : AccountConnectionState.ContractPending,
                null,
                _protocol.IsConfigured ? "AccountDisconnected" : "AccountContractPending",
                true,
                false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<RefreshSessionResult> TryRefreshSessionAsync(CancellationToken cancellationToken)
    {
        if (_session?.Tokens.RefreshToken is not { Length: > 0 } refreshToken ||
            !_protocol.IsConfigured)
        {
            return RefreshSessionResult.Expired;
        }

        try
        {
            AccountTokenSet? refreshed = await _protocol.TryRefreshAsync(
                refreshToken,
                cancellationToken);
            if (refreshed is null)
                return RefreshSessionResult.Expired;

            _session = _session with { Tokens = refreshed };
            await _tokenStore.SaveAsync(_session, cancellationToken);
            return RefreshSessionResult.Refreshed;
        }
        catch (Exception ex) when (IsOfflineFailure(ex))
        {
            return RefreshSessionResult.Offline;
        }
        catch (AccountSessionExpiredException)
        {
            return RefreshSessionResult.Expired;
        }
        catch (Exception)
        {
            return RefreshSessionResult.Expired;
        }
    }

    private void SetSnapshot(
        AccountConnectionState state,
        NexforgeAccountProfile? profile,
        string statusKey,
        bool canConnect,
        bool canDisconnect)
    {
        Snapshot = new AccountServiceSnapshot(
            state,
            profile,
            statusKey,
            canConnect,
            canDisconnect);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleRefreshForCurrentSession()
    {
        if (_session is null)
            return;

        TimeSpan delay = _session.Tokens.ExpiresAtUtc - DateTimeOffset.UtcNow - RefreshLeadTime;
        if (delay < TimeSpan.FromSeconds(5))
            delay = TimeSpan.FromSeconds(5);
        ScheduleRefresh(delay);
    }

    private void ScheduleRefresh(TimeSpan delay)
    {
        if (_lifetimeCts.IsCancellationRequested)
            return;

        _refreshTimer ??= new Timer(
            static state => _ = ((AccountService)state!).RefreshFromTimerAsync(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _refreshTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshFromTimerAsync()
    {
        if (_lifetimeCts.IsCancellationRequested)
            return;

        try
        {
            await _operationLock.WaitAsync(_lifetimeCts.Token);
            try
            {
                if (_session is null)
                    return;

                RefreshSessionResult result = await TryRefreshSessionAsync(_lifetimeCts.Token);
                if (result == RefreshSessionResult.Refreshed)
                {
                    NexforgeAccountProfile profile = await _protocol.GetProfileAsync(
                        _session.Tokens.AccessToken, _lifetimeCts.Token);
                    _session = _session with { Profile = profile };
                    await _tokenStore.SaveAsync(_session, _lifetimeCts.Token);
                    SetSnapshot(AccountConnectionState.Connected, profile,
                        "AccountConnected", false, true);
                    ScheduleRefreshForCurrentSession();
                }
                else if (result == RefreshSessionResult.Offline)
                {
                    SetSnapshot(AccountConnectionState.Offline, _session.Profile,
                        "AccountOffline", false, true);
                    ScheduleRefresh(OfflineRetryDelay);
                }
                else
                {
                    await _tokenStore.DeleteAsync();
                    SetSnapshot(AccountConnectionState.SessionExpired, _session.Profile,
                        "AccountSessionExpired", true, true);
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (AccountSessionExpiredException)
        {
            if (_session is not null)
            {
                await _tokenStore.DeleteAsync();
                SetSnapshot(AccountConnectionState.SessionExpired, _session.Profile,
                    "AccountSessionExpired", true, true);
            }
        }
        catch (Exception ex) when (IsOfflineFailure(ex))
        {
            if (_session is not null)
            {
                SetSnapshot(AccountConnectionState.Offline, _session.Profile,
                    "AccountOffline", false, true);
                ScheduleRefresh(OfflineRetryDelay);
            }
        }
        catch (Exception)
        {
            if (_session is not null)
                SetSnapshot(AccountConnectionState.Error, _session.Profile,
                    "AccountLoginFailed", true, true);
        }
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Shutdown()
    {
        if (_lifetimeCts.IsCancellationRequested)
            return;
        _lifetimeCts.Cancel();
        CancelConnect();
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        if (_protocol is IDisposable disposableProtocol)
            disposableProtocol.Dispose();
    }

    private static bool IsOfflineFailure(Exception exception)
    {
        if (exception is IOException or TaskCanceledException)
            return true;
        if (exception is not HttpRequestException httpException)
            return false;

        int? status = (int?)httpException.StatusCode;
        return status is null or 408 or 429 or 503 || status >= 500;
    }

    private static async Task<OAuthCallback> WaitForCallbackAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        string? requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
            throw new InvalidDataException("The loopback callback request was empty.");

        string[] requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2 || !requestParts[0].Equals("GET", StringComparison.Ordinal))
            throw new InvalidDataException("The loopback callback request was invalid.");

        var callbackUri = new Uri("http://127.0.0.1" + requestParts[1]);
        if (!callbackUri.AbsolutePath.Equals("/callback", StringComparison.Ordinal))
            throw new InvalidDataException("The loopback callback path was invalid.");

        Dictionary<string, string> query = ParseQuery(callbackUri.Query);
        const string page = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Nexforge</title></head>" +
                            "<body style=\"background:#07111d;color:#eaf7ff;font-family:Segoe UI;padding:48px\">" +
                            "<h1>Nexforge Account</h1><p>You can return to AION 2 Companion.</p></body></html>";
        byte[] body = Encoding.UTF8.GetBytes(page);
        string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                         $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        query.TryGetValue("code", out string? code);
        query.TryGetValue("state", out string? state);
        query.TryGetValue("error", out string? error);
        return new OAuthCallback(code, state, error);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = WebUtility.UrlDecode(parts[0]);
            string value = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            values[key] = value;
        }

        return values;
    }

    private static string CreateRandomBase64Url(int byteCount) =>
        Base64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null)
            return false;

        byte[] left = Encoding.UTF8.GetBytes(expected);
        byte[] right = Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private sealed record OAuthCallback(string? Code, string? State, string? Error);

    private enum RefreshSessionResult
    {
        Refreshed,
        Offline,
        Expired,
    }
}
