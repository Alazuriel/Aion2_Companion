using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Parsers;

public sealed record PetProgressFrameEntry(int PetId, int Gained);

public static class PetProgressFrameParser
{
    // Wire: 0D 90 | count u8 | count * (PetId u32 LE, gained u32 LE).
    public static bool TryParse(byte[]? payload, out IReadOnlyList<PetProgressFrameEntry>? entries)
    {
        entries = null;
        if (payload is null || payload.Length < 9)
            return false;

        int count = payload[0];
        if (count <= 0 || payload.Length - 1 < count * 8)
            return false;

        var values = new List<PetProgressFrameEntry>(count);
        for (int i = 0, p = 1; i < count; i++, p += 8)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(p, 4));
            uint gained = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(p + 4, 4));
            if (id is < 1000 or > 99999 || gained is < 1 or >= 1000)
                return false;
            values.Add(new PetProgressFrameEntry((int)id, (int)gained));
        }

        entries = values;
        return true;
    }
}
