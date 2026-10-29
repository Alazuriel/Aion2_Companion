using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AION_2_Companion.Core.Assets;

public static class CompanionImageService
{
    private const int MaximumDownloadBytes = 8 * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> MemoryCache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new();
    private static readonly string DiskCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nexforge", "AION 2 Companion", "image-cache");

    public static Task<Bitmap?> LoadAsync(
        string? source,
        int decodeWidth = 128,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Task.FromResult<Bitmap?>(null);

        string normalized = source.Trim();
        string cacheKey = $"{Math.Max(16, decodeWidth)}|{normalized}";
        Lazy<Task<Bitmap?>> lazy = MemoryCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<Bitmap?>>(
                () => LoadCoreAsync(normalized, Math.Max(16, decodeWidth)),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitWithCancellationAsync(lazy.Value, cancellationToken);
    }

    private static async Task<Bitmap?> LoadCoreAsync(string source, int decodeWidth)
    {
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                string cachedPath = GetDiskCachePath(uri);
                SemaphoreSlim downloadLock = DownloadLocks.GetOrAdd(cachedPath, _ => new SemaphoreSlim(1, 1));
                await downloadLock.WaitAsync();
                try
                {
                    if (!File.Exists(cachedPath))
                        await DownloadAsync(uri, cachedPath);
                }
                finally
                {
                    downloadLock.Release();
                }

                await using FileStream cached = File.OpenRead(cachedPath);
                return Bitmap.DecodeToWidth(cached, decodeWidth);
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out uri) &&
                uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                using Stream resource = AssetLoader.Open(uri);
                return Bitmap.DecodeToWidth(resource, decodeWidth);
            }

            if (Path.IsPathRooted(source) && File.Exists(source))
            {
                await using FileStream file = File.OpenRead(source);
                return Bitmap.DecodeToWidth(file, decodeWidth);
            }

            string resourcePath = NormalizeResourcePath(source);
            string assemblyName = typeof(CompanionImageService).Assembly.GetName().Name!;
            var resourceUri = new Uri($"avares://{assemblyName}/{resourcePath}");
            using Stream embedded = AssetLoader.Open(resourceUri);
            return Bitmap.DecodeToWidth(embedded, decodeWidth);
        }
        catch (Exception ex)
        {
            DebugConsole.Log("images", $"Image unavailable: {source} ({ex.Message})");
            return null;
        }
    }

    private static string NormalizeResourcePath(string source)
    {
        string path = source.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            return "Data/Assets/" + path["assets/".Length..];
        return path;
    }

    private static string GetDiskCachePath(Uri uri)
    {
        Directory.CreateDirectory(DiskCacheDirectory);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(DiskCacheDirectory, hash + ".img");
    }

    private static async Task DownloadAsync(Uri uri, string destination)
    {
        using HttpResponseMessage response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            throw new InvalidDataException("Image is larger than the supported limit.");

        string temporaryPath = destination + ".tmp";
        try
        {
            await using Stream input = await response.Content.ReadAsStreamAsync();
            await using FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > MaximumDownloadBytes)
                    throw new InvalidDataException("Image is larger than the supported limit.");
                await output.WriteAsync(buffer.AsMemory(0, read));
            }

            await output.FlushAsync();
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task<T> AwaitWithCancellationAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return await task;

        return await task.WaitAsync(cancellationToken);
    }
}
