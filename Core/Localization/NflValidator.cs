using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AION_2_Companion.Core.Localization;

public static partial class NflValidator
{
    public static IReadOnlyList<NflValidationIssue> Validate(NflDocument document, NflDocument? basis = null)
    {
        var issues = new List<NflValidationIssue>();
        foreach (string required in new[] { "language", "name" })
        {
            if (!document.Metadata.TryGetValue(required, out string? value) || string.IsNullOrWhiteSpace(value))
                issues.Add(new("error", $"Required metadata is missing: {required}."));
        }
        if (document.Language.Length > 0 && !LanguagePattern().IsMatch(document.Language))
            issues.Add(new("error", "The language code is not valid."));
        if (document.Sections.Count == 0)
            issues.Add(new("warning", "The file contains no translations."));
        if (basis is null)
            return issues;

        Dictionary<string, string> source = basis.Flatten();
        Dictionary<string, string> target = document.Flatten();
        foreach ((string key, string sourceValue) in source)
        {
            if (!target.TryGetValue(key, out string? targetValue) || string.IsNullOrWhiteSpace(targetValue))
            {
                issues.Add(new("warning", "Translation is missing.", key));
                continue;
            }
            string[] expected = Placeholders(sourceValue).Order().ToArray();
            string[] actual = Placeholders(targetValue).Order().ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                issues.Add(new("error", "Placeholders do not match.", key));
        }
        foreach (string key in target.Keys.Except(source.Keys, StringComparer.Ordinal))
            issues.Add(new("warning", "Unknown translation key.", key));
        return issues;
    }

    public static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
    [GeneratedRegex(@"(?<!\{)\{([A-Za-z_][A-Za-z0-9_.-]*|\d+)(?:,[^{}]+)?(?::[^{}]+)?\}(?!\})", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();
}
