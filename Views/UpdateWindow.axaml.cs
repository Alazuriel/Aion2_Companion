using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.ViewModels;
using AION_2_Companion.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace AION_2_Companion.Views;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly Button _installButton;
    private readonly Button _laterButton;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private bool _installing;
    private CancellationTokenSource? _downloadCancellation;

    public UpdateWindow()
        : this(new UpdateInfo(
            "0.0.0",
            "Nexforge AION 2 Companion",
            string.Empty,
            null,
            "https://github.com/Alazuriel/Aion2_Companion/releases",
            "Nexforge-AION2-Companion-Setup.exe",
            0,
            new string('0', 64),
            "https://github.com/Alazuriel/Aion2_Companion/releases"))
    {
    }

    public UpdateWindow(UpdateInfo update)
    {
        _update = update;
        AvaloniaXamlLoader.Load(this);

        this.FindControl<TextBlock>("ReleaseNameText")!.Text = update.Name;
        this.FindControl<TextBlock>("VersionBadgeText")!.Text = $"v{update.Version}";
        string meta = FormatSize(update.Size);
        if (update.PublishedAt is { } published)
            meta += $"  ·  {published.ToLocalTime():yyyy.MM.dd}";
        this.FindControl<TextBlock>("ReleaseMetaText")!.Text = meta;

        // GitHub returns Markdown, not formatted text. Render it as readable,
        // non-interactive headings, paragraphs, lists and code blocks without
        // a WebView or a new runtime dependency.
        FillReleaseNotes(this.FindControl<StackPanel>("ReleaseNotesPanel")!, update.Notes);

        _installButton = this.FindControl<Button>("InstallButton")!;
        _laterButton = this.FindControl<Button>("LaterButton")!;
        _progress = this.FindControl<ProgressBar>("DownloadProgress")!;
        _status = this.FindControl<TextBlock>("UpdateStatusText")!;
        Closing += (_, _) => _downloadCancellation?.Cancel();
    }

    private static void FillReleaseNotes(StackPanel panel, string? markdown)
    {
        panel.Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            AddNote(panel, LocalizationService.Get("UpdateNoNotes"), "#9FB4C6", 13);
            return;
        }

        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
                                 .Replace('\r', '\n').Split('\n');
        var paragraph = new StringBuilder();
        var code = new StringBuilder();
        bool insideCode = false;
        int added = 0;

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;
            AddNote(panel, PlainText(paragraph.ToString()), "#B6C9D8", 13, 0, 0);
            paragraph.Clear();
            added++;
        }

        void FlushCode()
        {
            if (code.Length == 0)
                return;
            var block = new Border
            {
                Background = Brush("#14273A"),
                BorderBrush = Brush("#31516C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12),
                Child = new SelectableTextBlock
                {
                    Text = code.ToString().TrimEnd(),
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    Foreground = Brush("#C6DDEB"),
                    TextWrapping = TextWrapping.Wrap,
                }
            };
            panel.Children.Add(block);
            code.Clear();
            added++;
        }

        foreach (string raw in lines)
        {
            if (added >= 220)
                break;
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                if (insideCode)
                    FlushCode();
                insideCode = !insideCode;
                continue;
            }
            if (insideCode)
            {
                code.AppendLine(raw);
                continue;
            }
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }
            if (Regex.IsMatch(line, @"^(?:-{3,}|\*{3,}|_{3,})$"))
            {
                FlushParagraph();
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brush("#294258"),
                    Margin = new Thickness(0, 5)
                });
                added++;
                continue;
            }
            Match heading = Regex.Match(line, @"^#{1,6}\s+(?<text>.+?)\s*#*$", RegexOptions.CultureInvariant);
            if (heading.Success)
            {
                FlushParagraph();
                AddNote(panel, PlainText(heading.Groups["text"].Value), "#F3F9FF", 15, 0, 9, FontWeight.SemiBold);
                added++;
                continue;
            }
            Match bullet = Regex.Match(line, @"^[-*+]\s+(?<text>.+)$", RegexOptions.CultureInvariant);
            if (bullet.Success)
            {
                FlushParagraph();
                AddNote(panel, "•  " + PlainText(bullet.Groups["text"].Value), "#B6C9D8", 13, 8, 0);
                added++;
                continue;
            }
            Match numbered = Regex.Match(line, @"^(?<number>\d+[.)])\s+(?<text>.+)$", RegexOptions.CultureInvariant);
            if (numbered.Success)
            {
                FlushParagraph();
                AddNote(panel, numbered.Groups["number"].Value + "  " + PlainText(numbered.Groups["text"].Value),
                    "#B6C9D8", 13, 8, 0);
                added++;
                continue;
            }
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                AddNote(panel, PlainText(line[2..]), "#8FB9D5", 13, 10, 0);
                added++;
                continue;
            }
            if (paragraph.Length > 0)
                paragraph.Append(' ');
            paragraph.Append(line);
        }

        FlushParagraph();
        FlushCode();
    }

    private static string PlainText(string text)
    {
        // Release bodies are untrusted Markdown; display as text (never HTML).
        string result = WebUtility.HtmlDecode(text.Trim());
        result = Regex.Replace(result, @"!\[(?<label>[^\]]*)\]\([^)]*\)", "${label}");
        result = Regex.Replace(result, @"\[(?<label>[^\]]+)\]\([^)]*\)", "${label}");
        result = result.Replace("**", "", StringComparison.Ordinal)
                       .Replace("__", "", StringComparison.Ordinal)
                       .Replace("`", "", StringComparison.Ordinal);
        return result;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private static void AddNote(StackPanel panel, string text, string color,
        double size, double left = 0, double top = 0, FontWeight weight = FontWeight.Normal)
    {
        panel.Children.Add(new SelectableTextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = Brush(color),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(left, top, 0, 0),
        });
    }

    private void LaterClick(object? sender, RoutedEventArgs e)
    {
        if (_installing)
        {
            _downloadCancellation?.Cancel();
            return;
        }

        Close();
    }

    private async void InstallClick(object? sender, RoutedEventArgs e)
    {
        if (_installing)
            return;

        // SYNTRA-UPDATER-PHASE1-CAPTURE-GUARD: no installation while capture is active.
        if (CaptureInProgress())
        {
            SetStatus(LocalizationService.Get("UpdatePauseCapture"), "#FFB454");
            return;
        }

        // Do not close the running Companion while it is monitoring a game.
        if (Owner is MainWindow mainWindow &&
            mainWindow.DataContext is MainWindowViewModel model && model.IsCapturing)
        {
            SetStatus(LocalizationService.Get("UpdatePauseCapture"), "#FFB454");
            return;
        }

        _installing = true;
        _downloadCancellation = new CancellationTokenSource();
        _installButton.IsEnabled = false;
        _laterButton.Content = LocalizationService.Get("UpdateCancelDownload");
        _laterButton.IsEnabled = true;
        _progress.IsVisible = true;
        SetStatus(LocalizationService.Get("UpdateDownloading"), "#45DFFF");

        try
        {
            var progress = new Progress<double>(value =>
                Dispatcher.UIThread.Post(() => _progress.Value = value * 100));
            string installer = await UpdateService.DownloadAsync(
                _update, progress, _downloadCancellation.Token);
            _downloadCancellation.Token.ThrowIfCancellationRequested();
            _laterButton.IsEnabled = false;
            SetStatus(LocalizationService.Get("UpdateLaunching"), "#58E0B2");
            // SYNTRA-UPDATER-PHASE1-FINAL-GUARD: capture may have started during download.
            if (CaptureInProgress())
            {
                SetStatus(LocalizationService.Get("UpdatePauseCapture"), "#FFB454");
                _installing = false;
                _installButton.IsEnabled = true;
                _laterButton.IsEnabled = true;
                _laterButton.Content = LocalizationService.Get("UpdateLater");
                _progress.IsVisible = false;
                return;
            }
            UpdateService.LaunchSilentInstaller(installer);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }
        catch (OperationCanceledException)
        {
            SetStatus(LocalizationService.Get("UpdateDownloadCanceled"), "#FFB454");
            ResetDownloadControls();
        }
        catch (Exception ex)
        {
            DebugConsole.Log("update", ex.Message);
            SetStatus(LocalizationService.Get("UpdateDownloadFailed"), "#F87171");
            ResetDownloadControls();
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    private void ResetDownloadControls()
    {
        _installing = false;
        _installButton.IsEnabled = true;
        _laterButton.IsEnabled = true;
        _laterButton.Content = LocalizationService.Get("UpdateLater");
        _progress.IsVisible = false;
    }

    private static bool CaptureInProgress() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
        desktop.MainWindow?.DataContext is MainWindowViewModel model && model.IsCapturing;

    private void SetStatus(string text, string color)
    {
        _status.Text = text;
        _status.Foreground = Brush(color);
    }

    private static string FormatSize(long bytes) =>
        $"{bytes / 1024d / 1024d:0.0} MB";
}
