namespace AION_2_Companion.Core.Parsers;

using System;
using AION_2_Companion.Core.Wire;

public sealed class DeathFrameParser
{
    public static bool TryParse(byte[] payload, out uint entityId)
    {
        entityId = 0;

        try
        {
            if (payload is null || payload.Length == 0)
                return false;

            var reader = new PacketReader(payload);

            uint id = reader.ReadVarInt();

            if (id == 0 || id > 9_999_999)
                return false;

            // A további payload mezői még nincsenek megfejtve.
            // Egyelőre csak az entity ID-t használjuk.
            entityId = id;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}