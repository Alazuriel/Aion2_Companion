using System;

namespace AION_2_Companion.Core.Phaseward;

public readonly record struct PhasewardCast(uint ActorRuntimeId, uint SkillId);

public static class PhasewardCastParser
{
    public static bool TryParse(byte[] payload, out PhasewardCast cast)
    {
        cast = default;
        if (payload is null || payload.Length < 7)
            return false;

        int position = 0;
        if (!TryReadVarUInt(payload, ref position, out uint actor) || actor == 0)
            return false;

        // Controlled captures show one mode byte followed by the raw LE skill ID.
        position++;
        if (position + 4 > payload.Length)
            return false;

        uint skill = BitConverter.ToUInt32(payload, position);
        if (skill == 0)
            return false;

        cast = new PhasewardCast(actor, skill);
        return true;
    }

    private static bool TryReadVarUInt(byte[] payload, ref int position, out uint value)
    {
        value = 0;
        int shift = 0;
        while (position < payload.Length && shift < 35)
        {
            byte current = payload[position++];
            value |= (uint)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return true;
            shift += 7;
        }
        return false;
    }
}
