using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using AION_2_Companion.Core.Capture;

namespace AION_2_Companion.Core.Wire;

/// <summary>
/// Stateful TCP stream reconstruction for CapturedPacket objects.
/// It handles sequence ordering, retransmissions, basic out-of-order buffering,
/// ACK-driven gap recovery, SYN sequence consumption, FIN/RST and frame extraction.
/// Feed packets in capture order and use one instance for the capture session.
/// </summary>
public sealed class TcpStreamDecoder
{
    private const int MaxPendingSegments = 256;
    private const int MaxPendingBytes = 2 * 1024 * 1024;

    private readonly Dictionary<FlowKey, StreamState> _streams = new();
    private readonly Action<WireFrame> _onFrame;

    public TcpStreamDecoder(Action<WireFrame> onFrame)
    {
        _onFrame = onFrame ?? throw new ArgumentNullException(nameof(onFrame));
    }

    public int ActiveStreams => _streams.Count;

    public void Feed(CapturedPacket packet)
    {
        if (!packet.IsTcp)
            return;

        if (!EndpointText.TryParse(packet.Source, out IPAddress srcIp, out ushort srcPort) ||
            !EndpointText.TryParse(packet.Destination, out IPAddress dstIp, out ushort dstPort))
            return;
        
        if (srcPort != 13328 && dstPort != 13328)
            return;

        uint seq = packet.TcpSequenceNumber ?? 0;
        uint ack = packet.TcpAcknowledgmentNumber ?? 0;
        byte flags = packet.TcpFlags;

        var key = new FlowKey(srcIp.ToString(), srcPort, dstIp.ToString(), dstPort);
        var reverse = key.Reverse();

        if ((flags & TcpFlags.Ack) != 0 && _streams.TryGetValue(reverse, out var acknowledgedStream))
            ReleaseAcknowledgedGap(acknowledgedStream, ack, packet.Time);

        bool syn = (flags & TcpFlags.Syn) != 0;
        bool fin = (flags & TcpFlags.Fin) != 0;
        bool rst = (flags & TcpFlags.Rst) != 0;

        if (packet.Payload.Length == 0 && !syn && !fin && !rst)
            return;

        var stream = GetOrCreate(key, packet.Time);
        stream.LastSeen = packet.Time;
        uint payloadSeq = syn ? unchecked(seq + 1) : seq;

        if (syn)
        {
            if (stream.Initialized && stream.NextSequence != payloadSeq)
            {
                _streams.Remove(key);
                stream = GetOrCreate(key, packet.Time);
                stream.LastSeen = packet.Time;
            }

            stream.NextSequence = payloadSeq;
            stream.Initialized = true;
        }
        else if (!stream.Initialized)
        {
            stream.NextSequence = payloadSeq;
            stream.Initialized = true;
        }

        if (packet.Payload.Length > 0)
            Place(stream, payloadSeq, packet.Payload, packet.Time);

        if (fin || rst)
        {
            stream.Frames.Flush(packet.Time);
            _streams.Remove(key);
            // The reverse direction is retained until its own close or stale cleanup.
        }

        Sweep(packet.Time);
    }

    public void Flush(DateTimeOffset time)
    {
        foreach (var state in _streams.Values.ToArray())
        {
            ReleaseAllPending(state, time);
            state.Frames.Flush(time);
        }
        _streams.Clear();
    }

    private StreamState GetOrCreate(FlowKey key, DateTimeOffset time)
    {
        if (_streams.TryGetValue(key, out var existing))
            return existing;

        var state = new StreamState(key, (t, opcode, payload, flags) =>
        {
            // Direction is assigned at the UI/integration layer until the server side is locked.
            _onFrame(new WireFrame(t, key.SourceText, key.DestinationText, opcode, payload, flags));
        });

        _streams[key] = state;
        return state;
    }

    private static void Place(StreamState state, uint seq, byte[] payload, DateTimeOffset time)
    {
        uint end = unchecked(seq + (uint)payload.Length);

        // Segment is wholly before or equal to the next expected byte: retransmission/duplicate.
        if (!IsAfter(end, state.NextSequence))
            return;

        // Segment overlaps the already consumed prefix.
        if (!IsAfter(seq, state.NextSequence))
        {
            int skip = unchecked((int)(state.NextSequence - seq));
            if (skip < payload.Length)
                Deliver(state, payload.AsSpan(skip), time);
            ReleaseContiguous(state);
            return;
        }

        // Out of order: queue it unless this would exceed limits.
        if (state.Pending.Count < MaxPendingSegments &&
            state.PendingBytes + payload.Length <= MaxPendingBytes)
        {
            if (!state.Pending.Any(p => p.Sequence == seq && p.Data.Length >= payload.Length))
            {
                state.Pending.Add(new PendingSegment(seq, payload.ToArray(), time));
                state.PendingBytes += payload.Length;
                state.Pending.Sort((a, b) => SequenceCompare(a.Sequence, b.Sequence, state.NextSequence));
            }
            return;
        }

        // If the bounded queue is full, abandon the gap and continue from this segment.
        state.Frames.MarkResynced();
        state.NextSequence = seq;
        Deliver(state, payload, time);
        ReleaseContiguous(state);
    }

