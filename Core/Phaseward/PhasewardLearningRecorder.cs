using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AION_2_Companion.Core;

namespace AION_2_Companion.Core.Phaseward;

// Offline, opt-in, bounded evidence recorder. Never retains names, token or raw packet bytes.
// Candidate rules are for research/backtesting only; do NOT drive live alerts from this file.
public sealed class PhasewardLearningRecorder
{
    public sealed class Sample
    {
        public string Kind { get; set; } = ""; // cast/hit/hp
        public double Second { get; set; }
        public uint SkillId { get; set; }
        public double? HpPercent { get; set; }
    }
    public sealed class Pull
    {
        public int NpcTemplateId { get; set; }
        public DateTimeOffset StartedAtUtc { get; set; }
        public List<Sample> Samples { get; set; } = new();
    }
    public sealed class Archive
    {
        public int Version { get; set; } = 1;
        public List<Pull> Pulls { get; set; } = new();
    }
    public sealed record Candidate(int NpcTemplateId, uint TargetSkillId,
        uint PrecursorSkillId, string PrecursorKind, int SupportingPulls,
        double MedianLeadSeconds, double SpreadSeconds);

    private const int MaxSamples = 600;
    private const int MaxPerBoss = 24;
    private const int MaxTotal = 160;
    private const int MaxFileBytes = 2 * 1024 * 1024;
    private const double MinLeadSeconds = 10;
    private const double MaxLeadSeconds = 30;
    private static readonly object Sync = new();
    private readonly Pull _pull;
    private double _lastHpSecond = -100;
    private double _lastHpPercent = double.NaN;
    private bool _finished;

    public static string FilePath => Path.Combine(
        CompanionSettings.SettingsDirectory, "phaseward-learning.json");

    public PhasewardLearningRecorder(int npcTemplateId, DateTimeOffset startedAt)
    {
        _pull = new Pull { NpcTemplateId = npcTemplateId, StartedAtUtc = startedAt.ToUniversalTime() };
    }

    private bool TrySecond(DateTimeOffset when, out double second)
    {
        second = (when - _pull.StartedAtUtc).TotalSeconds;
        return !_finished && _pull.Samples.Count < MaxSamples &&
               double.IsFinite(second) && second is >= 0 and <= 7200;
    }

    public void ObserveSkill(string kind, uint skillId, DateTimeOffset when)
    {
        if (!CompanionSettings.PhasewardLearningEnabled || skillId == 0 ||
            kind is not ("cast" or "hit") || !TrySecond(when, out double t)) return;
        // Same AoE skill seen on several party members is one cue, not 10 hits.
        Sample? previous = _pull.Samples.LastOrDefault(x => x.Kind == kind && x.SkillId == skillId);
        if (previous is not null && t - previous.Second < 0.6) return;
        _pull.Samples.Add(new Sample { Kind = kind, SkillId = skillId, Second = Math.Round(t, 2) });
    }

    public void ObserveHp(uint current, long maximum, DateTimeOffset when)
    {
        if (!CompanionSettings.PhasewardLearningEnabled || maximum <= 0 ||
            current > maximum || !TrySecond(when, out double t)) return;
        double pct = Math.Round(current * 100.0 / maximum, 1);
        if (t - _lastHpSecond < 1 && Math.Abs(pct - _lastHpPercent) < 1) return;
        _lastHpSecond = t; _lastHpPercent = pct;
        _pull.Samples.Add(new Sample { Kind = "hp", Second = Math.Round(t, 2), HpPercent = pct });
    }

