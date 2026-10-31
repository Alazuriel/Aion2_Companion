using System.Collections.Generic;
using System.Linq;

namespace AION_2_Companion.Core.Combat;

public static class CombatEndRules
{
    public static bool ShouldRequestEndForDeath(
        uint deadEntity,
        uint currentBoss,
        IReadOnlyCollection<uint> fightTargets,
        IReadOnlySet<uint> deadTargets)
    {
        if (deadEntity == 0 || fightTargets.Count == 0 || !fightTargets.Contains(deadEntity))
            return false;

        if (currentBoss != 0 && deadEntity == currentBoss)
            return true;

        return fightTargets.All(deadTargets.Contains);
    }

    public static bool HasReachedInactivityTimeout(
        long lastDamageTimestamp,
        long now,
        long timestampFrequency,
        double timeoutSeconds)
    {
        if (lastDamageTimestamp <= 0 || now < lastDamageTimestamp ||
            timestampFrequency <= 0 || timeoutSeconds <= 0)
            return false;

        return (now - lastDamageTimestamp) / (double)timestampFrequency >= timeoutSeconds;
    }
}
