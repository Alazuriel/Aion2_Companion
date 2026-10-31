using System;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Parsers;

public sealed class Hp
{
    public uint Target { get; init; }
    public uint Health { get; init; }
}

public class HpFrameParser
{
    public static bool TryParse(byte[] payload, out Hp? hp)
        => TryParse(payload, out hp, out _);

    public static bool TryParse(byte[] payload, out Hp? hp, out string failure)
    {
        hp = null;
        failure = string.Empty;

        try
        {
            var reader = new PacketReader(payload);
            if (payload.Length < 8)
            {
                failure = "PayloadTooShort";
                return false;
            }

            uint target = reader.ReadVarInt();
            if (target == 0)
            {
                failure = "MissingTarget";
                return false;
            }

            reader.Skip(3);
            if (reader.Remaining < sizeof(uint))
            {
                failure = "MissingHealthValue";
                return false;
            }

            uint health = reader.ReadUInt32LE();

            hp = new Hp
            {
                Target = target,
                Health = health
            };
            return true;
        }
        catch (FormatException)
        {
            failure = "MalformedPayload";
            return false;
        }
        catch (IndexOutOfRangeException)
        {
            failure = "MalformedPayload";
            return false;
        }
    }
}
