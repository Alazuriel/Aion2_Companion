using System;
using AION_2_Companion.Views.Overlays;

namespace AION_2_Companion.Core;

/// <summary>Read-only presentation; unknown orientation is never fabricated as front/back.</summary>
public static class PositionDetails
{
    // These values are per recorded player / per encounter, not estimated from damage.
    // Null is unknown; an observed zero is shown only when the count is actually zero.
    public static string DeathsValue(PlayerStat stat) =>
        stat.DeathCount is int n && n >= 0 ? n.ToString() : "?";

    public static string FrontValue(PlayerStat stat) => FormatConfirmedSide(stat.FrontHits, stat.BackHits);
    public static string BackValue(PlayerStat stat) => FormatConfirmedSide(stat.BackHits, stat.FrontHits);
    public static string UnknownValue(PlayerStat stat) =>
        stat.UnknownHits is int n && n >= 0 ? n.ToString() : "?";

    private static string FormatConfirmedSide(int? count, int? opposite)
    {
        // Both sides must be measured. Never turn an unsupported raw direction code into 0%.
        if (count is not int n || opposite is not int other || n < 0 || other < 0)
            return "N/A";
        long classified = (long)n + other;
        return classified == 0 ? $"{n} · N/A" : $"{n} · {n * 100.0 / classified:0.#}%";
    }

    public static string CoverageNote(PlayerStat stat)
    {
        if (stat.FrontHits is not int front || stat.BackHits is not int back || front < 0 || back < 0)
            return "Direction not verified — Front/Back N/A. Unknown hits are not classified.";
        long classified = (long)front + back;
        return $"Classified hits: {classified} · Unknown hits: {UnknownValue(stat)}. Percentages exclude unknown hits.";
    }

    public static string Format(PlayerStat stat, bool compact)
    {
        if (stat.FrontHits is not >= 0 || stat.BackHits is not >= 0)
            return stat.UnknownHits is int unknown
                ? $"Front/Back N/A · Unclassified {unknown} hits"
                : "Front/Back N/A · positional data unavailable";

        int total = stat.FrontHits.Value + stat.BackHits.Value;
        if (total == 0)
            return $"Front/Back N/A · Unclassified {stat.UnknownHits?.ToString() ?? "?"} hits";
        double front = stat.FrontHits.Value * 100.0 / total;
        double back = stat.BackHits.Value * 100.0 / total;
        return compact
            ? $"Front {front:0.#}% · Back {back:0.#}%"
            : $"Front {front:0.#}% ({stat.FrontHits}) · Back {back:0.#}% ({stat.BackHits}) · Unclassified: {stat.UnknownHits?.ToString() ?? "?"}";
    }
}
