namespace AION_2_Companion.Core.Parsers;

using System;
using AION_2_Companion.Core.Wire;

public sealed class DotFrameParser
{
    public static bool TryParse(byte[] payload, out DamageHit? hit)
    {
        hit = null;

        try
        {
            var reader = new PacketReader(payload);

            uint target = reader.ReadVarInt();
            byte effectType = reader.ReadByte();

            if (effectType != 0x02 && effectType != 0x0A)
                return false;

            uint actor = reader.ReadVarInt();

            _ = reader.ReadVarInt();                    // Unknown field

            uint rawSkill = reader.ReadUInt32LE();
            uint skill = rawSkill / 100;

            uint damage = reader.ReadVarInt();

            if (target == 0 ||
                actor == 0 ||
                actor == target ||
                damage == 0 ||
                damage > 99_999_999 ||
                skill == 0 ||
                skill > 299_999_999)
            {
                return false;
            }

            hit = new DamageHit
            {
                Target = target,
                Actor = actor,
                Skill = skill,
                Damage = damage,
                Type = effectType,
                Switch = 0,
                IsDot = true
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