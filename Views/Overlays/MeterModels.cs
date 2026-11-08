using Avalonia.Media.Imaging;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

// Nexforge Damage Taken Log Timeline v0.2
public enum MeterMode { Damage, Dps, Heal, Hps, DamageTaken }

// Incoming hostile -> party hits only. No fictitious boss spell names or icons.
public sealed record IncomingDamageSample(double Time, long Amount, bool IsBossSource);

public sealed class EncounterMob
{
    public int TemplateId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class SkillEvent
{
    public double Time { get; init; }
    public string Skill { get; init; } = string.Empty;
    public string SkillIcon { get; set; } = "";
    public long Amount { get; init; }
    public bool Crit { get; init; }
    public bool IsHeal { get; init; }
    public uint TargetRuntimeId { get; init; }
    public int TargetTemplateId { get; init; }
    public bool IsBossTarget { get; init; }
    // Raw capture evidence. DO NOT interpret as front/back before packet validation.
    public byte? RawDirectionCode { get; init; }
    public bool IsDot { get; init; }
}

public sealed class PlayerStat
{
    private const double MinimumRateDurationSeconds = 1.0;
    public string Name { get; init; } = string.Empty;
    // A null death count means unknown, not zero.
    [System.Text.Json.Serialization.JsonIgnore]
    public int? DeathCount { get; init; }
    public int? FrontHits { get; init; }
    public int? BackHits { get; init; }
    public int? UnknownHits { get; init; }
    public int ClassId { get; init; }
    public Bitmap? ClassIcon { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string ClassColor { get; init; } = "#9CA3AF";
    public double Duration { get; init; }
    public long Damage { get; init; }
    public long BossDamage { get; init; }
    public long Healing { get; init; }
    public long BossHealing { get; init; }
    // Nullable: old NSL logs do not contain damage-taken evidence.
    public long? DamageTaken { get; init; }
    public long? BossDamageTaken { get; init; }
    public IReadOnlyList<IncomingDamageSample> IncomingDamageEvents { get; init; } = Array.Empty<IncomingDamageSample>();
    public IReadOnlyList<SkillEvent> Events { get; init; } = Array.Empty<SkillEvent>();

    public int DamageEventCount => Events.Count(entry => !entry.IsHeal);
    public int HealingEventCount => Events.Count(entry => entry.IsHeal);
    public double Dps => Duration >= MinimumRateDurationSeconds ? Damage / Duration : 0;
    public double Hps => Duration >= MinimumRateDurationSeconds ? Healing / Duration : 0;
}

public sealed class MeterSnapshot
{
    public IReadOnlyList<PlayerStat> Players { get; init; } = Array.Empty<PlayerStat>();
    public IReadOnlyList<EncounterMob> Mobs { get; init; } = Array.Empty<EncounterMob>();
    public ushort ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public double Duration { get; init; }
    public long MobMaxHp { get; init; }
    public string? MobTemplateName { get; init; }
    public int MobTemplateId { get; init; }
    public bool IsBossEncounter { get; init; }
    public string BossId { get; init; } = string.Empty;
    public double? BossHpPercent { get; init; }
    public long? BossCurrentHp { get; init; }
    public double? EstimatedDeathSeconds { get; init; }
    public int? EnrageSeconds { get; init; }
    public double BossStartOffset { get; init; }
    public double BossDuration { get; init; }
    public double? EnrageRemainingSeconds { get; init; }
    public bool IsEnraged => EnrageRemainingSeconds is <= 0;
    public long TotalDamage => Players.Sum(p => p.Damage);
    public long TotalHealing => Players.Sum(p => p.Healing);
    public long TotalDamageTaken => Players.Sum(p => p.DamageTaken ?? 0);
    public bool HasDamageTakenData => Players.Any(p => p.DamageTaken.HasValue);

