using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion.Core.Characters;

public sealed record CharacterLoadResult(CharacterResolveResponse Response, bool FromLocalCache, bool Offline, DateTimeOffset FetchedAtUtc);
public sealed record CharacterSearchLoadResult(CharacterSearchResponse Response, bool FromLocalCache, bool Offline, DateTimeOffset FetchedAtUtc);

public sealed class CharacterCenterService : IDisposable
{
    public static CharacterCenterService Shared { get; } = new(new CharacterApiClient(), new CharacterCache());

    private readonly ICharacterApiClient _api;
    private readonly CharacterCache _cache;
    private readonly string _lastSelfPath;
    private CancellationTokenSource? _selfLoad;
    private bool _started;

    public CharacterCenterService(ICharacterApiClient api, CharacterCache cache, string? stateRoot = null)
    {
        _api = api;
        _cache = cache;
        string root = stateRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nexforge", "AION2Companion");
        Directory.CreateDirectory(root);
        _lastSelfPath = Path.Combine(root, "last-character.json");
        CurrentSelf = ReadLastSelf();
    }

    public event EventHandler? SelfChanged;
    public event EventHandler<CharacterLoadResult>? SelfProfileUpdated;
    public CharacterIdentity? CurrentSelf { get; private set; }
    public CharacterRefreshCooldown Cooldown { get; } = new();

    public void Start()
    {
        if (_started) return;
        _started = true;
        DetectedCharacterService.Shared.Changed += DetectedCharacterChanged;
        ApplyDetectedCharacter();
        if (CurrentSelf is not null) _ = RefreshSelfInBackgroundAsync(CurrentSelf);
    }

    public async Task<CharacterSearchLoadResult> SearchAsync(string name, string region, bool forceRefresh, CancellationToken ct)
    {
        CharacterCacheValue<CharacterSearchResponse>? cached = await _cache.GetSearchAsync(name, region, ct).ConfigureAwait(false);
        if (!forceRefresh && cached is { IsFresh: true })
            return new(cached.Value, true, false, cached.FetchedAtUtc);
        try
        {
            CharacterSearchResponse response = await _api.SearchAsync(name, region, forceRefresh, ct).ConfigureAwait(false);
            await _cache.SetSearchAsync(name, region, response, ct).ConfigureAwait(false);
            return new(response, false, false, DateTimeOffset.UtcNow);
        }
        catch (CharacterApiException ex) when (cached is not null && !ct.IsCancellationRequested && ex.StatusCode != 429)
        {
            return new(cached.Value, true, true, cached.FetchedAtUtc);
        }
        catch (HttpRequestException) when (cached is not null && !ct.IsCancellationRequested)
        {
            return new(cached.Value, true, true, cached.FetchedAtUtc);
        }
        catch (TaskCanceledException) when (cached is not null && !ct.IsCancellationRequested)
        {
            return new(cached.Value, true, true, cached.FetchedAtUtc);
        }
    }

    public async Task<CharacterLoadResult> LoadProfileAsync(CharacterIdentity identity, string detailLevel, bool forceRefresh, CancellationToken ct)
    {
        CharacterCacheValue<CharacterResolveResponse>? cached = await _cache.GetProfileAsync(identity, detailLevel, ct).ConfigureAwait(false);
        // A disk cache is a hint, never proof that a profile belongs to this server/character.
        if (cached is not null && !MatchesIdentity(identity, cached.Value)) cached = null;
        if (!forceRefresh && cached is { IsFresh: true })
            return new(cached.Value, true, false, cached.FetchedAtUtc);
        try
        {
            CharacterResolveResponse response = await _api.ResolveAsync(identity, detailLevel, forceRefresh, ct).ConfigureAwait(false);
            if (!MatchesIdentity(identity, response))
                throw new CharacterApiException("The character API returned a different character or server.");
            await _cache.SetProfileAsync(identity, detailLevel, response, ct).ConfigureAwait(false);
            return new(response, false, false, DateTimeOffset.UtcNow);
        }
        catch (CharacterApiException ex) when (cached is not null && !ct.IsCancellationRequested && ex.StatusCode != 429)
        {
            return new(cached.Value, true, true, cached.FetchedAtUtc);
        }
        catch (HttpRequestException) when (cached is not null && !ct.IsCancellationRequested)
        {
            return new(cached.Value, true, true, cached.FetchedAtUtc);
        }
        catch (TaskCanceledException) when (cached is not null && !ct.IsCancellationRequested)
        {
            return new(cached.Value, true, true, cached.FetchedAtUtc);
        }
    }

    public async Task<CharacterLoadResult?> GetCachedProfileAsync(CharacterIdentity identity, string detailLevel, CancellationToken ct = default)
    {
        CharacterCacheValue<CharacterResolveResponse>? cached = await _cache.GetProfileAsync(identity, detailLevel, ct).ConfigureAwait(false);
        return cached is null || !MatchesIdentity(identity, cached.Value)
            ? null : new(cached.Value, true, false, cached.FetchedAtUtc);
    }

    private static bool MatchesIdentity(CharacterIdentity identity, CharacterResolveResponse response)
    {
        CharacterApiHeader? actual = response.Data?.Character;
        return response.Ok && actual is not null && actual.ServerId == identity.ServerId &&
               string.Equals(CharacterApiClient.NormalizeRegion(actual.Region), CharacterApiClient.NormalizeRegion(identity.Region), StringComparison.Ordinal) &&
               string.Equals(actual.Name?.Trim(), identity.Name.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void SelectSelf(CharacterIdentity identity)
    {
        if (CurrentSelf?.CacheKey == identity.CacheKey) return;
        CurrentSelf = identity;
        SaveLastSelf(identity);
        SelfChanged?.Invoke(this, EventArgs.Empty);
        _ = RefreshSelfInBackgroundAsync(identity);
    }

    private void DetectedCharacterChanged(object? sender, EventArgs e) => ApplyDetectedCharacter();

    private void ApplyDetectedCharacter()
    {
        DetectedCharacterSnapshot? detected = DetectedCharacterService.Shared.Current;
        if (detected?.ServerId is not ushort serverId || serverId == 0 || string.IsNullOrWhiteSpace(detected.Name)) return;
        string region = string.IsNullOrWhiteSpace(detected.Region) ? "eu" : detected.Region;
        SelectSelf(new CharacterIdentity(region, serverId, detected.Name));
    }

    private async Task RefreshSelfInBackgroundAsync(CharacterIdentity identity)
    {
        _selfLoad?.Cancel();
        _selfLoad?.Dispose();
        _selfLoad = new CancellationTokenSource();
        try
        {
            CharacterLoadResult result = await LoadProfileAsync(identity, "standard", false, _selfLoad.Token).ConfigureAwait(false);
            if (CurrentSelf?.CacheKey == identity.CacheKey) SelfProfileUpdated?.Invoke(this, result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DebugConsole.Log("character", $"A karakterprofil frissítése sikertelen: {ex.Message}"); }
    }

    private CharacterIdentity? ReadLastSelf()
    {
        try { return File.Exists(_lastSelfPath) ? JsonSerializer.Deserialize<CharacterIdentity>(File.ReadAllText(_lastSelfPath)) : null; }
        catch { return null; }
    }

    private void SaveLastSelf(CharacterIdentity identity)
    {
        try
        {
            string temporary = _lastSelfPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(identity));
            File.Move(temporary, _lastSelfPath, true);
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        DetectedCharacterService.Shared.Changed -= DetectedCharacterChanged;
        _selfLoad?.Cancel();
        _selfLoad?.Dispose();
    }
}
