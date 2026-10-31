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

/// <summary>
/// Loads local and remote images. Remote images are always decoded from memory;
/// the disk cache is a best-effort optimization and may never block rendering.
/// </summary>
public static class CompanionImageService
{
    private const int MaximumDownloadBytes = 8 * 1024 * 1024;
    private const int BufferSize = 81920;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> MemoryCache = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new();
    private static readonly string DiskCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nexforge", "AION 2 Companion", "image-cache");

    static CompanionImageService()
    {
        // Seeing this line confirms that the application is using the patched binary.
        DebugConsole.Log("images", "ImageCache V2 MEMORY-FIRST active (2026-10-31).");
    }

    public static Task<Bitmap?> LoadAsync(
        string? source,
        int decodeWidth = 128,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Task.FromResult<Bitmap?>(null);

        string normalized = source.Trim();
        int width = Math.Max(16, decodeWidth);
        string cacheKey = $"{width}|{normalized}";

        Lazy<Task<Bitmap?>> lazy = MemoryCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<Bitmap?>>(
                () => LoadCoreAsync(normalized, width),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitAndEvictFailuresAsync(cacheKey, lazy.Value, cancellationToken);
    }

    private static async Task<Bitmap?> AwaitAndEvictFailuresAsync(
        string cacheKey,
        Task<Bitmap?> task,
        CancellationToken cancellationToken)
    {
        // Cancellation belongs to the caller; it must not cancel a load shared by other controls.
        Bitmap? result = cancellationToken.CanBeCanceled
            ? await task.WaitAsync(cancellationToken)
            : await task;

        // A temporary failure must not become a permanent in-memory null entry.
        if (result is null)
            MemoryCache.TryRemove(cacheKey, out _);

        return result;
    }

    private static async Task<Bitmap?> LoadCoreAsync(string source, int decodeWidth)
    {
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return await LoadRemoteBitmapAsync(uri, decodeWidth);
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out uri) &&
                uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                using Stream resource = AssetLoader.Open(uri);
                return Bitmap.DecodeToWidth(resource, decodeWidth);
            }

            if (Path.IsPathRooted(source) && File.Exists(source))
            {
                // Another application may have the local image open; allow concurrent readers.
                await using FileStream file = new(
                    source, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
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

    private static async Task<Bitmap> LoadRemoteBitmapAsync(Uri uri, int decodeWidth)
    {
        string cachedPath = GetDiskCachePath(uri);
        SemaphoreSlim gate = DownloadLocks.GetOrAdd(cachedPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            byte[]? cachedBytes = await TryReadCacheAsync(cachedPath);
            if (cachedBytes is not null)
            {
                try
                {
                    // Decoding a byte array, not a disk FileStream, means we never
                    // hold a file handle while Avalonia processes the image.
                    return DecodeBytes(cachedBytes, decodeWidth);
                }
                catch (Exception)
                {
                    // Interrupted downloads or older cache versions may have left
                    // truncated entries. A broken cache must not prevent a retry.
                    TryDelete(cachedPath);
                }
            }

            // The network response is buffered entirely in memory. Even if a cache
            // file is locked, unwritable, or being modified by another process,
            // the image can still be displayed immediately from these bytes.
            byte[] downloadedBytes = await DownloadBytesAsync(uri);
            Bitmap bitmap = DecodeBytes(downloadedBytes, decodeWidth);
            await TryWriteCacheAsync(cachedPath, downloadedBytes);
            return bitmap;
        }
        finally
        {
            gate.Release();
        }
    }

    private static Bitmap DecodeBytes(byte[] bytes, int decodeWidth)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        return Bitmap.DecodeToWidth(memory, decodeWidth);
    }

    private static async Task<byte[]?> TryReadCacheAsync(string path)
    {
        try
        {
            // The disk cache is optional; do not let its permissions or locks fail the request.
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length <= 0 || stream.Length > MaximumDownloadBytes)
                return null;

            using var output = new MemoryStream((int)stream.Length);
            await stream.CopyToAsync(output);
            return output.ToArray();
        }
        catch (IOException)
        {
            return null; // Missing, busy, or inaccessible cache: fetch over HTTPS instead.
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<byte[]> DownloadBytesAsync(Uri uri)
    {
        using HttpResponseMessage response = await Http.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            throw new InvalidDataException("Image exceeds the 8 MB download limit.");

        await using Stream input = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buffer = new byte[BufferSize];
        int total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory())) != 0)
        {
            total += read;
            if (total > MaximumDownloadBytes)
                throw new InvalidDataException("Image exceeds the 8 MB download limit.");

            await output.WriteAsync(buffer.AsMemory(0, read));
        }

        return output.ToArray();
    }

    private static async Task TryWriteCacheAsync(string destination, byte[] bytes)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(DiskCacheDirectory);
            // File.WriteAllBytesAsync closes its file handle before it returns.
            await File.WriteAllBytesAsync(temporary, bytes);
            // No overwrite: another Companion may already have published the same image.
            File.Move(temporary, destination);
        }
        catch (IOException)
        {
            // Sharing violation, existing destination, read-only cache: image is already decoded.
        }
        catch (UnauthorizedAccessException)
        {
            // Persistent cache is best-effort.
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetDiskCachePath(Uri uri)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(DiskCacheDirectory, hash + ".img");
    }

    private static string NormalizeResourcePath(string source)
    {
        string path = source.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            return "Data/Assets/" + path["assets/".Length..];
        return path;
    }
}
