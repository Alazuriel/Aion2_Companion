using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Map.Pets;
using AION_2_Companion.Core.EventTimers;
using AION_2_Companion.Core.GameData;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AION_2_Companion.Views;

public partial class SettingsView : UserControl
{
    private readonly CheckBox _mapHelperToggle;
    private readonly Border _mapAdminWarning;
    private readonly CheckBox _fileLoggingToggle;
    private readonly TextBlock _fileLoggingHint;
    private readonly CheckBox _autoDpsOverlayToggle;
    private readonly CheckBox _autoCaptureToggle;
    private readonly CheckBox _autoUpdateToggle;
    private readonly ComboBox _combatLogLimitCombo;
    private readonly ComboBox _closeActionCombo;
    private readonly ComboBox _languageCombo;
    private readonly TextBlock _petCollectionStatusText;
    private readonly Ellipse _petCollectionStatusDot;
    private readonly Button _checkUpdateButton;
    private readonly TextBlock _updateCheckStatusText;
    private readonly ComboBox _eventTimerRegionCombo;
    private readonly CheckBox _eventTimerClickThroughToggle;
    private readonly ComboBox _dpsEncounterModeCombo;
    private readonly TextBlock _forgeDataStatusText;
    private readonly TextBlock _forgeDataPathText;
    private readonly Ellipse _forgeDataStatusDot;
    private readonly Button _testLoginButton;

    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);

        _mapHelperToggle = this.FindControl<CheckBox>("MapHelperToggle")!;
        _mapAdminWarning = this.FindControl<Border>("MapAdminWarning")!;
        _fileLoggingToggle = this.FindControl<CheckBox>("FileLoggingToggle")!;
        _fileLoggingHint = this.FindControl<TextBlock>("FileLoggingHint")!;
        _autoDpsOverlayToggle = this.FindControl<CheckBox>("AutoDpsOverlayToggle")!;
        _autoCaptureToggle = this.FindControl<CheckBox>("AutoCaptureToggle")!;
        _autoUpdateToggle = this.FindControl<CheckBox>("AutoUpdateToggle")!;
        _combatLogLimitCombo = this.FindControl<ComboBox>("CombatLogLimitCombo")!;
        _closeActionCombo = this.FindControl<ComboBox>("CloseActionCombo")!;
        _languageCombo = this.FindControl<ComboBox>("LanguageCombo")!;
        _petCollectionStatusText = this.FindControl<TextBlock>("PetCollectionStatusText")!;
        _petCollectionStatusDot = this.FindControl<Ellipse>("PetCollectionStatusDot")!;
        _checkUpdateButton = this.FindControl<Button>("CheckUpdateButton")!;
        _updateCheckStatusText = this.FindControl<TextBlock>("UpdateCheckStatusText")!;
        _eventTimerRegionCombo = this.FindControl<ComboBox>("EventTimerRegionCombo")!;
        _eventTimerClickThroughToggle = this.FindControl<CheckBox>("EventTimerClickThroughToggle")!;
        _dpsEncounterModeCombo = this.FindControl<ComboBox>("DpsEncounterModeCombo")!;
        _forgeDataStatusText = this.FindControl<TextBlock>("ForgeDataStatusText")!;
        _forgeDataPathText = this.FindControl<TextBlock>("ForgeDataPathText")!;
        _forgeDataStatusDot = this.FindControl<Ellipse>("ForgeDataStatusDot")!;
        _testLoginButton = this.FindControl<Button>("TestLoginButton")!;
        if (Program.DeveloperMode)
        {
            _testLoginButton.IsEnabled = true;
            _testLoginButton.Content = LocalizationService.Get("TestLoginAction");
            ToolTip.SetTip(_testLoginButton, LocalizationService.Get("TestLoginDeveloperHint"));
        }
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{ChangelogService.GetCurrentVersion()}";
        this.FindControl<Border>("DeveloperSettingsPanel")!.IsVisible = Program.DeveloperMode;

        foreach (LanguageOption language in LocalizationService.SupportedLanguages)
        {
            _languageCombo.Items.Add(new ComboBoxItem
            {
                Content = language.DisplayName,
                Tag = language.Code,
            });
        }

        SelectLanguage(CompanionSettings.LanguageCode);

        bool isAdministrator = AdministratorAccess.IsRunningAsAdministrator;

        if (isAdministrator)
        {
            _mapHelperToggle.IsChecked = CompanionSettings.MapHelperEnabled;
        }
        else
        {
            CompanionSettings.MapHelperEnabled = false;
            _mapHelperToggle.IsChecked = false;
            _mapHelperToggle.IsEnabled = false;
            _mapAdminWarning.IsVisible = true;
        }

        _fileLoggingToggle.IsChecked = DebugConsole.FileLoggingEnabled;
        _autoDpsOverlayToggle.IsChecked = CompanionSettings.AutoManageDpsOverlay;
        _autoCaptureToggle.IsChecked = CompanionSettings.AutoCaptureEnabled;
        _autoUpdateToggle.IsChecked = CompanionSettings.CheckUpdatesAutomatically;
        SelectCombatLogLimit(CompanionSettings.CombatLogLimit);
        SelectDpsEncounterMode(CompanionSettings.DpsEncounterMode);
        SelectCloseAction(CompanionSettings.CloseAction);

        EventTimerService.Shared.Start();
        foreach (EventTimerRegionDefinition region in EventTimerService.Shared.Regions)
        {
            _eventTimerRegionCombo.Items.Add(new ComboBoxItem
            {
                Content = region.LocalizedName(LocalizationService.CurrentLanguageCode),
                Tag = region.Id,
            });
        }
        SelectEventTimerRegion(CompanionSettings.EventTimerRegion);
        _eventTimerClickThroughToggle.IsChecked = CompanionSettings.EventTimerOverlayClickThrough;

        _mapHelperToggle.IsCheckedChanged += MapHelperChanged;
        _fileLoggingToggle.IsCheckedChanged += FileLoggingChanged;
        _autoDpsOverlayToggle.IsCheckedChanged += AutoDpsOverlayChanged;
        _autoCaptureToggle.IsCheckedChanged += AutoCaptureChanged;
        _autoUpdateToggle.IsCheckedChanged += AutoUpdateChanged;
        _combatLogLimitCombo.SelectionChanged += CombatLogLimitChanged;
        _closeActionCombo.SelectionChanged += CloseActionChanged;
        _languageCombo.SelectionChanged += LanguageChanged;
        _eventTimerRegionCombo.SelectionChanged += EventTimerRegionChanged;
        _eventTimerClickThroughToggle.IsCheckedChanged += EventTimerClickThroughChanged;
        _dpsEncounterModeCombo.SelectionChanged += DpsEncounterModeChanged;
        _testLoginButton.Click += StartTestLoginClick;

        UpdateFileLoggingHint();
        UpdatePetCollectionStatus();
        UpdateForgeDataStatus();

        AttachedToVisualTree += (_, _) =>
        {
            PetCollectionBridge.Changed -= PetCollectionChanged;
            PetCollectionBridge.Changed += PetCollectionChanged;
            UpdatePetCollectionStatus();
        };
        DetachedFromVisualTree += (_, _) =>
            PetCollectionBridge.Changed -= PetCollectionChanged;
    }

    private void OpenDpsClick(object? sender, RoutedEventArgs e) =>
        Overlays.OverlayWindowManager.ToggleDps();

    private void StartTestLoginClick(object? sender, RoutedEventArgs e)
    {
        if (Program.DeveloperMode && TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            mainWindow.StartTestLogin();
    }

    private void OpenEventTimerOverlayClick(object? sender, RoutedEventArgs e) =>
        Overlays.OverlayWindowManager.ToggleEventTimers();

    private void EventTimerRegionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_eventTimerRegionCombo.SelectedItem is ComboBoxItem { Tag: string region })
            CompanionSettings.EventTimerRegion = region;
    }

    private void EventTimerClickThroughChanged(object? sender, RoutedEventArgs e) =>
        CompanionSettings.EventTimerOverlayClickThrough = _eventTimerClickThroughToggle.IsChecked == true;

    private void DpsEncounterModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_dpsEncounterModeCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out int value) &&
            Enum.IsDefined(typeof(DpsEncounterMode), value))
        {
            CompanionSettings.DpsEncounterMode = (DpsEncounterMode)value;
        }
    }

    private void SelectDpsEncounterMode(DpsEncounterMode mode)
    {
        foreach (object? item in _dpsEncounterModeCombo.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } &&
                int.TryParse(tag, out int value) && value == (int)mode)
            {
                _dpsEncounterModeCombo.SelectedItem = item;
                return;
            }
        }

        _dpsEncounterModeCombo.SelectedIndex = 0;
    }

    private void SelectEventTimerRegion(string region)
    {
        foreach (object? item in _eventTimerRegionCombo.Items)
        {
            if (item is ComboBoxItem { Tag: string code } &&
                code.Equals(region, StringComparison.OrdinalIgnoreCase))
            {
                _eventTimerRegionCombo.SelectedItem = item;
                return;
            }
        }

        if (_eventTimerRegionCombo.ItemCount > 0)
            _eventTimerRegionCombo.SelectedIndex = 0;
    }

    private void RefreshEventTimerRegionNames()
    {
        foreach (ComboBoxItem item in _eventTimerRegionCombo.Items.OfType<ComboBoxItem>())
        {
            EventTimerRegionDefinition? region = EventTimerService.Shared.Regions.FirstOrDefault(value =>
                value.Id.Equals(item.Tag as string, StringComparison.OrdinalIgnoreCase));
            if (region is not null)
                item.Content = region.LocalizedName(LocalizationService.CurrentLanguageCode);
        }
    }

    private void RestartTutorialClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            mainWindow.StartOnboarding(manual: true);
    }

    private void OpenNexforgeClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://github.com/Alazuriel/Aion2_Companion") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugConsole.Log("navigation", $"Nexforge link could not be opened: {ex.Message}");
        }
    }

    private void MapHelperChanged(object? sender, RoutedEventArgs e)
    {
        CompanionSettings.MapHelperEnabled = _mapHelperToggle.IsChecked == true;
    }

    private void FileLoggingChanged(object? sender, RoutedEventArgs e)
    {
        bool enabled = _fileLoggingToggle.IsChecked == true;
        DebugConsole.ConfigureFileLogging(enabled);

        bool active = DebugConsole.FileLoggingEnabled;
        CompanionSettings.FileLoggingEnabled = active;

        if (enabled != active)
            _fileLoggingToggle.IsChecked = active;

        UpdateFileLoggingHint();
    }

    private void UpdateFileLoggingHint()
    {
        string key = DebugConsole.FileLoggingEnabled ? "LoggingOn" : "LoggingOff";
        _fileLoggingHint.Text = string.Format(
            LocalizationService.Get(key),
            DebugConsole.LogDirectory);
    }

    private void PetCollectionChanged(IReadOnlyList<PetFarmOwnedState>? _) =>
        UpdatePetCollectionStatus();

    private void UpdatePetCollectionStatus()
    {
        string text;
        string color;
        switch (PetCollectionBridge.Status)
        {
            case PetCollectionSyncState.Ready:
                text = $"{LocalizationService.Get("PetReady")} // {LocalizationService.Get("PetReadyTitle").ToUpperInvariant()}";
                color = "#58E0B2";
                break;
            case PetCollectionSyncState.Partial:
                text = $"{LocalizationService.Get("PetPartial")} // {LocalizationService.Get("PetPartialTitle").ToUpperInvariant()}";
                color = "#45DFFF";
                break;
            default:
                text = $"{LocalizationService.Get("PetUnknown")} // {LocalizationService.Get("PetUnknownTitle").ToUpperInvariant()}";
                color = "#FFB454";
                break;
        }

        IBrush brush = new SolidColorBrush(Color.Parse(color));
        _petCollectionStatusText.Text = text;
        _petCollectionStatusText.Foreground = brush;
        _petCollectionStatusDot.Fill = brush;
    }

    private void AutoDpsOverlayChanged(object? sender, RoutedEventArgs e)
    {
        CompanionSettings.AutoManageDpsOverlay = _autoDpsOverlayToggle.IsChecked == true;
    }

    private void AutoCaptureChanged(object? sender, RoutedEventArgs e)
    {
        CompanionSettings.AutoCaptureEnabled = _autoCaptureToggle.IsChecked == true;
    }

    private void AutoUpdateChanged(object? sender, RoutedEventArgs e)
    {
        CompanionSettings.CheckUpdatesAutomatically = _autoUpdateToggle.IsChecked == true;
    }

    private async void CheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        _checkUpdateButton.IsEnabled = false;
        _updateCheckStatusText.Text = LocalizationService.Get("UpdateChecking");
        _updateCheckStatusText.Foreground = new SolidColorBrush(Color.Parse("#45DFFF"));

        try
        {
            UpdateCheckResult result = await UpdateService.CheckAsync();
            switch (result.State)
            {
                case UpdateCheckState.UpdateAvailable when result.Update is not null:
                    _updateCheckStatusText.Text = string.Format(
                        LocalizationService.Get("UpdateFound"), result.Update.Version);
                    _updateCheckStatusText.Foreground = new SolidColorBrush(Color.Parse("#58E0B2"));
                    if (TopLevel.GetTopLevel(this) is Window owner)
                        await new UpdateWindow(result.Update).ShowDialog(owner);
                    break;
                case UpdateCheckState.UpToDate:
                    _updateCheckStatusText.Text = LocalizationService.Get("UpdateCurrent");
                    _updateCheckStatusText.Foreground = new SolidColorBrush(Color.Parse("#58E0B2"));
                    break;
                case UpdateCheckState.ChannelUnavailable:
                    _updateCheckStatusText.Text = LocalizationService.Get("UpdateUnavailable");
                    _updateCheckStatusText.Foreground = new SolidColorBrush(Color.Parse("#FFB454"));
                    break;
                default:
                    _updateCheckStatusText.Text = LocalizationService.Get("UpdateCheckFailed");
                    _updateCheckStatusText.Foreground = new SolidColorBrush(Color.Parse("#F87171"));
                    if (!string.IsNullOrWhiteSpace(result.Error))
                        DebugConsole.Log("update", result.Error);
                    break;
            }
        }
        finally
        {
            _checkUpdateButton.IsEnabled = true;
        }
    }

    private void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_languageCombo.SelectedItem is not ComboBoxItem { Tag: string code })
            return;

        CompanionSettings.LanguageCode = code;
        LocalizationService.Apply(code);
        RefreshEventTimerRegionNames();
        UpdateFileLoggingHint();
        UpdatePetCollectionStatus();
        UpdateForgeDataStatus();
        if (Program.DeveloperMode)
        {
            _testLoginButton.Content = LocalizationService.Get("TestLoginAction");
            ToolTip.SetTip(_testLoginButton, LocalizationService.Get("TestLoginDeveloperHint"));
        }
    }

    private void UpdateForgeDataStatus()
    {
        string dataDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Data");
        _forgeDataPathText.Text = dataDirectory;

        int packCount = 0;
        try
        {
            if (Directory.Exists(dataDirectory))
                packCount = Directory.EnumerateFiles(dataDirectory, "*.nfg").Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.Log("forgedata", $"Runtime data pack status unavailable: {ex.Message}");
        }

        bool ready = packCount > 0;
        string color = ready ? "#58E0B2" : "#FFB454";
        _forgeDataStatusDot.Fill = new SolidColorBrush(Color.Parse(color));
        _forgeDataStatusText.Foreground = new SolidColorBrush(Color.Parse(color));
        _forgeDataStatusText.Text = ready
            ? LocalizationService.Format("ForgeDataReadyCount", packCount)
            : LocalizationService.Get("ForgeDataMissing");
    }

    private void SelectLanguage(string code)
    {
        foreach (object? item in _languageCombo.Items)
        {
            if (item is ComboBoxItem { Tag: string itemCode } &&
                string.Equals(itemCode, code, StringComparison.OrdinalIgnoreCase))
            {
                _languageCombo.SelectedItem = item;
                return;
            }
        }

        _languageCombo.SelectedIndex = 0;
    }

    private void CombatLogLimitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_combatLogLimitCombo.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !int.TryParse(tag, out int limit))
        {
            return;
        }

        CompanionSettings.CombatLogLimit = limit;
        CombatLogStore.Shared.ApplyRetentionLimit();
    }

    private void SelectCombatLogLimit(int limit)
    {
        foreach (object? item in _combatLogLimitCombo.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } &&
                int.TryParse(tag, out int itemLimit) &&
                itemLimit == limit)
            {
                _combatLogLimitCombo.SelectedItem = item;
                return;
            }
        }

        _combatLogLimitCombo.SelectedIndex = 1;
    }

    private void CloseActionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_closeActionCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out int value) &&
            Enum.IsDefined(typeof(MainWindowCloseAction), value))
        {
            CompanionSettings.CloseAction = (MainWindowCloseAction)value;
        }
    }

    private void SelectCloseAction(MainWindowCloseAction action)
    {
        foreach (object? item in _closeActionCombo.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } &&
                int.TryParse(tag, out int value) &&
                value == (int)action)
            {
                _closeActionCombo.SelectedItem = item;
                return;
            }
        }

        _closeActionCombo.SelectedIndex = 0;
    }
}
