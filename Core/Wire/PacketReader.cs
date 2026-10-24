using System;

namespace AION_2_Companion.Core.Wire;

/// <summary>
/// Safe binary reader for AION 2 wire payloads.
/// <para>
/// All read operations perform strict bounds checks. Truncation, invalid
/// varint encoding, or uint32 overflow in LEB128 throw <see cref="FormatException"/>.
/// The reader never silently overruns the buffer.
/// </para>
/// <para>
/// Position advances only on successful reads. After a failed read the
/// reader state is undefined; callers should not continue using the instance.
/// </para>
/// </summary>
public sealed class PacketReader
{
    private readonly byte[] _data;
    private int _pos;

    public PacketReader(byte[] payload)
    {
        _data = payload ?? throw new ArgumentNullException(nameof(payload));
        _pos = 0;
    }

    /// <summary>Number of bytes remaining from the current position to the end.</summary>
    public int Remaining => _data.Length - _pos;

    /// <summary>Current read offset (0-based).</summary>
    public int Position => _pos;

    /// <summary>Total length of the underlying buffer.</summary>
    public int Length => _data.Length;

    /// <summary>
    /// Ensures at least <paramref name="count"/> bytes are available.
    /// Throws <see cref="FormatException"/> on truncation or negative count.
    /// </summary>
    private void Ensure(int count)
    {
        if (count < 0 || count > Remaining)
            throw new FormatException("Truncated packet.");
    }

    /// <summary>Reads one unsigned byte and advances the position by 1.</summary>
    public byte ReadByte()
    {
        Ensure(1);
        return _data[_pos++];
    }

    /// <summary>Reads one signed byte and advances the position by 1.</summary>
    public sbyte ReadSByte()
    {
        return (sbyte)ReadByte();
    }

    /// <summary>Peeks at the next byte without advancing the position.</summary>
    public byte PeekByte()
    {
        Ensure(1);
        return _data[_pos];
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes and advances the position.</summary>
    public byte[] ReadBytes(int count)
    {
        Ensure(count);
        byte[] result = new byte[count];
        Array.Copy(_data, _pos, result, 0, count);
        _pos += count;
        return result;
    }

    /// <summary>Advances the position by <paramref name="count"/> bytes without returning data.</summary>
    public void Skip(int count)
    {
        Ensure(count);
        _pos += count;
    }

    /// <summary>Reads a little-endian unsigned 16-bit integer.</summary>
    public ushort ReadUInt16LE()
    {
        Ensure(2);
        ushort value = (ushort)(_data[_pos] | (_data[_pos + 1] << 8));
        _pos += 2;
        return value;
    }

    /// <summary>Reads a little-endian signed 16-bit integer.</summary>
    public short ReadInt16LE()
    {
        return (short)ReadUInt16LE();
    }

    /// <summary>Reads a little-endian unsigned 32-bit integer (exactly 4 bytes).</summary>
    public uint ReadUInt32LE()
    {
        Ensure(4);
        uint value =
            (uint)_data[_pos] |
            ((uint)_data[_pos + 1] << 8) |
            ((uint)_data[_pos + 2] << 16) |
            ((uint)_data[_pos + 3] << 24);
        _pos += 4;
        return value;
    }

    /// <summary>Reads a little-endian signed 32-bit integer (exactly 4 bytes).</summary>
    public int ReadInt32LE()
    {
        return (int)ReadUInt32LE();
    }

    /// <summary>Reads a little-endian unsigned 64-bit integer (exactly 8 bytes).</summary>
    public ulong ReadUInt64LE()
    {
        Ensure(8);
        ulong value =
            (ulong)_data[_pos] |
            ((ulong)_data[_pos + 1] << 8) |
            ((ulong)_data[_pos + 2] << 16) |
            ((ulong)_data[_pos + 3] << 24) |
            ((ulong)_data[_pos + 4] << 32) |
            ((ulong)_data[_pos + 5] << 40) |
            ((ulong)_data[_pos + 6] << 48) |
            ((ulong)_data[_pos + 7] << 56);
        _pos += 8;
        return value;
    }

    /// <summary>Reads a little-endian signed 64-bit integer (exactly 8 bytes).</summary>
    public long ReadInt64LE()
    {
        return (long)ReadUInt64LE();
    }

    /// <summary>
    /// Reads an unsigned LEB128 (varint) value.
    /// <para>
    /// Maximum of 5 bytes (35 bits). Rejects encoding that would overflow
    /// a 32-bit unsigned integer (high nibble of the 5th byte must be zero).
    /// Rejects unterminated sequences longer than 5 bytes.
    /// </para>
    /// </summary>
    /// <exception cref="FormatException">Truncated, overlong, or overflowing varint.</exception>
    public uint ReadVarInt()
    {
        uint value = 0;

        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = ReadByte();

            // Reject uint32 overflow in the fifth byte (bits 28–31).
            if (shift == 28 && (b & 0xF0) != 0)
                throw new FormatException("Varint overflow.");

            value |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                return value;
        }

        throw new FormatException("Invalid varint.");
    }

    // Signed varint is intentionally not implemented.
    // The observed AION 2 payloads use only unsigned LEB128 for entity IDs,
    // counts and scalars. No ZigZag or other signed encoding has been
    // confirmed. Implement only after the protocol encoding is identified.
}