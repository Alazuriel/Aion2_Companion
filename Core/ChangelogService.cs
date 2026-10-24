using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AION_2_Companion.Core;

public sealed record ChangelogItem(string Category, string Text);

public sealed class ChangelogEntry
{
    public string Version { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public IReadOnlyList<ChangelogItem> Items { get; init; } = [];
}

public static class ChangelogService
{
    public static IReadOnlyList<ChangelogEntry> Load()
    {
        Assembly assembly = typeof(ChangelogService).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("Nexforge.Changelog.md");
        if (stream is null)
            return [];

        using var reader = new StreamReader(stream);
        string markdown = reader.ReadToEnd();
        return Parse(markdown);
    }

    private static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        string currentVersion = GetCurrentVersion();
        var entries = new List<ChangelogEntry>();
        string? version = null;
        string date = string.Empty;
        string category = "Changes";
        var items = new List<ChangelogItem>();

        void Commit()
        {
            if (version is null)
                return;

            entries.Add(new ChangelogEntry
            {
                Version = version,
                Date = date,
                IsCurrent = string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase),
                Items = items.ToArray(),
            });
        }

        foreach (string rawLine in markdown.Replace("\r", string.Empty).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("## [", StringComparison.Ordinal) && line.Contains(']'))
            {
                Commit();
                int closingBracket = line.IndexOf(']');
                version = line[4..closingBracket];
                int separator = line.IndexOf(" - ", closingBracket, StringComparison.Ordinal);
                date = separator >= 0 ? line[(separator + 3)..].Trim() : string.Empty;
                category = "Changes";
                items = [];
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                category = line[4..].Trim();
                continue;
            }

            if (version is not null && line.StartsWith("- ", StringComparison.Ordinal))
                items.Add(new ChangelogItem(category, line[2..].Trim()));
        }

        Commit();
        return entries;
    }

    public static string GetCurrentVersion()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
