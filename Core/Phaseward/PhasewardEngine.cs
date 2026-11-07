using System;
using System.Collections.Generic;
using System.Linq;
using AION_2_Companion.Core.Diagnostics;
using AION_2_Companion.Core;

namespace AION_2_Companion.Core.Phaseward;

public sealed record PhasewardMechanicStatus(string Name, bool VerifiedTrigger,
    double? CastSeconds, double? CooldownSeconds, double? SecondsUntilPredicted,
    DateTimeOffset? LastObserved, bool CooldownElapsed, string Trigger,
    IReadOnlyList<string> BossLines, IReadOnlyList<string> Notes);

public sealed record PhasewardCueStatus(string Name, string Trigger,
    double? HpThreshold, double? SecondsUntilEstimated, bool Reached,
    DateTimeOffset? TriggeredAt, string Detail);

public sealed record PhasewardTimerStatus(string Name, double AtSeconds,
    double SecondsRemaining, bool Elapsed);

public sealed record PhasewardLearnedPrediction(uint TargetSkillId, string Name,
    string Basis, double SecondsRemaining, double WindowSeconds,
    int SupportingPulls, double HoldoutErrorSeconds);

public sealed record PhasewardSnapshot(string State, string BossName, string Instance,
    int BossNpcId, uint BossRuntimeId, DateTimeOffset? EngagedAt,
    IReadOnlyList<PhasewardMechanicStatus> Mechanics, IReadOnlyList<PhasewardCueStatus> Cues,
    IReadOnlyList<PhasewardTimerStatus> Timers, IReadOnlyList<string> CommunityNotes,
    IReadOnlyList<string> FrenzyReferenceTimes, double? BossHpPercent)
{
    public IReadOnlyList<PhasewardLearnedPrediction> LearnedPredictions { get; init; } =
        Array.Empty<PhasewardLearnedPrediction>();
}

public sealed class PhasewardEngine
{
    private readonly PhasewardPack _pack;
    private PhasewardEncounter? _encounter;
    private int _templateId;
    private uint _runtimeId;
    private DateTimeOffset? _engagedAt;
    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _cueTriggeredAt = new(StringComparer.Ordinal);
    private readonly Queue<(uint Current, long Maximum, DateTimeOffset At)> _healthSamples = new();
    private PhasewardLearningRecorder? _learning;
    private IReadOnlyList<PhasewardLearningAnalyzer.Rule> _learnedRules =
        Array.Empty<PhasewardLearningAnalyzer.Rule>();
    private readonly Dictionary<(string Kind, uint SkillId), DateTimeOffset> _liveSignals = new();
    private readonly HashSet<uint> _observedCasts = new();
    public PhasewardEngine(PhasewardPack pack) => _pack = pack;

    public void ObserveBoss(
        int npcTemplateId,
        uint runtimeId,
        DateTimeOffset when,
        string? detectedName = null,
        string? canonicalName = null)
    {
        if (runtimeId == 0) return;
        PhasewardEncounter? match = _pack.Find(npcTemplateId, detectedName, canonicalName);
        if (match is null) return;
        if (_runtimeId == runtimeId && ReferenceEquals(_encounter, match)) return;
        _learning?.Finish();
        PhasewardDetailedTraceRecorder.Cancel(); // different boss / abandoned attempt
        // Load only PREVIOUS completed pulls. The current pull must never train itself.
        _learnedRules = CompanionSettings.PhasewardLearningEnabled
            ? PhasewardLearningRecorder.ValidatedRules(npcTemplateId)
            : Array.Empty<PhasewardLearningAnalyzer.Rule>();
        _learning = CompanionSettings.PhasewardLearningEnabled
            ? new PhasewardLearningRecorder(npcTemplateId, when) : null;
        _liveSignals.Clear();
        _observedCasts.Clear();
        _encounter = match;
        _templateId = npcTemplateId;
        _runtimeId = runtimeId;
        _engagedAt = when;
        PhasewardDetailedTraceRecorder.Begin(npcTemplateId, runtimeId, when);
        _lastSeen.Clear();
        _cueTriggeredAt.Clear();
        _healthSamples.Clear();
        DiagnosticRuntime.Breadcrumb("phaseward_boss_seen");
    }

