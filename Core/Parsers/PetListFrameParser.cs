
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Parsers;

// Egy megszerzett pet állapota.
public sealed record PetListEntry(
    int PetId,
    int Level,
    int SoulProgress
);

// Egy teljes petgyűjtemény-pillanatkép.
public sealed record PetListFrame(
    uint ActivePetId,
    IReadOnlyList<PetListEntry> Pets,
    int TrailingBytes
);

public static class PetListFrameParser
{
    public static bool TryParse(
        byte[]? payload,
        out PetListFrame? result)
    {
        result = null;

        // A WireFrame.Payload nem tartalmazza az opcode 2 bájtját.
        // Első 4 bájt: aktív pet ID
        // Utána: varint pet count
        if (payload is null || payload.Length < 5)
            return false;

        ReadOnlySpan<byte> data = payload;

        uint activePetId =
            BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(0, 4));

        if (activePetId != 0 &&
            activePetId is < 1000 or > 99999)
            return false;

        int pos = 4;

        // Petek száma
        if (!TryReadVarint(data, ref pos, out int count))
            return false;

        if (count is < 1 or > 2000)
            return false;

        // Egy rekord:
        // [PetId u32] [PetId u32] [Level u32]
        if (data.Length - pos < count * 12)
            return false;

        var levels = new List<(int Id, int Level)>(count);
        var knownIds = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(
                data.Slice(pos, 4));

            uint repeatedId =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    data.Slice(pos + 4, 4));

            uint level =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    data.Slice(pos + 8, 4));

            pos += 12;

            // Ellenőrzés: ez tényleg petlista?
            if (id != repeatedId ||
                id is < 1000 or > 99999 ||
                level is < 1 or > 3)
                return false;

            if (!knownIds.Add((int)id))
                return false;

            levels.Add(((int)id, (int)level));
        }

        // Soul-progress lista
        // [varint count]
        // [PetId u32] [Progress u32]
        var progress = new Dictionary<int, int>();

        if (pos < data.Length)
        {
            if (!TryReadVarint(
                    data, ref pos, out int progressCount))
                return false;

            if (progressCount is < 0 or > 2000)
                return false;

            if (data.Length - pos < progressCount * 8)
                return false;

            for (int i = 0; i < progressCount; i++)
            {
                uint id =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        data.Slice(pos, 4));

                uint amount =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        data.Slice(pos + 4, 4));

                pos += 8;

                if (amount > int.MaxValue)
                    return false;

                progress[(int)id] = (int)amount;
            }
        }

        // Összefűzzük a szinteket és progress értékeket.
        var pets = new List<PetListEntry>(levels.Count);

        foreach (var pet in levels)
        {
            progress.TryGetValue(pet.Id, out int soul);

            pets.Add(new PetListEntry(
                pet.Id,
                pet.Level,
                soul));
        }

        result = new PetListFrame(
            activePetId,
            pets,
            data.Length - pos);

        return true;
    }

    private static bool TryReadVarint(
        ReadOnlySpan<byte> data,
        ref int pos,
        out int value)
    {
        value = 0;
        uint decoded = 0;

        for (int i = 0; i < 5 && pos < data.Length; i++)
        {
            byte b = data[pos++];

            // 32 bites varint túlcsordulás elleni védelem
            if (i == 4 && (b & 0xF0) != 0)
                return false;

            decoded |= (uint)(b & 0x7F) << (i * 7);

            if ((b & 0x80) == 0)
            {
                if (decoded > int.MaxValue)
                    return false;

                value = (int)decoded;
                return true;
            }
        }

        return false;
    }
}
