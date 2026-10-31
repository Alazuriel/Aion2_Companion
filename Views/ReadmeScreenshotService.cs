using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Views.Overlays;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AION_2_Companion.Views;

internal static class ReadmeScreenshotService
{
    internal static async Task<string> CaptureAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        string outputDirectory = ResolveOutputDirectory();
        Directory.CreateDirectory(outputDirectory);

        window.WindowState = WindowState.Normal;
        window.Width = 1360;
        window.Height = 900;
        await WaitForLayoutAsync();

        var pages = new List<(string Page, string File)>
        {
            ("Dashboard", "dashboard.png"),
            ("CombatLog", "fight-history.png"),
            ("Settings", "settings.png"),
        };

        if (AccountService.Shared.Snapshot.State != AccountConnectionState.Connected)
            pages.Add(("Account", "account.png"));

        foreach ((string page, string file) in pages)
        {
            window.NavigateTo(page);
            await WaitForLayoutAsync();
            await SaveControlAsync(window, Path.Combine(outputDirectory, file));
        }

        CombatLogEntry? fight = CombatLogStore.Shared.LoadAll()
            .FirstOrDefault(entry => entry.Completed && entry.IsBossEncounter)
            ?? CombatLogStore.Shared.LoadAll().FirstOrDefault(entry => entry.Completed);
        if (fight is not null)
        {
            OverlayWindowManager.ShowDps(fight.ToSnapshot(fight.IsBossEncounter));
            await WaitForLayoutAsync();
            if (OverlayWindowManager.DpsWindow is { } overlay)
            {
                string dpsPath = Path.Combine(outputDirectory, "dps-meter.png");
                await SaveControlAsync(overlay, dpsPath);
                File.Copy(dpsPath, Path.Combine(outputDirectory, "overlay.png"), overwrite: true);
                overlay.Hide();
            }
        }

        window.NavigateTo("Dashboard");
        return outputDirectory;
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
                throw new InvalidOperationException("A képernyőképhez nincs megjeleníthető felület.");

            using var bitmap = new RenderTargetBitmap(
                size,
                new Vector(96d * scale, 96d * scale));
            bitmap.Render(control);
            bitmap.Save(path);
        }
        finally
        {
            control.IsHitTestVisible = wasHitTestVisible;
        }
    }

    private static async Task WaitForLayoutAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Loaded);
        await Task.Delay(180);
    }

    private static string ResolveOutputDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return Path.Combine(directory.FullName, "docs", "images");
            }
        }

        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(pictures, "Nexforge", "AION 2 Companion", "README Screenshots");
    }
}
