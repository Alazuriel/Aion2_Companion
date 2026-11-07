using Avalonia;
using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using AION_2_Companion.ViewModels;
using AION_2_Companion.Views;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Diagnostics;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Characters;
using AION_2_Companion.Core.Characters.Verification;
using AION_2_Companion.Core.Integrity;
using AION_2_Companion.Core.Phaseward;

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
            PhasewardCommunitySync.Shared.Start();
            PhasewardTraceV2Sync.Shared.Start();
            CharacterCenterService.Shared.Start();
            CharacterVerificationService.Shared.Start();
            CombatLogStore.Shared.StartSession();
            CombatLogStore.Shared.ApplyRetentionLimit();
            bool autoStartCapture = !Program.TestLoginRequested &&
                !Program.AutoCaptureDisabled &&
                (Program.AutoCaptureRequested || CompanionSettings.AutoCaptureEnabled);
            var mainViewModel = new MainWindowViewModel(autoStartCapture);
            IntegrityMonitorService.Shared.StatusChanged += (_, _) =>
                IntegrityNotificationPresenter.ShowIfNeeded(IntegrityMonitorService.Shared.Snapshot);
            IntegrityMonitorService.Shared.Start(() => mainViewModel.HasActiveEncounterForUpdate);
            desktop.Exit += (_, _) =>
            {
                mainViewModel.SaveActiveFightForShutdown();
                PhasewardCommunitySync.Shared.Stop();
                PhasewardTraceV2Sync.Shared.Stop();
                AccountService.Shared.Shutdown();
                CharacterCenterService.Shared.Dispose();
                CharacterVerificationService.Shared.Dispose();
                try
                {
                    IntegrityMonitorService.Shared.TryLaunchPendingRepair();
                }
                catch (Exception ex)
                {
                    DebugConsole.Log("integrity", $"Prepared repair could not be started: {ex.Message}");
                }
                IntegrityMonitorService.Shared.Dispose();
                DebugConsole.Shutdown();
            };
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
            desktop.MainWindow.Opened += async (_, _) =>
            {
                // Existing v0.9 opt-ins must explicitly ACK raw packets after this adapter upgrade.
                // Do not silently assume seeing a notice meant accepting it.
                if (CompanionSettings.PhasewardTraceV2WelcomeVersion == PhasewardTraceV2Disclosure.NoticeVersion &&
                    (!CompanionSettings.PhasewardTraceV2UploadEnabled &&
                     !CompanionSettings.PhasewardTraceV2EnableOnNextLogin ||
                     CompanionSettings.PhasewardTraceV2RawUploadAcknowledged))
                    return;
                // Only on the first start after installing this update (or a new notice version).
                CompanionSettings.PhasewardTraceV2WelcomeVersion = PhasewardTraceV2Disclosure.NoticeVersion;
                bool traceConsentAccepted = await PhasewardTraceV2Disclosure.ShowAsync(desktop.MainWindow);
                if (traceConsentAccepted)
                {
                    CompanionSettings.PhasewardTraceV2RawUploadAcknowledged = true;
                    string? id = PhasewardTraceV2Sync.ConnectedAccountId;
                    if (id is null) CompanionSettings.PhasewardTraceV2EnableOnNextLogin = true;
                    else PhasewardTraceV2Sync.EnableForAccount(id);
                }
                else if (CompanionSettings.PhasewardTraceV2UploadEnabled ||
                         CompanionSettings.PhasewardTraceV2EnableOnNextLogin)
                {
                    // An existing opt-in must not silently remain active after declining
                    // the revised one-time notice.
                    PhasewardTraceV2Sync.DisableForActiveAccount();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime diagDesktop &&
            diagDesktop.MainWindow is not null)
            DiagnosticRuntime.StartUi(diagDesktop.MainWindow);
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
