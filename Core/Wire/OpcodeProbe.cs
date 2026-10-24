using System;
using System.Linq;

namespace AION_2_Companion.Core.Wire;

public static class OpcodeProbe
{
    public static string Describe(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
            return $"Too short ({payload.Length} B)";

        ushort littleEndian =
            (ushort)(payload[0] | (payload[1] << 8));

        ushort bigEndian =
            (ushort)((payload[0] << 8) | payload[1]);

        string hex = Convert.ToHexString(
            payload[..Math.Min(payload.Length, 24)]
        );

        return
            $"LE={littleEndian:X4} | " +
            $"BE={bigEndian:X4} | " +
            $"Length={payload.Length} B | " +
            $"HEX={hex}";
    }
}