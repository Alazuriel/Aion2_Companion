namespace AION_2_Companion.Core.Parsers;

using System;
using System.Collections.Generic;
using System.Linq;

public enum HitOrigin { HitOpcode, DotOpcode, NestedInWrapper }

public sealed class DamageHit
{
    public uint Target { get; init; }
    public uint Actor { get; init; }
    public uint Skill { get; init; }
    public uint Damage { get; init; }
    public uint Type { get; init; }
    public uint Switch { get; init; }
    public byte ActorHitCount { get; init; }
    public byte Mods { get; init; }
    public byte Direction { get; init; }
    public uint Sequence { get; init; }
    public uint Scalar { get; init; }
    public byte TrailerSeq { get; init; }
    public bool IsDot { get; init; }
    public HitOrigin Origin { get; init; }
    public int Offset { get; init; }
    public int Consumed { get; init; }
    public bool IsCritical => Type == 3;
}

public static class DamageScanner
{
    private const byte HitOp0 = 0x04, HitOp1 = 0x38;  // 04 38: Hit
    private const byte DotOp0 = 0x05, DotOp1 = 0x38;  // 05 38: DoT

    // Burkoló opcode-ok, amelyekben beágyazott Hit bizonyítottan van.
    // Új opcode csak a játék logjával való egyezés után kerüljön ide.
    private static readonly (byte, byte)[] NestedHosts = { (0x0B, 0x39) };

    public static List<DamageHit> ScanFrame(byte op0, byte op1, byte[] payload, uint minDamage)
    {
        var found = new List<DamageHit>();
        if (payload is null || payload.Length == 0)
            return found;

        bool isHit = op0 == HitOp0 && op1 == HitOp1;
        bool isDot = op0 == DotOp0 && op1 == DotOp1;
        bool isNested = Array.IndexOf(NestedHosts, (op0, op1)) >= 0;

        if (isDot)
        {
            // DoT: a rekord offset 0-n van; az eltolt offsetek zajt adnak.
            if (TryParseDot(payload, out var dot) && dot.Damage >= minDamage)
                found.Add(dot);
            return found;
        }

        if (!isHit && !isNested)
            return found;

        var origin = isHit ? HitOrigin.HitOpcode : HitOrigin.NestedInWrapper;

        int off = 0;
        while (off < payload.Length)
        {
            if (TryParseHit(payload, off, origin, out var hit) && hit.Damage >= minDamage)
            {
                found.Add(hit);
                off += Math.Max(1, hit.Consumed); // a rekord bájtjain belül nincs újabb rekord
            }
            else
            {
                off++;
            }
        }

        return found;
    }

    // Azonos (Origin, Target, Actor, Skill, Damage, Type, Sequence) esetén a legkisebb offsetet tartjuk meg.
    private static List<DamageHit> Deduplicate(List<DamageHit> hits) =>
        hits.GroupBy(h => (h.Origin, h.Target, h.Actor, h.Skill, h.Damage, h.Type, h.Sequence))
            .Select(g => g.OrderBy(h => h.Offset).First())
            .ToList();

