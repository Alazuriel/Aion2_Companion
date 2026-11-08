using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AION_2_Companion.Core;

/// <summary>One visible release-note paragraph or bullet.</summary>
public sealed record ChangelogItem(string Title, string Description)
{
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

public sealed class ChangelogSection
{
    public string Heading { get; init; } = string.Empty;
    public IReadOnlyList<ChangelogItem> Items { get; init; } = [];
}

public sealed class ChangelogEntry
{
    public string Version { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public IReadOnlyList<ChangelogSection> Sections { get; init; } = [];
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
        return Parse(reader.ReadToEnd());
    }

    private static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        string currentVersion = GetCurrentVersion();
        var entries = new List<ChangelogEntry>();
        string? version = null;
        string date = string.Empty;
        string? sectionName = null;
        var sections = new List<ChangelogSection>();
        var items = new List<ChangelogItem>();
        bool footer = false;

        void CommitSection()
        {
            if (items.Count == 0)
                return;
            sections.Add(new ChangelogSection
            {
                Heading = LocalizeCategory(sectionName ?? "Changes"),
                Items = items.ToArray(),
            });
            items.Clear();
        }

        void CommitVersion()
        {
            CommitSection();
            if (version is null)
                return;
            entries.Add(new ChangelogEntry
            {
                Version = version,
                Date = date,
                IsCurrent = string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase),
                Sections = sections.ToArray(),
            });
        }

        foreach (string rawLine in markdown.Replace("\r", string.Empty).Split('\n'))
        {
            string line = rawLine.Trim();
            Match versionMatch = Regex.Match(line, @"^##\s+\[([^\]]+)\](.*)$", RegexOptions.CultureInvariant);
            if (versionMatch.Success)
            {
                CommitVersion();
                version = versionMatch.Groups[1].Value.Trim();
                string suffix = versionMatch.Groups[2].Value;
                date = suffix.StartsWith(" - ", StringComparison.Ordinal) ? suffix[3..].Trim() : string.Empty;
                sectionName = null;
                items = [];
                sections = [];
                footer = false;
                continue;
            }

            if (version is null)
                continue;
            if (line is "---" or "***")
            {
                // Release footer lines are not part of the user-facing changelog.
                footer = true;
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                CommitSection();
                sectionName = line[4..].Trim();
                footer = false;
                continue;
            }
            if (footer || string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                items.Add(ParseItem(line[2..]));
                continue;
            }
            // Important release notes such as the Phaseward warning are prose,
            // not list bullets. Keep those paragraphs when under a ### heading.
            if (sectionName is not null && !line.StartsWith("#", StringComparison.Ordinal))
                items.Add(ParseItem(line));
        }

        CommitVersion();
        return entries;
    }

    private static ChangelogItem ParseItem(string source)
    {
        string text = source.Trim();
        // Typical release bullet: **Feature title** — explanation
        Match titled = Regex.Match(text, @"^\*\*(.+?)\*\*\s*(?:[—–:]\s*|\-\s+)(.*)$", RegexOptions.CultureInvariant);
        if (titled.Success)
            return new ChangelogItem(PlainText(titled.Groups[1].Value), PlainText(titled.Groups[2].Value));
        // Single bold paragraph: heading-style note, not raw Markdown.
        Match strong = Regex.Match(text, @"^\*\*(.+?)\*\*$", RegexOptions.CultureInvariant);
        if (strong.Success)
            return new ChangelogItem(PlainText(strong.Groups[1].Value), string.Empty);
        return new ChangelogItem(string.Empty, PlainText(text));
    }

    private static string PlainText(string text)
    {
        // Deliberately render plain release notes rather than inserting
        // Markdown or untrusted HTML into Avalonia visual trees.
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
        text = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
        return Regex.Replace(text, @"(?<!\w)[*_](?=\S)|(?<=\S)[*_](?!\w)", string.Empty).Trim();
    }

    private static string LocalizeCategory(string category)
    {
        string lowered = category.Trim().ToLowerInvariant();
        string key = lowered switch
        {
            "added" => "ChangelogCategoryAdded",
            "improved" => "ChangelogCategoryImproved",
            "fixed" => "ChangelogCategoryFixed",
            "changed" => "ChangelogCategoryChanged",
            "security" or "security & privacy" => "ChangelogCategorySecurityPrivacy",
            "installation" => "ChangelogCategoryInstallation",
            "requirements" => "ChangelogCategoryRequirements",
            "migration" => "ChangelogCategoryMigration",
            "verification status" => "ChangelogCategoryVerificationStatus",
            _ when lowered.StartsWith("known", StringComparison.Ordinal) => "ChangelogCategoryKnownIssues",
            _ when lowered.Contains("phaseward", StringComparison.Ordinal) => "ChangelogCategoryPhasewardNote",
            _ => "ChangelogCategoryChanges",
        };
        string localized = LocalizationService.Get(key);
        return localized == key && key == "ChangelogCategoryChanges" ? category : localized;
    }

    public static string GetCurrentVersion()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
