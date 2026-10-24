using System;
using System.Collections.Generic;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Map.Pets;
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
    private readonly ComboBox _combatLogLimitCombo;
    private readonly ComboBox _closeActionCombo;
    private readonly ComboBox _languageCombo;
    private readonly TextBlock _petCollectionStatusText;
    private readonly Ellipse _petCollectionStatusDot;

    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);

        _mapHelperToggle = this.FindControl<CheckBox>("MapHelperToggle")!;
        _mapAdminWarning = this.FindControl<Border>("MapAdminWarning")!;
        _fileLoggingToggle = this.FindControl<CheckBox>("FileLoggingToggle")!;
        _fileLoggingHint = this.FindControl<TextBlock>("FileLoggingHint")!;
        _autoDpsOverlayToggle = this.FindControl<CheckBox>("AutoDpsOverlayToggle")!;
        _autoCaptureToggle = this.FindControl<CheckBox>("AutoCaptureToggle")!;
        _combatLogLimitCombo = this.FindControl<ComboBox>("CombatLogLimitCombo")!;
        _closeActionCombo = this.FindControl<ComboBox>("CloseActionCombo")!;
        _languageCombo = this.FindControl<ComboBox>("LanguageCombo")!;
        _petCollectionStatusText = this.FindControl<TextBlock>("PetCollectionStatusText")!;
        _petCollectionStatusDot = this.FindControl<Ellipse>("PetCollectionStatusDot")!;
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
        SelectCombatLogLimit(CompanionSettings.CombatLogLimit);
        SelectCloseAction(CompanionSettings.CloseAction);

        _mapHelperToggle.IsCheckedChanged += MapHelperChanged;
        _fileLoggingToggle.IsCheckedChanged += FileLoggingChanged;
        _autoDpsOverlayToggle.IsCheckedChanged += AutoDpsOverlayChanged;
        _autoCaptureToggle.IsCheckedChanged += AutoCaptureChanged;
        _combatLogLimitCombo.SelectionChanged += CombatLogLimitChanged;
        _closeActionCombo.SelectionChanged += CloseActionChanged;
        _languageCombo.SelectionChanged += LanguageChanged;

        UpdateFileLoggingHint();
        UpdatePetCollectionStatus();

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

    private void OpenDbmClick(object? sender, RoutedEventArgs e) =>
        Overlays.OverlayWindowManager.ToggleDbm();

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

    private void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_languageCombo.SelectedItem is not ComboBoxItem { Tag: string code })
            return;

        CompanionSettings.LanguageCode = code;
        LocalizationService.Apply(code);
        UpdateFileLoggingHint();
        UpdatePetCollectionStatus();
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
