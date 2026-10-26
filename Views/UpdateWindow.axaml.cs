using System;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
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
        this.FindControl<TextBlock>("ReleaseMetaText")!.Text =
            $"v{update.Version}  ·  {FormatSize(update.Size)}";
        this.FindControl<SelectableTextBlock>("ReleaseNotesText")!.Text =
            string.IsNullOrWhiteSpace(update.Notes)
                ? LocalizationService.Get("UpdateNoNotes")
                : update.Notes;

        _installButton = this.FindControl<Button>("InstallButton")!;
        _laterButton = this.FindControl<Button>("LaterButton")!;
        _progress = this.FindControl<ProgressBar>("DownloadProgress")!;
        _status = this.FindControl<TextBlock>("UpdateStatusText")!;
    }

    private void LaterClick(object? sender, RoutedEventArgs e)
    {
        if (!_installing)
            Close();
    }

    private async void InstallClick(object? sender, RoutedEventArgs e)
    {
        if (_installing)
            return;

        _installing = true;
        _installButton.IsEnabled = false;
        _laterButton.IsEnabled = false;
        _progress.IsVisible = true;
        SetStatus(LocalizationService.Get("UpdateDownloading"), "#45DFFF");

        try
        {
            var progress = new Progress<double>(value =>
                Dispatcher.UIThread.Post(() => _progress.Value = value * 100));
            string installer = await UpdateService.DownloadAsync(_update, progress);
            SetStatus(LocalizationService.Get("UpdateLaunching"), "#58E0B2");
            UpdateService.LaunchSilentInstaller(installer);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }
        catch (Exception ex)
        {
            _installing = false;
            _installButton.IsEnabled = true;
            _laterButton.IsEnabled = true;
            _progress.IsVisible = false;
            DebugConsole.Log("update", ex.Message);
            SetStatus(LocalizationService.Get("UpdateDownloadFailed"), "#F87171");
        }
    }

    private void SetStatus(string text, string color)
    {
        _status.Text = text;
        _status.Foreground = new SolidColorBrush(Color.Parse(color));
    }

    private static string FormatSize(long bytes) =>
        $"{bytes / 1024d / 1024d:0.0} MB";
}
