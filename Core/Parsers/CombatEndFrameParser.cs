using System;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Parsers;

public class CombatEndFrameParser
{
    public static bool TryParse(byte[] payload, out uint id)
    {
        id = 0;
        try
        {
            if (payload is null || payload.Length < 6)
                return false;

            var reader = new PacketReader(payload);
            id = reader.ReadVarInt();

            return true;

        }
        catch (FormatException)
        {
            return false;
        }
    }
}