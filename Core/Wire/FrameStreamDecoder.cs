using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using K4os.Compression.LZ4;

namespace AION_2_Companion.Core.Wire;

/// <summary>
/// Splits a reconstructed TCP byte stream into AION 2 wire frames.
/// Frame body format follows a2kit: LEB128 length prefix, then a body whose
/// first two bytes are the little-endian opcode. Payload excludes the opcode.
/// </summary>
internal sealed class FrameStreamDecoder
{
    private const int MaxFrame = 1 << 20;
    private const int MaxBuffer = 1 << 20;
    private const int MaxBundleDepth = 4;
    private const int MaxPlain = 8_000_000;
    private const int MaxGrowth = 255;
    private const int MinBundle = 7;
    private const int StallLimit = 1 << 14;
    private bool _recovering;
    private readonly List<byte> _buffer = new();
    private readonly Action<DateTimeOffset, Opcode, byte[], WireFrameFlags> _emit;
    private bool _resynced;

    public FrameStreamDecoder(Action<DateTimeOffset, Opcode, byte[], WireFrameFlags> emit)
    {
        _emit = emit;
    }

    public void Feed(DateTimeOffset time, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (_buffer.Count + data.Length > MaxBuffer)
        {
            // Keep the newest data rather than allowing a malformed stream to grow forever.
            int remove = Math.Min(_buffer.Count, _buffer.Count + data.Length - MaxBuffer);
            _buffer.RemoveRange(0, remove);
            _resynced = true;
        }

        for (int i = 0; i < data.Length; i++)
            _buffer.Add(data[i]);

        Pump(time, trusted: false);
    }

    
    public void MarkResynced()
    {
        _buffer.Clear();

        _resynced = true;
        _recovering = true;
    }

    
    private static bool IsRecoveryAnchor(
        ReadOnlySpan<byte> body,
        int payloadLength)
    {
        if (body.Length < 2)
            return false;

        return (body[0], body[1]) switch
        {
            // Tick: a CSV-ben következetesen 8 bájt.
            (0x00, 0x36) => payloadLength == 8,

            // Hit: a vizsgált logban 24-32 bájt.
            (0x04, 0x38) =>
                payloadLength >= 24 && payloadLength <= 32,

            // DoT: a vizsgált logban 16-18 bájt.
            (0x05, 0x38) =>
                payloadLength >= 16 && payloadLength <= 18,

            _ => false
        };
    }

    public void Flush(DateTimeOffset time)
    {
        Pump(time, trusted: false, final: true);
    }

    
    private void Pump(
        DateTimeOffset time,
        bool trusted,
        bool final = false)
    {
        int offset = 0;

        while (offset < _buffer.Count)
        {
            if (_buffer[offset] == 0)
            {
                offset++;
                continue;
            }

            ReadOnlySpan<byte> remaining =
                CollectionsMarshal.AsSpan(_buffer).Slice(offset);

            var (value, width) = ReadUVarint(remaining);

            if (width == 0)
            {
                if (!final)
                    break;

                offset++;
                _resynced = true;
                _recovering = true;
                continue;
            }

            if (width < 0 || value < 6)
            {
                offset++;
                _resynced = true;
                _recovering = true;
                continue;
            }

            long frameLengthLong = (long)value + width - 4;

            if (frameLengthLong < width + 2 ||
                frameLengthLong > MaxFrame)
            {
                offset++;
                _resynced = true;
                _recovering = true;
                continue;
            }

            int frameLength = (int)frameLengthLong;

            if (remaining.Length < width + 2)
            {
                if (!final)
                    break;

                offset++;
                _resynced = true;
                _recovering = true;
                continue;
            }

            int payloadLength = frameLength - width - 2;

            ReadOnlySpan<byte> bodyStart =
                remaining.Slice(width);

            // Resync közben kizárólag erős
            // frame-jelölteket vizsgálunk.
            if (_recovering &&
                !IsRecoveryAnchor(bodyStart, payloadLength))
            {
                offset++;
                _resynced = true;
                continue;
            }

            // FONTOS: hiányos frame-et nem vizsgálunk
            // teljes frame-ként és nem dobunk el.
            if (remaining.Length < frameLength)
            {
                if (!final)
                    break;

                offset++;
                _resynced = true;
                _recovering = true;
                continue;
            }

            ReadOnlySpan<byte> body =
                remaining.Slice(width, frameLength - width);

            if (!IsPlausible(body))
            {
                offset++;
                _resynced = true;
                _recovering = true;
                continue;
            }

            ParseBody(time, body, 0, WireFrameFlags.None);

            _recovering = false;
            offset += frameLength;
        }

        if (offset > 0)
            _buffer.RemoveRange(0, offset);
    }

    

