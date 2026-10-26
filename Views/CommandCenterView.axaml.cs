using System;
using System.ComponentModel;
using System.Diagnostics;
using AION_2_Companion.Core;
using AION_2_Companion.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AION_2_Companion.Views;

public partial class CommandCenterView : UserControl
{
    private const string NexforgeUrl = "https://github.com/Alazuriel/Aion2_Companion";
    private readonly TextBlock _characterNameText;
    private readonly Border _characterClassPill;
    private readonly TextBlock _characterClassText;
    private MainWindowViewModel? _viewModel;

    public CommandCenterView()
    {
        InitializeComponent();
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{ChangelogService.GetCurrentVersion()}";
        _characterNameText = this.FindControl<TextBlock>("CharacterNameText")!;
        _characterClassPill = this.FindControl<Border>("CharacterClassPill")!;
        _characterClassText = this.FindControl<TextBlock>("CharacterClassText")!;

        DataContextChanged += (_, _) => AttachViewModel(DataContext as MainWindowViewModel);
        AttachedToVisualTree += (_, _) =>
        {
            AttachViewModel(DataContext as MainWindowViewModel);
            LocalizationService.LanguageChanged -= LocalizationChanged;
            LocalizationService.LanguageChanged += LocalizationChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            AttachViewModel(null);
            LocalizationService.LanguageChanged -= LocalizationChanged;
        };
        UpdateCharacterSummary();
    }

    public CommandCenterView(MainWindowViewModel viewModel) : this() => DataContext = viewModel;

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            UpdateCharacterSummary();
            return;
        }

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;

        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModelPropertyChanged;

        UpdateCharacterSummary();
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CharacterName)
            or nameof(MainWindowViewModel.CharacterClass)
            or nameof(MainWindowViewModel.CharacterClassColor))
        {
            UpdateCharacterSummary();
        }
    }

    private void LocalizationChanged(object? sender, EventArgs e) => UpdateCharacterSummary();

    private void UpdateCharacterSummary()
    {
        string? name = _viewModel?.CharacterName;
        _characterNameText.Text = string.IsNullOrWhiteSpace(name)
            ? LocalizationService.Get("WaitingForCharacter")
            : name;

        string? characterClass = _viewModel?.CharacterClass;
        bool hasClass = !string.IsNullOrWhiteSpace(characterClass) && characterClass != "—";
        _characterClassPill.IsVisible = hasClass;
        if (!hasClass)
            return;

        _characterClassText.Text = characterClass;
        try
        {
            _characterClassText.Foreground =
                new SolidColorBrush(Color.Parse(_viewModel?.CharacterClassColor ?? "#9CA3AF"));
        }
        catch (FormatException)
        {
            _characterClassText.Foreground = new SolidColorBrush(Color.Parse("#9CA3AF"));
        }
    }

    private void OpenNexforgeClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(NexforgeUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugConsole.Log("navigation", $"Nexforge link could not be opened: {ex.Message}");
        }
    }

    private void OpenCharactersClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            mainWindow.NavigateTo("Characters");
    }
}
