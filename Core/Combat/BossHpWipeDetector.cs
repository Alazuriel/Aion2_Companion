using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Combat;

// Conservative HP reset heuristic: two separate high HP samples after a sudden
// jump from a substantially damaged boss. Does not infer a wipe from one packet.
public enum BossHpResetStatus { None, Candidate, Confirmed }

public sealed class BossHpWipeDetector
{
    private sealed record Sample(uint Hp, long Maximum, DateTimeOffset At);
    private sealed record Pending(long Maximum, DateTimeOffset FirstHighAt);
    private readonly Dictionary<uint, Sample> _last = new();
    private readonly Dictionary<uint, Pending> _pending = new();

    public void Reset()
    {
        _last.Clear();
        _pending.Clear();
    }

    public BossHpResetStatus Observe(uint runtimeId, uint hp, long maximum,
        DateTimeOffset when, bool eligibleFight)
    {
        if (!eligibleFight || runtimeId == 0 || maximum <= 0 || hp > maximum)
            return BossHpResetStatus.None;

        if (_last.TryGetValue(runtimeId, out Sample? prior) && when <= prior.At)
            return BossHpResetStatus.None;

        _last.TryGetValue(runtimeId, out Sample? previous);
        _last[runtimeId] = new Sample(hp, maximum, when);

        if (_pending.TryGetValue(runtimeId, out Pending? pending))
        {
            double sinceHigh = (when - pending.FirstHighAt).TotalSeconds;
            _pending.Remove(runtimeId);
            if (pending.Maximum == maximum && sinceHigh >= 0.05 && sinceHigh <= 2.5 &&
                hp >= maximum * .98 && previous is not null &&
                previous.Maximum == maximum && previous.Hp >= maximum * .98)
                return BossHpResetStatus.Confirmed;
        }

        // Never infer from the spawn's first full-HP packet, HP=0/death, an
        // unknown max, a small heal, or a large gap in capture evidence.
        if (previous is null || previous.Maximum != maximum || previous.Hp == 0 ||
            hp < maximum * .99 || previous.Hp > maximum * .90 ||
            hp - (long)previous.Hp < maximum * .15 ||
            (when - previous.At).TotalSeconds > 2.5)
            return BossHpResetStatus.None;

        _pending[runtimeId] = new Pending(maximum, when);
        return BossHpResetStatus.Candidate;
    }
}
