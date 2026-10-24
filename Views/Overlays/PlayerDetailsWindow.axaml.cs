using System;
using System.Collections.ObjectModel;
using System.Linq;
using AION_2_Companion.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AION_2_Companion.Views.Overlays;

// Egy skill sora a részletes táblában
public sealed class SkillBreakRow
{
    public string SkillIcon { get; set; } = string.Empty;   // path / url
    public Bitmap? SkillIconBitmap { get; set; }
    public string Name { get; init; } = string.Empty;
    public string Initial { get; init; } = string.Empty;
    public IBrush Brush { get; init; } = Brushes.Gray;
    public string Dps { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
    public string Share { get; init; } = string.Empty;
    public string Crit { get; init; } = string.Empty;
    public string Hits { get; init; } = string.Empty;
    public double Percent { get; init; }
}

public partial class PlayerDetailsWindow : Window
{
    private PlayerStat _player = null!;
    private MeterSnapshot _snapshot = null!;

    public ObservableCollection<SkillBreakRow> Skills { get; } = new();

    public PlayerDetailsWindow() : this(DemoData.Build().Players[0], DemoData.Build()) { }

    public PlayerDetailsWindow(PlayerStat player, MeterSnapshot snapshot)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        UpdatePlayer(player, snapshot);
    }

    public void UpdatePlayer(PlayerStat player, MeterSnapshot snapshot)
    {
        _player = player;
        _snapshot = snapshot;
        Skills.Clear();
        FillHeader();
        FillSkills();
    }

    private void FillHeader()
    {
        this.FindControl<Border>("DetailBadge")!.Background = Visuals.ForId(_player.ClassId);
        this.FindControl<TextBlock>("DetailBadgeText")!.Text = _player.ClassId.ToString();
        this.FindControl<TextBlock>("DetailName")!.Text = _player.Name;

        int secs = (int)_snapshot.Duration;
        this.FindControl<TextBlock>("DetailMeta")!.Text =
            $"Harc {secs / 60:00}:{secs % 60:00}  ·  {_player.Events.Count} esemény";

        this.FindControl<TextBlock>("DetailDps")!.Text = Meter.Format(_player.Dps, MeterMode.Dps);
        this.FindControl<TextBlock>("DetailDamage")!.Text = Meter.Num(_player.Damage);
        this.FindControl<TextBlock>("DetailPercent")!.Text =
            Meter.Percent(_player.Damage, _snapshot.MobMaxHp);
        this.FindControl<TextBlock>("DetailHeal")!.Text = Meter.Num(_player.Healing);
    }

    private void FillSkills()
    {
        double dur = Math.Max(1, _snapshot.Duration);

        var groups = _player.Events
            .Where(e => !e.IsHeal)
            .GroupBy(e => e.Skill)
            .Select(g => new
            {
                Name = g.Key,
                Icon = g.Select(e => e.SkillIcon)
                           .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                       ?? string.Empty,
                Total = g.Sum(e => e.Amount),
                Hits = g.Count(),
                Crits = g.Count(e => e.Crit),
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        long grand = groups.Sum(g => g.Total);

        foreach (var g in groups)
        {
            double share = grand > 0 ? (double)g.Total / grand * 100 : 0;
            double critRate = g.Hits > 0 ? (double)g.Crits / g.Hits * 100 : 0;

            Skills.Add(new SkillBreakRow
            {
                Name = g.Name,
                SkillIcon = g.Icon,
                SkillIconBitmap = SkillIconLoader.Load(g.Icon),
                Initial = g.Name.Length > 0 ? g.Name[..1].ToUpperInvariant() : "?",
                Brush = Visuals.ForText(g.Name),
                Dps = Meter.Format(g.Total / dur, MeterMode.Dps),
                Total = Meter.Num(g.Total),
                Share = $"{share:F0}%",
                Crit = $"{critRate:F0}%",
                Hits = g.Hits.ToString(),
                Percent = share,
            });
        }
    }

    private void TimelineClick(object? sender, RoutedEventArgs e) =>
        OverlayWindowManager.ShowTimeline(_player, _snapshot);

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
