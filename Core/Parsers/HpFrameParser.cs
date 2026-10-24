using System;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Parsers;

public sealed class Hp
{
    public uint Target { get; init; }
    public byte[] Marker { get; init; } = Array.Empty<byte>();
    public uint Health { get; init; }
}

public class HpFrameParser
{
    public static bool TryParse(byte[] payload, out Hp? hp)
    {
        hp = null;

        try
        {
            var reader = new PacketReader(payload);
            if (payload.Length < 7)
                return false;
            uint target = reader.ReadVarInt();
            
            if (target == 0)
                return false;

            byte[] marker =
            {
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte()
            };
    
    
            int hpOffset = payload.Length - 7;
            
            uint health = (uint)(
                payload[hpOffset] |
                (payload[hpOffset + 1] << 8)
            );
            
            hp = new Hp
            {
                Target = target,
                Marker = marker,
                Health = health
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