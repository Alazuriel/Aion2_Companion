using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AION_2_Companion.Views;

public partial class DiagnosticsView : UserControl
{
    private readonly CheckBox _includeCombatRecordsToggle;
    private readonly Button _exportButton;
    private readonly TextBlock _exportStatusText;
    private readonly TextBlock _monitoringStatusText;

    public DiagnosticsView()
    {
        AvaloniaXamlLoader.Load(this);
        _includeCombatRecordsToggle = this.FindControl<CheckBox>("IncludeCombatRecordsToggle")!;
        _exportButton = this.FindControl<Button>("ExportButton")!;
        _exportStatusText = this.FindControl<TextBlock>("ExportStatusText")!;
        _monitoringStatusText = this.FindControl<TextBlock>("MonitoringStatusText")!;

        AttachedToVisualTree += (_, _) => UpdateMonitoringStatus();
        LocalizationService.LanguageChanged += LocalizationChanged;
        DetachedFromVisualTree += (_, _) => LocalizationService.LanguageChanged -= LocalizationChanged;
    }

    private void LocalizationChanged(object? sender, EventArgs e) => UpdateMonitoringStatus();

    private void UpdateMonitoringStatus()
    {
        bool active = (DataContext as MainWindowViewModel)?.IsCapturing == true;
        _monitoringStatusText.Text = LocalizationService.Get(
            active ? "SupportStatusRunning" : "SupportStatusStopped");
        _monitoringStatusText.Foreground = new SolidColorBrush(
            Color.Parse(active ? "#58E0B2" : "#FFB454"));
    }

    private async void ExportClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = LocalizationService.Get("SupportExport"),
                SuggestedFileName = $"nexforge-support-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType(LocalizationService.Get("SupportFileType"))
                    {
                        Patterns = ["*.zip"],
                        MimeTypes = ["application/zip"],
                    },
                ],
            });

        if (file is null)
        {
            SetStatus(LocalizationService.Get("SupportCancelled"), "#9FB1C1");
            return;
        }

        _exportButton.IsEnabled = false;
        SetStatus(LocalizationService.Get("SupportExporting"), "#45DFFF");

        try
        {
            await using var stream = await file.OpenWriteAsync();
            bool active = (DataContext as MainWindowViewModel)?.IsCapturing == true;
            await SupportExportService.CreateAsync(
                stream,
                _includeCombatRecordsToggle.IsChecked == true,
                active);
            SetStatus(LocalizationService.Get("SupportSuccess"), "#58E0B2");
        }
        catch (Exception ex)
        {
            SetStatus(
                string.Format(LocalizationService.Get("SupportFailure"), ex.Message),
                "#F87171");
        }
        finally
        {
            _exportButton.IsEnabled = true;
        }
    }

    private void SetStatus(string text, string color)
    {
        _exportStatusText.Text = text;
        _exportStatusText.Foreground = new SolidColorBrush(Color.Parse(color));
    }
}
