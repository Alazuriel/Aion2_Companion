using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AION_2_Companion.Core.GameData;

namespace AION_2_Companion.Core.Leaderboards;

/// <summary>
/// Optional canonical-ID passthrough for Group fights. We NEVER infer a dungeon
/// slug from a human-readable location and NEVER maintain hard-coded boss aliases.
/// When no single canonical ID is recorded locally, return null and let the
/// server's authoritative NPC catalog resolve the boss/instance association.
/// Server validation remains mandatory even for explicitly supplied IDs.
/// </summary>
public static class GroupInstanceResolver
{
    private static readonly Regex CanonicalPattern = new(
        @"^[a-z0-9][a-z0-9_-]*/[a-z0-9][a-z0-9_/-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? Resolve(BossDefinition? boss, int bossNpcId, string? phasewardInstance)
    {
        if (boss is null || !boss.Enabled || bossNpcId <= 0 ||
            !boss.TemplateIds.Contains(bossNpcId))
            return null;

        // Explicit, already-canonical values only. Human labels such as
        // "Vakron Sky Island" and "Eltnen" are ignored, not slugified.
        var canonical = new HashSet<string>(StringComparer.Ordinal);
        AddCanonical(canonical, boss.InstanceId);
        AddCanonical(canonical, boss.Instance);
        AddCanonical(canonical, phasewardInstance);

        // If two local sources disagree, defer to the server instead of guessing.
        return canonical.Count == 1 ? canonical.Single() : null;
    }

    private static void AddCanonical(HashSet<string> values, string? input)
    {
        string candidate = (input ?? string.Empty).Trim();
        if (candidate.Length is >= 3 and <= 100 &&
            CanonicalPattern.IsMatch(candidate) &&
            !candidate.Contains("//", StringComparison.Ordinal))
            values.Add(candidate);
    }
}
