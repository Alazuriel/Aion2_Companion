using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core.Characters;

var tests = new (string Name, Func<Task> Run)[]
{
    ("multi-server exact search", MultiServerSearch),
    ("profile cache keys include server and detail", CacheKeys),
    ("character switch keeps the latest identity", CharacterSwitch),
    ("offline request keeps cached profile", OfflineFallback),
    ("refresh cooldown and Retry-After extension", Cooldown),
};

int failures = 0;
foreach ((string name, Func<Task> run) in tests)
{
    try { await run(); Console.WriteLine($"PASS  {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL  {name}: {ex.Message}"); }
}
return failures;

static Task MultiServerSearch()
{
    const string json = """
    {"results":[
      {"character_id":"a","character_name":"Deltusz","server_id":1311,"server_name":"Tahavatha","region":"eu","level":45},
      {"character_id":"b","character_name":"Deltusz","server_id":1317,"server_name":"Tsenka","region":"eu","level":45}
    ]}
    """;
    CharacterSearchResponse response = JsonSerializer.Deserialize<CharacterSearchResponse>(json)!;
    Assert(response.Results.Count == 2, "both exact-name servers must remain visible");
    Assert(response.Results[0].ServerId != response.Results[1].ServerId, "server identity was collapsed");
    return Task.CompletedTask;
}

static async Task CacheKeys()
{
    string root = TempRoot();
    var cache = new CharacterCache(root);
    var first = new CharacterIdentity("eu", 1311, "SameName");
    var second = new CharacterIdentity("eu", 1317, "SameName");
    await cache.SetProfileAsync(first, "standard", Response(first));
    await cache.SetProfileAsync(second, "full", Response(second));
    CharacterCacheValue<CharacterResolveResponse>? a = await cache.GetProfileAsync(first, "standard");
    CharacterCacheValue<CharacterResolveResponse>? b = await cache.GetProfileAsync(second, "full");
    Assert(a?.Value.Data.Character.ServerId == 1311, "first server cache mismatch");
    Assert(b?.Value.Data.Character.ServerId == 1317, "second server cache mismatch");
    Directory.Delete(root, true);
}

static async Task CharacterSwitch()
{
    string root = TempRoot();
    var api = new FakeApi { Delay = TimeSpan.FromMilliseconds(40) };
    using var service = new CharacterCenterService(api, new CharacterCache(Path.Combine(root, "cache")), Path.Combine(root, "state"));
    service.SelectSelf(new CharacterIdentity("eu", 1311, "First"));
    service.SelectSelf(new CharacterIdentity("eu", 1317, "Second"));
    await Task.Delay(120);
    Assert(service.CurrentSelf?.Name == "Second" && service.CurrentSelf.ServerId == 1317, "older load replaced the selected character");
    Directory.Delete(root, true);
}

static async Task OfflineFallback()
{
    string root = TempRoot();
    var identity = new CharacterIdentity("eu", 1311, "CachedHero");
    var cache = new CharacterCache(Path.Combine(root, "cache"));
    await cache.SetProfileAsync(identity, "standard", Response(identity));
    using var service = new CharacterCenterService(new FakeApi { Offline = true }, cache, Path.Combine(root, "state"));
    CharacterLoadResult loaded = await service.LoadProfileAsync(identity, "standard", true, default);
    Assert(loaded.FromLocalCache && loaded.Offline, "offline fallback was not marked cached/offline");
    Assert(loaded.Response.Data.Character.Name == "CachedHero", "cached profile disappeared after network failure");
    Directory.Delete(root, true);
}

static Task Cooldown()
{
    DateTimeOffset now = DateTimeOffset.UtcNow;
    var cooldown = new CharacterRefreshCooldown(() => now);
    Assert(cooldown.TryBegin("x", out IDisposable? lease, out _), "first refresh should start");
    cooldown.StartCooldown("x");
    lease!.Dispose();
    Assert(!cooldown.TryBegin("x", out _, out TimeSpan remaining) && remaining.TotalSeconds >= 9.9, "10-second cooldown missing");
    cooldown.StartCooldown("x", TimeSpan.FromSeconds(27));
    Assert(cooldown.Remaining("x").TotalSeconds >= 26.9, "Retry-After did not extend cooldown");
    now = now.AddSeconds(28);
    Assert(cooldown.TryBegin("x", out IDisposable? finalLease, out _), "refresh did not unlock");
    finalLease!.Dispose();
    return Task.CompletedTask;
}

static CharacterResolveResponse Response(CharacterIdentity identity) => new()
{
    Ok = true,
    DetailLevel = "standard",
    Data = new CharacterResolveData
    {
        Character = new CharacterApiHeader { Name = identity.Name, ServerId = identity.ServerId, Region = identity.Region, ServerName = $"Server {identity.ServerId}" },
    },
};

static string TempRoot()
{
    string root = Path.Combine(Path.GetTempPath(), "nexforge-character-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

sealed class FakeApi : ICharacterApiClient
{
    public bool Offline { get; init; }
    public TimeSpan Delay { get; init; }
    public Task<CharacterSearchResponse> SearchAsync(string name, string region, bool forceRefresh, CancellationToken cancellationToken) => Task.FromResult(new CharacterSearchResponse());
    public async Task<CharacterResolveResponse> ResolveAsync(CharacterIdentity identity, string detailLevel, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        if (Offline) throw new HttpRequestException("offline");
        return new CharacterResolveResponse
        {
            Ok = true,
            DetailLevel = detailLevel,
            Data = new CharacterResolveData
            {
                Character = new CharacterApiHeader
                {
                    Name = identity.Name,
                    ServerId = identity.ServerId,
                    Region = identity.Region,
                    ServerName = $"Server {identity.ServerId}",
                },
            },
        };
    }
}
