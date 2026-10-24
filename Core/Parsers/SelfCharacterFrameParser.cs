namespace AION_2_Companion.Core.Parsers;

using System;
using System.Text;
using AION_2_Companion.Core.Wire;

public sealed class OwnCharacter
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;

    // Név utáni blokk: +0..+1 server ID (LE), +2 class/job (nyers érték)
    public ushort ServerId { get; init; }
    public byte? ClassId { get; init; }

    public byte? Level { get; init; }

    public uint Gs { get; init; }
}

public sealed class OwnCharacterParser
{
    // Név utáni (nameEnd-relatív) offsetek
    private const int ServerIdOffset = 0;  // 2 byte, LE
    private const int ClassIdOffset = 2;   // class/job, nyers
    private const int Marker3Offset = 6;   // 01
    private const int LevelOffset = 7;     // szint
    private const int GsOffset = 11;       // GS (4 byte)

    public static bool TryParse(
        byte[] payload,
        out OwnCharacter? character)
    {
        character = null;

        try
        {
            var reader = new PacketReader(payload);

            uint id = reader.ReadVarInt();

            if (id is < 1 or > 9_999_999)
                return false;

            int idLen = VarIntLength(payload);

            // ID | 4 unknown | mask | name length | name
            int maskIndex = idLen + 4;
            int nameLenIndex = idLen + 5;
            int nameStart = idLen + 6;

            if (nameLenIndex >= payload.Length)
                return false;

            byte mask = payload[maskIndex];

            if ((mask & 0x01) == 0)
                return false;

            byte nameLength = payload[nameLenIndex];

            if (nameLength is < 1 or > 36)
                return false;

            int nameEnd = nameStart + nameLength;

            if (nameEnd > payload.Length)
                return false;

            string name;

            try
            {
                name = new UTF8Encoding(false, true)
                    .GetString(payload[nameStart..nameEnd]);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (!IsValidName(name))
                return false;

            ushort? serverId = ReadU16(payload, nameEnd + ServerIdOffset);
            byte? classId = At(payload, nameEnd + ClassIdOffset);

            byte? level = null;
            if (At(payload, nameEnd + Marker3Offset) == 0x01)
                level = At(payload, nameEnd + LevelOffset);

            uint gs = ReadU32(payload, nameEnd + GsOffset) ?? 0;

            character = new OwnCharacter
            {
                Id = id,
                Name = name,
                ServerId = serverId ?? 0,
                ClassId = classId,
                Level = level,
                Gs = gs
            };
            
            Console.WriteLine();
            Console.WriteLine("========== OWN CHARACTER ==========");
            Console.WriteLine($"Name       : {name}");
            Console.WriteLine($"Entity ID  : {id}");
            Console.WriteLine($"Server ID  : {serverId?.ToString() ?? "NOT FOUND"}");
            Console.WriteLine($"Class ID   : {(classId.HasValue ? classId.Value.ToString() : "NOT FOUND")}");
            Console.WriteLine($"Level      : {(level.HasValue ? level.Value.ToString() : "NOT FOUND")}");
            Console.WriteLine($"GS         : {gs}");
            Console.WriteLine($"BS         : ?");
            Console.WriteLine("===================================");
            Console.WriteLine();

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

    // A varint hossza bájtban: a folytatóbitet (0x80) nézi.
    private static int VarIntLength(byte[] payload)
    {
        int i = 0;

        while (i < payload.Length && i < 5 && (payload[i] & 0x80) != 0)
            i++;

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