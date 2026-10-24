using System;

namespace AION_2_Companion.Core.Parsers;

using AION_2_Companion.Core.Wire;

public sealed class CombatStart
{
    public uint targetId;
    public uint actorId;
}
public class CombatStartFrameParser
{
    public static bool TryParse(byte[] payload, out CombatStart? combatStarter)
    {
        combatStarter = null;

        try
        {
            if (payload is null || payload.Length > 6)
                return false;
            
            var reader = new PacketReader(payload);
            uint target = reader.ReadVarInt();
            uint anyad = reader.ReadVarInt();
            uint actor = reader.ReadVarInt();

            combatStarter = new CombatStart
            {
                targetId = target,
                actorId = actor
            };

            return true;

        }
        catch (FormatException)
        {
            return false;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }
}