        private static bool TryParseHit(byte[] p, int start, HitOrigin origin, out DamageHit hit)
    {
        hit = null!;
        var c = new Cursor(p, start);

        if (!c.Varint(out uint target)) return false;
        if (!c.Varint(out uint sw)) return false;
        if (!c.Varint(out _)) return false;                  // ismeretlen, megfigyelt 0
        if (!c.Varint(out uint actor)) return false;
        if (!c.U32(out uint skill)) return false;            // nyers skill, NEM /100
        if (!c.U8(out byte actorHitCount)) return false;
        if (!c.Small(out uint type)) return false;

        byte mods = 0, direction = 0;
        int kind = (int)(sw & 0x0F);
        switch (kind)
        {
            case 0:
            case 4:
                break;
            case 6:
                if (!c.U8(out mods)) return false;
                if (!c.U8(out byte zero) || zero != 0) return false;
                if (!c.U8(out direction)) return false;
                break;
            default:
                return false;
        }

        if (!c.Skip(4)) return false;                        // skill-függő ismeretlen bájtok
        if (!c.U32(out uint sequence)) return false;
        if (!c.Varint(out uint scalar)) return false;

        uint damage = 0;
        if (kind != 0 && !c.Varint(out damage)) return false;

        if ((sw & 0x20) != 0)
        {
            if (!c.Varint(out uint count) || count > c.Remaining) return false;
            for (uint i = 0; i < count; i++)
                if (!c.Varint(out _)) return false;
        }

        if (sequence > 255) return false;
        if (target == 0 || actor == 0) return false;

        // Trailer: ha nem értelmezhető, csak a payload elején lévő, érvényes sebzésű
        // 04 38 Hit fogadható el (a fejléc és a sebzés már ellenőrzött).
        byte trailerSeq = 0, trailerZero = 0;
        bool hasTrailer = c.U8(out trailerSeq) && c.U8(out trailerZero);
        bool trailerOk = hasTrailer && trailerZero < 2;
        bool softAccept = origin == HitOrigin.HitOpcode
                          && start == 0
                          && damage > 0
                          && damage <= 100_999;

        if (!trailerOk && !softAccept)
            return false;

        hit = new DamageHit
        {
            Target = target,
            Actor = actor,
            Skill = skill,
            Damage = damage,
            Type = type,
            Switch = sw,
            ActorHitCount = actorHitCount,
            Mods = mods,
            Direction = direction,
            Sequence = sequence,
            Scalar = scalar,
            TrailerSeq = trailerSeq,
            IsDot = false,
            Origin = origin,
            Offset = start,
            Consumed = c.Pos - start,
        };
        return true;
    }

    private static bool TryParseDot(byte[] p, out DamageHit hit)
    {
        hit = null!;
        var c = new Cursor(p, 0);

        if (!c.Varint(out uint target)) return false;
        if (!c.U8(out byte effect) || (effect != 0x02 && effect != 0x0A)) return false;
        if (!c.Varint(out uint actor)) return false;
        if (!c.Varint(out _)) return false;                  // ismeretlen
        if (!c.U32(out uint raw)) return false;
        uint skill = raw / 100;
        if (!c.Varint(out uint damage)) return false;

        if (target == 0 || actor == 0 || actor == target || damage == 0
            || damage > 100_999 || skill == 0 || skill > 299_999_999)
            return false;

        hit = new DamageHit
        {
            Target = target,
            Actor = actor,
            Skill = skill,
            Damage = damage,
            Type = effect,
            IsDot = true,
            Origin = HitOrigin.DotOpcode,
            Offset = 0,
            Consumed = c.Pos,
        };
        return true;
    }

    private struct Cursor
    {
        private readonly byte[] _d;
        public int Pos;

        public Cursor(byte[] d, int pos)
        {
            _d = d;
            Pos = pos;
        }

        public int Remaining => _d.Length - Pos;

        public bool U8(out byte v)
        {
            v = 0;
            if (Remaining < 1) return false;
            v = _d[Pos++];
            return true;
        }

        public bool U32(out uint v)
        {
            v = 0;
            if (Remaining < 4) return false;
            v = (uint)_d[Pos]
              | ((uint)_d[Pos + 1] << 8)
              | ((uint)_d[Pos + 2] << 16)
              | ((uint)_d[Pos + 3] << 24);
            Pos += 4;
            return true;
        }

        public bool Skip(int n)
        {
            if (n < 0 || Remaining < n) return false;
            Pos += n;
            return true;
        }

        public bool Varint(out uint v)
        {
            v = 0;
            for (int i = 0; i < 5; i++)
            {
                if (Remaining < 1) return false;
                byte b = _d[Pos++];
                if (i == 4 && (b & 0xF0) != 0) return false; // uint32 túlcsordulás
                v |= (uint)(b & 0x7F) << (i * 7);
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }

        public bool Small(out uint v)
        {
            if (!Varint(out v)) return false;
            return v <= 255;
        }
    }
}