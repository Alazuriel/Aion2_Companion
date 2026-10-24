using System;

namespace AION_2_Companion.Core.Wire;

public readonly record struct Opcode(ushort Value)
{
    public byte FirstByte => (byte)(Value & 0xFF);
    public byte SecondByte => (byte)(Value >> 8);
    public static bool operator ==(Opcode opcode, OpcodeId id)
        => OpcodeCatalog.Matches(opcode, id);

    public static bool operator !=(Opcode opcode, OpcodeId id)
        => !(opcode == id);

    public static bool operator ==(OpcodeId id, Opcode opcode)
        => opcode == id;

    public static bool operator !=(OpcodeId id, Opcode opcode)
        => !(opcode == id);
    public override string ToString()
        => $"{FirstByte:X2} {SecondByte:X2}";

    public static Opcode FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
            throw new ArgumentException(
                "An opcode requires at least two bytes.",
                nameof(bytes));

        return new Opcode(
            (ushort)(bytes[0] | (bytes[1] << 8)));
    }
}
