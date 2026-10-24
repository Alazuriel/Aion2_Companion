using System;
using System.Buffers.Binary;

namespace AION_2_Companion.Core.Parsers;

public sealed record PetAddedFrame(int PetId, int Level);

public static class PetAddedFrameParser
{
    // Wire: 09 90 | 2 unknown bytes | PetId u32 LE | Level u32 LE | optional trailer.
    // The caller passes WireFrame.Payload (the opcode itself is already removed).
    public static bool TryParse(byte[]? payload, out PetAddedFrame? result)
    {
        result = null;
        if (payload is null || payload.Length < 10)
            return false;

        uint id = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(2, 4));
        uint level = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(6, 4));
        if (id is < 1000 or > 99999 || level is < 1 or > 3)
            return false;

        result = new PetAddedFrame((int)id, (int)level);
        return true;
    }
}
