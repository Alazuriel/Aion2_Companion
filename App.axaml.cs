using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using AION_2_Companion.ViewModels;
using AION_2_Companion.Views;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (Program.RequestedLanguage is { } requestedLanguage)
                CompanionSettings.LanguageCode = requestedLanguage;
            LocalizationService.Apply(CompanionSettings.LanguageCode);
            bool fileLoggingRequested = CompanionSettings.FileLoggingEnabled;
            DebugConsole.ConfigureFileLogging(fileLoggingRequested);
            if (fileLoggingRequested && !DebugConsole.FileLoggingEnabled)
                CompanionSettings.FileLoggingEnabled = false;

            AccountService.Shared.ConfigureProtocol(new NexforgeAccountAuthProtocol());
            _ = AccountService.Shared.InitializeAsync();

            CombatLogStore.Shared.StartSession();
            CombatLogStore.Shared.ApplyRetentionLimit();
            bool autoStartCapture = !Program.TestLoginRequested &&
                !Program.AutoCaptureDisabled &&
                (Program.AutoCaptureRequested || CompanionSettings.AutoCaptureEnabled);
            var mainViewModel = new MainWindowViewModel(autoStartCapture);
            desktop.Exit += (_, _) =>
            {
                mainViewModel.SaveActiveFightForShutdown();
                AccountService.Shared.Shutdown();
                DebugConsole.Shutdown();
            };
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
