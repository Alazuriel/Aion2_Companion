using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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

public enum UpdatePackageKind
{
    Installer,
    Patch,
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
    string ReleaseUrl,
    UpdatePackageKind PackageKind = UpdatePackageKind.Installer);

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    UpdateInfo? Update = null,
    string? Error = null);

public static class UpdateService
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/Alazuriel/Aion2_Companion/releases/latest";
    private const string InstallerPrefix = "Nexforge-AION2-Companion-Setup-";
    private const string PatchPrefix = "Nexforge-AION2-Companion-Patch-";
    private const long MaxInstallerBytes = 512L * 1024 * 1024;
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(8);

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);

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

            string releaseVersion = NormalizeVersion(release.TagName);
            if (!TryParseVersion(releaseVersion, out Version? availableVersion))
                return new UpdateCheckResult(UpdateCheckState.ChannelUnavailable);

            Version currentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            if (availableVersion <= currentVersion)
                return new UpdateCheckResult(UpdateCheckState.UpToDate);

            string current = currentVersion.ToString(3);
            string expectedPatchName = PatchPrefix + current + "-to-" + releaseVersion + ".zip";
            string expectedInstallerName = InstallerPrefix + releaseVersion + ".exe";
            GitHubAsset? package = FindValidAsset(release.Assets, expectedPatchName);
            UpdatePackageKind packageKind = UpdatePackageKind.Patch;
            if (package is null)
            {
                package = FindValidAsset(release.Assets, expectedInstallerName);
                packageKind = UpdatePackageKind.Installer;
            }

            if (package is null || !TryReadSha256(package.Digest, out string sha256))
                return new UpdateCheckResult(UpdateCheckState.ChannelUnavailable);

            return new UpdateCheckResult(
                UpdateCheckState.UpdateAvailable,
                new UpdateInfo(
                    releaseVersion,
                    string.IsNullOrWhiteSpace(release.Name) ? $"Version {releaseVersion}" : release.Name,
                    TrimNotes(release.Body),
                    release.PublishedAt,
                    package.BrowserDownloadUrl,
                    package.Name,
                    package.Size,
                    sha256,
                    release.HtmlUrl,
                    packageKind));
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

    public static async Task<UpdateInfo?> GetRepairInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                LatestReleaseApi,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                content,
                cancellationToken: cancellationToken);
            if (release is null || release.Draft || release.Prerelease)
                return null;

            string releaseVersion = NormalizeVersion(release.TagName);
            if (!TryParseVersion(releaseVersion, out Version? releaseNumber))
                return null;
            Version currentNumber = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            if (releaseNumber < currentNumber)
                return null;

            string expectedInstallerName = InstallerPrefix + releaseVersion + ".exe";
            GitHubAsset? installer = FindValidAsset(release.Assets, expectedInstallerName);
            if (installer is null || !TryReadSha256(installer.Digest, out string sha256))
                return null;

            return new UpdateInfo(
                releaseVersion,
                string.IsNullOrWhiteSpace(release.Name) ? $"Version {releaseVersion}" : release.Name,
                TrimNotes(release.Body),
                release.PublishedAt,
                installer.BrowserDownloadUrl,
                installer.Name,
                installer.Size,
                sha256,
                release.HtmlUrl,
                UpdatePackageKind.Installer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            DebugConsole.Log("integrity", $"Repair source is unavailable: {ex.Message}");
            return null;
        }
    }

    public static async Task<string> DownloadAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await DownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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

        CleanupOldPackages(updateDirectory, destinationPath);
        progress?.Report(1);
        return destinationPath;
    
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    public static void LaunchUpdatePackage(UpdateInfo update, string packagePath)
    {
        if (update.PackageKind == UpdatePackageKind.Patch)
        {
            LaunchPatch(update, packagePath);
            return;
        }

        LaunchSilentInstaller(packagePath);
    }

    public static Task<bool> VerifyPackageAsync(
        string packagePath,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath) ||
            expectedSha256.Length != 64 ||
            expectedSha256.Any(character => !Uri.IsHexDigit(character)) ||
            !File.Exists(packagePath))
        {
            return Task.FromResult(false);
        }

        return VerifySha256Async(packagePath, expectedSha256, cancellationToken);
    }

    private static void LaunchSilentInstaller(string installerPath)
    {
        string language = LocalizationService.NormalizeLanguageCode(
            CompanionSettings.LanguageCode);
        string installDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(AppContext.BaseDirectory));
        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
        };
        startInfo.ArgumentList.Add("--silent-update");
        startInfo.ArgumentList.Add("--language=" + language);
        startInfo.ArgumentList.Add("--install-dir=" + installDirectory);
        startInfo.ArgumentList.Add("--wait-pid=" + Environment.ProcessId);

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the background updater.");
    }

    private static void LaunchPatch(UpdateInfo update, string patchPath)
    {
        string installDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(AppContext.BaseDirectory));
        string programPath = Path.Combine(installDirectory, "AION 2 Companion.exe");
        string powershell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException("Windows PowerShell is unavailable.", powershell);

        const string script = """
$ErrorActionPreference = 'Stop'
$patch = $env:NEXFORGE_UPDATE_PATCH
$install = $env:NEXFORGE_UPDATE_INSTALL
$program = $env:NEXFORGE_UPDATE_PROGRAM
$expectedSource = $env:NEXFORGE_UPDATE_SOURCE
$expectedTarget = $env:NEXFORGE_UPDATE_TARGET
$waitPid = [int]$env:NEXFORGE_UPDATE_WAIT_PID
$logFolder = Join-Path $env:LOCALAPPDATA 'Nexforge\AION 2 Companion\Updates'
$errorLog = Join-Path $logFolder 'last-update-error.log'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('Nexforge\patch-' + [Guid]::NewGuid().ToString('N'))
try {
    $parent = Get-Process -Id $waitPid -ErrorAction SilentlyContinue
    if ($parent) { Wait-Process -Id $waitPid -Timeout 120 -ErrorAction Stop }
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    Expand-Archive -LiteralPath $patch -DestinationPath $temp -Force
    $manifestPath = Join-Path $temp 'patch-manifest.json'
    $applyPath = Join-Path $temp 'Apply-Patch.ps1'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $applyPath -PathType Leaf)) { throw 'Invalid patch package.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schema -ne 1 -or [string]$manifest.product -ne 'AION 2 Companion' -or
        [string]$manifest.source_version -ne $expectedSource -or
        [string]$manifest.target_version -ne $expectedTarget) { throw 'Patch version does not match this installation.' }
    & $applyPath -InstallDirectory $install
    Remove-Item -LiteralPath $errorLog -Force -ErrorAction SilentlyContinue
}
catch {
    New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
    Set-Content -LiteralPath $errorLog -Value ((Get-Date).ToUniversalTime().ToString('o') + "`n" + $_) -Encoding utf8
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $program) { Start-Process -FilePath $program -WorkingDirectory $install }
}
""";

        var startInfo = new ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        startInfo.Environment["NEXFORGE_UPDATE_PATCH"] = Path.GetFullPath(patchPath);
        startInfo.Environment["NEXFORGE_UPDATE_INSTALL"] = installDirectory;
        startInfo.Environment["NEXFORGE_UPDATE_PROGRAM"] = programPath;
        startInfo.Environment["NEXFORGE_UPDATE_SOURCE"] =
            (typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);
        startInfo.Environment["NEXFORGE_UPDATE_TARGET"] = update.Version;
        startInfo.Environment["NEXFORGE_UPDATE_WAIT_PID"] = Environment.ProcessId.ToString();
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the background patcher.");
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

    private static GitHubAsset? FindValidAsset(GitHubAsset[]? assets, string expectedName)
    {
        GitHubAsset? asset = assets?.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, expectedName, StringComparison.OrdinalIgnoreCase));
        return asset is not null &&
               asset.Size > 0 &&
               asset.Size <= MaxInstallerBytes &&
               TryReadSha256(asset.Digest, out _) &&
               IsTrustedDownloadUrl(asset.BrowserDownloadUrl)
            ? asset
            : null;
    }

    private static void CleanupOldPackages(string directory, string keepPath)
    {
        string[] installers = Directory.GetFiles(directory, $"{InstallerPrefix}*.exe");
        string[] patches = Directory.GetFiles(directory, $"{PatchPrefix}*.zip");
        foreach (string path in installers.Concat(patches))
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
