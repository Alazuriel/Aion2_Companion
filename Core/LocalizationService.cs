using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;

namespace AION_2_Companion.Core;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["AppReady"] = "READY",
        ["NavModules"] = "MODULES",
        ["NavHome"] = "Home",
        ["NavDps"] = "DPS meter",
        ["NavCombatLog"] = "Combat history",
        ["NavBoss"] = "Boss timers",
        ["NavGuides"] = "Class guides",
        ["NavSettings"] = "Settings",
        ["NavSupport"] = "Support",
        ["NavSystem"] = "DEVELOPER",
        ["NavDeveloper"] = "Live monitor",
        ["UnderForge"] = "UNDER FORGE",
        ["DevBuild"] = "NEXFORGE LABS // DEVELOPMENT BUILD",
        ["SettingsTitle"] = "Settings",
        ["SettingsSubtitle"] = "Choose how the Companion and its overlays behave.",
        ["LanguageSection"] = "LANGUAGE",
        ["LanguageLabel"] = "Application language",
        ["LanguageHint"] = "Changes are applied immediately and saved for the next launch.",
        ["OverlaySection"] = "OVERLAYS",
        ["DpsMeter"] = "DPS meter",
        ["DpsOverlayHint"] = "A compact floating window. Drag it by its top bar.",
        ["BossAssistant"] = "Boss assistant",
        ["BossOverlayHint"] = "A compact timer window. Drag it by its top bar.",
        ["Open"] = "Open",
        ["MapHelper"] = "Map helper",
        ["MapHelperHint"] = "When enabled, the map helper reacts to the M key in the game.",
        ["MapHelperTooltip"] = "The Map Helper works reliably in borderless fullscreen mode. In other display modes it may not appear or may be positioned incorrectly.",
        ["Enabled"] = "Enabled",
        ["MapAdminWarning"] = "Run AION 2 Companion as administrator to use the Map Helper.",
        ["PetCollection"] = "PET COLLECTION",
        ["PetCollectionStatus"] = "Collection data status",
        ["PetUnknown"] = "UNKNOWN",
        ["PetUnknownTitle"] = "Waiting for a full list",
        ["PetUnknownText"] = "Your collection is not known yet. Pets with an unknown status are shown temporarily.",
        ["PetPartial"] = "PARTIAL",
        ["PetPartialTitle"] = "Partial data",
        ["PetPartialText"] = "At least one owned pet has been detected, but the rest of the collection is still unknown.",
        ["PetReady"] = "READY",
        ["PetReadyTitle"] = "Full list available",
        ["PetReadyText"] = "Your full pet collection is available. Missing and level filters are ready to use.",
        ["ApplicationSection"] = "APPLICATION",
        ["AutoCapture"] = "Start monitoring automatically",
        ["AutoCaptureHint"] = "Starts the background game-data connection when the Companion opens.",
        ["CloseBehavior"] = "When the main window is closed",
        ["CloseBehaviorHint"] = "Keeping the app in the background leaves active overlays and helpers running.",
        ["CloseAsk"] = "Ask the first time",
        ["CloseTray"] = "Keep running in background",
        ["CloseExit"] = "Exit the application",
        ["LoggingSection"] = "DIAGNOSTIC LOGS",
        ["LoggingToggle"] = "Save diagnostic log files",
        ["LoggingOn"] = "Enabled. Up to three 5 MB files are kept in: {0}",
        ["LoggingOff"] = "Disabled by default. When enabled, no more than three 5 MB files are kept.",
        ["CombatLogSection"] = "COMBAT HISTORY",
        ["AutoDps"] = "Show and hide the DPS overlay automatically during combat",
        ["SavedFights"] = "Saved combats",
        ["SavedFightsHint"] = "Favorites are never removed automatically when the limit is reached.",
        ["Fights100"] = "100 combats",
        ["Fights500"] = "500 combats",
        ["Fights1000"] = "1,000 combats",
        ["Unlimited"] = "Unlimited",
        ["SupportTitle"] = "Support & diagnostics",
        ["SupportSubtitle"] = "Create a support package when the Nexforge team asks for one.",
        ["SupportWhatTitle"] = "What will be included?",
        ["SupportWhatText"] = "Application version, basic system information, a settings summary and recent diagnostic logs. Protected application data and credentials are not included.",
        ["SupportCombatToggle"] = "Include up to 20 recent combat records",
        ["SupportPrivacyTitle"] = "Review before sharing",
        ["SupportPrivacyText"] = "Logs can contain character, party and enemy names or network addresses. You choose where the package is saved and who receives it.",
        ["SupportExport"] = "Create support package",
        ["SupportExporting"] = "Creating the package…",
        ["SupportSuccess"] = "Support package saved successfully.",
        ["SupportCancelled"] = "Export cancelled.",
        ["SupportFailure"] = "The support package could not be created: {0}",
        ["SupportFileType"] = "Nexforge support package",
        ["SupportStatusRunning"] = "Background monitoring is active",
        ["SupportStatusStopped"] = "Background monitoring is not active",
        ["CloseTitle"] = "Close window",
        ["CloseText"] = "The Companion can keep running in the background or exit completely. You can change this later in Settings.",
        ["Cancel"] = "Cancel",
        ["Exit"] = "Exit",
        ["TrayOpen"] = "Open Companion",
        ["TrayExit"] = "Exit",
        ["BackgroundMonitoring"] = "Background monitoring",
        ["AutomaticMode"] = "AUTOMATIC",
        ["BossTimelineHint"] = "The timeline updates automatically when encounter information is available.",
        ["HealingAccuracyHint"] = "Healing totals may be incomplete while support for every healing effect is being expanded.",
        ["PartyGuideHint"] = "Party composition details appear automatically when they become available.",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["hu"] = new Dictionary<string, string>
            {
                ["AppReady"]="KÉSZENLÉT", ["NavModules"]="MODULOK", ["NavHome"]="Főoldal", ["NavDps"]="DPS mérő", ["NavCombatLog"]="Harcnapló", ["NavBoss"]="Boss időzítő", ["NavGuides"]="Class guide", ["NavSettings"]="Beállítások", ["NavSupport"]="Támogatás", ["NavSystem"]="FEJLESZTŐ", ["NavDeveloper"]="Élő monitor",
                ["SettingsTitle"]="Beállítások", ["SettingsSubtitle"]="A Companion és a lebegő ablakok működésének beállítása.", ["LanguageSection"]="NYELV", ["LanguageLabel"]="Alkalmazás nyelve", ["LanguageHint"]="A módosítás azonnal életbe lép, és a következő indításra is megmarad.",
                ["OverlaySection"]="LEBEGŐ ABLAKOK", ["DpsMeter"]="DPS mérő", ["DpsOverlayHint"]="Kompakt lebegő ablak. A felső sávval húzható.", ["BossAssistant"]="Boss segéd", ["BossOverlayHint"]="Kompakt időzítő. A felső sávval húzható.", ["Open"]="Megnyitás",
                ["MapHelper"]="Térképsegéd", ["MapHelperHint"]="Bekapcsolva az M billentyűre reagál a játékban.", ["MapHelperTooltip"]="A Térképsegéd borderless fullscreen módban működik megbízhatóan. Más megjelenítési módban előfordulhat, hogy nem jelenik meg vagy pontatlanul igazodik.", ["Enabled"]="Engedélyezve", ["MapAdminWarning"]="A Térképsegéd használatához rendszergazdaként indítsd el az AION 2 Companiont.",
                ["PetCollection"]="PETGYŰJTEMÉNY", ["PetCollectionStatus"]="Gyűjteményadatok állapota", ["PetUnknown"]="ISMERETLEN", ["PetUnknownTitle"]="Teljes lista nélkül", ["PetUnknownText"]="A gyűjteményed még nem ismert. Az ismeretlen állapotú peteket ideiglenesen mutatjuk.", ["PetPartial"]="RÉSZLEGES", ["PetPartialTitle"]="Részleges adat", ["PetPartialText"]="Legalább egy megszerzett petet már érzékeltünk, de a gyűjtemény többi része még ismeretlen.", ["PetReady"]="KÉSZ", ["PetReadyTitle"]="Teljes lista elérhető", ["PetReadyText"]="A teljes petgyűjtemény elérhető. A hiányzó- és szintszűrők használhatók.",
                ["ApplicationSection"]="ALKALMAZÁS", ["AutoCapture"]="Automatikus megfigyelés indításkor", ["AutoCaptureHint"]="A Companion megnyitásakor automatikusan elindítja a játékadatok háttérbeli figyelését.", ["CloseBehavior"]="Főablak bezárásakor", ["CloseBehaviorHint"]="A háttérben futó mód aktívan tartja a lebegő ablakokat és segédeket.", ["CloseAsk"]="Első alkalommal kérdez", ["CloseTray"]="Háttérben marad", ["CloseExit"]="Kilép az alkalmazásból",
                ["LoggingSection"]="DIAGNOSZTIKAI NAPLÓK", ["LoggingToggle"]="Diagnosztikai naplófájlok mentése", ["LoggingOn"]="Bekapcsolva. Legfeljebb három 5 MB-os fájl marad meg itt: {0}", ["LoggingOff"]="Alapból kikapcsolva. Bekapcsolva is legfeljebb három 5 MB-os fájl marad meg.",
                ["CombatLogSection"]="HARCNAPLÓ", ["AutoDps"]="A DPS ablak automatikusan jelenjen meg és tűnjön el harc közben", ["SavedFights"]="Megtartott harcok", ["SavedFightsHint"]="A kedvencek a limit elérésekor sem törlődnek automatikusan.", ["Fights100"]="100 harc", ["Fights500"]="500 harc", ["Fights1000"]="1 000 harc", ["Unlimited"]="Korlátlan",
                ["SupportTitle"]="Támogatás és diagnosztika", ["SupportSubtitle"]="Készíts támogatási csomagot, ha a Nexforge csapata kéri.", ["SupportWhatTitle"]="Mi kerül bele?", ["SupportWhatText"]="Az alkalmazás verziója, alapvető rendszerinformációk, a beállítások összefoglalója és a legutóbbi diagnosztikai naplók. Védett alkalmazásadatok és hitelesítési adatok nem kerülnek bele.", ["SupportCombatToggle"]="Legfeljebb 20 friss harc csatolása", ["SupportPrivacyTitle"]="Megosztás előtt ellenőrizd", ["SupportPrivacyText"]="A naplók karakter-, csapat- és ellenfélneveket, illetve hálózati címeket is tartalmazhatnak. Te választod ki a mentési helyet és azt is, kinek küldöd el.", ["SupportExport"]="Támogatási csomag készítése", ["SupportExporting"]="Csomag készítése…", ["SupportSuccess"]="A támogatási csomag elkészült.", ["SupportCancelled"]="Az exportálás megszakítva.", ["SupportFailure"]="A támogatási csomag nem készíthető el: {0}", ["SupportFileType"]="Nexforge támogatási csomag", ["SupportStatusRunning"]="A háttérbeli megfigyelés aktív", ["SupportStatusStopped"]="A háttérbeli megfigyelés nem aktív",
                ["CloseTitle"]="Ablak bezárása", ["CloseText"]="A Companion futhat tovább a háttérben, vagy teljesen leállhat. Ezt később a Beállításokban módosíthatod.", ["Cancel"]="Mégse", ["Exit"]="Kilépés", ["TrayOpen"]="Companion megnyitása", ["TrayExit"]="Kilépés",
                ["BackgroundMonitoring"]="Háttérbeli megfigyelés", ["AutomaticMode"]="AUTOMATIKUS",
                ["BossTimelineHint"]="Az idővonal automatikusan frissül, amikor elérhetők a harchoz tartozó információk.", ["HealingAccuracyHint"]="A gyógyítási összesítés hiányos lehet, amíg minden gyógyító hatás támogatása elkészül.", ["PartyGuideHint"]="A csapat összeállítása automatikusan megjelenik, amikor elérhetővé válik.",
            },
            ["de"] = new Dictionary<string, string>
            {
                ["AppReady"]="BEREIT", ["NavModules"]="MODULE", ["NavHome"]="Start", ["NavDps"]="DPS-Meter", ["NavCombatLog"]="Kampfverlauf", ["NavBoss"]="Boss-Timer", ["NavGuides"]="Klassen-Guides", ["NavSettings"]="Einstellungen", ["NavSupport"]="Support", ["NavSystem"]="ENTWICKLER", ["NavDeveloper"]="Live-Monitor",
                ["SettingsTitle"]="Einstellungen", ["SettingsSubtitle"]="Verhalten des Companions und der Overlays festlegen.", ["LanguageSection"]="SPRACHE", ["LanguageLabel"]="Anwendungssprache", ["LanguageHint"]="Änderungen werden sofort übernommen und für den nächsten Start gespeichert.", ["OverlaySection"]="OVERLAYS", ["DpsMeter"]="DPS-Meter", ["DpsOverlayHint"]="Kompaktes schwebendes Fenster, an der oberen Leiste verschiebbar.", ["BossAssistant"]="Boss-Assistent", ["BossOverlayHint"]="Kompaktes Timer-Fenster, an der oberen Leiste verschiebbar.", ["Open"]="Öffnen",
                ["MapHelper"]="Kartenhilfe", ["MapHelperHint"]="Reagiert im Spiel auf die M-Taste, wenn aktiviert.", ["MapHelperTooltip"]="Die Kartenhilfe funktioniert zuverlässig im randlosen Vollbildmodus. In anderen Modi kann sie fehlen oder falsch ausgerichtet sein.", ["Enabled"]="Aktiviert", ["MapAdminWarning"]="Starte AION 2 Companion als Administrator, um die Kartenhilfe zu verwenden.",
                ["PetCollection"]="PET-SAMMLUNG", ["PetCollectionStatus"]="Status der Sammlungsdaten", ["PetUnknown"]="UNBEKANNT", ["PetUnknownTitle"]="Warte auf die vollständige Liste", ["PetUnknownText"]="Deine Sammlung ist noch nicht bekannt. Pets mit unbekanntem Status werden vorübergehend angezeigt.", ["PetPartial"]="TEILWEISE", ["PetPartialTitle"]="Teilweise Daten", ["PetPartialText"]="Mindestens ein eigenes Pet wurde erkannt, der Rest der Sammlung ist noch unbekannt.", ["PetReady"]="BEREIT", ["PetReadyTitle"]="Vollständige Liste verfügbar", ["PetReadyText"]="Deine vollständige Pet-Sammlung ist verfügbar. Filter für fehlende Pets und Stufen können verwendet werden.",
                ["ApplicationSection"]="ANWENDUNG", ["AutoCapture"]="Überwachung automatisch starten", ["AutoCaptureHint"]="Startet beim Öffnen automatisch die Hintergrundverbindung zu den Spieldaten.", ["CloseBehavior"]="Beim Schließen des Hauptfensters", ["CloseBehaviorHint"]="Im Hintergrund bleiben aktive Overlays und Hilfen weiterhin verfügbar.", ["CloseAsk"]="Beim ersten Mal fragen", ["CloseTray"]="Im Hintergrund weiterlaufen", ["CloseExit"]="Anwendung beenden",
                ["LoggingSection"]="DIAGNOSEPROTOKOLLE", ["LoggingToggle"]="Diagnoseprotokolle speichern", ["LoggingOn"]="Aktiviert. Bis zu drei Dateien mit je 5 MB werden hier gespeichert: {0}", ["LoggingOff"]="Standardmäßig deaktiviert. Aktiviert werden höchstens drei Dateien mit je 5 MB behalten.", ["CombatLogSection"]="KAMPFVERLAUF", ["AutoDps"]="DPS-Overlay im Kampf automatisch ein- und ausblenden", ["SavedFights"]="Gespeicherte Kämpfe", ["SavedFightsHint"]="Favoriten werden beim Erreichen des Limits nicht automatisch gelöscht.", ["Fights100"]="100 Kämpfe", ["Fights500"]="500 Kämpfe", ["Fights1000"]="1.000 Kämpfe", ["Unlimited"]="Unbegrenzt",
                ["SupportTitle"]="Support & Diagnose", ["SupportSubtitle"]="Erstelle ein Supportpaket, wenn das Nexforge-Team darum bittet.", ["SupportWhatTitle"]="Was ist enthalten?", ["SupportWhatText"]="Anwendungsversion, grundlegende Systeminformationen, eine Zusammenfassung der Einstellungen und aktuelle Diagnoseprotokolle. Geschützte Anwendungsdaten und Zugangsdaten werden nicht aufgenommen.", ["SupportCombatToggle"]="Bis zu 20 aktuelle Kampfaufzeichnungen einschließen", ["SupportPrivacyTitle"]="Vor dem Teilen prüfen", ["SupportPrivacyText"]="Protokolle können Charakter-, Gruppen- und Gegnernamen sowie Netzwerkadressen enthalten. Du bestimmst Speicherort und Empfänger.", ["SupportExport"]="Supportpaket erstellen", ["SupportExporting"]="Paket wird erstellt…", ["SupportSuccess"]="Supportpaket wurde gespeichert.", ["SupportCancelled"]="Export abgebrochen.", ["SupportFailure"]="Das Supportpaket konnte nicht erstellt werden: {0}", ["SupportFileType"]="Nexforge-Supportpaket", ["SupportStatusRunning"]="Hintergrundüberwachung ist aktiv", ["SupportStatusStopped"]="Hintergrundüberwachung ist nicht aktiv",
                ["CloseTitle"]="Fenster schließen", ["CloseText"]="Der Companion kann im Hintergrund weiterlaufen oder vollständig beendet werden. Dies kann später in den Einstellungen geändert werden.", ["Cancel"]="Abbrechen", ["Exit"]="Beenden", ["TrayOpen"]="Companion öffnen", ["TrayExit"]="Beenden",
                ["BackgroundMonitoring"]="Hintergrundüberwachung", ["AutomaticMode"]="AUTOMATISCH",
                ["BossTimelineHint"]="Die Zeitleiste wird automatisch aktualisiert, sobald Informationen zum Kampf verfügbar sind.", ["HealingAccuracyHint"]="Heilungswerte können unvollständig sein, solange die Unterstützung aller Heileffekte erweitert wird.", ["PartyGuideHint"]="Details zur Gruppenzusammenstellung erscheinen automatisch, sobald sie verfügbar sind.",
            },
            ["pl"] = new Dictionary<string, string>
            {
                ["AppReady"]="GOTOWE", ["NavModules"]="MODUŁY", ["NavHome"]="Strona główna", ["NavDps"]="Miernik DPS", ["NavCombatLog"]="Historia walk", ["NavBoss"]="Timery bossów", ["NavGuides"]="Poradniki klas", ["NavSettings"]="Ustawienia", ["NavSupport"]="Wsparcie", ["NavSystem"]="DEWELOPER", ["NavDeveloper"]="Monitor na żywo",
                ["SettingsTitle"]="Ustawienia", ["SettingsSubtitle"]="Dostosuj działanie Companiona i jego nakładek.", ["LanguageSection"]="JĘZYK", ["LanguageLabel"]="Język aplikacji", ["LanguageHint"]="Zmiany są stosowane od razu i zapisywane na następne uruchomienie.", ["OverlaySection"]="NAKŁADKI", ["DpsMeter"]="Miernik DPS", ["DpsOverlayHint"]="Kompaktowe okno pływające. Przeciągnij je za górny pasek.", ["BossAssistant"]="Asystent bossów", ["BossOverlayHint"]="Kompaktowe okno timerów. Przeciągnij je za górny pasek.", ["Open"]="Otwórz",
                ["MapHelper"]="Pomocnik mapy", ["MapHelperHint"]="Po włączeniu reaguje w grze na klawisz M.", ["MapHelperTooltip"]="Pomocnik mapy działa niezawodnie w trybie pełnoekranowym bez ramek. W innych trybach może się nie pojawić lub być źle ustawiony.", ["Enabled"]="Włączone", ["MapAdminWarning"]="Uruchom AION 2 Companion jako administrator, aby używać Pomocnika mapy.",
                ["PetCollection"]="KOLEKCJA PETÓW", ["PetCollectionStatus"]="Stan danych kolekcji", ["PetUnknown"]="NIEZNANE", ["PetUnknownTitle"]="Oczekiwanie na pełną listę", ["PetUnknownText"]="Twoja kolekcja nie jest jeszcze znana. Pety o nieznanym stanie są tymczasowo wyświetlane.", ["PetPartial"]="CZĘŚCIOWE", ["PetPartialTitle"]="Dane częściowe", ["PetPartialText"]="Wykryto co najmniej jednego posiadanego peta, ale reszta kolekcji nadal jest nieznana.", ["PetReady"]="GOTOWE", ["PetReadyTitle"]="Pełna lista dostępna", ["PetReadyText"]="Pełna kolekcja petów jest dostępna. Możesz używać filtrów brakujących petów i poziomów.",
                ["ApplicationSection"]="APLIKACJA", ["AutoCapture"]="Automatycznie uruchamiaj monitorowanie", ["AutoCaptureHint"]="Uruchamia połączenie z danymi gry w tle przy otwarciu Companiona.", ["CloseBehavior"]="Po zamknięciu głównego okna", ["CloseBehaviorHint"]="Praca w tle pozostawia aktywne nakładki i pomocników.", ["CloseAsk"]="Zapytaj za pierwszym razem", ["CloseTray"]="Pozostaw w tle", ["CloseExit"]="Zamknij aplikację",
                ["LoggingSection"]="DZIENNIKI DIAGNOSTYCZNE", ["LoggingToggle"]="Zapisuj dzienniki diagnostyczne", ["LoggingOn"]="Włączone. Przechowywane są maksymalnie trzy pliki po 5 MB w: {0}", ["LoggingOff"]="Domyślnie wyłączone. Po włączeniu przechowywane są najwyżej trzy pliki po 5 MB.", ["CombatLogSection"]="HISTORIA WALK", ["AutoDps"]="Automatycznie pokazuj i ukrywaj nakładkę DPS podczas walki", ["SavedFights"]="Zapisane walki", ["SavedFightsHint"]="Ulubione nie są automatycznie usuwane po osiągnięciu limitu.", ["Fights100"]="100 walk", ["Fights500"]="500 walk", ["Fights1000"]="1 000 walk", ["Unlimited"]="Bez limitu",
                ["SupportTitle"]="Wsparcie i diagnostyka", ["SupportSubtitle"]="Utwórz pakiet wsparcia, gdy poprosi o niego zespół Nexforge.", ["SupportWhatTitle"]="Co zostanie dołączone?", ["SupportWhatText"]="Wersja aplikacji, podstawowe informacje o systemie, podsumowanie ustawień i ostatnie dzienniki diagnostyczne. Chronione dane aplikacji i dane logowania nie są dołączane.", ["SupportCombatToggle"]="Dołącz maksymalnie 20 ostatnich zapisów walk", ["SupportPrivacyTitle"]="Sprawdź przed udostępnieniem", ["SupportPrivacyText"]="Dzienniki mogą zawierać nazwy postaci, grup i przeciwników oraz adresy sieciowe. Ty wybierasz miejsce zapisu oraz odbiorcę.", ["SupportExport"]="Utwórz pakiet wsparcia", ["SupportExporting"]="Tworzenie pakietu…", ["SupportSuccess"]="Pakiet wsparcia został zapisany.", ["SupportCancelled"]="Eksport anulowany.", ["SupportFailure"]="Nie udało się utworzyć pakietu wsparcia: {0}", ["SupportFileType"]="Pakiet wsparcia Nexforge", ["SupportStatusRunning"]="Monitorowanie w tle jest aktywne", ["SupportStatusStopped"]="Monitorowanie w tle nie jest aktywne",
                ["CloseTitle"]="Zamknij okno", ["CloseText"]="Companion może działać dalej w tle albo zostać całkowicie zamknięty. Możesz to później zmienić w Ustawieniach.", ["Cancel"]="Anuluj", ["Exit"]="Zamknij", ["TrayOpen"]="Otwórz Companion", ["TrayExit"]="Zamknij",
                ["BackgroundMonitoring"]="Monitorowanie w tle", ["AutomaticMode"]="AUTOMATYCZNE",
                ["BossTimelineHint"]="Oś czasu aktualizuje się automatycznie, gdy dane o starciu są dostępne.", ["HealingAccuracyHint"]="Suma leczenia może być niepełna, dopóki obsługa wszystkich efektów leczenia jest rozwijana.", ["PartyGuideHint"]="Skład grupy pojawi się automatycznie, gdy dane będą dostępne.",
            },
        };

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("en", "English"),
        new("hu", "Magyar"),
        new("de", "Deutsch"),
        new("pl", "Polski"),
    ];

    public static string CurrentLanguageCode { get; private set; } = "en";
    public static event EventHandler? LanguageChanged;

    public static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "en";

        foreach (LanguageOption language in SupportedLanguages)
        {
            if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
                return language.Code;
        }

        return "en";
    }

    public static string Get(string key)
    {
        if (Translations.TryGetValue(CurrentLanguageCode, out var translation) &&
            translation.TryGetValue(key, out string? value))
        {
            return value;
        }

        return English.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public static void Apply(string? languageCode)
    {
        string normalized = NormalizeLanguageCode(languageCode);
        CurrentLanguageCode = normalized;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // The English resources remain a safe fallback.
        }

        if (Application.Current is { } app)
        {
            foreach ((string key, string value) in English)
                app.Resources[key] = value;

            if (Translations.TryGetValue(normalized, out var translation))
            {
                foreach ((string key, string value) in translation)
                    app.Resources[key] = value;
            }
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}
