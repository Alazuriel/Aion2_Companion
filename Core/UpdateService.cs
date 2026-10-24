using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core;

public enum UpdateCheckState
{
    UpdateAvailable,
    UpToDate,
    ChannelUnavailable,
    Failed,
}

public sealed record UpdateInfo(
    string Version,
    string Name,
    string Notes,
    DateTimeOffset? PublishedAt,
    string DownloadUrl,
    string FileName,
    long Size,
    string Sha256,
    string ReleaseUrl);

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    UpdateInfo? Update = null,
    string? Error = null);

public static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/Alazuriel/Aion2_Companion/releases/latest";
    private const string InstallerPrefix = "Nexforge-AION2-Companion-Setup-";
    private const long MaxInstallerBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(8);

    private static readonly HttpClient Http = CreateClient();

    public static bool ShouldCheckAutomatically()
    {
        if (!CompanionSettings.CheckUpdatesAutomatically)
            return false;

        DateTimeOffset? lastCheck = CompanionSettings.LastUpdateCheckUtc;
        return lastCheck is null || DateTimeOffset.UtcNow - lastCheck >= AutomaticCheckInterval;
    }

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                LatestReleaseApi,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            CompanionSettings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                return new UpdateCheckResult(UpdateCheckState.ChannelUnavailable);

            response.EnsureSuccessStatusCode();
            await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                content,
                cancellationToken: cancellationToken);

            if (release is null || release.Draft || release.Prerelease)
                return new UpdateCheckResult(UpdateCheckState.ChannelUnavailable);

            GitHubAsset? installer = release.Assets?.FirstOrDefault(asset =>
                asset.Name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (installer is null ||
                installer.Size <= 0 ||
                installer.Size > MaxInstallerBytes ||
                !TryReadSha256(installer.Digest, out string sha256) ||
                !IsTrustedDownloadUrl(installer.BrowserDownloadUrl))
            {
                return new UpdateCheckResult(UpdateCheckState.ChannelUnavailable);
            }

            string releaseVersion = NormalizeVersion(release.TagName);
            if (!TryParseVersion(releaseVersion, out Version? availableVersion))
                return new UpdateCheckResult(UpdateCheckState.ChannelUnavailable);

            Version currentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            if (availableVersion <= currentVersion)
                return new UpdateCheckResult(UpdateCheckState.UpToDate);

            return new UpdateCheckResult(
                UpdateCheckState.UpdateAvailable,
                new UpdateInfo(
                    releaseVersion,
                    string.IsNullOrWhiteSpace(release.Name) ? $"Version {releaseVersion}" : release.Name,
                    TrimNotes(release.Body),
                    release.PublishedAt,
                    installer.BrowserDownloadUrl,
                    installer.Name,
                    installer.Size,
                    sha256,
                    release.HtmlUrl));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return new UpdateCheckResult(UpdateCheckState.Failed, Error: ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            return new UpdateCheckResult(UpdateCheckState.Failed, Error: ex.Message);
        }
    }

    public static async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string updateDirectory = Path.Combine(CompanionSettings.SettingsDirectory, "Updates");
        Directory.CreateDirectory(updateDirectory);
        string destinationPath = Path.Combine(updateDirectory, Path.GetFileName(update.FileName));
        string temporaryPath = destinationPath + ".download";

        if (File.Exists(destinationPath) &&
            await VerifySha256Async(destinationPath, update.Sha256, cancellationToken))
        {
            progress?.Report(1);
            return destinationPath;
        }

        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long declaredLength = response.Content.Headers.ContentLength ?? update.Size;
            if (declaredLength <= 0 || declaredLength > MaxInstallerBytes)
                throw new InvalidDataException("The downloaded installer has an invalid size.");

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > MaxInstallerBytes || total > update.Size + 1024)
                        throw new InvalidDataException("The downloaded installer is larger than expected.");

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(Math.Clamp((double)total / update.Size, 0, 1));
                }
            }

            if (!await VerifySha256Async(temporaryPath, update.Sha256, cancellationToken))
                throw new InvalidDataException("The downloaded installer did not pass integrity verification.");

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                // The partial file is overwritten before the next download attempt.
            }

            throw;
        }

        CleanupOldInstallers(updateDirectory, destinationPath);
        progress?.Report(1);
        return destinationPath;
    }

    public static void LaunchInstaller(string installerPath)
    {
        string language = CompanionSettings.LanguageCode.ToLowerInvariant() switch
        {
            "hu" => "hungarian",
            "de" => "german",
            "pl" => "polish",
            _ => "english",
        };

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/LANG={language} /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "Nexforge-AION2-Companion",
            ChangelogService.GetCurrentVersion()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }

    private static bool IsTrustedDownloadUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string tag) =>
        tag.Trim().TrimStart('v', 'V').Split('-', 2)[0];

    private static bool TryParseVersion(string value, out Version? version)
    {
        if (Version.TryParse(value, out version))
            return true;

        version = null;
        return false;
    }

    private static bool TryReadSha256(string? digest, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrWhiteSpace(digest) ||
            !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string value = digest[7..].Trim();
        if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            return false;

        sha256 = value.ToUpperInvariant();
        return true;
    }

    private static async Task<bool> VerifySha256Async(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexString(hash),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void CleanupOldInstallers(string directory, string keepPath)
    {
        foreach (string path in Directory.GetFiles(directory, $"{InstallerPrefix}*.exe"))
        {
            if (string.Equals(path, keepPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked installer can be removed during a later update.
            }
        }
    }

    private static string TrimNotes(string? notes)
    {
        const int maxLength = 20_000;
        if (string.IsNullOrWhiteSpace(notes))
            return string.Empty;

        string normalized = notes.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
        [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
