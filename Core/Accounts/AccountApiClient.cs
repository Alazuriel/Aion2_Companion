using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Accounts;

/// <summary>
/// HTTP transport for Nexforge account requests.
/// </summary>
public sealed class AccountApiClient : IDisposable
{
    public static Uri ApiBaseUri { get; } = new("https://companion.nexforge.hu/api/v1/");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public AccountApiClient(HttpClient? httpClient = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= ApiBaseUri;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Nexforge-AION2-Companion");
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        string? accessToken = null,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Uri.TryCreate(relativePath, UriKind.Absolute, out _))
        {
            throw new ArgumentException("A non-empty relative API path is required.", nameof(relativePath));
        }

        using var request = new HttpRequestMessage(method, relativePath) { Content = content };
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
