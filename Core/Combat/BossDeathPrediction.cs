using System;

namespace AION_2_Companion.Core.Combat;

public static class BossDeathPrediction
{
    public const double MinimumSampleSeconds = 3;

    public static double? EstimateSeconds(
        long? remainingHp,
        long accumulatedBossDamage,
        double bossCombatSeconds,
        bool fightIsRunning)
    {
        if (!fightIsRunning || remainingHp is null or <= 0 ||
            accumulatedBossDamage <= 0 || bossCombatSeconds < MinimumSampleSeconds)
            return null;

        double averagePartyDps = accumulatedBossDamage / bossCombatSeconds;
        if (!double.IsFinite(averagePartyDps) || averagePartyDps <= 0)
            return null;

        double seconds = remainingHp.Value / averagePartyDps;
        return double.IsFinite(seconds) && seconds >= 0 ? seconds : null;
    }
}
