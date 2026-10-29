using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Accounts;

/// <summary>Exact desktop OAuth contract supplied by the Nexforge API.</summary>
public sealed class NexforgeAccountAuthProtocol : IAccountAuthProtocol, IDeviceAccountAuthProtocol, IDisposable
{
    private const string ClientId = "nexforge-companion-desktop";
    private static readonly Uri ConnectPage = new("https://companion.nexforge.hu/connect");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly AccountApiClient _api;
    private readonly bool _ownsApi;

    public NexforgeAccountAuthProtocol(AccountApiClient? api = null)
    {
        _ownsApi = api is null;
        _api = api ?? new AccountApiClient();
    }

    public bool IsConfigured => true;

    public async Task<DeviceAuthorization> StartDeviceAsync(string deviceName, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _api.SendAsync(HttpMethod.Post, "desktop/device/start",
            content: JsonContent(new { client_id = ClientId, device_name = deviceName }),
            cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        DeviceStartResponse? data = await JsonSerializer.DeserializeAsync<DeviceStartResponse>(stream, JsonOptions, cancellationToken);
        if (data is null || string.IsNullOrWhiteSpace(data.DeviceCode) ||
            string.IsNullOrWhiteSpace(data.UserCode) || data.ExpiresIn <= 0 ||
            !Uri.TryCreate(data.VerificationUriComplete, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "companion.nexforge.hu" ||
            uri.Port != 443)
            throw new InvalidOperationException("Invalid device authorization response.");
        return new DeviceAuthorization(data.DeviceCode, data.UserCode, uri,
            DateTimeOffset.UtcNow.AddSeconds(data.ExpiresIn),
            TimeSpan.FromSeconds(Math.Max(5, data.Interval)));
    }

    public async Task<DevicePollResult> PollDeviceAsync(string deviceCode, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _api.SendAsync(HttpMethod.Post, "desktop/device/token",
            content: JsonContent(new { client_id = ClientId,
                grant_type = "urn:ietf:params:oauth:grant-type:device_code", device_code = deviceCode }),
            cancellationToken: cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            TokenResponse? token = await JsonSerializer.DeserializeAsync<TokenResponse>(stream, JsonOptions, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) ||
                string.IsNullOrWhiteSpace(token.RefreshToken) || token.ExpiresIn <= 0 ||
                !string.Equals(token.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid device token response.");
            return new DevicePollResult(DevicePollState.Approved,
                new AccountTokenSet(token.AccessToken, token.RefreshToken,
                    DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)));
        }
        await using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        ApiErrorEnvelope? error = null;
        try { error = await JsonSerializer.DeserializeAsync<ApiErrorEnvelope>(errorStream, JsonOptions, cancellationToken); }
        catch (JsonException) { }
        string? code = error?.Error?.Code;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
            retryAfter = date - DateTimeOffset.UtcNow;
        return code switch
        {
            "authorization_pending" => new DevicePollResult(DevicePollState.Pending, RetryAfter: retryAfter),
            "slow_down" => new DevicePollResult(DevicePollState.SlowDown, RetryAfter: retryAfter),
            "access_denied" => new DevicePollResult(DevicePollState.Denied),
            "expired_token" => new DevicePollResult(DevicePollState.Expired),
            "invalid_grant" => new DevicePollResult(DevicePollState.Invalid),
            _ => throw new HttpRequestException("Device authorization failed: " +
                (code ?? response.StatusCode.ToString()), null, response.StatusCode)
        };
    }

    public async Task CancelDeviceAsync(string deviceCode, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _api.SendAsync(HttpMethod.Post, "desktop/device/cancel",
            content: JsonContent(new { client_id = ClientId, device_code = deviceCode }),
            cancellationToken: cancellationToken);
        // Cancellation is best-effort; already consumed or expired codes may be rejected.
    }

    private sealed class DeviceStartResponse
    {
        [JsonPropertyName("device_code")] public string? DeviceCode { get; init; }
        [JsonPropertyName("user_code")] public string? UserCode { get; init; }
        [JsonPropertyName("verification_uri_complete")] public string? VerificationUriComplete { get; init; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
        [JsonPropertyName("interval")] public int Interval { get; init; }
    }

    public ValueTask<Uri> CreateAuthorizationUriAsync(
        AccountAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string query = string.Join('&',
            "client_id=" + Escape(ClientId),
            "redirect_uri=" + Escape(context.RedirectUri.AbsoluteUri),
            "code_challenge=" + Escape(context.CodeChallenge),
            "code_challenge_method=" + Escape(context.CodeChallengeMethod),
            "state=" + Escape(context.State));
        return ValueTask.FromResult(new UriBuilder(ConnectPage) { Query = query }.Uri);
    }

    public async Task<AccountTokenSet> ExchangeAuthorizationCodeAsync(
        string authorizationCode,
        string codeVerifier,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        var request = new TokenRequest
        {
            ClientId = ClientId,
            GrantType = "authorization_code",
            Code = authorizationCode,
            RedirectUri = redirectUri.AbsoluteUri,
            CodeVerifier = codeVerifier,
        };
        return await RequestTokenAsync(request, cancellationToken);
    }

    public async Task<NexforgeAccountProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Get, "desktop/me", accessToken, cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        MeResponse? payload = await JsonSerializer.DeserializeAsync<MeResponse>(
            stream, JsonOptions, cancellationToken);
        UserResponse? user = payload?.User;
        if (user is null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Username))
            throw new InvalidOperationException("Nexforge returned an invalid account profile.");

        return new NexforgeAccountProfile(
            user.Id,
            user.Username,
            user.Status ?? string.Empty,
            user.Email,
            user.EmailVerified);
    }

    public Task<AccountTokenSet?> TryRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken) =>
        RequestRefreshAsync(refreshToken, cancellationToken);

