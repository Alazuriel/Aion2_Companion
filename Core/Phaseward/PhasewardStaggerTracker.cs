using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Phaseward;

public sealed record PhasewardStaggerStatus(
    bool Active,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastEndedAt,
    double ObservedSeconds,
    int CompletedIntervals,
    bool EvidenceExpired)
{
    public int SignalCount { get; init; }
    public bool EstimatedEndFromOpcode { get; init; }
    public double? EstimatedLastDurationSeconds { get; init; }
    public bool EstimatedFromOpcode { get; init; }
    public double AppliedDelaySeconds { get; init; }
    public double? ActiveRemainingSeconds { get; init; }
    public double? NextEstimatedSeconds { get; init; }
    public double? EstimatedCycleSeconds { get; init; }
    public static PhasewardStaggerStatus Unknown { get; } = new(
        false, null, null, 0, 0, false);
}

/// <summary>
/// Per-encounter control-state history. Explicitly decoded boss states and
/// separately labelled NFG opcode estimates are kept distinct.
/// Skill inactivity and elapsed time alone are not evidence.
/// </summary>
public sealed class PhasewardStaggerTracker
{
    private static readonly TimeSpan MaximumEvidenceAge = TimeSpan.FromSeconds(20);
    private const int MaximumStoredIntervals = 32;
    private readonly List<(DateTimeOffset Started, DateTimeOffset Ended)> _intervals = new();
    private DateTimeOffset? _started;
    private DateTimeOffset? _ended;
    private DateTimeOffset? _estimatedEnded;
    private readonly List<DateTimeOffset> _opcodeSignals = new();
    private const double AssumedPauseSeconds = 5.0;
    private static readonly TimeSpan DuplicateSignalWindow = TimeSpan.FromSeconds(8);

    public void Reset()
    {
        _intervals.Clear();
        _started = null;
        _ended = null;
        _estimatedEnded = null;
        _opcodeSignals.Clear();
    }

    // NFG-mapped opcode only: the packet actor/state layout is not yet validated.
    // This is an explicitly estimated encounter-scoped signal, not a verified stun.
    public bool ObserveEstimatedOpcode(DateTimeOffset when)
    {
        if (_opcodeSignals.Count > 0)
        {
            DateTimeOffset last = _opcodeSignals[^1];
            if (when <= last || when - last < DuplicateSignalWindow)
                return false;
        }
        if (_opcodeSignals.Count >= MaximumStoredIntervals)
            _opcodeSignals.RemoveAt(0);
        _opcodeSignals.Add(when);
        return true;
    }

    // Provisional closure from a boss-matched 364B zero event. Never
    // introduce an additional correction or treat missing evidence as an end.
    public bool ObserveEstimatedEnd(DateTimeOffset when)
    {
        if (_opcodeSignals.Count == 0) return false;
        DateTimeOffset start = _opcodeSignals[^1];
        double elapsed = (when - start).TotalSeconds;
        if (elapsed is < 3.0 or > 8.0 ||
            (_estimatedEnded is DateTimeOffset previous && previous >= start))
            return false;
        _estimatedEnded = when;
        return true;
    }

    // Five seconds are applied per unique Stagger signal (temporary MVP policy).
    // Return a per-pull bounded estimated adjustment; never adjust HP or hard enrage.
    public double AppliedDelaySeconds =>
        Math.Min(120, AssumedPauseSeconds *
            (_opcodeSignals.Count + _intervals.Count + (_started.HasValue ? 1 : 0)));

    public double DelaySince(DateTimeOffset lastMechanic, DateTimeOffset now)
    {
        int count = 0;
        foreach (DateTimeOffset signal in _opcodeSignals)
            if (signal >= lastMechanic && signal <= now) count++;
        foreach (var interval in _intervals)
            if (interval.Started >= lastMechanic && interval.Started <= now) count++;
        if (_started is DateTimeOffset started &&
            started >= lastMechanic && started <= now)
            count++;
        return Math.Min(120, AssumedPauseSeconds * count);
    }

