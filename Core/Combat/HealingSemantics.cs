using System;
using System.Text.RegularExpressions;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Parsers;

namespace AION_2_Companion.Core.Combat;

public static partial class HealingSemantics
{
    public static bool IsHealingHit(DamageHit hit, SkillDefinition? skill)
    {
        if (hit is null || hit.IsDot || hit.Damage == 0 || skill is null)
            return false;

        // Verified against exported AION 2 frames. Type 2 is also used by
        // damage, therefore the wire signature is never accepted without a
        // healing-capable skill definition and friendly actor/target checks.
        return hit.Type == 2 &&
               (hit.Switch & 0x0F) == 4 &&
               ProvidesHpHealing(skill.Description);
    }

    public static bool ProvidesHpHealing(string? description) =>
        !string.IsNullOrWhiteSpace(description) &&
        HpHealingPattern().IsMatch(description);

    [GeneratedRegex(
        @"\b(?:restore(?:s|d)?|heal(?:s|ed|ing)|absorb(?:s|ed|ing)?)\b[^.!?;]{0,120}\b(?:max\s+)?hp\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HpHealingPattern();
}
