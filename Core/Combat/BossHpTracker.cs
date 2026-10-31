using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Combat;

public readonly record struct BossHpSample(
    uint RawValue,
    long ReceivedAt);

public sealed class BossHpTracker
{
    private readonly Dictionary<uint, BossHpSample> _samples = [];

    public bool TryUpdate(
        uint target,
        uint rawValue,
        long receivedAt,
        long? verifiedMaximumHp,
        out BossHpSample sample)
    {
        sample = default;
        if (target == 0 || receivedAt <= 0)
            return false;

        if (verifiedMaximumHp is > 0 && rawValue > verifiedMaximumHp.Value)
            return false;

        if (_samples.TryGetValue(target, out BossHpSample previous) &&
            receivedAt < previous.ReceivedAt)
            return false;

        sample = new BossHpSample(rawValue, receivedAt);
        _samples[target] = sample;
        return true;
    }

    public bool TryGet(uint target, out BossHpSample sample) =>
        _samples.TryGetValue(target, out sample);

    public void Remove(uint target) => _samples.Remove(target);
}