    // A damage hit is an observable consequence, NOT a proven cast-start signal.
    // Never scan arbitrary payload offsets or infer a skill ID from a name/cooldown.
    public bool ObserveBossDamage(uint actorRuntimeId, uint skillId, DateTimeOffset when)
    {
        if (_encounter is null || _runtimeId == 0 || _runtimeId != actorRuntimeId || skillId == 0)
            return false;
        _learning?.ObserveSkill("hit", skillId, when);
        ObserveLiveSignal("hit", skillId, when);
        bool observed = false;
        foreach (PhasewardMechanic mechanic in _encounter.Mechanics)
        {
            if (!mechanic.SkillIds.Contains(skillId)) continue;
            if (_lastSeen.TryGetValue(mechanic.Id, out var last) &&
                when >= last && (when - last).TotalSeconds < 1.5)
                continue; // one AoE skill can hit several party members
            if (when < last) continue; // stale or replayed packet
            _lastSeen[mechanic.Id] = when;
            observed = true;
            DiagnosticRuntime.Breadcrumb("phaseward_event");
        }
        return observed;
    }

    public bool ObserveBossCast(uint actorRuntimeId, uint skillId, DateTimeOffset when)
    {
        if (_encounter is null || _runtimeId == 0 || _runtimeId != actorRuntimeId || skillId == 0)
            return false;
        PhasewardDetailedTraceRecorder.MarkCast(skillId, when);
        _learning?.ObserveSkill("cast", skillId, when);
        ObserveLiveSignal("cast", skillId, when);
        _observedCasts.Add(skillId);

        bool observed = false;
        foreach (PhasewardMechanic mechanic in _encounter.Mechanics)
        {
            // The final decimal digit is a verified runtime difficulty/variant suffix.
            // Do not broaden this comparison: neighbouring skills can differ by tens.
            if (!mechanic.SkillIds.Any(candidate =>
                    candidate == skillId || candidate >= 10 && candidate / 10 == skillId / 10))
                continue;
            if (_lastSeen.TryGetValue(mechanic.Id, out DateTimeOffset last) &&
                (when < last || (when - last).TotalSeconds < 0.15))
                continue;
            _lastSeen[mechanic.Id] = when;
            observed = true;
            DiagnosticRuntime.Breadcrumb("phaseward_event");
        }
        return observed;
    }

    public void ObserveBossHealth(uint runtimeId, uint current, long maximum, DateTimeOffset when)
    {
        if (_encounter is null || runtimeId == 0 || runtimeId != _runtimeId ||
            maximum <= 0 || current > maximum)
            return;

        PhasewardDetailedTraceRecorder.MarkBossHp(current, maximum, when);
        _learning?.ObserveHp(current, maximum, when);
        double? previousPercent = CurrentHpPercent();
        double currentPercent = Math.Clamp(current * 100.0 / maximum, 0, 100);
        if (previousPercent is double previous)
        {
            foreach (PhasewardCue cue in _encounter.Cues)
            foreach (double threshold in cue.HpThresholds)
            {
                if (previous > threshold && currentPercent <= threshold)
                    _cueTriggeredAt[$"{cue.Id}:{threshold:0.###}"] = when;
            }
        }

        _healthSamples.Enqueue((current, maximum, when));
        while (_healthSamples.Count > 20 ||
               _healthSamples.Count > 1 &&
               (when - _healthSamples.Peek().At).TotalSeconds > 20)
        {
            _healthSamples.Dequeue();
        }
    }

    public void EndBoss(uint runtimeId)
    {
        if (_runtimeId == runtimeId)
        {
            DiagnosticRuntime.Breadcrumb("phaseward_end");
            Reset(communityPullCompleted: true, outcome: "killed");
        }
    }

    public void Reset(bool communityPullCompleted = false, string outcome = "unknown")
    {
        if (communityPullCompleted)
            PhasewardDetailedTraceRecorder.Finish(outcome);
        else PhasewardDetailedTraceRecorder.Cancel();
        _learning?.Finish(communityPullCompleted, outcome);
        _learning = null;
        _learnedRules = Array.Empty<PhasewardLearningAnalyzer.Rule>();
        _liveSignals.Clear();
        _observedCasts.Clear();
        _encounter = null;
        _templateId = 0;
        _runtimeId = 0;
        _engagedAt = null;
        _lastSeen.Clear();
        _cueTriggeredAt.Clear();
        _healthSamples.Clear();
    }

