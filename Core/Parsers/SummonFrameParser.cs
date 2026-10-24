using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Parsers;

using System;
using System.Collections.Generic;

public sealed class CloseMobs
{
    public Opcode opcode { get; init; }
    public uint MobId { get; init; }
    public uint MobTemplateId { get; init; }
}
public sealed class SummonLink
{
    public Opcode Opcode { get; init; }
    public int AnchorPos { get; init; }
    public int VarintStart { get; init; }
    public uint SummonId { get; init; }
    public uint OwnerId { get; init; }
    public uint NpcId { get; init; }
    public bool IsVerified { get; init; }

    // "07_02" = a 07 02 xx [u32LE] mintából, "HDR_2A38" = a rekord előtti 2A 38 fejlécből, "" = nincs
    public string OwnerSource { get; init; } = "";
}

public sealed class SummonFrameParser
{

    // Horgony: 1F 10 00 ?? 8E 2C 00 ?? 02 00 (null = bármilyen bájt)
    private static readonly byte?[] PetAnchor =
    {
        0x1F, 0x10, 0x00, null,
        0x8E, 0x2C, 0x00, null,
        0x02, null
    };

    private const int AnchorLength = 10;
    private const int MaxVarintBytes = 5;

    // Hipotézis: az NPC típus u32LE a horgony utáni első 4 bájt.
    // Tesztelés után ezt lehet korrigálni.
    private const int NpcOffsetFromAnchorEnd = 0;

    public static bool TryParse(
        Opcode opcode,
        byte[] payload,
        Func<uint, bool> isKnownOwner,
        out List<SummonLink> links,
        out CloseMobs? closeMobs)
    {
        links = new List<SummonLink>();
        closeMobs = null;
        
        if (payload is null || payload.Length < AnchorLength + 4)
            return false;
        
        if (opcode != OpcodeId.Spawn && opcode != OpcodeId.Characters && opcode != OpcodeId.TheFuck && opcode != OpcodeId.TheFuck2)
            return false;

        if (opcode == OpcodeId.TheFuck2)
        {
            if (payload.Length != 47)
                return false;
            var reader = new PacketReader(payload);
            uint ownerId = reader.ReadVarInt();
            reader.Skip(24);
            uint summonId = reader.ReadVarInt();
            links.Add(new SummonLink
            {
                Opcode = opcode,
                AnchorPos = 0,
                VarintStart = 0,
                SummonId = summonId,
                OwnerId = ownerId,
                NpcId = 0,
                IsVerified = ownerId != 0 && isKnownOwner(ownerId),
                OwnerSource = "Deltaa"
            });
            
            return links.Count > 0;
        }

        if (opcode == OpcodeId.Spawn || payload.Length == 102)
        {
            var reader = new PacketReader(payload);
            uint mobId = reader.ReadVarInt();
            
            closeMobs = new CloseMobs
            {
                opcode = opcode,
                MobId = mobId,
                MobTemplateId = BitConverter.ToUInt32(payload, 6)
            };

            return true;
        }
        
        var seenSummonIds = new HashSet<uint>();
        
        for (int a = 1; a + AnchorLength + 4 <= payload.Length; a++)
        {
            if (!MatchesAt(payload, a, PetAnchor))
                continue;

            // A horgony előtti bájt varint-záróbájt kell legyen (MSB = 0)
            if ((payload[a - 1] & 0x80) != 0)
                continue;

            int varintStart = FindVarintStart(payload, a);
            uint summonId = DecodeVarint(payload, varintStart, a);

            if (summonId == 0 || !seenSummonIds.Add(summonId))
                continue;

            int npcPos = a + AnchorLength + NpcOffsetFromAnchorEnd;
            uint npcId = npcPos + 4 <= payload.Length ? ReadU32LE(payload, npcPos) : 0;

            // 1) 07 02 xx [u32LE] minta
            uint ownerId = FindOwner(payload, summonId, isKnownOwner);
            string source = ownerId != 0 ? "07_02" : "";

            // 2) ha nincs, a rekord előtti 2A 38 fejléc (B stratégia)
            if (ownerId == 0)
            {
                ownerId = FindOwnerByHeader(payload, varintStart - 2, isKnownOwner);
                if (ownerId != 0)
                    source = "HDR_2A38";
            }

            links.Add(new SummonLink
            {
                Opcode = opcode,
                AnchorPos = a,
                VarintStart = varintStart,
                SummonId = summonId,
                OwnerId = ownerId,
                NpcId = npcId,
                IsVerified = ownerId != 0,
                OwnerSource = source
            });
        }

        return links.Count > 0;
    }

    // Visszafelé keresi a varint kezdetét, legfeljebb MaxVarintBytes hosszan.
    private static int FindVarintStart(byte[] data, int anchorPos)
    {
        int start = anchorPos - 1;

        while (start > 0
               && (data[start - 1] & 0x80) != 0
               && anchorPos - start < MaxVarintBytes)
        {
            start--;
        }

        return start;
    }

    private static uint DecodeVarint(byte[] data, int start, int end)
    {
        uint value = 0;
        int shift = 0;

        for (int k = start; k < end; k++)
        {
            value |= (uint)(data[k] & 0x7F) << shift;
            shift += 7;
        }

        return value;
    }

    private static uint FindOwner(byte[] payload, uint summonId, Func<uint, bool> isKnownOwner)
    {
        for (int i = 0; i + 6 < payload.Length; i++)
        {
            if (payload[i] != 0x07 || payload[i + 1] != 0x02)
                continue;

            // payload[i + 2] az "xx" bájt, jelentése még nem ismert
            uint candidate = ReadU32LE(payload, i + 3);

            if (candidate != 0 && candidate != summonId && isKnownOwner(candidate))
                return candidate;
        }

        return 0;
    }

    // B stratégia: a 41 36 (opPos) előtti legközelebbi "2A 38 <varint>" fejléc varintja.
    // opPos = a 41 36 opcode pozíciója (az entity varint két bájttal később kezdődik).
    private static uint FindOwnerByHeader(byte[] payload, int opPos, Func<uint, bool> isKnownOwner)
    {
        if (opPos < 0 || opPos + 1 >= payload.Length)
            return 0;
        if (payload[opPos] != 0x41 || payload[opPos + 1] != 0x36)
            return 0;

        uint nearest = 0;
        for (int i = 0; i + 1 < opPos; i++)
        {
            if (payload[i] != 0x2A || payload[i + 1] != 0x38)
                continue;

            if (TryReadVarint(payload, i + 2, out uint value) && value != 0)
                nearest = value; // a ciklus növekvő, így az utolsó a legközelebbi
        }

        return nearest != 0 && isKnownOwner(nearest) ? nearest : 0;
    }

    private static bool TryReadVarint(byte[] data, int pos, out uint value)
    {
        value = 0;
        int shift = 0;

        while (pos < data.Length && shift <= 28)
        {
            byte b = data[pos++];
            value |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
                return true;

            shift += 7;
        }

        value = 0;
        return false;
    }

    private static uint ReadU32LE(byte[] data, int pos)
    {
        return (uint)data[pos]
             | ((uint)data[pos + 1] << 8)
             | ((uint)data[pos + 2] << 16)
             | ((uint)data[pos + 3] << 24);
    }

    private static bool MatchesAt(byte[] data, int pos, byte?[] pattern)
    {
        if (pos < 0 || pos + pattern.Length > data.Length)
            return false;

        for (int k = 0; k < pattern.Length; k++)
        {
            if (pattern[k] is byte b && data[pos + k] != b)
                return false;
        }

        return true;
    }
}