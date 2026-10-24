using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core;

public static class SupportExportService
{
    private const int MaxCombatRecords = 20;
    private const int MaxDiagnosticLogs = 3;

    public static async Task CreateAsync(
        Stream destination,
        bool includeCombatRecords,
        bool monitoringActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        DebugConsole.Flush();

        if (destination.CanSeek)
            destination.SetLength(0);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await AddTextAsync(
            archive,
            "support-info.txt",
            BuildSummary(monitoringActive),
            cancellationToken);

        foreach (string logPath in SafeGetFiles(DebugConsole.LogDirectory, "companion-*.log")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(MaxDiagnosticLogs))
        {
            await AddFileAsync(
                archive,
                logPath,
                $"diagnostic-logs/{Path.GetFileName(logPath)}",
                cancellationToken);
        }

        if (!includeCombatRecords)
            return;

        foreach (string combatPath in SafeGetFiles(CombatLogStore.LogDirectory, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(MaxCombatRecords))
        {
            await AddFileAsync(
                archive,
                combatPath,
                $"combat-history/{Path.GetFileName(combatPath)}",
                cancellationToken);
        }
    }

    private static string BuildSummary(bool monitoringActive)
    {
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var text = new StringBuilder();
        text.AppendLine("Nexforge AION 2 Companion support package");
        text.AppendLine($"Created (UTC): {DateTimeOffset.UtcNow:O}");
        text.AppendLine($"Application version: {version}");
        text.AppendLine($"Operating system: {Environment.OSVersion}");
        text.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
        text.AppendLine($".NET runtime: {Environment.Version}");
        text.AppendLine($"Language: {CompanionSettings.LanguageCode}");
        text.AppendLine($"Administrator: {AdministratorAccess.IsRunningAsAdministrator}");
        text.AppendLine($"Background monitoring active: {monitoringActive}");
        text.AppendLine($"Map helper enabled: {CompanionSettings.MapHelperEnabled}");
        text.AppendLine($"Automatic monitoring enabled: {CompanionSettings.AutoCaptureEnabled}");
        text.AppendLine($"Diagnostic file logging enabled: {CompanionSettings.FileLoggingEnabled}");
        text.AppendLine($"Automatic DPS overlay enabled: {CompanionSettings.AutoManageDpsOverlay}");
        text.AppendLine($"Combat history limit: {CompanionSettings.CombatLogLimit}");
        return text.ToString();
    }

    private static string[] SafeGetFiles(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static async Task AddTextAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                useAsync: true);
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using Stream target = entry.Open();
            await source.CopyToAsync(target, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // One busy or inaccessible log must not prevent the rest of the package.
        }
    }
}
