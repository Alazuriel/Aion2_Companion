namespace AION_2_Companion.Core.Parsers;

using System;
using System.Text;
using AION_2_Companion.Core.Wire;

public static class PartyCharacterParser
{
    private const int ServerIdOffset = 0;
    private const int ClassIdOffset = 2;
    private const int Marker3Offset = 6;
    private const int LevelOffset = 7;
    private const int GsOffset = 11;

    public static bool TryParse(
        byte[] payload,
        out PartyCharacter? character)
    {
        character = null;
        if (payload is null || payload.Length == 0)
            return false;

        try
        {
            var reader = new PacketReader(payload);

            uint id = reader.ReadVarInt();

            if (id is < 1 or > 9_999_999)
                return false;

            int idLength = VarIntLength(payload);

            // ID | 4 unknown | mask | name length | name
            
            int maskIndex = idLength + 4;
            int nameLengthIndex = idLength + 5;
            int nameStart = idLength + 6;
            
            if (nameLengthIndex >= payload.Length)
                return false;

            byte mask = payload[maskIndex];

            if ((mask & 0x01) == 0)
                return false;

            int nameLength = payload[nameLengthIndex];

            if (nameLength is < 1 or > 36)
                return false;

            int nameEnd = nameStart + nameLength;

            if (nameEnd > payload.Length)
                return false;

            string name;

            try
            {
                name = new UTF8Encoding(false, true)
                    .GetString(payload, nameStart, nameLength);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (!IsValidName(name))
                return false;

            ushort? serverId =
                ReadU16(payload, nameEnd + ServerIdOffset);

            byte? classId =
                At(payload, nameEnd + ClassIdOffset);

            byte? level = null;

            if (At(payload, nameEnd + Marker3Offset) == 0x01)
                level = At(payload, nameEnd + LevelOffset);

            uint gs =
                ReadU32(payload, nameEnd + GsOffset) ?? 0;

            character = new PartyCharacter
            {
                Id = id,
                Name = name,
                ServerId = serverId ?? 0,
                ClassId = classId,
                Level = level,
                Gs = gs
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
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int VarIntLength(byte[] payload)
    {
        int i = 0;

        while (i < payload.Length &&
               i < 5 &&
               (payload[i] & 0x80) != 0)
        {
            i++;
        }

        return i + 1;
    }

    private static byte? At(byte[] payload, int index)
        => index >= 0 && index < payload.Length
            ? payload[index]
            : null;

    private static ushort? ReadU16(byte[] payload, int index)
        => index >= 0 && index + 2 <= payload.Length
            ? BitConverter.ToUInt16(payload, index)
            : null;

    private static uint? ReadU32(byte[] payload, int index)
        => index >= 0 && index + 4 <= payload.Length
            ? BitConverter.ToUInt32(payload, index)
            : null;

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        bool hasLetter = false;

        foreach (char c in name)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
                return false;

            if (char.IsLetter(c))
                hasLetter = true;
        }

        return hasLetter;
    }
}