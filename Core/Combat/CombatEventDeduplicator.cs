using System;
using System.Collections.Generic;
using System.Linq;
using AION_2_Companion.Core.Parsers;

namespace AION_2_Companion.Core.Combat;

public sealed class CombatEventDeduplicator
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(150);
    private const int MaxEntries = 4096;

    private readonly Dictionary<Fingerprint, DateTimeOffset> _recent = [];

    public bool ShouldAccept(DamageHit hit, DateTimeOffset capturedAt)
    {
        var fingerprint = new Fingerprint(
            hit.Target,
            hit.Actor,
            hit.Skill,
            hit.Damage,
            hit.Type,
            hit.Switch,
            hit.ActorHitCount,
            hit.Sequence,
            hit.Scalar,
            hit.TrailerSeq,
            hit.IsDot);

        if (_recent.TryGetValue(fingerprint, out DateTimeOffset previous) &&
            capturedAt >= previous &&
            capturedAt - previous <= DuplicateWindow)
        {
            return false;
        }

        _recent[fingerprint] = capturedAt;
        if (_recent.Count > MaxEntries)
            Trim(capturedAt);

        return true;
    }

    public void Clear() => _recent.Clear();

    private void Trim(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - TimeSpan.FromSeconds(2);
        foreach ((Fingerprint key, DateTimeOffset time) in _recent.ToArray())
        {
            if (time < cutoff)
                _recent.Remove(key);
        }

        if (_recent.Count <= MaxEntries)
            return;

        _recent.Clear();
    }

    private readonly record struct Fingerprint(
        uint Target,
        uint Actor,
        uint Skill,
        uint Amount,
        uint Type,
        uint Switch,
        byte ActorHitCount,
        uint Sequence,
        uint Scalar,
        byte TrailerSequence,
        bool IsDot);
}
