using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

// Egy sor a rangsorban (az overlay saját modellje)
public sealed class PartyRow
{
    public int Index { get; init; }
    public string Rank { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ClassLabel { get; init; } = string.Empty;
    public Bitmap? ClassIcon { get; init; }
    public IBrush ClassBrush { get; init; } = Brushes.Gray;
    public string Metric { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
    public string Share { get; init; } = string.Empty;
    public string Crit { get; init; } = string.Empty;
    public double Percent { get; init; }
    public bool ShowTotal { get; init; }
    public bool ShowShare { get; init; }
    public bool ShowCrit { get; init; }
}

public partial class DpsOverlayWindow : Window
{
    private static readonly MeterMode[] Modes =
    {
        MeterMode.Dps, MeterMode.Damage, MeterMode.Hps, MeterMode.Heal,
    };

    private static readonly double[] Opacities = { 1.0, 0.85, 0.7 };

    private MeterSnapshot _snapshot;
    private int _modeIndex;
    private int _opacityIndex;
    private bool _collapsed;
    private bool _showTotal = true;
    private bool _showShare = true;
    private bool _showCrit;

    public ObservableCollection<PartyRow> Rows { get; } = new();

    public DpsOverlayWindow() : this(new MeterSnapshot()) { }

    public DpsOverlayWindow(MeterSnapshot snapshot)
    {
        _snapshot = snapshot;
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        OverlayPlacementTracker.Attach(this, "dps-meter-compact-v2", "dps-meter");
        Opened += (_, _) => WindowClickThrough.Apply(this, enabled: false);
        LocalizationService.LanguageChanged += LanguageChanged;
        CompanionSettings.DpsEncounterModeChanged += EncounterModeChanged;
        Closed += (_, _) =>
        {
            LocalizationService.LanguageChanged -= LanguageChanged;
            CompanionSettings.DpsEncounterModeChanged -= EncounterModeChanged;
        };
        Refresh();
    }

    public void UpdateSnapshot(MeterSnapshot snapshot)
    {
        _snapshot = snapshot;
        Refresh();
    }

    // Régi hívás (UpdateDps) kompatibilitása.
    public void UpdateDps(IEnumerable<(string Name, string Class, long Damage, double Dps)> players)
    {
        var list = players.ToList();
        double duration = list
            .Where(p => p.Dps > 0)
            .Select(p => p.Damage / p.Dps)
            .DefaultIfEmpty(0)
            .Max();

        UpdateSnapshot(new MeterSnapshot
        {
            Duration = duration,
            IsDemo = false,
            Players = list.Select(p => new PlayerStat
            {
                Name = p.Name,
                Damage = p.Damage,
                Duration = duration,
            }).ToList(),
        });
    }

    private MeterMode Mode => Modes[_modeIndex];

    private void Refresh()
    {
        var mode = Mode;
        bool bossOnly = CompanionSettings.DpsEncounterMode == DpsEncounterMode.Bosses;
        var s = bossOnly
            ? _snapshot.IsBossEncounter ? _snapshot.ToBossOnly() : new MeterSnapshot()
            : _snapshot;
        bool heal = mode is MeterMode.Hps or MeterMode.Heal;
        bool rate = mode is MeterMode.Dps or MeterMode.Hps;
        double dur = Math.Max(1, s.Duration);

        // Csoport-szintű idősor a mód szerint (sebzés vagy gyógyítás)
        var groupSeries = GroupSeries(s, heal);
        double groupTotal = groupSeries.Sum();
        var rolling = Rolling(groupSeries, 5);

        // Összegzés
        double now = rolling.Length > 0 ? rolling[^1] : 0;
        double avg = groupTotal / dur;
        double peak = rolling.Length > 0 ? rolling.Max() : 0;
        var rateMode = heal ? MeterMode.Hps : MeterMode.Dps;

        this.FindControl<TextBlock>("NowValue")!.Text = Meter.Format(now, rateMode);
        this.FindControl<TextBlock>("AvgValue")!.Text = Meter.Format(avg, rateMode);
        this.FindControl<TextBlock>("PeakValue")!.Text = Meter.Format(peak, rateMode);
        this.FindControl<TextBlock>("TimerText")!.Text = FormatTime(s.Duration);
        this.FindControl<TextBlock>("MobName")!.Text =
            string.IsNullOrWhiteSpace(s.MobTemplateName) ? "—" : s.MobTemplateName;

        this.FindControl<Button>("AllEncountersButton")!.Classes.Set("primary", !bossOnly);
        this.FindControl<Button>("BossEncountersButton")!.Classes.Set("primary", bossOnly);
        this.FindControl<TextBlock>("EncounterModeHint")!.Text = LocalizationService.Get(
            bossOnly ? "BossTargetsOnly" : "AllTargets");

        Border bossPanel = this.FindControl<Border>("BossStatusPanel")!;
        bossPanel.IsVisible = s.IsBossEncounter;
        if (s.IsBossEncounter)
        {
            double percent = Math.Clamp(s.BossHpPercent ?? 0, 0, 100);
            this.FindControl<ProgressBar>("BossHpBar")!.Value = percent;
            this.FindControl<TextBlock>("BossHpText")!.Text = s.BossHpPercent.HasValue
                ? s.BossCurrentHp.HasValue && s.MobMaxHp > 0
                    ? $"{Meter.Num(s.BossCurrentHp.Value)} / {Meter.Num(s.MobMaxHp)}  ·  {percent:F1}%"
                    : $"{percent:F1}%"
                : s.MobMaxHp > 0
                    ? $"— / {Meter.Num(s.MobMaxHp)}"
                    : LocalizationService.Get("BossHpWaiting");

            this.FindControl<TextBlock>("EnrageText")!.Text = s.EnrageRemainingSeconds switch
            {
                <= 0 => LocalizationService.Get("Enraged"),
                double remaining => LocalizationService.Format("EnrageIn", FormatTime(remaining)),
                _ => LocalizationService.Get("EnrageUnknown"),
            };
        }

        // Rangsor
        var items = s.Players
            .Select((p, i) =>
            {
                var st = Stream(p, heal);
                double metric = rate ? st.Amount / dur : st.Amount;
                return (Player: p, Index: i, Amount: st.Amount, Hits: st.Hits, Crits: st.Crits, Metric: metric);
            })
            .OrderByDescending(x => x.Metric)
            .ToList();

        double top = items.Count > 0 ? items[0].Metric : 0;

        Rows.Clear();
        for (int r = 0; r < items.Count; r++)
        {
            var it = items[r];
            Rows.Add(new PartyRow
            {
                Index = it.Index,
                Rank = (r + 1).ToString(),
                Name = it.Player.Name,
                ClassLabel = string.IsNullOrWhiteSpace(it.Player.ClassName)
                    ? "?"
                    : it.Player.ClassName[..1].ToUpperInvariant(),
                ClassBrush = Brush.Parse(it.Player.ClassColor),
                ClassIcon = it.Player.ClassIcon,
                Metric = rate ? Meter.Format(it.Metric, rateMode) : Meter.Num(it.Metric),
                Total = Meter.Num(it.Amount),
                Share = groupTotal > 0 ? $"{it.Amount / groupTotal * 100:F0}%" : "–",
                Crit = it.Hits > 0 ? $"{(double)it.Crits / it.Hits * 100:F0}%" : "–",
                Percent = top > 0 ? it.Metric / top * 100 : 0,
                ShowTotal = _showTotal,
                ShowShare = _showShare,
                ShowCrit = _showCrit,
            });
        }

        // Fejléc és kapcsolók
        this.FindControl<TextBlock>("ModeLabel")!.Text = Meter.ModeName(mode);
        this.FindControl<TextBlock>("HdrMetric")!.Text = rate
            ? (heal ? "HPS" : "DPS")
            : LocalizationService.Get("Total");
        this.FindControl<TextBlock>("HdrTotal")!.IsVisible = _showTotal;
        this.FindControl<TextBlock>("HdrShare")!.IsVisible = _showShare;
        this.FindControl<TextBlock>("HdrCrit")!.IsVisible = _showCrit;

        this.FindControl<Button>("ToggleTotal")!.Classes.Set("primary", _showTotal);
        this.FindControl<Button>("ToggleShare")!.Classes.Set("primary", _showShare);
        this.FindControl<Button>("ToggleCrit")!.Classes.Set("primary", _showCrit);

        this.FindControl<TextBlock>("FooterText")!.Text = s.Players.Count == 0
            ? LocalizationService.Get(bossOnly ? "BossModeWaiting" : "DpsWaiting")
            : LocalizationService.Get("DpsDetailsHint");
    }

    private void LanguageChanged(object? sender, EventArgs e) => Refresh();

    private void EncounterModeChanged(object? sender, EventArgs e) => Refresh();

    // Csoport idősor: másodpercenként összesített sebzés (vagy gyógyítás)
    private static double[] GroupSeries(MeterSnapshot s, bool heal)
    {
        int n = Math.Max(1, (int)Math.Ceiling(s.Duration));
        var buckets = new double[n];

        foreach (var p in s.Players)
            foreach (var e in p.Events)
                if (e.IsHeal == heal)
                    buckets[Math.Clamp((int)e.Time, 0, n - 1)] += e.Amount;

        return buckets;
    }

    // Csúszóablak átlaga (5 mp), az élő DPS-hez
    private static double[] Rolling(double[] buckets, int window)
    {
        var rate = new double[buckets.Length];
        double sum = 0;
        for (int i = 0; i < buckets.Length; i++)
        {
            sum += buckets[i];
            if (i >= window)
                sum -= buckets[i - window];

            rate[i] = sum / Math.Min(i + 1, window);
        }
        return rate;
    }

    // Játékos összegei a mód szerint (sebzés vagy gyógyítás), találatok és krit számok
    private static (long Amount, int Hits, int Crits) Stream(PlayerStat p, bool heal)
    {
        int hits = 0;
        int crits = 0;

        foreach (var entry in p.Events)
        {
            if (entry.IsHeal != heal)
                continue;

            hits++;
            if (entry.Crit)
                crits++;
        }

        // Az összegzett számláló nem veszít adatot akkor sem, ha a részletes
        // eseménylista eléri a memóriavédelmi limitet és a régi elemek kiesnek.
        return (heal ? p.Healing : p.Damage, hits, crits);
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)seconds;
        return $"{total / 60:00}:{total % 60:00}";
    }

    // ---------- Vezérlők ----------

    private void PrevModeClick(object? sender, RoutedEventArgs e)
    {
        _modeIndex = (_modeIndex + Modes.Length - 1) % Modes.Length;
        Refresh();
    }

    private void NextModeClick(object? sender, RoutedEventArgs e)
    {
        _modeIndex = (_modeIndex + 1) % Modes.Length;
        Refresh();
    }

    private void ToggleTotalClick(object? sender, RoutedEventArgs e)
    {
        _showTotal = !_showTotal;
        Refresh();
    }

    private void ToggleShareClick(object? sender, RoutedEventArgs e)
    {
        _showShare = !_showShare;
        Refresh();
    }

    private void ToggleCritClick(object? sender, RoutedEventArgs e)
    {
        _showCrit = !_showCrit;
        Refresh();
    }

    private void EncounterModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode })
            return;
        CompanionSettings.DpsEncounterMode = mode.Equals("Bosses", StringComparison.OrdinalIgnoreCase)
            ? DpsEncounterMode.Bosses
            : DpsEncounterMode.All;
    }

    private void OpacityClick(object? sender, RoutedEventArgs e)
    {
        _opacityIndex = (_opacityIndex + 1) % Opacities.Length;
        Opacity = Opacities[_opacityIndex];
    }

    private void CollapseClick(object? sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        this.FindControl<Grid>("Body")!.IsVisible = !_collapsed;
        Height = _collapsed ? 132 : 408;
    }

    private void RowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: int index } &&
            index >= 0 && index < _snapshot.Players.Count)
        {
            OverlayWindowManager.ShowPlayerDetails(_snapshot.Players[index], _snapshot);
        }
    }

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
