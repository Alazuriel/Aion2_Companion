using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// Examines a single, explicitly verified packet layout. Without a verified
/// profile, candidate packets are diagnostics only and cannot affect predictions.
/// </summary>
public static class PhasewardStaggerProtocol
{
    // External protocol research lists this as a GROGGY_OR_GUARD candidate.
    // Its semantic fields are not established for the current game client.
    public const ushort ResearchCandidateOpcode = 0xE005;
    // Candidate control-value notification; only a boss-scoped zero after
    // an E005 start candidate may close an estimated interval.
    private const ushort ResearchEndCandidateOpcode = 0x364B;

    private sealed record Rule(ushort Opcode, int ActorOffset,
        bool ActorIsVarUInt, int StateOffset, byte StartValue, byte EndValue);

    private static Rule? _rule;
    private static long _lastDiagnostic;

    public static void Load()
    {
        _rule = null;
        ClearCandidateSeries();
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nexforge", "AION 2 Companion", "phaseward-stagger-profile.json");
        if (!File.Exists(path)) return;

        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > 16384)
                throw new InvalidDataException("Invalid profile length");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1 ||
                root.GetProperty("verified_by_capture").GetBoolean() != true)
                throw new InvalidDataException("Verified profile required");

            string opcodeText = root.GetProperty("opcode").GetString() ?? "";
            if (!opcodeText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !ushort.TryParse(opcodeText.AsSpan(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ushort opcode) || opcode == 0)
                throw new InvalidDataException("Invalid opcode");

            int actorOffset = root.GetProperty("actor_offset").GetInt32();
            int stateOffset = root.GetProperty("state_offset").GetInt32();
            string actorFormat = root.GetProperty("actor_format").GetString() ?? "";
            int start = root.GetProperty("start_value").GetInt32();
            int end = root.GetProperty("end_value").GetInt32();
            if (actorOffset is < 0 or > 255 || stateOffset is < 0 or > 255 ||
                actorFormat is not ("uvarint" or "u32le") ||
                start is < 0 or > 255 || end is < 0 or > 255 || start == end ||
                actorOffset == stateOffset)
                throw new InvalidDataException("Invalid field layout");

            _rule = new Rule(opcode, actorOffset, actorFormat == "uvarint",
                stateOffset, (byte)start, (byte)end);
            DebugConsole.Log("phaseward", "Verified Stagger decoder profile loaded");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                    JsonException or InvalidDataException or
                                    KeyNotFoundException or FormatException or InvalidOperationException)
        {
            DebugConsole.Log("phaseward", $"Stagger profile rejected: {ex.Message}");
        }
    }

    public static bool ShouldProcess(Opcode opcode, bool developerMode) =>
        opcode == OpcodeId.Stagger || _rule?.Opcode == opcode.Value ||
        (_rule is null && opcode.Value == ResearchEndCandidateOpcode) ||
        developerMode && opcode.Value == ResearchCandidateOpcode;

    /// <summary>Called on the existing serialized UI packet processing path.</summary>
    public static void Observe(WireFrame frame, PhasewardEngine? engine, bool developerMode)
    {
        if (engine is null || engine.CurrentBossRuntimeId == 0 ||
            (frame.Flags & WireFrameFlags.FromClient) != 0 ||
            (frame.Flags & WireFrameFlags.Resynced) != 0)
            return;

        if (_rule is null && frame.Opcode.Value == ResearchEndCandidateOpcode)
        {
            ObserveCandidateEnd(frame, engine, developerMode);
            return;
        }

        if (_rule is not Rule rule || frame.Opcode.Value != rule.Opcode)
        {
            if (_rule is null && frame.Opcode.Value == ResearchCandidateOpcode)
            {
                ObserveCandidateSeries(frame, engine, developerMode);
            }
            else if (developerMode && frame.Opcode.Value == ResearchCandidateOpcode)
                LogCandidate(frame);
            return;
        }

        if (!TryReadActor(frame.Payload, rule, out uint actor) ||
            actor != engine.CurrentBossRuntimeId ||
            rule.StateOffset >= frame.Payload.Length)
            return;

        byte state = frame.Payload[rule.StateOffset];
        if (state != rule.StartValue && state != rule.EndValue)
            return;

        bool active = state == rule.StartValue;
        if (engine.ObserveBossStagger(actor, active, frame.Time) && developerMode)
            DebugConsole.Log("phaseward", active
                ? $"Verified Stagger START, boss={actor}, opcode=0x{rule.Opcode:X4}"
                : $"Verified Stagger END, boss={actor}, opcode=0x{rule.Opcode:X4}");
    }

    // E005 capture pattern seen in a single pull: entity varuint, followed by
    // eleven-byte decreasing gauge updates, then a three-byte transition marker.
    // Field meanings remain tentative. The gauge itself must NEVER shift timers.
    private static uint _candidateBoss;
    private static DateTimeOffset _candidateFirstAt;
    private static DateTimeOffset _candidateLastAt;
    private static DateTimeOffset _candidateReportedAt;
    private static int _candidateLastValue;
    private static int _candidateSamples;
    private static bool _candidateTransitionReported;
    private static bool _candidateEndReported;

    private static void ClearCandidateSeries()
    {
        _candidateBoss = 0;
        _candidateFirstAt = default;
        _candidateLastAt = default;
        _candidateReportedAt = default;
        _candidateLastValue = 0;
        _candidateSamples = 0;
        _candidateTransitionReported = false;
        _candidateEndReported = false;
    }

    private static void ObserveCandidateSeries(
        WireFrame frame, PhasewardEngine engine, bool developerMode)
    {
        ReadOnlySpan<byte> packet = frame.Payload;
        if (!TryReadCandidateActor(packet, out uint actor, out int bodyOffset) ||
            actor != engine.CurrentBossRuntimeId)
            return;

        ReadOnlySpan<byte> body = packet.Slice(bodyOffset);
        // Both observed wire shapes are boss-scoped. The 11-byte body is a
        // guard/groggy gauge; its final byte changed 02 -> 03 in the later pull.
        bool gauge = body.Length == 11 &&
            body[0] == 0x03 && body[1] == 0x01 &&
            body[2] == 0xB0 && body[3] == 0x04 &&
            body[4] == 0 && body[5] == 0 &&
            (body[10] == 0x02 || body[10] == 0x03);
        bool shortTransition = body.Length == 3 &&
            body[0] == 0x00 && body[1] == 0x03 && body[2] == 0x02;

        if (gauge)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(6, 4));
            if (value is < 0 or > 1200) return;

            bool sameSeries = _candidateBoss == actor &&
                _candidateSamples > 0 && frame.Time >= _candidateLastAt &&
                frame.Time - _candidateLastAt <= TimeSpan.FromSeconds(10);
            if (!sameSeries)
            {
                ClearCandidateSeries();
                _candidateBoss = actor;
                _candidateFirstAt = frame.Time;
            }
            _candidateSamples++;
            _candidateLastAt = frame.Time;
            _candidateLastValue = value;

            if (body[10] == 0x02)
            {
                // An 02 sample after a previous episode may arm the next one,
                // but never allow another +5s inside the 8-second duplicate gate.
                if (_candidateTransitionReported &&
                    frame.Time - _candidateReportedAt > TimeSpan.FromSeconds(8))
                    _candidateTransitionReported = false;
                return;
            }

            // 03 itself is a transition candidate: do not wait for two gauge
            // samples or for three Staggers. Some exports end on this packet.
            if (!_candidateTransitionReported)
            {
                _candidateTransitionReported = true;
                _candidateReportedAt = frame.Time;
                bool counted = engine.ObserveBossStaggerEstimated(frame.Time);
                if (developerMode)
                    DebugConsole.Log("phaseward",
                        $"E005 boss state=03; actor={actor}, gauge={value}, " +
                        $"estimatedStagger={counted}, timingShift={(counted ? 5 : 0)}s.");
            }
            return;
        }

        if (!shortTransition) return;
        // Previous captures showed a different, short end-of-series marker.
        // Require at least one recent boss gauge, not several Staggers/pulls.
        bool recentBossGauge = _candidateBoss == actor && _candidateSamples > 0 &&
            frame.Time >= _candidateLastAt &&
            frame.Time - _candidateLastAt <= TimeSpan.FromSeconds(2) &&
            frame.Time - _candidateFirstAt <= TimeSpan.FromSeconds(20);
        bool alreadyReported = _candidateTransitionReported;
        ClearCandidateSeries();
        if (!recentBossGauge || alreadyReported) return;
        // Retain this start candidate so a later matching 364B zero packet
        // can close the same interval; never count a second start.
        _candidateBoss = actor;
        _candidateReportedAt = frame.Time;
        _candidateTransitionReported = true;

        // The semantic meaning of this marker is still an estimate. We do not
        // claim an exact start/end or overwrite the raw encounter timestamps.
        bool accepted = engine.ObserveBossStaggerEstimated(frame.Time);
        if (developerMode)
            DebugConsole.Log("phaseward",
                $"E005 boss short marker; actor={actor}, " +
                $"estimatedStagger={accepted}, timingShift={(accepted ? 5 : 0)}s.");
    }

    // The observed 364B body is 01 CD 00 followed by an int32 LE value.
    // A value of zero occurred 4.998s after a boss E005 02->03 transition
    // in one capture. This is provisional evidence for an end, not proof.
    private static void ObserveCandidateEnd(
        WireFrame frame, PhasewardEngine engine, bool developerMode)
    {
        ReadOnlySpan<byte> packet = frame.Payload;
        if (!_candidateTransitionReported || _candidateEndReported ||
            !TryReadCandidateActor(packet, out uint actor, out int offset) ||
            actor != engine.CurrentBossRuntimeId || actor != _candidateBoss)
            return;
        ReadOnlySpan<byte> body = packet.Slice(offset);
        if (body.Length != 7 || body[0] != 0x01 ||
            body[1] != 0xCD || body[2] != 0x00 ||
            BinaryPrimitives.ReadInt32LittleEndian(body.Slice(3, 4)) != 0)
            return;
        double duration = (frame.Time - _candidateReportedAt).TotalSeconds;
        if (duration is < 3.0 or > 8.0) return;
        if (!engine.ObserveBossStaggerEndEstimated(actor, frame.Time)) return;
        _candidateEndReported = true;
        if (developerMode)
            DebugConsole.Log("phaseward",
                $"E005 candidate Stagger END, boss={actor}, after={duration:0.000}s; " +
                "0x364B value=0. Interpretation provisional; no additional +5s shift.");
    }

    private static bool TryReadCandidateActor(
        ReadOnlySpan<byte> payload, out uint actor, out int bodyOffset)
    {
        actor = 0;
        bodyOffset = 0;
        for (int index = 0; index < payload.Length && index < 5; index++)
        {
            byte next = payload[index];
            if (index == 4 && (next & 0xF0) != 0) return false;
            actor |= (uint)(next & 0x7F) << (index * 7);
            if ((next & 0x80) == 0)
            {
                bodyOffset = index + 1;
                return actor != 0;
            }
        }
        return false;
    }

    private static void LogCandidate(WireFrame frame)
    {
        long now = Stopwatch.GetTimestamp();
        long previous = _lastDiagnostic;
        if (previous != 0 && (now - previous) < Stopwatch.Frequency / 2)
            return;
        _lastDiagnostic = now;
        // No raw packet bytes, account data or actor IDs are logged.
        DebugConsole.Log("phaseward",
            $"Stagger candidate opcode=0x{frame.Opcode.Value:X4}, length={frame.PayloadLength}, time={frame.Time:O}. Unverified; no timeline correction.");
    }

    private static bool TryReadActor(byte[] payload, Rule rule, out uint actor)
    {
        actor = 0;
        if (rule.ActorOffset >= payload.Length) return false;
        if (!rule.ActorIsVarUInt)
        {
            if (payload.Length - rule.ActorOffset < 4) return false;
            actor = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(rule.ActorOffset, 4));
            return actor != 0;
        }

        int shift = 0;
        for (int i = rule.ActorOffset; i < payload.Length && i < rule.ActorOffset + 5; i++)
        {
            byte value = payload[i];
            if (shift == 28 && (value & 0xF0) != 0) return false;
            actor |= (uint)(value & 0x7F) << shift;
            if ((value & 0x80) == 0) return actor != 0;
            shift += 7;
        }
        return false;
    }
}