    private static void Deliver(StreamState state, ReadOnlySpan<byte> payload, DateTimeOffset time)
    {
        if (payload.IsEmpty)
            return;

        state.NextSequence = unchecked(state.NextSequence + (uint)payload.Length);
        state.Frames.Feed(time, payload);
    }

    private static void ReleaseContiguous(StreamState state)
    {
        bool progress;
        do
        {
            progress = false;
            for (int i = 0; i < state.Pending.Count; i++)
            {
                var p = state.Pending[i];
                uint end = unchecked(p.Sequence + (uint)p.Data.Length);

                if (!IsAfter(end, state.NextSequence))
                {
                    state.PendingBytes -= p.Data.Length;
                    state.Pending.RemoveAt(i--);
                    progress = true;
                    continue;
                }

                if (!IsAfter(p.Sequence, state.NextSequence))
                {
                    int skip = unchecked((int)(state.NextSequence - p.Sequence));
                    state.PendingBytes -= p.Data.Length;
                    state.Pending.RemoveAt(i);
                    Deliver(state, p.Data.AsSpan(skip), p.Time);
                    progress = true;
                    break;
                }
            }
        } while (progress);
    }

    private static void ReleaseAcknowledgedGap(StreamState state, uint ack, DateTimeOffset time)
    {
        if (state.Pending.Count == 0)
            return;

        // An ACK from the receiver means bytes before ACK were expected. If we missed them,
        // skip to the earliest buffered segment that is itself acknowledged.
        state.Pending.Sort((a, b) => SequenceCompare(a.Sequence, b.Sequence, state.NextSequence));

        var first = state.Pending[0];
        if (IsAfter(first.Sequence, state.NextSequence) && !IsAfter(first.Sequence, ack))
        {
            state.Frames.MarkResynced();
            state.NextSequence = first.Sequence;
            ReleaseContiguous(state);
        }
    }

    private static void ReleaseAllPending(StreamState state, DateTimeOffset time)
    {
        state.Pending.Sort((a, b) => SequenceCompare(a.Sequence, b.Sequence, state.NextSequence));
        while (state.Pending.Count > 0)
        {
            var p = state.Pending[0];
            state.Pending.RemoveAt(0);
            state.PendingBytes -= p.Data.Length;

            if (IsAfter(p.Sequence, state.NextSequence))
            {
                state.Frames.MarkResynced();
                state.NextSequence = p.Sequence;
            }

            int skip = IsAfter(state.NextSequence, p.Sequence)
                ? Math.Min(unchecked((int)(state.NextSequence - p.Sequence)), p.Data.Length)
                : 0;

            if (skip < p.Data.Length)
                Deliver(state, p.Data.AsSpan(skip), p.Time);
            ReleaseContiguous(state);
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        // Prevent stale connection state from accumulating in long-running captures.
        foreach (var item in _streams.ToArray())
        {
            if (now - item.Value.LastSeen > TimeSpan.FromMinutes(2))
            {
                item.Value.Frames.Flush(now);
                _streams.Remove(item.Key);
            }
        }
    }

    private static bool IsAfter(uint a, uint b) => unchecked((int)(a - b)) > 0;

    private static int SequenceCompare(uint a, uint b, uint pivot)
    {
        int da = unchecked((int)(a - pivot));
        int db = unchecked((int)(b - pivot));
        return da.CompareTo(db);
    }

    private sealed class StreamState
    {
        public StreamState(FlowKey key, Action<DateTimeOffset, Opcode, byte[], WireFrameFlags> emit)
        {
            Key = key;
            Frames = new FrameStreamDecoder(emit);
        }

        public FlowKey Key { get; }
        public FrameStreamDecoder Frames { get; }
        public uint NextSequence { get; set; }
        public bool Initialized { get; set; }
        public List<PendingSegment> Pending { get; } = new();
        public int PendingBytes { get; set; }
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    }
    
    private sealed record PendingSegment(uint Sequence, byte[] Data, DateTimeOffset Time);

    private readonly record struct FlowKey(string SourceIp, ushort SourcePort, string DestinationIp, ushort DestinationPort)
    {
        public FlowKey Reverse() => new(DestinationIp, DestinationPort, SourceIp, SourcePort);
        public string SourceText => $"{SourceIp}:{SourcePort}";
        public string DestinationText => $"{DestinationIp}:{DestinationPort}";
    }

    private static class TcpFlags
    {
        public const byte Fin = 0x01;
        public const byte Syn = 0x02;
        public const byte Rst = 0x04;
        public const byte Ack = 0x10;
    }
}
