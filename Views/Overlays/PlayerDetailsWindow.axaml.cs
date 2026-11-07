using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AION_2_Companion.Core;
using AION_2_Companion.Core.GameData;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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

    public PlayerDetailsWindow() : this(new PlayerStat(), new MeterSnapshot()) { }

    public PlayerDetailsWindow(PlayerStat player, MeterSnapshot snapshot)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        LocalizationService.LanguageChanged += LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LanguageChanged;
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

    // Class bitmaps are not embedded in the saved NSL/JSON snapshot. Prefer
    // PlayerStat.ClassIcon, then recover the NFG-defined bundled image.
    private static readonly Lazy<ClassDatabase?> HeaderClassDatabase = new(() =>
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData", "AION2_Class.nfg");
            return File.Exists(path) ? ClassDatabase.Load(path) : null;
        }
        catch
        {
            return null;
        }
    });

    private static readonly object HeaderIconLock = new();
    private static readonly Dictionary<string, Bitmap> HeaderIconCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static Bitmap? ResolveHeaderClassIcon(PlayerStat player)
    {
        if (player.ClassIcon is not null)
            return player.ClassIcon;

        var database = HeaderClassDatabase.Value;
        var definition = player.ClassId is >= 0 and <= byte.MaxValue
            ? database?.GetByJobValue((byte)player.ClassId)
            : null;
        if (definition is null && !string.IsNullOrWhiteSpace(player.ClassName))
        {
            definition = database?.GetByKey(player.ClassName.Trim())
                ?? database?.Classes.FirstOrDefault(item =>
                    string.Equals(item.Name, player.ClassName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (TryLoadHeaderIcon(definition?.Icon) is { } resolved)
            return resolved;

        string key = (definition?.Key ?? player.ClassName ?? string.Empty).Trim().ToLowerInvariant();
        // Restrict fallback to known built-in resources (never construct untrusted paths).
        return key switch
        {
            "assassin" or "chanter" or "cleric" or "elementalist"
                or "gladiator" or "ranger" or "sorcerer" or "templar" =>
                TryLoadHeaderIcon($"Data/Assets/Classes/{key}.png"),
            _ => null,
        };
    }

    private static Bitmap? TryLoadHeaderIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        string relative = path.Trim().Replace('\\', '/').TrimStart('/');
        if (relative.Contains("..", StringComparison.Ordinal) ||
            relative.Contains(":", StringComparison.Ordinal))
            return null;
        lock (HeaderIconLock)
        {
            if (HeaderIconCache.TryGetValue(relative, out var bitmap))
                return bitmap;
            try
            {
                var assembly = typeof(PlayerDetailsWindow).Assembly.GetName().Name;
                using var stream = AssetLoader.Open(new Uri($"avares://{assembly}/{relative}"));
                var loaded = new Bitmap(stream);
                HeaderIconCache.Add(relative, loaded);
                return loaded;
            }
            catch
            {
                return null;
            }
        }
    }

    private void FillHeader()
    {
        this.FindControl<Border>("DetailBadge")!.Background = Visuals.ForId(_player.ClassId);
        var badgeIcon = ResolveHeaderClassIcon(_player);
        var classImage = this.FindControl<Image>("DetailClassIcon")!;
        classImage.Source = badgeIcon;
        classImage.IsVisible = badgeIcon is not null;
        var badgeLabel = this.FindControl<TextBlock>("DetailBadgeText")!;
        badgeLabel.Text = string.IsNullOrWhiteSpace(_player.ClassName)
            ? "?" : _player.ClassName.Trim()[..1].ToUpperInvariant();
        badgeLabel.IsVisible = badgeIcon is null;
        this.FindControl<TextBlock>("DetailName")!.Text = _player.Name;

        int secs = (int)_snapshot.Duration;
        this.FindControl<TextBlock>("DetailMeta")!.Text = LocalizationService.Format(
            "PlayerFightMeta", $"{secs / 60:00}:{secs % 60:00}", _player.Events.Count);
        this.FindControl<TextBlock>("DetailDeathsCount")!.Text = PositionDetails.DeathsValue(_player);
        this.FindControl<TextBlock>("DetailFrontHits")!.Text = PositionDetails.FrontValue(_player);
        this.FindControl<TextBlock>("DetailBackHits")!.Text = PositionDetails.BackValue(_player);
        this.FindControl<TextBlock>("DetailUnknownHits")!.Text = PositionDetails.UnknownValue(_player);
        this.FindControl<TextBlock>("DetailPositionCoverage")!.Text =
            PositionDetails.CoverageNote(_player);

        this.FindControl<TextBlock>("DetailDps")!.Text = Meter.Format(_player.Dps, MeterMode.Dps);
        this.FindControl<TextBlock>("DetailDamage")!.Text = Meter.Num(_player.Damage);
        this.FindControl<TextBlock>("DetailPercent")!.Text =
            Meter.Percent(_player.Damage, _snapshot.MobMaxHp);
        this.FindControl<TextBlock>("DetailHeal")!.Text = Meter.Num(_player.Healing);
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        Skills.Clear();
        FillHeader();
        FillSkills();
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
