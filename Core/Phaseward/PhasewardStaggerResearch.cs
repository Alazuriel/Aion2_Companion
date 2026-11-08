using System;
using System.Buffers.Binary;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Phaseward;

// Observational, boss-scoped research only. This monitor NEVER adjusts skill timers,
// submits packets, or trains the Phaseward prediction engine.
public sealed record PhasewardStaggerResearchState(
    bool Active,
    bool RecentlyEnded,
    double? SecondsRemaining,
    int? GaugeCurrent,
    int? GaugeMaximum,
    byte? GaugeState,
    string EndEvidence,
    bool Estimated);

public static class PhasewardStaggerResearch
{
    private const ushort GaugeOpcode = 0xE005; // wire: 05 E0
    private const ushort ControlOpcode = 0x364B; // wire: 4B 36
    private const double EstimatedSeconds = 5.0;

    private static uint _boss;
    private static DateTimeOffset? _lastGaugeAt;
    private static int? _gauge;
    private static int? _gaugeMax;
    private static byte? _gaugeState;
    private static DateTimeOffset? _started;
    private static DateTimeOffset? _ended;
    private static string _endEvidence = string.Empty;

    // Called only from ProcessDecodedFrameOnUi, like the Phaseward engine.
    public static void Observe(WireFrame frame, bool developerMode)
    {
        if (frame.Opcode.Value != GaugeOpcode && frame.Opcode.Value != ControlOpcode)
            return;
        if ((frame.Flags & (WireFrameFlags.FromClient | WireFrameFlags.Resynced)) != 0)
            return;
        PhasewardSnapshot boss = PhasewardHub.Snapshot();
        if (boss.State != "encounter" || boss.BossRuntimeId == 0 ||
            boss.EngagedAt is not DateTimeOffset engaged ||
            frame.Time < engaged.AddSeconds(-2) ||
            (frame.Time - engaged).TotalHours > 2)
            return;
        if (!TryReadActor(frame.Payload, out uint actor, out int offset) ||
            actor != boss.BossRuntimeId)
            return;
        if (_boss != actor) Reset(actor);
        ReadOnlySpan<byte> body = frame.Payload.AsSpan(offset);
        if (frame.Opcode.Value == GaugeOpcode)
            ReadGauge(body, frame.Time, developerMode);
        else
            ReadControl(body, frame.Time, developerMode);
    }

    public static PhasewardStaggerResearchState Snapshot(uint activeBoss, DateTimeOffset now)
    {
        if (activeBoss == 0 || activeBoss != _boss)
            return new(false, false, null, null, null, null, "", true);

        bool gaugeFresh = _lastGaugeAt is DateTimeOffset gaugeAt &&
                          now >= gaugeAt && (now - gaugeAt).TotalSeconds <= 3;
        int? current = gaugeFresh ? _gauge : null;
        int? maximum = gaugeFresh ? _gaugeMax : null;
        byte? state = gaugeFresh ? _gaugeState : null;

        if (_started is not DateTimeOffset began || now < began)
            return new(false, false, null, current, maximum, state, "", true);

        bool ended = _ended is DateTimeOffset endedAt && endedAt >= began;
        double age = (now - began).TotalSeconds;
        bool active = !ended && age >= 0 && age < EstimatedSeconds;
        // An expired estimate is not a proven end: keep that distinction visible.
        bool justEnded = ended
            ? now >= _ended!.Value && (now - _ended.Value).TotalSeconds <= 2.5
            : age >= EstimatedSeconds && age <= EstimatedSeconds + 2.5;
        string evidence = ended ? _endEvidence : "5s estimate elapsed; end not verified";
        return new(active, justEnded, active ? Math.Max(0, EstimatedSeconds - age) : null,
            current, maximum, state, evidence, true);
    }