    public void Finish(bool completedEncounter = false, string outcome = "unknown")
    {
        if (_finished) return;
        _finished = true;
        if (!CompanionSettings.PhasewardLearningEnabled || _pull.NpcTemplateId <= 0 ||
            _pull.Samples.Count < 2 || _pull.Samples.Last().Second < 5) return;
        lock (Sync)
        {
            try
            {
                Archive data = LoadSafe();
                data.Pulls.Add(_pull);
                // Bound storage to last 24 attempts per boss and 160 globally.
                data.Pulls = data.Pulls
                    .GroupBy(x => x.NpcTemplateId)
                    .SelectMany(x => x.OrderByDescending(y => y.StartedAtUtc).Take(MaxPerBoss))
                    .OrderByDescending(x => x.StartedAtUtc)
                    .Take(MaxTotal)
                    .OrderBy(x => x.StartedAtUtc).ToList();
                Directory.CreateDirectory(CompanionSettings.SettingsDirectory);
                byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(data);
                if (encoded.Length > MaxFileBytes) return;
                string temp = FilePath + ".tmp";
                File.WriteAllBytes(temp, encoded);
                File.Move(temp, FilePath, true);
                // Only an explicitly finalized encounter enters community queue.
                // Manual stop/shutdown uses Finish() (local learning only).
                if (completedEncounter && CompanionSettings.PhasewardCommunityQueueEnabled)
                    PhasewardCommunityOutbox.TryQueue(_pull, DateTimeOffset.UtcNow, outcome,
                        sampleTruncated: _pull.Samples.Count >= MaxSamples);
            }
            catch (IOException) { /* Analysis must not interrupt combat. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Archive LoadSafe()
    {
        try
        {
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length is > 0 and < MaxFileBytes)
            {
                Archive? archive = JsonSerializer.Deserialize<Archive>(File.ReadAllText(FilePath));
                return archive?.Pulls is not null ? archive : new Archive();
            }
        }
        catch (Exception) { /* Corrupted learning cache is noncritical. */ }
        return new Archive();
    }

    // Validated rules are computed from FINISHED previous pulls only, never the current pull.
    public static IReadOnlyList<PhasewardLearningAnalyzer.Rule> ValidatedRules(int npcTemplateId)
    {
        lock (Sync)
            return PhasewardLearningAnalyzer.FindValidatedRules(LoadSafe().Pulls, npcTemplateId);
    }

    public static (int Pulls, int Bosses, int ValidatedRules) Summary()
    {
        lock (Sync)
        {
            Pull[] pulls = LoadSafe().Pulls.Where(p => p is not null && p.NpcTemplateId > 0 &&
                p.Samples is { Count: >= 2 and <= MaxSamples }).ToArray();
            int[] bosses = pulls.Select(p => p.NpcTemplateId).Distinct().ToArray();
            int count = bosses.Sum(id =>
                PhasewardLearningAnalyzer.FindValidatedRules(pulls, id).Count);
            return (pulls.Length, bosses.Length, count);
        }
    }

    // Legacy research candidate browser. Only ValidatedRules may reach live HUD.
    public static IReadOnlyList<Candidate> FindCandidates(int npcTemplateId)
    {
        lock (Sync)
        {
            var pulls = LoadSafe().Pulls.Where(x => x.NpcTemplateId == npcTemplateId)
                .Where(x => x.Samples.Count is >= 2 and <= MaxSamples).ToArray();
            if (pulls.Length < 3) return Array.Empty<Candidate>();
            var matches = new Dictionary<(uint Target, uint Cue, string Kind), List<double>>();
            foreach (var pull in pulls)
            {
                var casts = pull.Samples.Where(x => x.Kind == "cast" && x.SkillId > 0).ToArray();
                foreach (var target in casts.GroupBy(x => x.SkillId).Select(x => x.First()))
                {
                    var before = pull.Samples.Where(x => x.SkillId > 0 && x.Second < target.Second &&
                        target.Second - x.Second is >= MinLeadSeconds and <= MaxLeadSeconds &&
                        (x.Kind == "hit" || x.Kind == "cast"))
                        .GroupBy(x => (x.Kind, x.SkillId))
                        .Select(group => group.OrderBy(x => Math.Abs(target.Second - x.Second - 20)).First());
                    foreach (var cue in before)
                    {
                        if (cue.SkillId == target.SkillId) continue;
                        var key = (target.SkillId, cue.SkillId, cue.Kind);
                        if (!matches.TryGetValue(key, out List<double>? leads))
                            matches[key] = leads = new List<double>();
                        leads.Add(target.Second - cue.Second);
                    }
                }
            }
            return matches.Where(x => x.Value.Count >= 3)
                .Select(x =>
                {
                    var sorted = x.Value.Order().ToArray();
                    return new Candidate(npcTemplateId, x.Key.Target, x.Key.Cue, x.Key.Kind,
                        sorted.Length, sorted[sorted.Length / 2], sorted[^1] - sorted[0]);
                })
                .Where(x => x.SpreadSeconds <= 8)
                .OrderByDescending(x => x.SupportingPulls)
                .ThenBy(x => x.SpreadSeconds)
                .Take(30).ToArray();
        }
    }
}
