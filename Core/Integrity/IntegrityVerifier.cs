using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Integrity;

public enum IntegrityIssueKind
{
    Missing,
    Corrupt,
    VersionMismatch,
    InvalidManifest,
}

public sealed record IntegrityIssue(string Path, string Category, IntegrityIssueKind Kind);

public sealed record IntegrityVerificationResult(
    bool ManifestAvailable,
    bool ManifestTrusted,
    string ManifestVersion,
    IReadOnlyList<IntegrityIssue> Issues,
    int ApplicationFiles,
    int LanguageFiles,
    int ForgeDataFiles)
{
    public bool IsHealthy => ManifestAvailable && ManifestTrusted && Issues.Count == 0;
}

public sealed class IntegrityManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("product")] public string Product { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("created_utc")] public DateTimeOffset CreatedUtc { get; set; }
    [JsonPropertyName("key_id")] public string KeyId { get; set; } = string.Empty;
    [JsonPropertyName("files")] public IntegrityManifestEntry[] Files { get; set; } = [];
}

public sealed class IntegrityManifestEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
}

public static class IntegrityVerifier
{
    public const string ManifestFileName = "integrity-manifest.json";
    public const string SignatureFileName = "integrity-manifest.sig";
    private const int MaxEntries = 5_000;
    private const long MaxFileBytes = 768L * 1024 * 1024;

    public static async Task<IntegrityVerificationResult> VerifyAsync(
        string installationDirectory,
        string expectedVersion,
        ReadOnlyMemory<byte> publicKey,
        TimeSpan interFileDelay,
        CancellationToken cancellationToken = default,
        Func<bool>? shouldPause = null)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationDirectory));
        string manifestPath = Path.Combine(root, ManifestFileName);
        string signaturePath = Path.Combine(root, SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
            return new(false, false, string.Empty, [], 0, 0, 0);

        try
        {
            byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
            if (manifestBytes.Length is <= 0 or > 4 * 1024 * 1024)
                return InvalidManifest();

            string signatureText = (await File.ReadAllTextAsync(signaturePath, cancellationToken)).Trim();
            byte[] signature = Convert.FromBase64String(signatureText);
            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKey.Span, out _);
            if (!rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                return InvalidManifest();

            IntegrityManifest? manifest = JsonSerializer.Deserialize<IntegrityManifest>(manifestBytes);
            if (manifest is null || manifest.Schema != 1 ||
                !string.Equals(manifest.Product, "AION 2 Companion", StringComparison.Ordinal) ||
                manifest.Files.Length is <= 0 or > MaxEntries)
            {
                return InvalidManifest();
            }

            var issues = new List<IntegrityIssue>();
            if (!string.Equals(manifest.Version, expectedVersion, StringComparison.OrdinalIgnoreCase))
                issues.Add(new("", "application", IntegrityIssueKind.VersionMismatch));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int applicationFiles = 0;
            int languageFiles = 0;
            int forgeDataFiles = 0;
            foreach (IntegrityManifestEntry entry in manifest.Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsValidEntry(entry) || !seen.Add(entry.Path))
                    return InvalidManifest();

                switch (entry.Category)
                {
                    case "language": languageFiles++; break;
                    case "forgedata": forgeDataFiles++; break;
                    default: applicationFiles++; break;
                }

                string relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
                string fullPath = Path.GetFullPath(Path.Combine(root, relative));
                if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return InvalidManifest();

                if (!File.Exists(fullPath))
                {
                    issues.Add(new(entry.Path, entry.Category, IntegrityIssueKind.Missing));
                    continue;
                }

                var info = new FileInfo(fullPath);
                if (info.Length != entry.Size)
                {
                    issues.Add(new(entry.Path, entry.Category, IntegrityIssueKind.Corrupt));
                    continue;
                }

                try
                {
                    await using var stream = new FileStream(
                        fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                        64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    byte[] hash = await ComputeHashAsync(
                        stream, interFileDelay, cancellationToken, shouldPause);
                    if (!string.Equals(Convert.ToHexString(hash), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        issues.Add(new(entry.Path, entry.Category, IntegrityIssueKind.Corrupt));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    issues.Add(new(entry.Path, entry.Category, IntegrityIssueKind.Corrupt));
                }

            }

            return new(true, true, manifest.Version, issues, applicationFiles, languageFiles, forgeDataFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            return InvalidManifest();
        }
    }

    private static bool IsValidEntry(IntegrityManifestEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Path) &&
        !Path.IsPathRooted(entry.Path) &&
        !entry.Path.Contains("..", StringComparison.Ordinal) &&
        entry.Size is >= 0 and <= MaxFileBytes &&
        entry.Sha256.Length == 64 && entry.Sha256.All(Uri.IsHexDigit) &&
        entry.Category is "application" or "language" or "forgedata";

    private static async Task<byte[]> ComputeHashAsync(
        Stream stream,
        TimeSpan throttleDelay,
        CancellationToken cancellationToken,
        Func<bool>? shouldPause)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            while (shouldPause?.Invoke() == true)
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            hash.AppendData(buffer, 0, read);
            if (throttleDelay > TimeSpan.Zero)
                await Task.Delay(throttleDelay, cancellationToken);
        }
        return hash.GetHashAndReset();
    }

    private static IntegrityVerificationResult InvalidManifest() =>
        new(true, false, string.Empty,
            [new("", "application", IntegrityIssueKind.InvalidManifest)], 0, 0, 0);
}