    private static (uint Value, int Width) ReadUVarint(ReadOnlySpan<byte> data)
    {
        uint value = 0;
        for (int i = 0; i < 5; i++)
        {
            if (i >= data.Length)
                return (0, 0);

            byte b = data[i];
            value |= (uint)(b & 0x7F) << (7 * i);
            if ((b & 0x80) != 0)
                continue;

            if ((i == 4 && b > 0x0F) || (i > 0 && b == 0))
                return (0, -1);

            return (value, i + 1);
        }

        return (0, -1);
    }

    
    private static bool IsPlausible(ReadOnlySpan<byte> body)
    {
        if (body.Length < 2)
            return false;

        // Compressed/bundled frame prefix.
        if (body[0] == 0xFF && body[1] == 0xFF)
            return body.Length >= MinBundle;

        if (body[0] >= 0xF0 &&
            body[0] < 0xFF &&
            body[1] == 0xFF)
        {
            return body.Length > MinBundle;
        }

        // Exact match from the encrypted runtime catalog.
        Opcode opcode = Opcode.FromBytes(body);

        if (OpcodeCatalog.IsKnown(opcode))
            return true;

        // Fallback for not-yet-registered opcodes in previously observed families.
        return body[1] is
            0x36 or 0x37 or 0x38 or 0x39 or
            0x56 or 0x8A or 0x8D or 0x92 or
            0x96 or 0x97;
    }


    private void ParseBody(DateTimeOffset time, ReadOnlySpan<byte> body, int depth, WireFrameFlags flags)
    {
        if (body.Length < 2)
            return;

        if (body[0] == 0xFF && body[1] == 0xFF)
        {
            TryUnwrap(time, body, depth, flags);
            return;
        }

        if (body.Length > MinBundle && body[0] >= 0xF0 && body[0] < 0xFF &&
            body[1] == 0xFF && body.Length >= 3 && body[2] == 0xFF)
        {
            if (TryUnwrap(time, body[1..], depth, flags))
                return;
        }

        var opcode = Opcode.FromBytes(body);
        var payload = body[2..].ToArray();
        if (_resynced)
        {
            flags |= WireFrameFlags.Resynced;
            _resynced = false;
        }
        _emit(time, opcode, payload, flags);
    }

    private bool TryUnwrap(DateTimeOffset time, ReadOnlySpan<byte> body, int depth, WireFrameFlags flags)
    {
        if (depth >= MaxBundleDepth || body.Length < MinBundle)
            return false;

        uint expected = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(2, 4));
        int compressedLength = body.Length - 6;

        if (expected == 0 ||
            (ulong)expected > (ulong)MaxGrowth * (ulong)compressedLength ||
            expected > MaxPlain)
            return false;

        int expectedLength = checked((int)expected);
        byte[] compressed = ArrayPool<byte>.Shared.Rent(compressedLength);
        byte[] plain = ArrayPool<byte>.Shared.Rent(expectedLength);

        try
        {
            body[6..].CopyTo(compressed);

            int decoded = LZ4Codec.Decode(
                compressed, 0, compressedLength,
                plain, 0, expectedLength);

            if (decoded != expectedLength)
                return false;

            WalkBundle(
                time,
                plain.AsSpan(0, decoded),
                depth + 1,
                flags | WireFrameFlags.WasLz4 | WireFrameFlags.WasBundled);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
            ArrayPool<byte>.Shared.Return(plain);
        }
    }

    private void WalkBundle(DateTimeOffset time, ReadOnlySpan<byte> data, int depth, WireFrameFlags flags)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            if (data[offset] == 0)
            {
                offset++;
                continue;
            }

            var (value, width) = ReadUVarint(data[offset..]);
            if (width <= 0 || value < 6)
                return;

            long totalLong = (long)value + width - 4;
            if (totalLong > MaxFrame || totalLong < width + 2)
                return;

            int total = (int)totalLong;
            if (data.Length - offset < total)
                return;

            ReadOnlySpan<byte> body = data.Slice(offset + width, total - width);
            ParseBody(time, body, depth, flags);
            offset += total;
        }
    }
}