    public PhasewardSnapshot Snapshot(DateTimeOffset now)
    {
        if (_encounter is null)
            return new PhasewardSnapshot("waiting", "", "", 0, 0, null, [], [], [], [], [], null);
        var statuses = _encounter.Mechanics.Select(mechanic =>
        {
            DateTimeOffset? seen = _lastSeen.TryGetValue(mechanic.Id, out var at) ? at : null;
            double? remaining = seen is not null && mechanic.CooldownSeconds is > 0
                ? (seen.Value.AddSeconds(mechanic.CooldownSeconds.Value) - now).TotalSeconds
                : null;
            bool elapsed = remaining is <= 0;
            double? countdown = remaining is > 0 ? remaining : null;
            string trigger = mechanic.SkillIds.Length > 0 ? "cast"
                : mechanic.HpThresholds.Length > 0 ? "hp"
                : mechanic.BossLines.Length > 0 ? "speech"
                : "guide";
            return new PhasewardMechanicStatus(mechanic.Name, mechanic.SkillIds.Length > 0,
                mechanic.CastSeconds, mechanic.CooldownSeconds, countdown, seen, elapsed,
                trigger, mechanic.BossLines, mechanic.Notes);
        }).ToArray();

        double? hpPercent = CurrentHpPercent();
        double? hpPerSecond = EstimatedHpLossPerSecond();
        var cues = _encounter.Cues.SelectMany(cue => cue.HpThresholds.Length > 0
                ? cue.HpThresholds.Select(threshold => BuildHpCue(cue, threshold, hpPercent, hpPerSecond))
                : [new PhasewardCueStatus(cue.Name, "speech", null, null, false, null,
                    cue.BossLines.FirstOrDefault() ?? cue.Details.FirstOrDefault() ?? string.Empty)])
            .ToArray();
        var timers = BuildTimers(now);
        return new PhasewardSnapshot("encounter", _encounter.Name, _encounter.Instance,
            _templateId, _runtimeId, _engagedAt, statuses, cues, timers,
            _encounter.CommunityNotes, _encounter.FrenzyReferenceTimes, hpPercent)
        {
            LearnedPredictions = BuildLearnedPredictions(now, hpPercent)
        };
    }

    // This is only the first occurrence of a raw cue per pull; later AoE packets
    // cannot move an already-displayed forecast forward. All events are boss-scoped.
    private void ObserveLiveSignal(string kind, uint skillId, DateTimeOffset when)
    {
        if (_engagedAt is null || when < _engagedAt.Value ||
            (when - _engagedAt.Value).TotalSeconds > 7200) return;
        var key = (kind, skillId);
        if (!_liveSignals.TryGetValue(key, out DateTimeOffset prior) || when < prior)
            _liveSignals[key] = when;
    }

    private IReadOnlyList<PhasewardLearnedPrediction> BuildLearnedPredictions(
        DateTimeOffset now, double? hpPercent)
    {
        if (_engagedAt is null || _encounter is null ||
            !CompanionSettings.PhasewardLearningEnabled || _learnedRules.Count == 0)
            return Array.Empty<PhasewardLearnedPrediction>();

        var choices = new List<(int Priority, PhasewardLearnedPrediction Prediction)>();
        foreach (var rule in _learnedRules)
        {
            if (_observedCasts.Contains(rule.TargetSkillId)) continue;
            double? secondsRemaining = null;
            double window = 30;
            string basis;
            int priority;
            switch (rule.Kind)
            {
                case PhasewardLearningAnalyzer.RuleKind.Precursor:
                    if (!_liveSignals.TryGetValue((rule.CueKind, rule.CueSkillId),
                        out DateTimeOffset cueAt)) continue;
                    secondsRemaining = (cueAt.AddSeconds(rule.Value) - now).TotalSeconds;
                    basis = $"{rule.CueKind}:{rule.CueSkillId}";
                    priority = 0;
                    break;
                case PhasewardLearningAnalyzer.RuleKind.BossHp:
                    if (hpPercent is not double hp || hp <= rule.Value ||
                        _healthSamples.Count < 2 ||
                        (now - _healthSamples.Last().At).TotalSeconds > 3) continue;
                    double? hpLoss = EstimatedHpLossPerSecond();
                    if (hpLoss is not > 0) continue;
                    var newest = _healthSamples.Last();
                    secondsRemaining = newest.Maximum * (hp - rule.Value) / 100.0 / hpLoss.Value;
                    basis = $"HP ~{rule.Value:0.#}%";
                    priority = 1;
                    break;
                case PhasewardLearningAnalyzer.RuleKind.PullTime:
                    secondsRemaining = (_engagedAt.Value.AddSeconds(rule.Value) - now).TotalSeconds;
                    basis = "pull time";
                    priority = 2;
                    window = Math.Max(30, rule.Value);
                    break;
                default:
                    continue;
            }
            if (secondsRemaining is not double remaining || !double.IsFinite(remaining) ||
                remaining is <= 0 or > 90) continue;
            string skillName = _encounter.Mechanics
                .FirstOrDefault(m => m.SkillIds.Any(x => x == rule.TargetSkillId ||
                    x >= 10 && x / 10 == rule.TargetSkillId / 10))?.Name
                ?? $"Skill {rule.TargetSkillId}";
            choices.Add((priority, new PhasewardLearnedPrediction(rule.TargetSkillId,
                skillName, basis, remaining, window, rule.SupportingPulls,
                rule.MaximumHoldoutError)));
        }
        return choices.GroupBy(x => x.Prediction.TargetSkillId)
            .Select(g => g.OrderBy(x => x.Priority).ThenBy(x => x.Prediction.SecondsRemaining)
                .First().Prediction)
            .OrderBy(x => x.SecondsRemaining).Take(6).ToArray();
    }