    public bool Observe(bool active, DateTimeOffset when)
    {
        if (active)
        {
            if (_started is DateTimeOffset existing &&
                when >= existing && when - existing <= MaximumEvidenceAge)
                return false;
            if (_ended is DateTimeOffset lastEnd && when <= lastEnd)
                return false;
            _started = when;
            return true;
        }

        if (_started is not DateTimeOffset start || when <= start)
            return false;
        _started = null;
        _ended = when;
        if (when - start > MaximumEvidenceAge)
            return false; // truncated/uncertain; never assume its full duration

        if (_intervals.Count == MaximumStoredIntervals)
            _intervals.RemoveAt(0);
        _intervals.Add((start, when));
        return true;
    }

    public PhasewardStaggerStatus Snapshot(DateTimeOffset now)
    {
        double observed = 0;
        foreach (var interval in _intervals)
            observed += (interval.Ended - interval.Started).TotalSeconds;

        DateTimeOffset? start = _started;
        bool expired = start.HasValue && (now < start.Value ||
            now - start.Value > MaximumEvidenceAge);
        if (start.HasValue && !expired)
            observed += Math.Max(0, (now - start.Value).TotalSeconds);

        bool estimated = _opcodeSignals.Count > 0;
        DateTimeOffset? opcodeStart = estimated ? _opcodeSignals[^1] : null;
        double? remaining = opcodeStart is DateTimeOffset signal
            ? (signal.AddSeconds(AssumedPauseSeconds) - now).TotalSeconds
            : null;
        bool estimatedEnded = opcodeStart is DateTimeOffset recent &&
            _estimatedEnded is DateTimeOffset end && end >= recent;
        bool opcodeActive = remaining is > 0 and <= AssumedPauseSeconds && !estimatedEnded;
        bool verifiedActive = start.HasValue && !expired;
        bool active = verifiedActive || opcodeActive;

        // Prediction requires at least three signals (two real measured intervals).
        // Wide variance means no prediction is displayed.
        double? next = null;
        double? cycle = null;
        if (_opcodeSignals.Count >= 3)
        {
            var intervals = new List<double>();
            int first = Math.Max(1, _opcodeSignals.Count - 5);
            for (int i = first; i < _opcodeSignals.Count; i++)
            {
                double gap = (_opcodeSignals[i] - _opcodeSignals[i - 1]).TotalSeconds;
                if (gap is >= 12 and <= 900) intervals.Add(gap);
            }
            if (intervals.Count >= 2)
            {
                double mean = 0;
                foreach (double gap in intervals) mean += gap;
                mean /= intervals.Count;
                double maximumDeviation = 0;
                foreach (double gap in intervals)
                    maximumDeviation = Math.Max(maximumDeviation, Math.Abs(gap - mean));
                if (maximumDeviation <= mean * 0.35)
                {
                    double estimatedNext = (_opcodeSignals[^1].AddSeconds(mean) - now)
                        .TotalSeconds;
                    if (estimatedNext is > 0 and <= 900)
                    {
                        next = estimatedNext;
                        cycle = mean;
                    }
                }
            }
        }

        return new PhasewardStaggerStatus(
            active,
            verifiedActive ? start : opcodeActive ? opcodeStart : null,
            _estimatedEnded is DateTimeOffset provisionalEnd &&
                (_ended is null || provisionalEnd > _ended.Value)
                ? provisionalEnd : _ended,
            observed, _intervals.Count, expired)
        {
            SignalCount = _opcodeSignals.Count,
            EstimatedFromOpcode = opcodeActive,
            EstimatedEndFromOpcode = estimatedEnded,
            EstimatedLastDurationSeconds = estimatedEnded && opcodeStart is DateTimeOffset from &&
                _estimatedEnded is DateTimeOffset until
                ? (until - from).TotalSeconds : null,
            ActiveRemainingSeconds = opcodeActive ? Math.Max(0, remaining!.Value) : null,
            AppliedDelaySeconds = AppliedDelaySeconds,
            NextEstimatedSeconds = next,
            EstimatedCycleSeconds = cycle
        };
    }
}
