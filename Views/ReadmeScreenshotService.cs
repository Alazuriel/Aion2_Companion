using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AION_2_Companion.Views;

/// <summary>
/// Explicit, local-only README export. It is never executed by build or publish.
/// Rendering is done from the real Avalonia views and never captures the desktop.
/// </summary>
internal static class ReadmeScreenshotService
{
    private const int MainWidth = 1360;
    private const int MainHeight = 900;
    private static readonly string[] PreviewNames =
    [
        "Aetheria", "Forgeguard", "Nyxara", "Stormveil",
        "Lumira", "Kaelith", "Orialis", "Seraphine"
    ];

    internal static async Task<string> CaptureAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        string outputDirectory = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);

        window.WindowState = WindowState.Normal;
        window.Width = MainWidth;
        window.Height = MainHeight;
        await WaitForLayoutAsync();

        List<CombatLogEntry> fights = FindScreenshotFights()
            .Select(Anonymize)
            .ToList();
        if (fights.Count == 0)
            fights.Add(Anonymize(CreateFallbackFight()));
        CombatLogEntry fight = fights.First();
        MeterSnapshot snapshot = CreatePreviewSnapshot(fight);

        await CaptureDpsAsync(snapshot, outputDirectory);
        await CaptureCombatHistoryAsync(window, fights, outputDirectory);
        await CapturePlayerDetailsAsync(snapshot, outputDirectory);
        await CaptureCharacterCenterAsync(window, outputDirectory);
        await CaptureGroupPreviewAsync(fight, outputDirectory);
        await CaptureMainPageAsync(window, "Support", "diagnostics-center", outputDirectory);

        window.NavigateTo("Dashboard");
        return outputDirectory;
    }

    private static IReadOnlyList<CombatLogEntry> FindScreenshotFights()
    {
        CombatLogPage page = CombatLogStore.Shared.LoadPage(0, 200);
        List<CombatLogEntry> completed = page.Entries
            .Where(entry => entry.Completed && entry.Players.Count > 0)
            .OrderByDescending(entry => entry.EndedAtUtc)
            .Take(12)
            .ToList();
        CombatLogEntry? boss = completed.FirstOrDefault(entry => entry.IsBossEncounter);
        if (boss is not null)
        {
            completed.Remove(boss);
            completed.Insert(0, boss);
        }
        return completed;
    }

    private static async Task CaptureDpsAsync(MeterSnapshot snapshot, string outputDirectory)
    {
        var overlay = new DpsOverlayWindow(snapshot)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-20000, -20000),
        };
        overlay.Show();
        try
        {
            await WaitForLayoutAsync();
            await SavePairAsync(overlay, "dps-meter", "DPS METER", outputDirectory);
        }
        finally
        {
            overlay.Close();
        }
    }

    private static async Task CaptureCombatHistoryAsync(
        MainWindow window,
        IReadOnlyList<CombatLogEntry> fights,
        string outputDirectory)
    {
        window.NavigateTo("CombatLog");
        await WaitForLayoutAsync();
        if (window.FindControl<ContentControl>("PageHost")?.Content is CombatLogWindow log)
            log.LoadReadmeDemo(fights);
        await WaitForLayoutAsync();
        await SavePairAsync(window, "combat-history", "COMBAT HISTORY · ALL FIGHTS", outputDirectory);
    }

    private static async Task CapturePlayerDetailsAsync(MeterSnapshot snapshot, string outputDirectory)
    {
        PlayerStat? player = snapshot.Players.OrderByDescending(value => value.Damage).FirstOrDefault();
        if (player is null)
            return;

        var details = new PlayerDetailsWindow(player, snapshot)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-20000, -20000),
        };
        details.Show();
        try
        {
            await WaitForLayoutAsync();
            await SavePairAsync(details, "combat-details", "COMBAT DETAILS", outputDirectory);
        }
        finally
        {
            details.Close();
        }
    }

    private static async Task CaptureCharacterCenterAsync(MainWindow window, string outputDirectory)
    {
        CharacterProfile? profile = CharacterProfileService.Shared.Current;
        CharacterIdentity? identity = CharacterCenterService.Shared.CurrentSelf;
        if (profile is null && identity is not null)
        {
            CharacterLoadResult? cached = await CharacterCenterService.Shared
                .GetCachedProfileAsync(identity, "full", CancellationToken.None);
            cached ??= await CharacterCenterService.Shared
                .GetCachedProfileAsync(identity, "standard", CancellationToken.None);
            if (cached is not null)
                profile = CharacterProfileMapper.Map(cached);
        }

        if (profile is null)
        {
            DeletePair(outputDirectory, "character-center");
            return;
        }

        CharacterProfileService.Shared.Replace(profile with
        {
            Name = "Aetheria",
            ServerName = "EU Preview",
            GuildName = null,
        });
        window.NavigateTo("CharacterDetails");
        await WaitForLayoutAsync();
        await SavePairAsync(window, "character-center", "CHARACTER CENTER", outputDirectory);
        CharacterProfileService.Shared.Clear();
    }

    private static async Task CaptureGroupPreviewAsync(CombatLogEntry fight, string outputDirectory)
    {
        var dialog = new LeaderboardSubmissionDialog(fight)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-20000, -20000),
        };
        dialog.ShowGroupPreviewForReadme();
        dialog.Show();
        try
        {
            await WaitForLayoutAsync();
            await SavePairAsync(dialog, "leaderboards", "GROUP FIGHT PREVIEW", outputDirectory);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static async Task CaptureMainPageAsync(
        MainWindow window,
        string page,
        string fileStem,
        string outputDirectory)
    {
        window.NavigateTo(page);
        await WaitForLayoutAsync();
        await SavePairAsync(window, fileStem, fileStem.Replace('-', ' ').ToUpperInvariant(), outputDirectory);
    }

    private static async Task SavePairAsync(
        Control control,
        string stem,
        string title,
        string outputDirectory)
    {
        string cutout = Path.Combine(outputDirectory, $"readme-{stem}-cutout.png");
        string showcase = Path.Combine(outputDirectory, $"readme-{stem}-showcase.png");
        await SaveControlAsync(control, cutout);
        CreateShowcase(cutout, showcase, title);
    }

    private static void DeletePair(string outputDirectory, string stem)
    {
        foreach (string suffix in new[] { "cutout", "showcase" })
        {
            string path = Path.Combine(outputDirectory, $"readme-{stem}-{suffix}.png");
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static async Task SaveControlAsync(Control control, string path)
    {
        bool wasHitTestVisible = control.IsHitTestVisible;
        control.IsHitTestVisible = false;
        try
        {
            await WaitForLayoutAsync();
            double scale = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1d;
            PixelSize size = PixelSize.FromSize(control.Bounds.Size, scale);
            if (size.Width <= 0 || size.Height <= 0)
                throw new InvalidOperationException("The view has no renderable surface.");

            using var bitmap = new RenderTargetBitmap(size, new Vector(96d * scale, 96d * scale));
            bitmap.Render(control);
            bitmap.Save(path);
        }
        finally
        {
            control.IsHitTestVisible = wasHitTestVisible;
        }
    }

    private static void CreateShowcase(string sourcePath, string outputPath, string title)
    {
        const int width = 1600;
        const int height = 1000;
        using var source = new System.Drawing.Bitmap(sourcePath);
        using var output = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(output);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var background = new LinearGradientBrush(
                   new System.Drawing.Rectangle(0, 0, width, height),
                   System.Drawing.Color.FromArgb(5, 10, 18),
                   System.Drawing.Color.FromArgb(12, 29, 45),
                   28f))
            graphics.FillRectangle(background, 0, 0, width, height);

        using (var cyanGlow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(26, 44, 220, 255)))
            graphics.FillEllipse(cyanGlow, -220, -260, 900, 760);
        using (var goldGlow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(18, 232, 175, 93)))
            graphics.FillEllipse(goldGlow, 1110, 610, 720, 620);

        float scale = Math.Min((width - 150f) / source.Width, (height - 190f) / source.Height);
        scale = Math.Min(scale, source.Width < 700 ? 1.85f : 1f);
        int drawWidth = (int)Math.Round(source.Width * scale);
        int drawHeight = (int)Math.Round(source.Height * scale);
        int x = (width - drawWidth) / 2;
        int y = 105 + (height - 145 - drawHeight) / 2;

        using (var shadow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(115, 0, 0, 0)))
            graphics.FillRectangle(shadow, x - 18, y + 20, drawWidth + 36, drawHeight + 26);
        using (var frame = new System.Drawing.Pen(System.Drawing.Color.FromArgb(130, 69, 223, 255), 2f))
            graphics.DrawRectangle(frame, x - 2, y - 2, drawWidth + 4, drawHeight + 4);
        graphics.DrawImage(source, new System.Drawing.Rectangle(x, y, drawWidth, drawHeight));

        using var labelFont = new System.Drawing.Font("Segoe UI", 13, System.Drawing.FontStyle.Bold);
        using var titleFont = new System.Drawing.Font("Segoe UI", 22, System.Drawing.FontStyle.Bold);
        using var cyan = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(69, 223, 255));
        using var white = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(236, 247, 255));
        graphics.DrawString("NEXFORGE // AION 2 COMPANION", labelFont, cyan, 72, 34);
        graphics.DrawString(title, titleFont, white, 70, 60);

        output.Save(outputPath, ImageFormat.Png);
    }

    private static CombatLogEntry Anonymize(CombatLogEntry source)
    {
        return new CombatLogEntry
        {
            SchemaVersion = source.SchemaVersion,
            Id = "readme-preview",
            StartedAtUtc = source.StartedAtUtc,
            EndedAtUtc = source.EndedAtUtc,
            Completed = source.Completed,
            IsFavorite = source.IsFavorite,
            Notes = string.Empty,
            MobName = source.MobName,
            ServerId = source.ServerId,
            ServerName = "EU Preview",
            Duration = source.Duration,
            MobMaxHp = source.MobMaxHp,
            MobTemplateId = source.MobTemplateId,
            IsBossEncounter = source.IsBossEncounter,
            BossId = source.BossId,
            EnrageSeconds = source.EnrageSeconds,
            BossStartOffset = source.BossStartOffset,
            BossDuration = source.BossDuration,
            Mobs = source.Mobs.Select(mob => new StoredEncounterMob
            {
                TemplateId = mob.TemplateId,
                Name = mob.Name,
                Count = mob.Count,
            }).ToList(),
            Players = source.Players.Select((player, index) => new StoredPlayerStat
            {
                Name = SafePreviewName(player.Name, index),
                ClassId = player.ClassId,
                ClassName = player.ClassName,
                ClassColor = player.ClassColor,
                Damage = player.Damage,
                BossDamage = player.BossDamage,
                Healing = player.Healing,
                BossHealing = player.BossHealing,
                DeathCount = player.DeathCount,
                FrontHits = player.FrontHits,
                BackHits = player.BackHits,
                UnknownHits = player.UnknownHits,
                Events = player.Events.Select(CloneEvent).ToList(),
            }).ToList(),
        };
    }

    private static string SafePreviewName(string original, int index)
    {
        string candidate = index < PreviewNames.Length ? PreviewNames[index] : $"Nexforge-{index + 1:00}";
        return candidate.Equals(original, StringComparison.OrdinalIgnoreCase)
            ? candidate + "-Preview"
            : candidate;
    }

    private static StoredSkillEvent CloneEvent(StoredSkillEvent value) => new()
    {
        Time = value.Time,
        Skill = value.Skill,
        SkillIcon = value.SkillIcon,
        Amount = value.Amount,
        Crit = value.Crit,
        IsHeal = value.IsHeal,
        TargetRuntimeId = value.TargetRuntimeId,
        TargetTemplateId = value.TargetTemplateId,
        IsBossTarget = value.IsBossTarget,
        RawDirectionCode = value.RawDirectionCode,
        IsDot = value.IsDot,
    };

    private static MeterSnapshot CreatePreviewSnapshot(CombatLogEntry fight)
    {
        MeterSnapshot source = fight.ToSnapshot(fight.IsBossEncounter);
        return new MeterSnapshot
        {
            Players = source.Players,
            Mobs = source.Mobs,
            ServerId = source.ServerId,
            ServerName = source.ServerName,
            Duration = source.Duration,
            MobMaxHp = source.MobMaxHp,
            MobTemplateName = source.MobTemplateName,
            MobTemplateId = source.MobTemplateId,
            IsBossEncounter = source.IsBossEncounter,
            BossId = source.BossId,
            BossHpPercent = source.IsBossEncounter ? 37.4 : null,
            BossCurrentHp = source.MobMaxHp > 0 ? (long)(source.MobMaxHp * .374) : null,
            EstimatedDeathSeconds = source.IsBossEncounter ? 48 : null,
            EnrageSeconds = source.EnrageSeconds,
            EnrageRemainingSeconds = source.EnrageRemainingSeconds,
            BossStartOffset = source.BossStartOffset,
            BossDuration = source.BossDuration,
        };
    }

    private static CombatLogEntry CreateFallbackFight()
    {
        const double duration = 94;
        var players = new[]
        {
            ("Aetheria", "Gladiator", "#E0786A", 7_820_000L, 45_000L),
            ("Forgeguard", "Sorcerer", "#7FA7E0", 6_940_000L, 12_000L),
            ("Nyxara", "Assassin", "#B98BC9", 5_760_000L, 8_000L),
            ("Lumira", "Cleric", "#8FBFA8", 2_310_000L, 3_480_000L),
        };
        return new CombatLogEntry
        {
            Id = "readme-fallback",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
            EndedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Completed = true,
            IsBossEncounter = true,
            MobName = "Training Construct",
            ServerId = 1,
            ServerName = "EU Preview",
            Duration = duration,
            BossDuration = duration,
            MobMaxHp = 28_000_000,
            EnrageSeconds = 360,
            Mobs = [new StoredEncounterMob { TemplateId = 1, Name = "Training Construct", Count = 1 }],
            Players = players.Select((player, playerIndex) => new StoredPlayerStat
            {
                Name = player.Item1,
                ClassName = player.Item2,
                ClassColor = player.Item3,
                Damage = player.Item4,
                BossDamage = player.Item4,
                Healing = player.Item5,
                BossHealing = player.Item5,
                DeathCount = playerIndex == 2 ? 1 : 0,
                Events = Enumerable.Range(1, 18).Select(i => new StoredSkillEvent
                {
                    Time = i * 5,
                    Skill = "Recorded skill",
                    Amount = player.Item4 / 18,
                    Crit = i % 4 == 0,
                    IsBossTarget = true,
                    TargetTemplateId = 1,
                }).Concat(player.Item5 > 50_000 ? Enumerable.Range(1, 12).Select(i => new StoredSkillEvent
                {
                    Time = i * 7,
                    Skill = "Recorded heal",
                    Amount = player.Item5 / 12,
                    IsHeal = true,
                }) : []).ToList(),
            }).ToList(),
        };
    }

    private static async Task WaitForLayoutAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        await Task.Delay(220);
    }

    private static string ResolveOutputDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return Path.Combine(directory.FullName, "Assets", "README");
        }

        return Path.Combine(AppContext.BaseDirectory, "README_ASSETS");
    }
}