    private PhasewardCueStatus BuildHpCue(PhasewardCue cue, double threshold,
        double? hpPercent, double? hpPerSecond)
    {
        bool reached = hpPercent is not null && hpPercent <= threshold;
        double? seconds = null;
        if (!reached && hpPercent is double current && hpPerSecond is > 0 &&
            _healthSamples.Count > 0)
        {
            var latest = _healthSamples.Last();
            double hpToThreshold = latest.Maximum * (current - threshold) / 100.0;
            seconds = Math.Max(0, hpToThreshold / hpPerSecond.Value);
        }
        string detail = cue.Details.FirstOrDefault() ?? string.Empty;
        _cueTriggeredAt.TryGetValue($"{cue.Id}:{threshold:0.###}", out DateTimeOffset triggeredAt);
        return new PhasewardCueStatus(cue.Name, "hp", threshold, seconds, reached,
            triggeredAt == default ? null : triggeredAt, detail);
    }

    private double? CurrentHpPercent()
    {
        if (_healthSamples.Count == 0)
            return null;
        var latest = _healthSamples.Last();
        if (latest.Maximum <= 0) return null;
        return Math.Clamp(latest.Current * 100.0 / latest.Maximum, 0, 100);
    }

    private double? EstimatedHpLossPerSecond()
    {
        if (_healthSamples.Count < 2) return null;
        var samples = _healthSamples.ToArray();
        var first = samples[0];
        var last = samples[^1];
        double seconds = (last.At - first.At).TotalSeconds;
        long loss = (long)first.Current - last.Current;
        return seconds >= 2 && loss > 0 ? loss / seconds : null;
    }

    private PhasewardTimerStatus[] BuildTimers(DateTimeOffset now)
    {
        if (_engagedAt is null || _encounter is null) return [];
        double elapsed = Math.Max(0, (now - _engagedAt.Value).TotalSeconds);
        return _encounter.FrenzyReferenceTimes
            .Select(ParseTimerSeconds)
            .Where(seconds => seconds is > 0)
            .Select(seconds => new PhasewardTimerStatus("Frenzy", seconds!.Value,
                Math.Max(0, seconds.Value - elapsed), elapsed >= seconds.Value))
            .OrderBy(timer => timer.AtSeconds)
            .ToArray();
    }

    private static double? ParseTimerSeconds(string value)
    {
        string[] parts = value.Trim().Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) &&
            int.TryParse(parts[1], out int seconds) && minutes >= 0 && seconds is >= 0 and < 60)
            return minutes * 60 + seconds;
        return double.TryParse(value, out double raw) && raw > 0 ? raw : null;
    }
}

// Shared only between the capture VM and presentation. All mutations occur on Avalonia's UI thread.
public static class PhasewardHub
{
    public static PhasewardEngine? Current { get; private set; }
    public static string LoadStatus { get; private set; } = "Phaseward data pack not loaded";
    public static void SetEngine(PhasewardEngine? engine, string status)
    {
        Current = engine;
        LoadStatus = status;
    }
    public static PhasewardSnapshot Snapshot() => Current?.Snapshot(DateTimeOffset.UtcNow)
        ?? new PhasewardSnapshot("unavailable", "", "", 0, 0, null, [], [], [], [], [], null);
}
