using Avalonia.Media.Imaging;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

public enum MeterMode { Damage, Dps, Heal, Hps }

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
}

public sealed class PlayerStat
{
    public string Name { get; init; } = string.Empty;
    public int ClassId { get; init; }
    public Bitmap? ClassIcon { get; init; }
    public string ClassName { get; init; } = string.Empty;
    public string ClassColor { get; init; } = "#9CA3AF";
    public double Duration { get; init; }
    public long Damage { get; init; }
    public long BossDamage { get; init; }
    public long Healing { get; init; }
    public long BossHealing { get; init; }
    public IReadOnlyList<SkillEvent> Events { get; init; } = Array.Empty<SkillEvent>();

    public double Dps => Duration > 0 ? Damage / Duration : 0;
    public double Hps => Duration > 0 ? Healing / Duration : 0;
}

public sealed class MeterSnapshot
{
    public IReadOnlyList<PlayerStat> Players { get; init; } = Array.Empty<PlayerStat>();
    public double Duration { get; init; }
    public long MobMaxHp { get; init; }
    public bool IsDemo { get; init; }
    public string? MobTemplateName { get; init; }
    public int MobTemplateId { get; init; }
    public bool IsBossEncounter { get; init; }
    public string BossId { get; init; } = string.Empty;
    public double? BossHpPercent { get; init; }
    public long? BossCurrentHp { get; init; }
    public int? EnrageSeconds { get; init; }
    public double BossStartOffset { get; init; }
    public double BossDuration { get; init; }
    public double? EnrageRemainingSeconds { get; init; }
    public bool IsEnraged => EnrageRemainingSeconds is <= 0;
    public long TotalDamage => Players.Sum(p => p.Damage);
    public long TotalHealing => Players.Sum(p => p.Healing);

    public MeterSnapshot ToBossOnly()
    {
        if (!IsBossEncounter)
            return this;

        return new MeterSnapshot
        {
            Players = Players.Select(player => new PlayerStat
            {
                Name = player.Name,
                ClassId = player.ClassId,
                ClassIcon = player.ClassIcon,
                ClassName = player.ClassName,
                ClassColor = player.ClassColor,
                Duration = BossDuration > 0 ? BossDuration : player.Duration,
                Damage = player.BossDamage,
                BossDamage = player.BossDamage,
                Healing = player.BossHealing,
                BossHealing = player.BossHealing,
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
                    }).ToList(),
            }).ToList(),
            Duration = BossDuration > 0 ? BossDuration : Duration,
            MobMaxHp = MobMaxHp,
            IsDemo = IsDemo,
            MobTemplateName = MobTemplateName,
            MobTemplateId = MobTemplateId,
            IsBossEncounter = true,
            BossId = BossId,
            BossHpPercent = BossHpPercent,
            BossCurrentHp = BossCurrentHp,
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
        _ => "HPS",
    };

    public static double Metric(PlayerStat p, MeterMode m) => m switch
    {
        MeterMode.Damage => p.Damage,
        MeterMode.Dps => p.Dps,
        MeterMode.Heal => p.Healing,
        _ => p.Hps,
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
            _ => Num(p.Healing),
        };

        return (primary, secondary);
    }

    // Csoport összesítő sor (a fejlécben).
    public static string GroupLine(MeterSnapshot s, MeterMode m)
    {
        double dur = s.Duration;
        double dps = dur > 0 ? s.TotalDamage / dur : 0;
        double hps = dur > 0 ? s.TotalHealing / dur : 0;

        return m switch
        {
            MeterMode.Damage => $"{Num(s.TotalDamage)}  ({Format(dps, MeterMode.Dps)})",
            MeterMode.Dps => $"{Format(dps, MeterMode.Dps)}  ({Num(s.TotalDamage)})",
            MeterMode.Heal => $"{Num(s.TotalHealing)}  ({Format(hps, MeterMode.Hps)})",
            _ => $"{Format(hps, MeterMode.Hps)}  ({Num(s.TotalHealing)})",
        };
    }

    // Idősoros adat a grafikonhoz: 1 mp-es bucket-ek.
    // Sebzés/Gyógyítás: kumulatív összeg. DPS/HPS: 5 mp-es csúszóablak.
    public static double[] Series(PlayerStat p, MeterMode m, double duration)
    {
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

    // Osztály-badge (később a valódi ikon ide cserélhető, pl. ImageBrush).
    public static IBrush ForId(int id) =>
        Brush.Parse(Palette[Math.Abs(id) % Palette.Length]);

    // Skill-badge: név alapján determinisztikus szín.
    public static IBrush ForText(string text)
    {
        int hash = text.Aggregate(17, (acc, c) => unchecked(acc * 31 + c));
        return Brush.Parse(Palette[(hash & 0x7FFFFFFF) % Palette.Length]);
    }
}

public static class DemoData
{
    private static readonly string[] Names = { "Vexa", "Orin", "Mira", "Dask", "Lyss" };
    private static readonly string[] SkillNames =
    {
        "Lezúzó csapás", "Gyors vágás", "Villámcsapás", "Nyilazás", "Rúnakör",
    };
    private const string HealSkill = "Gyógyító fény";
    private const double EncounterSeconds = 154;

    public static MeterSnapshot Build()
    {
        var rnd = new Random(42);
        var players = new List<PlayerStat>();

        for (int p = 0; p < Names.Length; p++)
        {
            var events = new List<SkillEvent>();

            int hits = 60 + rnd.Next(40);
            for (int i = 0; i < hits; i++)
            {
                events.Add(new SkillEvent
                {
                    Time = rnd.NextDouble() * EncounterSeconds,
                    Skill = SkillNames[rnd.Next(SkillNames.Length)],
                    Amount = rnd.Next(3_000, 9_000),
                    Crit = rnd.NextDouble() < 0.22,
                });
            }

            // Demó gyógyító: a 3. játékos
            if (p == 2)
            {
                for (int i = 0; i < 45; i++)
                {
                    events.Add(new SkillEvent
                    {
                        Time = rnd.NextDouble() * EncounterSeconds,
                        Skill = HealSkill,
                        Amount = rnd.Next(1_500, 4_000),
                        Crit = rnd.NextDouble() < 0.1,
                        IsHeal = true,
                    });
                }
            }

            events.Sort((a, b) => a.Time.CompareTo(b.Time));

            players.Add(new PlayerStat
            {
                Name = Names[p],
                ClassId = p + 1,           // placeholder, a te ClassId-d lesz
                Duration = EncounterSeconds,
                Damage = events.Where(e => !e.IsHeal).Sum(e => e.Amount),
                Healing = events.Where(e => e.IsHeal).Sum(e => e.Amount),
                Events = events,
            });
        }

        return new MeterSnapshot
        {
            Players = players,
            Duration = EncounterSeconds,
            MobMaxHp = 2_840_000,
            IsDemo = true,
        };
    }
}
