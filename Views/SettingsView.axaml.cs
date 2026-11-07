using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Diagnostics;
using AION_2_Companion.Core.Map.Pets;
using AION_2_Companion.Core.EventTimers;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Localization;
using AION_2_Companion.Core.Integrity;
using AION_2_Companion.Core.Phaseward;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

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
    private readonly CheckBox _backgroundUpdateToggle;
    private readonly CheckBox _integrityMonitoringToggle;
    private readonly Button _integrityCheckButton;
    private readonly TextBlock _integrityStatusTitle;
    private readonly TextBlock _integrityStatusText;
    private readonly Ellipse _integrityStatusDot;
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
    private readonly TextBlock _languageImportStatus;

    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        // Phaseward Advanced panels use native button-driven disclosure instead of default black Expander headers.
        var phasewardAdvancedContent = this.FindControl<StackPanel>("PhasewardAdvancedContent")!;
        var phasewardAdvancedChevron = this.FindControl<TextBlock>("PhasewardAdvancedChevron")!;
        this.FindControl<Button>("PhasewardAdvancedDisclosure")!.Click += (_, _) =>
        {
            phasewardAdvancedContent.IsVisible = !phasewardAdvancedContent.IsVisible;
            phasewardAdvancedChevron.Text = phasewardAdvancedContent.IsVisible ? "⌃" : "⌄";
        };
        var phasewardTechnicalContent = this.FindControl<StackPanel>("PhasewardTechnicalContent")!;
        this.FindControl<Button>("PhasewardTechnicalDisclosure")!.Click += (_, _) =>
            phasewardTechnicalContent.IsVisible = !phasewardTechnicalContent.IsVisible;
        this.FindControl<TextBlock>("DiagnosticsHeadingText")!.Text = DiagnosticConsentWindow.Translate(
            "Diagnostics Center", "Diagnosztikai központ", "Diagnosezentrum", "Centrum diagnostyki");
        this.FindControl<TextBlock>("DiagnosticsSubtitleText")!.Text = DiagnosticConsentWindow.Translate(
            "Privacy-safe crash reports. Encrypted locally; only sent if you choose to send.",
            "Adatvédelmi szempontból biztonságos hibajelentések. Helyben titkosítva, csak kérésedre küldjük el.",
            "Datenschutzfreundliche Absturzberichte. Lokal verschlüsselt; Versand nur mit Zustimmung.",
            "Raporty awarii z ochroną prywatności. Szyfrowane lokalnie, wysyłane tylko za zgodą.");
        this.FindControl<Button>("DiagnosticsManualButton")!.Content = DiagnosticConsentWindow.Translate("Create diagnostic report", "Hibajelentés készítése", "Diagnosebericht erstellen", "Utwórz raport diagnostyczny");
        this.FindControl<Button>("DiagnosticsReviewButton")!.Content = DiagnosticConsentWindow.Translate("Review saved reports", "Mentett jelentések", "Gespeicherte Berichte", "Zapisane raporty");

        _mapHelperToggle = this.FindControl<CheckBox>("MapHelperToggle")!;
        _mapAdminWarning = this.FindControl<Border>("MapAdminWarning")!;
        _fileLoggingToggle = this.FindControl<CheckBox>("FileLoggingToggle")!;
        _fileLoggingHint = this.FindControl<TextBlock>("FileLoggingHint")!;
        _autoDpsOverlayToggle = this.FindControl<CheckBox>("AutoDpsOverlayToggle")!;
        _autoCaptureToggle = this.FindControl<CheckBox>("AutoCaptureToggle")!;
        _autoUpdateToggle = this.FindControl<CheckBox>("AutoUpdateToggle")!;
        _backgroundUpdateToggle = this.FindControl<CheckBox>("BackgroundUpdateToggle")!;
        _integrityMonitoringToggle = this.FindControl<CheckBox>("IntegrityMonitoringToggle")!;
        _integrityCheckButton = this.FindControl<Button>("IntegrityCheckButton")!;
        _integrityStatusTitle = this.FindControl<TextBlock>("IntegrityStatusTitle")!;
        _integrityStatusText = this.FindControl<TextBlock>("IntegrityStatusText")!;
        _integrityStatusDot = this.FindControl<Ellipse>("IntegrityStatusDot")!;
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
        _languageImportStatus = this.FindControl<TextBlock>("LanguageImportStatus")!;
        this.FindControl<TextBlock>("VersionText")!.Text = $"v{ChangelogService.GetCurrentVersion()}";
        this.FindControl<Border>("DeveloperSettingsPanel")!.IsVisible = Program.DeveloperMode;

        RefreshLanguageOptions();
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
        this.FindControl<CheckBox>("AutoPhasewardToggle")!.IsChecked = CompanionSettings.AutoPhasewardOverlay;
        this.FindControl<CheckBox>("PhasewardLearningToggle")!.IsChecked = CompanionSettings.PhasewardLearningEnabled;
        var detailedTraceToggle = this.FindControl<CheckBox>("PhasewardDetailedTraceToggle")!;
        detailedTraceToggle.IsChecked = CompanionSettings.PhasewardDetailedTraceEnabled;
        detailedTraceToggle.IsCheckedChanged += (_, _) =>
            CompanionSettings.PhasewardDetailedTraceEnabled = detailedTraceToggle.IsChecked == true;
        this.FindControl<TextBlock>("PhasewardSettingsHeading")!.Text = DiagnosticConsentWindow.Translate(
            "PHASEWARD // COMMUNITY", "PHASEWARD // KÖZÖSSÉG", "PHASEWARD // COMMUNITY", "PHASEWARD // SPOŁECZNOŚĆ");
        this.FindControl<TextBlock>("PhasewardSettingsSubheading")!.Text = DiagnosticConsentWindow.Translate(
            "Boss observations and optional research traces are sent to Nexforge for server-side analysis.",
            "A bossmegfigyelések és az engedélyezett kutatási naplók a Nexforge szerverére kerülnek elemzésre.",
            "Boss-Beobachtungen und optional freigegebene Forschungsdaten werden an Nexforge zur Serveranalyse gesendet.",
            "Obserwacje bossów i opcjonalne ślady badawcze są wysyłane na serwer Nexforge do analizy.");
        this.FindControl<TextBlock>("PhasewardV2PrivacyBrief")!.Text = DiagnosticConsentWindow.Translate(
            "Raw packet data may include identifiers, location or other personal data. Separate consent is required.",
            "A nyers csomagadatok azonosítót, helyadatot vagy más személyes adatot tartalmazhatnak. Külön hozzájárulás szükséges.",
            "Rohpakete können Kennungen, Standort- und andere personenbezogene Daten enthalten. Eine separate Einwilligung ist erforderlich.",
            "Surowe pakiety mogą zawierać identyfikatory, lokalizacje lub inne dane osobowe. Wymagana jest osobna zgoda.");
        var traceUpload = this.FindControl<CheckBox>("PhasewardTraceV2UploadToggle")!;
        this.FindControl<TextBlock>("PhasewardAdvancedTitle")!.Text = DiagnosticConsentWindow.Translate(
            "ADVANCED // RECORDING", "HALADÓ // RÖGZÍTÉS", "ERWEITERT // AUFZEICHNUNG", "ZAAWANSOWANE // NAGRYWANIE");
        this.FindControl<TextBlock>("PhasewardAdvancedSubtitle")!.Text = DiagnosticConsentWindow.Translate(
            "Local capture, research and queue controls", "Helyi rögzítés, kutatás és várólisták", "Lokale Erfassung, Forschung und Warteschlangen", "Nagrywanie lokalne, badania i kolejki");
        this.FindControl<TextBlock>("PhasewardAdvancedLearningTag")!.Text = DiagnosticConsentWindow.Translate(
            "LOCAL ANALYSIS", "HELYI ELEMZÉS", "LOKALE ANALYSE", "ANALIZA LOKALNA");
        this.FindControl<TextBlock>("PhasewardAdvancedCaptureTag")!.Text = DiagnosticConsentWindow.Translate(
            "LOCAL CAPTURE", "HELYI RÖGZÍTÉS", "LOKALE AUFZEICHNUNG", "NAGRYWANIE LOKALNE");
        this.FindControl<TextBlock>("PhasewardAdvancedQueueTag")!.Text = DiagnosticConsentWindow.Translate(
            "COMMUNITY QUEUE", "KÖZÖSSÉGI VÁRÓLISTA", "COMMUNITY-WARTESCHLANGE", "KOLEJKA SPOŁECZNOŚCI");
        this.FindControl<TextBlock>("PhasewardAdvancedCaptureHelp")!.Text = DiagnosticConsentWindow.Translate(
            "Enables the recording used for new Research V2 uploads.",
            "A Research V2 feltöltésekhez szükséges részletes rögzítés.",
            "Ermöglicht die Aufzeichnung für neue Research-V2-Uploads.",
            "Włącza nagrywanie potrzebne do nowych wysyłek Research V2.");
        this.FindControl<TextBlock>("PhasewardAdvancedDependencyInfo")!.Text = DiagnosticConsentWindow.Translate(
            "Server uploads enable the required local recording automatically. Turning uploads off stops sending; local recording may stay on.",
            "A szerveres feltöltés magától bekapcsolja a szükséges rögzítést. A küldés kikapcsolása után a helyi rögzítés aktív maradhat.",
            "Server-Uploads aktivieren nötige lokale Aufzeichnung automatisch. Nach dem Abschalten des Uploads kann die Aufzeichnung an bleiben.",
            "Wysyłanie na serwer automatycznie włącza potrzebne nagrywanie. Po wyłączeniu wysyłania nagrywanie lokalne może pozostać aktywne.");
        this.FindControl<TextBlock>("PhasewardAdvancedTechnicalTitle")!.Text = DiagnosticConsentWindow.Translate(
            "Technical information", "Technikai információk", "Technische Informationen", "Informacje techniczne");
        this.FindControl<CheckBox>("PhasewardDetailedTraceToggle")!.Content = DiagnosticConsentWindow.Translate(
            "Record detailed boss data", "Részletes bossadatok rögzítése", "Detaillierte Bossdaten aufzeichnen", "Nagrywaj szczegółowe dane bossa");
        this.FindControl<Button>("PhasewardTraceV2Clear")!.Content = DiagnosticConsentWindow.Translate(
            "Delete queued research data", "Várakozó kutatási adatok törlése", "Wartende Forschungsdaten löschen", "Usuń oczekujące dane badawcze");

        traceUpload.Content = DiagnosticConsentWindow.Translate(
            "Share detailed boss research (V2)", "Részletes bosskutatási adatok küldése (V2)",
            "Detaillierte Boss-Forschungsdaten senden (V2)", "Wyślij szczegółowe dane badawcze bossa (V2)");
        var traceStatus = this.FindControl<TextBlock>("PhasewardTraceV2UploadStatus")!;
        bool v2ToggleInternal = false;
        traceUpload.IsChecked = CompanionSettings.PhasewardTraceV2UploadEnabled ||
            CompanionSettings.PhasewardTraceV2EnableOnNextLogin;
        this.FindControl<TextBlock>("PhasewardTraceV2UploadHint")!.Text =
            DiagnosticConsentWindow.Translate(
                "Separate V2 opt-in. Uploads the non-DPS fight stream plus ALL packets within ±10s of boss casts. UNKNOWN opcode RAW bytes may include identifiers, locations or other personal data. Automatic HTTPS after each completed fight. Nothing downloaded from the server.",
                "Külön V2-engedély: a teljes nem-DPS harcfolyam és a boss-castok ±10 mp-es összes csomagja feltöltődik, NYERS ismeretlen opcode-adatokkal. Ezek azonosítót, helyadatot vagy más személyes adatot is tartalmazhatnak. Harconként automatikus HTTPS; visszafelé nem töltünk le bossadatot.",
                "Separate V2-Zustimmung für Roh-Pakete (inkl. unbekannter Opcode-Daten); diese können Personen- und Positionsdaten enthalten. Automatischer HTTPS-Upload, keine Community-Downloads.",
                "Osobna zgoda V2: surowe pakiety (także nieznane kody) mogą zawierać dane osobowe i pozycję. Automatyczna wysyłka HTTPS; bez pobierania danych społeczności.");
        void RefreshTraceStatus() => traceStatus.Text = "Research V2: " +
            PhasewardTraceV2Sync.Shared.Status + " | pending: " + PhasewardTraceV2Sync.PendingCount();
        RefreshTraceStatus();
        void TraceStatusChanged(object? sender, EventArgs e) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshTraceStatus);
        PhasewardTraceV2Sync.Shared.Changed += TraceStatusChanged;
        DetachedFromVisualTree += (_, _) => PhasewardTraceV2Sync.Shared.Changed -= TraceStatusChanged;
        traceUpload.IsCheckedChanged += async (_, _) =>
        {
            if (v2ToggleInternal) return;
            bool requested = traceUpload.IsChecked == true;
            if (requested)
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                bool accepted = owner is not null && await PhasewardTraceV2Disclosure.ShowAsync(owner);
                if (accepted)
                {
                    CompanionSettings.PhasewardTraceV2RawUploadAcknowledged = true;
                    string? accountId = PhasewardTraceV2Sync.ConnectedAccountId;
                    if (accountId is null) CompanionSettings.PhasewardTraceV2EnableOnNextLogin = true;
                    else PhasewardTraceV2Sync.EnableForAccount(accountId);
                    detailedTraceToggle.IsChecked = true;
                }
                else
                {
                    v2ToggleInternal = true; traceUpload.IsChecked = false; v2ToggleInternal = false;
                }
            }
            else PhasewardTraceV2Sync.DisableForActiveAccount();
            RefreshTraceStatus();
        };
        this.FindControl<Button>("PhasewardTraceV2Clear")!.Click += (_, _) =>
        {
            PhasewardTraceV2Sync.DeletePendingForActiveAccount();
            RefreshTraceStatus();
        };
        this.FindControl<CheckBox>("PhasewardLearningToggle")!.Content = LocalizationService.Get("PwLearningToggle");
        this.FindControl<CheckBox>("PhasewardLearningToggle")!.Content = DiagnosticConsentWindow.Translate(
            "Local pattern recognition", "Helyi mintafelismerés", "Lokale Mustererkennung", "Lokalne wykrywanie wzorców");

        this.FindControl<TextBlock>("PhasewardLearningHint")!.Text = LocalizationService.Get("PwLearningHint");
        var learningSummary = AION_2_Companion.Core.Phaseward.PhasewardLearningRecorder.Summary();
        this.FindControl<TextBlock>("PhasewardLearningStatusText")!.Text = string.Format(
            LocalizationService.Get("PwLearningStatus"),
            learningSummary.Pulls, learningSummary.Bosses, learningSummary.ValidatedRules);
        var uploadToggle = this.FindControl<CheckBox>("PhasewardCommunityUploadToggle")!;
        uploadToggle.IsChecked = CompanionSettings.PhasewardCommunityUploadConsentConfirmed ||
            CompanionSettings.PhasewardCommunityEnableOnNextLogin;
        uploadToggle.Content = DiagnosticConsentWindow.Translate(
            "Automatically upload future boss observations to Nexforge",
            "Új bossmegfigyelések automatikus feltöltése a Nexforge szerverére",
            "Neue Boss-Beobachtungen automatisch zu Nexforge hochladen",
            "Automatycznie przesyłaj nowe obserwacje bossów do Nexforge");
        this.FindControl<TextBlock>("PhasewardCommunityUploadHint")!.Text = DiagnosticConsentWindow.Translate(
            "Separate consent: sends boss NPC ID, skill IDs, relative timing and HP samples to the server after each eligible fight. No player names, raw packets or community data downloads. Requires linked Nexforge account.",
            "Külön hozzájárulás: minden megfelelő harc után boss NPC ID-t, skill ID-kat, relatív időket és HP-mintákat küld a szerverre. Nincs játékosnév, nyers csomag vagy közösségi adatletöltés. Nexforge-fiók szükséges.",
            "Separate Einwilligung: Boss-NPC-ID, Skill-IDs, Zeiten und HP-Werte werden nach Kämpfen hochgeladen. Keine Spielernamen, Rohpakete oder Community-Downloads. Nexforge-Konto erforderlich.",
            "Oddzielna zgoda: po walce wysyłane są ID bossa, umiejętności, czasy i próbki HP. Bez nazw graczy, surowych pakietów ani pobierania danych społeczności. Wymagane konto Nexforge.");
        void RefreshCommunityStatus()
        {
            this.FindControl<TextBlock>("PhasewardCommunityUploadStatus")!.Text =
                "Community Sync: " + PhasewardCommunitySync.Shared.Status +
                " | pending: " + PhasewardCommunityOutbox.PendingCount() +
                " | old local-only: " + PhasewardCommunityOutbox.LegacyPendingCount();
        }
        RefreshCommunityStatus();
        PhasewardCommunitySync.Shared.Changed += CommunitySyncStatusChanged;
        void CommunitySyncStatusChanged(object? sender, EventArgs e) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshCommunityStatus);
        DetachedFromVisualTree += (_, _) =>
            PhasewardCommunitySync.Shared.Changed -= CommunitySyncStatusChanged;
        var queueToggle = this.FindControl<CheckBox>("PhasewardCommunityQueueToggle")!;
        queueToggle.Content = LocalizationService.Get("PwCommunityQueueToggle");
        queueToggle.Content = DiagnosticConsentWindow.Translate(
            "Prepare community observations", "Közösségi megfigyelések előkészítése",
            "Community-Beobachtungen vorbereiten", "Przygotuj obserwacje społeczności");

        queueToggle.IsChecked = CompanionSettings.PhasewardCommunityQueueEnabled;
        this.FindControl<TextBlock>("PhasewardCommunityQueueHint")!.Text = LocalizationService.Get("PwCommunityQueueHint");
        var queueStatus = this.FindControl<TextBlock>("PhasewardCommunityQueueStatus")!;
        queueStatus.Text = string.Format(LocalizationService.Get("PwCommunityQueueStatus"),
            AION_2_Companion.Core.Phaseward.PhasewardCommunityOutbox.PendingCount());
        var queueClear = this.FindControl<Button>("PhasewardCommunityQueueClear")!;
        queueClear.Content = LocalizationService.Get("PwCommunityQueueDelete");
        queueClear.Content = DiagnosticConsentWindow.Translate(
            "Delete pending observations", "Várakozó megfigyelések törlése",
            "Ausstehende Beobachtungen löschen", "Usuń oczekujące obserwacje");

        queueClear.Click += (_, _) =>
        {
            AION_2_Companion.Core.Phaseward.PhasewardCommunityOutbox.DeletePending();
            queueStatus.Text = string.Format(LocalizationService.Get("PwCommunityQueueStatus"),
                AION_2_Companion.Core.Phaseward.PhasewardCommunityOutbox.PendingCount());
        };
        _autoCaptureToggle.IsChecked = CompanionSettings.AutoCaptureEnabled;
        _autoUpdateToggle.IsChecked = CompanionSettings.CheckUpdatesAutomatically;
        _backgroundUpdateToggle.IsChecked = CompanionSettings.DownloadUpdatesInBackground;
        _integrityMonitoringToggle.IsChecked = CompanionSettings.IntegrityMonitoringEnabled;
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
        this.FindControl<CheckBox>("AutoPhasewardToggle")!.IsCheckedChanged += (_, _) =>
            CompanionSettings.AutoPhasewardOverlay = this.FindControl<CheckBox>("AutoPhasewardToggle")!.IsChecked == true;
        this.FindControl<CheckBox>("PhasewardLearningToggle")!.IsCheckedChanged += (_, _) =>
            CompanionSettings.PhasewardLearningEnabled = this.FindControl<CheckBox>("PhasewardLearningToggle")!.IsChecked == true;
        uploadToggle.IsCheckedChanged += (_, _) =>
        {
            bool enabled = uploadToggle.IsChecked == true;
            bool hadConfirmedConsent = CompanionSettings.PhasewardCommunityUploadConsentConfirmed;
            string? currentAccount = PhasewardCommunityOutbox.CurrentAccountId;
            if (enabled && string.IsNullOrWhiteSpace(currentAccount))
            {
                CompanionSettings.PhasewardCommunityEnableOnNextLogin = true;
                CompanionSettings.PhasewardCommunityQueueEnabled = true;
                CompanionSettings.PhasewardLearningEnabled = true;
                if (queueToggle.IsChecked != true) queueToggle.IsChecked = true;
                RefreshCommunityStatus();
                return;
            }
            if (enabled)
            {
                // Do not retroactively send pulls that were only prepared before upload consent.
                PhasewardCommunityOutbox.DeletePending();
                CompanionSettings.PhasewardCommunityUploadAccountHash = PhasewardCommunityOutbox.AccountHash(currentAccount!);
                CompanionSettings.PhasewardCommunityRevokePending = false;
                CompanionSettings.PhasewardCommunityEnablePending = true;
                CompanionSettings.PhasewardCommunityEnableOnNextLogin = false;
                if (queueToggle.IsChecked != true) queueToggle.IsChecked = true;
            }
            else
            {
                // Opting out immediately clears unsent per-account observations.
                PhasewardCommunityOutbox.DeletePending();
                CompanionSettings.PhasewardCommunityRevokePending = hadConfirmedConsent;
                CompanionSettings.PhasewardCommunityEnablePending = false;
                CompanionSettings.PhasewardCommunityEnableOnNextLogin = false;
            }
            CompanionSettings.PhasewardCommunityUploadConsentConfirmed = enabled;
            if (enabled || hadConfirmedConsent)
                PhasewardCommunitySync.Shared.OnUploadPreferenceChanged(enabled);
            else
                PhasewardCommunitySync.Shared.Wake();
            RefreshCommunityStatus();
        };
        queueToggle.IsCheckedChanged += (_, _) =>
        {
            bool enabled = queueToggle.IsChecked == true;
            if (enabled && !CompanionSettings.PhasewardLearningEnabled)
            {
                CompanionSettings.PhasewardLearningEnabled = true;
                this.FindControl<CheckBox>("PhasewardLearningToggle")!.IsChecked = true;
            }
            CompanionSettings.PhasewardCommunityQueueEnabled = enabled;
            if (!enabled && uploadToggle.IsChecked == true) uploadToggle.IsChecked = false;
            PhasewardCommunitySync.Shared.Wake();
            queueStatus.Text = string.Format(LocalizationService.Get("PwCommunityQueueStatus"),
                AION_2_Companion.Core.Phaseward.PhasewardCommunityOutbox.PendingCount());
        };
        _autoCaptureToggle.IsCheckedChanged += AutoCaptureChanged;
        _autoUpdateToggle.IsCheckedChanged += AutoUpdateChanged;
        _backgroundUpdateToggle.IsCheckedChanged += BackgroundUpdateChanged;
        _integrityMonitoringToggle.IsCheckedChanged += IntegrityMonitoringChanged;
        _combatLogLimitCombo.SelectionChanged += CombatLogLimitChanged;
        _closeActionCombo.SelectionChanged += CloseActionChanged;
        _languageCombo.SelectionChanged += LanguageChanged;
        _eventTimerRegionCombo.SelectionChanged += EventTimerRegionChanged;
        _eventTimerClickThroughToggle.IsCheckedChanged += EventTimerClickThroughChanged;
        _dpsEncounterModeCombo.SelectionChanged += DpsEncounterModeChanged;
        UpdateFileLoggingHint();
        UpdatePetCollectionStatus();
        UpdateForgeDataStatus();
        UpdateIntegrityStatus();

        AttachedToVisualTree += (_, _) =>
        {
            PetCollectionBridge.Changed -= PetCollectionChanged;
            PetCollectionBridge.Changed += PetCollectionChanged;
            IntegrityMonitorService.Shared.StatusChanged -= IntegrityStatusChanged;
            IntegrityMonitorService.Shared.StatusChanged += IntegrityStatusChanged;
            UpdatePetCollectionStatus();
            UpdateIntegrityStatus();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            PetCollectionBridge.Changed -= PetCollectionChanged;
            IntegrityMonitorService.Shared.StatusChanged -= IntegrityStatusChanged;
        };
    }

    private void DiagnosticManualClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) DiagnosticConsentWindow.CreateManual(owner);
    }
    private void DiagnosticReviewClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner) DiagnosticConsentWindow.ShowPending(owner);
    }

    private void OpenDpsClick(object? sender, RoutedEventArgs e) =>
        Overlays.OverlayWindowManager.ToggleDps();

    private void OpenPhasewardOverlayClick(object? sender, RoutedEventArgs e) =>
        Overlays.OverlayWindowManager.ToggleDbm();

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

    private void OpenOverlayStudioClick(object? sender, RoutedEventArgs e) =>
        AION_2_Companion.Views.Overlays.OverlayWindowManager.OpenStudio();

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

    private void BackgroundUpdateChanged(object? sender, RoutedEventArgs e)
    {
        CompanionSettings.DownloadUpdatesInBackground = _backgroundUpdateToggle.IsChecked == true;
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
        UpdateIntegrityStatus();
    }

    private void IntegrityMonitoringChanged(object? sender, RoutedEventArgs e) =>
        IntegrityMonitorService.Shared.SetEnabled(_integrityMonitoringToggle.IsChecked == true);

    private async void IntegrityCheckClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await IntegrityMonitorService.Shared.CheckNowAsync();
        }
        catch (Exception ex)
        {
            DebugConsole.Log("integrity", $"Manual integrity check failed: {ex.Message}");
        }
    }

    private void IntegrityStatusChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateIntegrityStatus);

    private void UpdateIntegrityStatus()
    {
        IntegrityMonitorSnapshot snapshot = IntegrityMonitorService.Shared.Snapshot;
        (string titleKey, string textKey, string color) = snapshot.State switch
        {
            IntegrityMonitorState.Disabled => ("IntegrityDisabled", "IntegrityDisabledText", "#71879B"),
            IntegrityMonitorState.Checking => ("IntegrityChecking", "IntegrityCheckingText", "#45DFFF"),
            IntegrityMonitorState.Healthy => ("IntegrityHealthy", "IntegrityHealthyText", "#58E0B2"),
            IntegrityMonitorState.ProblemDetected => ("IntegrityProblem", "IntegrityProblemText", "#FF9B52"),
            IntegrityMonitorState.PreparingRepair => ("IntegrityRepairPreparing", "IntegrityRepairPreparingText", "#45DFFF"),
            IntegrityMonitorState.RepairReady => ("IntegrityRepairReady", "IntegrityRepairReadyText", "#58E0B2"),
            IntegrityMonitorState.RestartRequired => ("IntegrityRestartRequired", "IntegrityRestartRequiredText", "#FF9B52"),
            IntegrityMonitorState.RepairFailed => ("IntegrityRepairFailed", "IntegrityRepairFailedText", "#FF6B6B"),
            IntegrityMonitorState.NotAvailable => ("IntegrityNotAvailable", "IntegrityNotAvailableText", "#71879B"),
            _ => ("IntegrityWaiting", "IntegrityWaitingText", "#71879B"),
        };
        _integrityStatusTitle.Text = LocalizationService.Get(titleKey);
        _integrityStatusText.Text = LocalizationService.Get(textKey);
        _integrityStatusDot.Fill = new SolidColorBrush(Color.Parse(color));
        _integrityCheckButton.IsEnabled = snapshot.State is not
            (IntegrityMonitorState.Checking or IntegrityMonitorState.PreparingRepair);
    }

    private void RefreshLanguageOptions()
    {
        string selected = CompanionSettings.LanguageCode;
        _languageCombo.Items.Clear();
        foreach (LanguageOption language in LocalizationService.SupportedLanguages)
        {
            _languageCombo.Items.Add(new ComboBoxItem
            {
                Content = language.DisplayName,
                Tag = language.Code,
            });
        }
        SelectLanguage(selected);
    }

    private async void ImportLanguageClick(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = LocalizationService.Get("ImportLanguage"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Nexforge Language") { Patterns = ["*.nfl"] },
                ],
            });
        if (files.Count == 0)
            return;
        try
        {
            string? source = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(source))
                throw new IOException("The selected file is not available locally.");
            NflLanguagePack pack = NflLanguagePackService.Import(source);
            RefreshLanguageOptions();
            SelectLanguage(pack.Code);
            _languageImportStatus.Text = LocalizationService.Get("LanguageImportReady");
            _languageImportStatus.Foreground = new SolidColorBrush(Color.Parse("#58E0B2"));
            _languageImportStatus.IsVisible = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NflParseException)
        {
            _languageImportStatus.Text = LocalizationService.Format("LanguageImportFailed", exception.Message);
            _languageImportStatus.Foreground = new SolidColorBrush(Color.Parse("#F87171"));
            _languageImportStatus.IsVisible = true;
        }
    }

    private void OpenLanguageFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(NflLanguagePackService.CommunityDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = NflLanguagePackService.CommunityDirectory,
            UseShellExecute = true,
        });
    }

    private void UpdateForgeDataStatus()
    {
        string dataDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData");
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