    private static void ReadGauge(ReadOnlySpan<byte> body, DateTimeOffset when, bool developerMode)
    {
        // Known 14-byte packet with three-byte actor VarUInt:
        // 03 01 | [maximum uint32 LE] | [current uint32 LE] | [state 02/03]
        if (body.Length != 11 || body[0] != 0x03 || body[1] != 0x01 ||
            body[10] is not (0x02 or 0x03))
        {
            // A shorter 6-byte packet with the same actor may mark the transition.
            if (body.Length == 3 && body[0] == 0x00 && body[1] == 0x03 &&
                body[2] == 0x02 && _lastGaugeAt is DateTimeOffset last &&
                when >= last && (when - last).TotalSeconds <= 2 &&
                _gauge is int gauge && _gaugeMax is int gaugeMax && gaugeMax > 0 &&
                gauge * 100 <= gaugeMax * 3 && _gaugeState == 0x02)
                Start(when, developerMode, "E005 depleted-gauge transition");
            return;
        }

        uint max = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(2, 4));
        uint current = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(6, 4));
        if (max is < 1 or > 100000 || current > max)
            return;
        byte oldState = _gaugeState ?? 0;
        int? oldGauge = _gauge;
        _lastGaugeAt = when;
        _gaugeMax = (int)max;
        _gauge = (int)current;
        _gaugeState = body[10];

        if (developerMode)
            DebugConsole.Log("phaseward", $"[STAGGER GAUGE] boss={_boss} {current}/{max} " +
                $"({current * 100.0 / max:0.0}%) state={body[10]:X2}");

        if (body[10] == 0x03 && oldState != 0x03)
        {
            // A state 03 while damage continues can be a LOCKED gauge,
            // including at 83%-97%. Do not count down from state 03 alone.
            // Wait for the short transition after a genuinely depleted gauge.
            if (developerMode)
                DebugConsole.Log("phaseward", $"[STAGGER LOCKED/STATE03] boss={_boss} " +
                    $"gauge={current}/{max}; waiting for depleted transition");
        }
        else if (current == max && oldGauge is int previous && previous < max &&
                 _started is DateTimeOffset began && when >= began &&
                 (when - began).TotalSeconds is >= 3 and <= 15)
            End(when, developerMode, "E005 gauge reset to maximum");
    }

    private static void ReadControl(ReadOnlySpan<byte> body, DateTimeOffset when, bool developerMode)
    {
        if (body.Length != 7 || body[0] != 0x01 || body[1] != 0xCD ||
            body[2] != 0x00 || BinaryPrimitives.ReadInt32LittleEndian(body.Slice(3, 4)) != 0 ||
            _started is not DateTimeOffset began || when < began ||
            (when - began).TotalSeconds is < 3 or > 8)
            return;
        End(when, developerMode, "364B control value zero");
    }

    private static void Start(DateTimeOffset when, bool developerMode, string evidence)
    {
        if (_started is DateTimeOffset previous && when >= previous &&
            (when - previous).TotalSeconds < 8)
            return; // No duplicate countdown from the same transition.
        _started = when;
        _ended = null;
        _endEvidence = string.Empty;
        if (developerMode)
            DebugConsole.Log("phaseward", $"[STAGGER START?] boss={_boss} " +
                $"{evidence}, estimated 5s countdown; no timer shift");
    }

    private static void End(DateTimeOffset when, bool developerMode, string evidence)
    {
        if (_started is not DateTimeOffset began || _ended is not null || when < began)
            return;
        _ended = when;
        _endEvidence = evidence;
        if (developerMode)
            DebugConsole.Log("phaseward", $"[STAGGER END?] boss={_boss} " +
                $"{evidence}, duration={(when - began).TotalSeconds:0.000}s");
    }

    private static void Reset(uint boss)
    {
        _boss = boss;
        _lastGaugeAt = null;
        _gauge = null;
        _gaugeMax = null;
        _gaugeState = null;
        _started = null;
        _ended = null;
        _endEvidence = string.Empty;
    }

    private static bool TryReadActor(ReadOnlySpan<byte> data, out uint actor, out int position)
    {
        actor = 0;
        position = 0;
        for (int i = 0; i < 5 && position < data.Length; i++)
        {
            byte current = data[position++];
            if (i == 4 && (current & 0xF0) != 0) return false;
            actor |= (uint)(current & 0x7F) << (7 * i);
            if ((current & 0x80) == 0) return actor != 0;
        }
        return false;
    }
}