    public async Task SignOutAsync(AccountTokenSet tokens, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
            return;

        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Post,
            "desktop/revoke",
            content: JsonContent(new RevokeRequest
            {
                ClientId = ClientId,
                Token = tokens.RefreshToken,
            }),
            cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<AccountTokenSet?> RequestRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var request = new TokenRequest
        {
            ClientId = ClientId,
            GrantType = "refresh_token",
            RefreshToken = refreshToken,
        };
        return await RequestTokenAsync(request, cancellationToken);
    }

    private async Task<AccountTokenSet> RequestTokenAsync(
        TokenRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _api.SendAsync(
            HttpMethod.Post,
            "desktop/token",
            content: JsonContent(request),
            cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        TokenResponse? payload = await JsonSerializer.DeserializeAsync<TokenResponse>(
            stream, JsonOptions, cancellationToken);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            string.IsNullOrWhiteSpace(payload.RefreshToken) ||
            payload.ExpiresIn <= 0 ||
            !string.Equals(payload.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Nexforge returned an invalid token response.");
        }

        return new AccountTokenSet(
            payload.AccessToken,
            payload.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn));
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        ApiErrorEnvelope? error = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            error = await JsonSerializer.DeserializeAsync<ApiErrorEnvelope>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // The HTTP status remains authoritative when an upstream proxy returns non-JSON.
        }

        string message = error?.Error?.Message ?? "The Nexforge account service rejected the request.";
        string? code = error?.Error?.Code;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.UnprocessableEntity &&
            (string.Equals(code, "invalid_grant", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(code, "invalid_token", StringComparison.OrdinalIgnoreCase)))
        {
            throw new AccountSessionExpiredException(message);
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static string Escape(string value) => Uri.EscapeDataString(value);

    public void Dispose()
    {
        if (_ownsApi)
            _api.Dispose();
    }

    private sealed class TokenRequest
    {
        [JsonPropertyName("client_id")] public string ClientId { get; init; } = string.Empty;
        [JsonPropertyName("grant_type")] public string GrantType { get; init; } = string.Empty;
        [JsonPropertyName("code")] public string? Code { get; init; }
        [JsonPropertyName("redirect_uri")] public string? RedirectUri { get; init; }
        [JsonPropertyName("code_verifier")] public string? CodeVerifier { get; init; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; init; }
        [JsonPropertyName("token_type")] public string? TokenType { get; init; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
        [JsonPropertyName("scope")] public string? Scope { get; init; }
    }

    private sealed class MeResponse
    {
        [JsonPropertyName("user")] public UserResponse? User { get; init; }
    }

    private sealed class UserResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("username")] public string? Username { get; init; }
        [JsonPropertyName("email")] public string? Email { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("email_verified")] public bool EmailVerified { get; init; }
    }

    private sealed class RevokeRequest
    {
        [JsonPropertyName("client_id")] public string ClientId { get; init; } = string.Empty;
        [JsonPropertyName("token")] public string Token { get; init; } = string.Empty;
    }

    private sealed class ApiErrorEnvelope
    {
        [JsonPropertyName("error")] public ApiError? Error { get; init; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("code")] public string? Code { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
    }
}
