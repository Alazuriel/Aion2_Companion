using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Characters;

public sealed record CharacterCacheValue<T>(T Value, DateTimeOffset FetchedAtUtc, bool IsFresh);

public sealed class CharacterCache
{
    public static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan StandardTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FullTtl = TimeSpan.FromMinutes(15);
    private const int SchemaVersion = 1;
    private const long MaxCacheBytes = 64L * 1024 * 1024;
    private const int MaxFiles = 400;

    private readonly string _root;
    private readonly ConcurrentDictionary<string, object> _memory = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public CharacterCache(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nexforge", "AION2Companion", "CharacterCache");
        Directory.CreateDirectory(_root);
    }

    public Task<CharacterCacheValue<CharacterSearchResponse>?> GetSearchAsync(string name, string region, CancellationToken ct = default) =>
        GetAsync<CharacterSearchResponse>(SearchKey(name, region), SearchTtl, ct);

    public Task SetSearchAsync(string name, string region, CharacterSearchResponse value, CancellationToken ct = default) =>
        SetAsync(SearchKey(name, region), value, ct);

    public Task<CharacterCacheValue<CharacterResolveResponse>?> GetProfileAsync(CharacterIdentity identity, string detailLevel, CancellationToken ct = default) =>
        GetAsync<CharacterResolveResponse>(ProfileKey(identity, detailLevel), ProfileTtl(detailLevel), ct);

    public Task SetProfileAsync(CharacterIdentity identity, string detailLevel, CharacterResolveResponse value, CancellationToken ct = default) =>
        SetAsync(ProfileKey(identity, detailLevel), value, ct);

    private async Task<CharacterCacheValue<T>?> GetAsync<T>(string key, TimeSpan ttl, CancellationToken ct)
    {
        if (_memory.TryGetValue(key, out object? cached) && cached is CacheEnvelope<T> memory)
            return new(memory.Value, memory.FetchedAtUtc, DateTimeOffset.UtcNow - memory.FetchedAtUtc <= ttl);

        string path = CachePath(key);
        if (!File.Exists(path)) return null;
        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CacheEnvelope<T>? envelope = await ReadEnvelopeAsync<T>(path, ct).ConfigureAwait(false);
            if (envelope is null || envelope.SchemaVersion != SchemaVersion || envelope.Key != key)
            {
                TryDelete(path);
                return null;
            }
            _memory[key] = envelope;
            return new(envelope.Value, envelope.FetchedAtUtc, DateTimeOffset.UtcNow - envelope.FetchedAtUtc <= ttl);
        }
        catch (JsonException)
        {
            TryDelete(path);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        finally { _ioGate.Release(); }
    }

    private async Task SetAsync<T>(string key, T value, CancellationToken ct)
    {
        var envelope = new CacheEnvelope<T>(SchemaVersion, key, DateTimeOffset.UtcNow, value);
        _memory[key] = envelope;
        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            string path = CachePath(key);
            string temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true))
                await JsonSerializer.SerializeAsync(stream, envelope, _json, ct).ConfigureAwait(false);
            File.Move(temporary, path, true);
            Trim();
        }
        finally { _ioGate.Release(); }
    }

    private async Task<CacheEnvelope<T>?> ReadEnvelopeAsync<T>(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, true);
        return await JsonSerializer.DeserializeAsync<CacheEnvelope<T>>(stream, _json, ct).ConfigureAwait(false);
    }

    private void Trim()
    {
        FileInfo[] files = new DirectoryInfo(_root).GetFiles("*.json")
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        long total = files.Sum(file => file.Length);
        for (int index = 0; index < files.Length; index++)
        {
            if (index < MaxFiles && total <= MaxCacheBytes) continue;
            total -= files[index].Length;
            TryDelete(files[index].FullName);
        }
    }

    private string CachePath(string key) => Path.Combine(_root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    private static string SearchKey(string name, string region) => $"search|{CharacterApiClient.NormalizeRegion(region)}|{name.Trim().ToLowerInvariant()}|exact";
    private static string ProfileKey(CharacterIdentity identity, string detail) => $"profile|{identity.CacheKey}|{detail.Trim().ToLowerInvariant()}";
    private static TimeSpan ProfileTtl(string detail) => detail.Equals("full", StringComparison.OrdinalIgnoreCase) ? FullTtl : StandardTtl;
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    private sealed record CacheEnvelope<T>(int SchemaVersion, string Key, DateTimeOffset FetchedAtUtc, T Value);
}
