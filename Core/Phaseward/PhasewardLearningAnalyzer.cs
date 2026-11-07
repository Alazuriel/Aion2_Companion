using System;
using System.Collections.Generic;
using System.Linq;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// Offline-only, boss-NPC-scoped analysis of previously recorded pulls.
/// This is statistical evidence, not game-server proof. No raw packets or character identities.
/// Only a prior-pull, leave-one-pull-out checked rule may reach the HUD.
/// </summary>
public static class PhasewardLearningAnalyzer
{
    public enum RuleKind { Precursor, BossHp, PullTime }

    public sealed record Rule(int BossNpcId, uint TargetSkillId, RuleKind Kind,
        uint CueSkillId, string CueKind, double Value, int SupportingPulls,
        int EvaluatedPulls, double MaximumHoldoutError, double Spread,
        int FalseTriggers);

    private const int MinimumPulls = 3;
    private const double MinimumLead = 10;
    private const double MaximumLead = 30;

    public static IReadOnlyList<Rule> FindValidatedRules(
        IEnumerable<PhasewardLearningRecorder.Pull> recorded, int bossNpcId)
    {
        // Group and validate by NPC; never learn across different bosses or from a partial sample.
        var pulls = recorded.Where(p => p is not null && p.NpcTemplateId == bossNpcId &&
                             p.Samples is { Count: >= 2 and <= 600 })
            .OrderBy(p => p.StartedAtUtc).TakeLast(24).ToArray();
        if (bossNpcId <= 0 || pulls.Length < MinimumPulls) return Array.Empty<Rule>();

        var castsByPull = pulls.Select(p => p.Samples
            .Where(s => s.Kind == "cast" && s.SkillId > 0 && Valid(s))
            .GroupBy(s => s.SkillId).ToDictionary(g => g.Key, g => g.Min(x => x.Second)))
            .ToArray();
        var targets = castsByPull.SelectMany(x => x.Keys).Distinct().ToArray();
        var result = new List<Rule>();

        foreach (uint targetSkill in targets)
        {
            var observed = Enumerable.Range(0, pulls.Length)
                .Where(index => castsByPull[index].ContainsKey(targetSkill)).ToArray();
            if (observed.Length < MinimumPulls) continue;
            // We require the target cast to be observed in every sufficiently long complete
            // training pull; misses remain unknown rather than evidence of an absent cast.

            double[] castTimes = observed.Select(i => castsByPull[i][targetSkill]).ToArray();
            double medianCastTime = Median(castTimes.Order().ToArray());
            // A sufficiently long recorded pull without the expected target cast
            // is contradictory evidence; shorter/partial pulls are inconclusive.
            bool contradictedTime = Enumerable.Range(0, pulls.Length).Any(i =>
                !castsByPull[i].ContainsKey(targetSkill) &&
                pulls[i].Samples.Where(Valid).Select(s => s.Second).DefaultIfEmpty(0).Max()
                    >= medianCastTime + 5);
            if (!contradictedTime &&
                TryCrossValidate(castTimes, maxHoldout: 5, maxSpread: 8,
                    out double seconds, out double timeError, out double timeSpread))
                result.Add(new Rule(bossNpcId, targetSkill, RuleKind.PullTime, 0, "",
                    seconds, observed.Length, observed.Length, timeError, timeSpread, 0));

            var hpValues = new List<double>();
            foreach (int i in observed)
            {
                double t = castsByPull[i][targetSkill];
                var hp = pulls[i].Samples.Where(s => s.Kind == "hp" && Valid(s) &&
                            s.HpPercent is >= 1 and <= 99 && s.Second <= t && t - s.Second <= 3)
                    .OrderByDescending(s => s.Second).FirstOrDefault();
                if (hp?.HpPercent is double pct) hpValues.Add(pct);
            }
            bool contradictedHp = hpValues.Count == observed.Length &&
                Enumerable.Range(0, pulls.Length).Any(i =>
                    !castsByPull[i].ContainsKey(targetSkill) &&
                    pulls[i].Samples.Any(s => s.Kind == "hp" && Valid(s) &&
                        s.HpPercent is double pct && pct <= Median(hpValues.Order().ToArray()) - 2));
            if (!contradictedHp && hpValues.Count == observed.Length &&
                TryCrossValidate(hpValues, maxHoldout: 3.5, maxSpread: 5,
                    out double hpTarget, out double hpError, out double hpSpread))
                result.Add(new Rule(bossNpcId, targetSkill, RuleKind.BossHp, 0, "hp",
                    hpTarget, observed.Length, observed.Length, hpError, hpSpread, 0));

            // Collect distinct precursor identities that precede this cast by 10–30s.
            var candidates = new HashSet<(string Kind, uint Skill)>();
            foreach (int i in observed)
            {
                double target = castsByPull[i][targetSkill];
                foreach (var s in pulls[i].Samples)
                    if (Valid(s) && s.SkillId > 0 && s.SkillId != targetSkill &&
                        (s.Kind is "cast" or "hit") &&
                        target - s.Second is >= MinimumLead and <= MaximumLead)
                        candidates.Add((s.Kind, s.SkillId));
            }

            foreach (var (kind, cueSkill) in candidates)
            {
                var leads = new List<double>();
                bool allSupported = true;
                foreach (int i in observed)
                {
                    double target = castsByPull[i][targetSkill];
                    double[] matches = pulls[i].Samples.Where(s => Valid(s) &&
                            s.Kind == kind && s.SkillId == cueSkill &&
                            target - s.Second is >= MinimumLead and <= MaximumLead)
                        .Select(s => target - s.Second).ToArray();
                    if (matches.Length == 0) { allSupported = false; break; }
                    // Repeated AoE hits: select the precursor nearest the cross-pull
                    // detection window centre. Only one vote per independent pull.
                    leads.Add(matches.OrderBy(x => Math.Abs(x - 20)).First());
                }
                if (!allSupported || leads.Count != observed.Length ||
                    !TryCrossValidate(leads, maxHoldout: 4.0, maxSpread: 6,
                        out double lead, out double leadError, out double leadSpread))
                    continue;

                int falseTriggers = 0;
                for (int i = 0; i < pulls.Length; i++)
                {
                    if (castsByPull[i].ContainsKey(targetSkill)) continue;
                    double last = pulls[i].Samples.Where(Valid).Select(s => s.Second)
                        .DefaultIfEmpty(0).Max();
                    if (pulls[i].Samples.Any(s => Valid(s) && s.Kind == kind &&
                        s.SkillId == cueSkill && s.Second + lead + 5 < last))
                        falseTriggers++;
                }
                if (falseTriggers > 0) continue;
                result.Add(new Rule(bossNpcId, targetSkill, RuleKind.Precursor,
                    cueSkill, kind, lead, observed.Length, observed.Length,
                    leadError, leadSpread, falseTriggers));
            }
        }

        // Only one best cue per target, plus time/HP fallbacks. Stable deterministic ordering.
        return result.GroupBy(r => (r.TargetSkillId, r.Kind))
            .Select(g => g.OrderBy(r => r.MaximumHoldoutError)
                .ThenByDescending(r => r.SupportingPulls).ThenBy(r => r.CueSkillId).First())
            .OrderBy(r => r.TargetSkillId).ThenBy(r => r.Kind).Take(72).ToArray();
    }

    private static bool Valid(PhasewardLearningRecorder.Sample? s) =>
        s is not null && double.IsFinite(s.Second) && s.Second is >= 0 and <= 7200;

    // Leave-one-pull-out: predict every held-out observation using only OTHER pulls.
    private static bool TryCrossValidate(IReadOnlyList<double> values,
        double maxHoldout, double maxSpread, out double median,
        out double maxError, out double spread)
    {
        median = maxError = spread = 0;
        if (values.Count < MinimumPulls || values.Any(x => !double.IsFinite(x))) return false;
        double[] sorted = values.Order().ToArray();
        median = Median(sorted);
        spread = sorted[^1] - sorted[0];
        if (spread > maxSpread) return false;
        for (int i = 0; i < values.Count; i++)
        {
            double heldOut = values[i];
            double[] train = values.Where((_, n) => n != i).Order().ToArray();
            maxError = Math.Max(maxError, Math.Abs(heldOut - Median(train)));
        }
        return maxError <= maxHoldout;
    }

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count % 2 != 0
        ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