    public MeterSnapshot ToBossOnly()
    {
        if (!IsBossEncounter)
            return this;

        return new MeterSnapshot
        {
            Players = Players.Select(player => new PlayerStat
            {
                Name = player.Name,
                DeathCount = player.DeathCount,
                FrontHits = player.FrontHits,
                BackHits = player.BackHits,
                UnknownHits = player.UnknownHits,
                ClassId = player.ClassId,
                ClassIcon = player.ClassIcon,
                ClassName = player.ClassName,
                ClassColor = player.ClassColor,
                Duration = BossDuration > 0 ? BossDuration : player.Duration,
                Damage = player.BossDamage,
                BossDamage = player.BossDamage,
                Healing = player.BossHealing,
                BossHealing = player.BossHealing,
                DamageTaken = player.BossDamageTaken,
                BossDamageTaken = player.BossDamageTaken,
                IncomingDamageEvents = player.IncomingDamageEvents
                    .Where(e => e.IsBossSource && e.Time >= BossStartOffset)
                    .Select(e => new IncomingDamageSample(
                        Math.Max(0, e.Time - BossStartOffset), e.Amount, true)).ToArray(),
                Events = player.Events
                    .Where(entry => entry.Time >= BossStartOffset && (entry.IsHeal || entry.IsBossTarget))
                    .Select(entry => new SkillEvent
                    {
                        Time = Math.Max(0, entry.Time - BossStartOffset),
                        Skill = entry.Skill,
                        SkillIcon = entry.SkillIcon,
                        Amount = entry.Amount,
                        Crit = entry.Crit,
                        IsHeal = entry.IsHeal,
                        TargetRuntimeId = entry.TargetRuntimeId,
                        TargetTemplateId = entry.TargetTemplateId,
                        IsBossTarget = entry.IsBossTarget,
                        RawDirectionCode = entry.RawDirectionCode,
                        IsDot = entry.IsDot,
                    }).ToList(),
            }).ToList(),
            Mobs = Mobs,
            ServerId = ServerId,
            ServerName = ServerName,
            Duration = BossDuration > 0 ? BossDuration : Duration,
            MobMaxHp = MobMaxHp,
            MobTemplateName = MobTemplateName,
            MobTemplateId = MobTemplateId,
            IsBossEncounter = true,
            BossId = BossId,
            BossHpPercent = BossHpPercent,
            BossCurrentHp = BossCurrentHp,
            EstimatedDeathSeconds = EstimatedDeathSeconds,
            EnrageSeconds = EnrageSeconds,
            EnrageRemainingSeconds = EnrageRemainingSeconds,
            BossStartOffset = 0,
            BossDuration = BossDuration,
        };
    }
}
public sealed class MeterRow
{
    public int Index { get; init; }
    public string Rank { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ClassLabel { get; init; } = string.Empty;
    public IBrush ClassBrush { get; init; } = Brushes.Gray;
    public string Primary { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool ShowDetail { get; init; }
    public double Percent { get; init; }
}

public sealed class SkillRow
{
    public string Name { get; init; } = string.Empty;
    public IBrush Brush { get; init; } = Brushes.Gray;
    public string Total { get; init; } = string.Empty;
    public string Hits { get; init; } = string.Empty;
    public string Crit { get; init; } = string.Empty;
    public string Max { get; init; } = string.Empty;
    public string Avg { get; init; } = string.Empty;
    public string Share { get; init; } = string.Empty;
    public double Percent { get; init; }
}

public static class Meter
{
    public static string ModeName(MeterMode m) => m switch
    {
        MeterMode.Damage => LocalizationService.Get("Damage"),
        MeterMode.Dps => "DPS",
        MeterMode.Heal => LocalizationService.Get("Healing"),
        MeterMode.Hps => "HPS",
        _ => "Damage Taken",
    };

    public static double Metric(PlayerStat p, MeterMode m) => m switch
    {
        MeterMode.Damage => p.Damage,
        MeterMode.Dps => p.Dps,
        MeterMode.Heal => p.Healing,
        MeterMode.Hps => p.Hps,
        _ => p.DamageTaken ?? 0,
    };

    public static string Num(double v)
    {
        if (v >= 1_000_000) return $"{v / 1_000_000:F2}M";
        if (v >= 1_000) return $"{v / 1_000:F1}k";
        return $"{v:N0}";
    }

    public static string Format(double value, MeterMode m) =>
        m is MeterMode.Dps or MeterMode.Hps ? $"{Num(value)}/s" : Num(value);

    public static string Percent(double part, double whole) =>
        whole > 0 ? $"{part / whole * 100:F1}%" : "–";

    // Sor értéke: elsődleges (a mód szerint) + zárójelben a másodlagos,
    // vagy ha a % mód be van kapcsolva, a %-os részesedés.
    public static (string Primary, string Secondary) Pair(
        PlayerStat p, MeterMode m, MeterSnapshot s, bool percentMode)
    {
        bool dmg = m is MeterMode.Damage or MeterMode.Dps;
        string primary = Format(Metric(p, m), m);

        if (percentMode)
        {
            return dmg
                ? (primary, LocalizationService.Format(
                    "ShareOfMob", Percent(p.Damage, s.MobMaxHp)))
                : (primary, LocalizationService.Format(
                    "ShareOfHealing", Percent(p.Healing, s.TotalHealing)));
        }

        string secondary = m switch
        {
            MeterMode.Damage => Format(p.Dps, MeterMode.Dps),
            MeterMode.Dps => Num(p.Damage),
            MeterMode.Heal => Format(p.Hps, MeterMode.Hps),
            MeterMode.Hps => Num(p.Healing),
            _ => "—",
        };

        return (primary, secondary);
    }

    // Csoport összesítő sor (a fejlécben).
    public static string GroupLine(MeterSnapshot s, MeterMode m)
    {
        double dur = s.Duration;
        double dps = dur >= 1 ? s.TotalDamage / dur : 0;
        double hps = dur >= 1 ? s.TotalHealing / dur : 0;

        return m switch
        {
            MeterMode.Damage => $"{Num(s.TotalDamage)}  ({Format(dps, MeterMode.Dps)})",
            MeterMode.Dps => $"{Format(dps, MeterMode.Dps)}  ({Num(s.TotalDamage)})",
            MeterMode.Heal => $"{Num(s.TotalHealing)}  ({Format(hps, MeterMode.Hps)})",
            MeterMode.Hps => $"{Format(hps, MeterMode.Hps)}  ({Num(s.TotalHealing)})",
            _ => s.HasDamageTakenData ? $"{Num(s.TotalDamageTaken)} TAKEN" : "TAKEN —",
        };
    }

    // Idősoros adat a grafikonhoz: 1 mp-es bucket-ek.
    // Sebzés/Gyógyítás: kumulatív összeg. DPS/HPS: 5 mp-es csúszóablak.
    public static double[] Series(PlayerStat p, MeterMode m, double duration)
    {
        if (m == MeterMode.DamageTaken)
        {
            int count = Math.Max(1, (int)Math.Ceiling(duration));
            var received = new double[count];
            foreach (IncomingDamageSample e in p.IncomingDamageEvents)
            {
                if (e.Amount <= 0 || !double.IsFinite(e.Time) || e.Time < 0 ||
                    e.Time > duration + 0.5) continue;
                received[Math.Clamp((int)e.Time, 0, count - 1)] += e.Amount;
            }
            for (int i = 1; i < received.Length; i++) received[i] += received[i - 1];
            return received;
        }
        int n = Math.Max(1, (int)Math.Ceiling(duration));
        bool heal = m is MeterMode.Heal or MeterMode.Hps;
        var buckets = new double[n];

        foreach (var e in p.Events)
        {
            if (e.IsHeal != heal) continue;
            int i = Math.Clamp((int)e.Time, 0, n - 1);
            buckets[i] += e.Amount;
        }

        if (m is MeterMode.Damage or MeterMode.Heal)
        {
            for (int i = 1; i < n; i++)
                buckets[i] += buckets[i - 1];
            return buckets;
        }

        const int window = 5;
        var rate = new double[n];
        for (int i = 0; i < n; i++)
        {
            int from = Math.Max(0, i - window + 1);
            rate[i] = buckets.Skip(from).Take(i - from + 1).Average();
        }
        return rate;
    }
}

public static class Visuals
{
    private static readonly string[] Palette =
    {
        "#C4A574", "#9EB8C4", "#8FBFA8", "#B98BC9",
        "#E0786A", "#D9C27A", "#7FA7E0", "#C98A5B",
    };

    public static IBrush Heal => Brush.Parse("#6BCB8B");

    public static IBrush ForId(int id) =>
        Brush.Parse(Palette[Math.Abs(id) % Palette.Length]);

    public static IBrush ForText(string text)
    {
        int hash = text.Aggregate(17, (acc, c) => unchecked(acc * 31 + c));
        return Brush.Parse(Palette[(hash & 0x7FFFFFFF) % Palette.Length]);
    }
}
