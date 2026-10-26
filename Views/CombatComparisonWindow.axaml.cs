using System;
using System.Collections.ObjectModel;
using System.Linq;
using AION_2_Companion.Core;
using AION_2_Companion.Views.Overlays;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AION_2_Companion.Views;

public sealed class CombatComparisonPlayerRow
{
    public string Name { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string DpsA { get; init; } = "—";
    public string DpsB { get; init; } = "—";
    public string DpsDelta { get; init; } = "—";
    public string DamageDelta { get; init; } = "—";
    public string HpsDelta { get; init; } = "—";
    public IBrush DpsDeltaBrush { get; init; } = Brushes.Gray;
    public IBrush DamageDeltaBrush { get; init; } = Brushes.Gray;
    public IBrush HpsDeltaBrush { get; init; } = Brushes.Gray;
}

public partial class CombatComparisonWindow : Window
{
    private static readonly IBrush PositiveBrush = Brush.Parse("#78C69B");
    private static readonly IBrush NegativeBrush = Brush.Parse("#E07A7A");
    private static readonly IBrush NeutralBrush = Brush.Parse("#9A9EA8");

    public string FightAName { get; }
    public string FightBName { get; }
    public string FightAMeta { get; }
    public string FightBMeta { get; }
    public string GroupDpsText { get; }
    public string GroupDpsDelta { get; }
    public IBrush GroupDpsDeltaBrush { get; }
    public string DamageText { get; }
    public string DamageDelta { get; }
    public IBrush DamageDeltaBrush { get; }
    public string HealingText { get; }
    public string HealingDelta { get; }
    public IBrush HealingDeltaBrush { get; }
    public ObservableCollection<CombatComparisonPlayerRow> Players { get; } = new();

    public CombatComparisonWindow()
        : this(
            new CombatLogEntry { MobName = "Fight A", EndedAtUtc = DateTimeOffset.UtcNow },
            new CombatLogEntry { MobName = "Fight B", EndedAtUtc = DateTimeOffset.UtcNow })
    {
    }

    public CombatComparisonWindow(CombatLogEntry fightA, CombatLogEntry fightB)
        : this(fightA, fightB, false)
    {
    }

    public CombatComparisonWindow(CombatLogEntry fightA, CombatLogEntry fightB, bool bossOnly)
    {
        MeterSnapshot snapshotA = fightA.ToSnapshot(bossOnly);
        MeterSnapshot snapshotB = fightB.ToSnapshot(bossOnly);
        double groupDpsA = snapshotA.TotalDamage / Math.Max(0.001, fightA.Duration);
        double groupDpsB = snapshotB.TotalDamage / Math.Max(0.001, fightB.Duration);

        FightAName = fightA.MobName;
        FightBName = fightB.MobName;
        FightAMeta = FormatMeta(fightA);
        FightBMeta = FormatMeta(fightB);
        GroupDpsText = $"{Meter.Num(groupDpsA)}/s  →  {Meter.Num(groupDpsB)}/s";
        GroupDpsDelta = Signed(groupDpsB - groupDpsA, "/s");
        GroupDpsDeltaBrush = DeltaBrush(groupDpsB - groupDpsA);
        DamageText = $"{Meter.Num(snapshotA.TotalDamage)}  →  {Meter.Num(snapshotB.TotalDamage)}";
        DamageDelta = Signed(snapshotB.TotalDamage - snapshotA.TotalDamage);
        DamageDeltaBrush = DeltaBrush(snapshotB.TotalDamage - snapshotA.TotalDamage);
        HealingText = $"{Meter.Num(snapshotA.TotalHealing)}  →  {Meter.Num(snapshotB.TotalHealing)}";
        HealingDelta = Signed(snapshotB.TotalHealing - snapshotA.TotalHealing);
        HealingDeltaBrush = DeltaBrush(snapshotB.TotalHealing - snapshotA.TotalHealing);

        foreach (string playerName in snapshotA.Players.Select(player => player.Name)
                     .Union(snapshotB.Players.Select(player => player.Name), StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(name =>
                         snapshotB.Players.FirstOrDefault(player => SameName(player.Name, name))?.Dps ?? 0))
        {
            PlayerStat? playerA = snapshotA.Players.FirstOrDefault(player => SameName(player.Name, playerName));
            PlayerStat? playerB = snapshotB.Players.FirstOrDefault(player => SameName(player.Name, playerName));
            double dpsA = playerA?.Dps ?? 0;
            double dpsB = playerB?.Dps ?? 0;
            long damageA = playerA?.Damage ?? 0;
            long damageB = playerB?.Damage ?? 0;
            double hpsA = playerA?.Hps ?? 0;
            double hpsB = playerB?.Hps ?? 0;

            Players.Add(new CombatComparisonPlayerRow
            {
                Name = playerName,
                ClassName = FirstNonEmpty(playerB?.ClassName, playerA?.ClassName, "Ismeretlen class"),
                DpsA = playerA is null ? "—" : Meter.Num(dpsA),
                DpsB = playerB is null ? "—" : Meter.Num(dpsB),
                DpsDelta = Signed(dpsB - dpsA),
                DamageDelta = Signed(damageB - damageA),
                HpsDelta = Signed(hpsB - hpsA),
                DpsDeltaBrush = DeltaBrush(dpsB - dpsA),
                DamageDeltaBrush = DeltaBrush(damageB - damageA),
                HpsDeltaBrush = DeltaBrush(hpsB - hpsA),
            });
        }

        AvaloniaXamlLoader.Load(this);
        DataContext = this;
    }

    private static string FormatMeta(CombatLogEntry fight)
    {
        int totalSeconds = Math.Max(0, (int)Math.Round(fight.Duration));
        string state = fight.Completed ? string.Empty : " · megszakított";
        return $"{fight.EndedAtUtc.ToLocalTime():yyyy.MM.dd. HH:mm:ss} · " +
               $"{totalSeconds / 60:00}:{totalSeconds % 60:00} · {fight.Players.Count} játékos{state}";
    }

    private static bool SameName(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string Signed(double value, string suffix = "")
    {
        string sign = value > 0 ? "+" : string.Empty;
        return $"{sign}{Meter.Num(value)}{suffix}";
    }

    private static IBrush DeltaBrush(double value) =>
        value > 0 ? PositiveBrush : value < 0 ? NegativeBrush : NeutralBrush;
}
