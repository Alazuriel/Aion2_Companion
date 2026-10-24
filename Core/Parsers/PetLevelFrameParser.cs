using System;
using System.Buffers.Binary;

namespace AION_2_Companion.Core.Parsers;

public sealed record PetLevelFrame(int PetId, int Level);

public static class PetLevelFrameParser
{
    // Wire: 14 90 | 00 00 | PetId u32 LE | new Level u32 LE | optional trailer.
    public static bool TryParse(byte[]? payload, out PetLevelFrame? result)
    {
        result = null;
        if (payload is null || payload.Length < 10 || payload[0] != 0 || payload[1] != 0)
            return false;

        uint id = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4));
        uint level = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(6, 4));
        if (id is < 1000 or > 99999 || level is < 1 or > 3)
            return false;

        result = new PetLevelFrame((int)id, (int)level);
        return true;
    }
}
