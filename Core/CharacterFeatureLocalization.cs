using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core;

internal static class CharacterFeatureLocalization
{
    internal static IReadOnlyDictionary<string, string> English { get; } = new Dictionary<string, string>
    {
        ["NavAccount"] = "Account",
        ["NavEventTimers"] = "Timers",
        ["NavMyCharacters"] = "My characters",
        ["NavCharacterSearch"] = "Character search",
        ["NavLeaderboards"] = "Leaderboards",
        ["BackToCharacters"] = "Back to my characters",
        ["AccountPageKicker"] = "NEXFORGE ACCOUNT",
        ["AccountPageTitle"] = "My profile",
        ["AccountPageSubtitle"] = "Manage your Nexforge connection and the online features linked to it.",
        ["AccountNotConnectedTitle"] = "Connect your Nexforge Account",
        ["AccountNotConnectedText"] = "Your local combat tools work without signing in. Connecting adds character profiles and future online features.",
        ["AccountConnectedTitle"] = "Account connected",
        ["OpenAccountCenter"] = "Open account center",
        ["AccountBenefitsTitle"] = "More with Nexforge",
        ["AccountBenefitsText"] = "Keep character profiles, future rankings and connected services together under one account.",
        ["AccountCharactersSummary"] = "Your detected characters stay available locally, even when Nexforge is offline.",
        ["OpenMyCharacters"] = "Open my characters",
        ["PairingCode"] = "PAIRING CODE",
        ["PairingWaiting"] = "Finish connecting your account in the browser. The Companion will continue automatically.",
        ["MyCharactersKicker"] = "LOCAL CHARACTER ROSTER",
        ["MyCharactersTitle"] = "My characters",
        ["MyCharactersSubtitle"] = "Characters reliably detected from your own running game session.",
        ["NoCharacterTitle"] = "No character detected",
        ["NoCharacterText"] = "Start AION 2 and enable game monitoring to detect your character.",
        ["DetectedLocally"] = "DETECTED LOCALLY",
        ["Unverified"] = "UNVERIFIED",
        ["UnverifiedHint"] = "Detected in your game session. Ownership has not been verified yet.",
        ["ViewCharacter"] = "View character",
        ["LevelValue"] = "Level {0}",
        ["GearScoreValue"] = "Gear score {0}",
        ["UnknownLevel"] = "Level unknown",
        ["UnknownGearScore"] = "Gear score unknown",
        ["ServerIdUnconfirmed"] = "Server {0} · not confirmed",
        ["UnknownServer"] = "Server not confirmed",
        ["Region"] = "REGION",
        ["DetectedAt"] = "Detected {0}",
        ["CharacterDetailsKicker"] = "CHARACTER DETAILS",
        ["CharacterDetailsTitle"] = "Character profile",
        ["CharacterDetailsSubtitle"] = "A visual overview of equipment, abilities and progression.",
        ["OverviewTab"] = "Overview",
        ["EquipmentTab"] = "Equipment",
        ["SkillsTab"] = "Skills",
        ["StigmasTab"] = "Stigmas",
        ["DaevanionTab"] = "Daevanion",
        ["TalentsTab"] = "Talents",
        ["ArcanaTab"] = "Arcana",
        ["StatsTab"] = "Stats",
        ["LiveIdentity"] = "LIVE IDENTITY",
        ["ProfileSourceHint"] = "These details come directly from your current game session.",
        ["DetailedDataUnavailable"] = "Detailed character data is not available yet",
        ["EquipmentEmptyText"] = "Equipment slots are ready for real item icons, rarity frames, enhancement levels and details.",
        ["SkillsEmptyText"] = "Skill icons, levels and descriptions will appear here when character skill data is available.",
        ["StigmasEmptyText"] = "Equipped stigmas, levels and detailed effects will appear here when available.",
        ["DaevanionEmptyText"] = "The interactive progression board is ready for unlocked nodes and future builds.",
        ["TalentsEmptyText"] = "Talent branches and spent points will appear here when reliable data is available.",
        ["ArcanaEmptyText"] = "Arcana cards, levels and bonuses will appear here when reliable data is available.",
        ["StatsEmptyText"] = "Additional combat and character statistics will appear here when available.",
        ["NoItemsLoaded"] = "No equipment data loaded",
        ["NoSkillsLoaded"] = "No skill data loaded",
        ["NoStigmasLoaded"] = "No stigma data loaded",
        ["NoProgressionLoaded"] = "No progression data loaded",
        ["NoTalentsLoaded"] = "No talent data loaded",
        ["NoArcanaLoaded"] = "No arcana data loaded",
        ["NoStatsLoaded"] = "No additional statistics loaded",
        ["EquipmentSlotWeapon"] = "Main weapon",
        ["EquipmentSlotOffhand"] = "Off hand",
        ["EquipmentSlotHead"] = "Head",
        ["EquipmentSlotChest"] = "Chest",
        ["EquipmentSlotHands"] = "Hands",
        ["EquipmentSlotLegs"] = "Legs",
        ["EquipmentSlotFeet"] = "Feet",
        ["EquipmentSlotShoulders"] = "Shoulders",
        ["EquipmentSlotNecklace"] = "Necklace",
        ["EquipmentSlotEarring1"] = "Earring I",
        ["EquipmentSlotEarring2"] = "Earring II",
        ["EquipmentSlotRing1"] = "Ring I",
        ["EquipmentSlotRing2"] = "Ring II",
        ["EquipmentSlotBelt"] = "Belt",
        ["CharacterSearchKicker"] = "NEXFORGE CHARACTER NETWORK",
        ["CharacterSearchTitle"] = "Character search",
        ["CharacterSearchSubtitle"] = "Search public character profiles when the online directory becomes available.",
        ["CharacterSearchWatermark"] = "Character name…",
        ["Search"] = "Search",
        ["SearchNotAvailable"] = "Character search is still being forged. No online search is performed yet.",
        ["LeaderboardsKicker"] = "NEXFORGE RANKINGS",
        ["LeaderboardsTitle"] = "Leaderboards",
        ["LeaderboardsSubtitle"] = "Verified encounter rankings will appear here when the service is ready.",
        ["LeaderboardPlayer"] = "PLAYER",
        ["LeaderboardBoss"] = "BOSS",
        ["LeaderboardDps"] = "DPS",
        ["LeaderboardServer"] = "SERVER",
        ["LeaderboardsNotAvailable"] = "Rankings are still being forged. No scores are published from this screen yet.",
    };

    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Languages { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["hu"] = new Dictionary<string, string>
            {
                ["NavAccount"]="Fiók", ["NavEventTimers"]="Időzítők", ["NavMyCharacters"]="Karaktereim", ["NavCharacterSearch"]="Karakterkereső", ["NavLeaderboards"]="Ranglisták", ["BackToCharacters"]="Vissza a karaktereimhez",
                ["AccountPageKicker"]="NEXFORGE FIÓK", ["AccountPageTitle"]="Profilom", ["AccountPageSubtitle"]="A Nexforge-fiókod és a hozzá tartozó online funkciók kezelése.", ["AccountNotConnectedTitle"]="Csatlakoztasd a Nexforge-fiókodat", ["AccountNotConnectedText"]="A helyi harci eszközök belépés nélkül is működnek. A fiók később karakterprofilokat és online funkciókat ad.", ["AccountConnectedTitle"]="A fiók csatlakoztatva", ["OpenAccountCenter"]="Fiókközpont megnyitása", ["AccountBenefitsTitle"]="Több lehetőség Nexforge-fiókkal", ["AccountBenefitsText"]="A karakterprofilok, későbbi ranglisták és kapcsolt szolgáltatások egy fiók alá kerülnek.", ["AccountCharactersSummary"]="A felismert karaktereid helyben akkor is elérhetők, ha a Nexforge éppen offline.", ["OpenMyCharacters"]="Karaktereim megnyitása", ["PairingCode"]="PÁROSÍTÁSI KÓD", ["PairingWaiting"]="Fejezd be a csatlakoztatást a böngészőben. A Companion ezután automatikusan továbblép.",
                ["MyCharactersKicker"]="HELYBEN FELISMERT KARAKTEREK", ["MyCharactersTitle"]="Karaktereim", ["MyCharactersSubtitle"]="A saját futó játékodból megbízhatóan felismert karakterek.", ["NoCharacterTitle"]="Nincs felismert karakter", ["NoCharacterText"]="Indítsd el az AION 2-t és kapcsold be a játékfigyelést a karakter felismeréséhez.", ["DetectedLocally"]="HELYBEN FELISMERVE", ["Unverified"]="NINCS HITELESÍTVE", ["UnverifiedHint"]="A jelenlegi játékmenetben felismert karakter. A tulajdonjog még nincs hitelesítve.", ["ViewCharacter"]="Karakter megnyitása", ["LevelValue"]="{0}. szint", ["GearScoreValue"]="Felszerelési pont: {0}", ["UnknownLevel"]="Ismeretlen szint", ["UnknownGearScore"]="Ismeretlen felszerelési pont", ["ServerIdUnconfirmed"]="{0}. szerver · nincs megerősítve", ["UnknownServer"]="A szerver nincs megerősítve", ["Region"]="RÉGIÓ", ["DetectedAt"]="Felismerve: {0}",
                ["CharacterDetailsKicker"]="KARAKTERADATLAP", ["CharacterDetailsTitle"]="Karakterprofil", ["CharacterDetailsSubtitle"]="A felszerelés, képességek és fejlődés vizuális áttekintése.", ["OverviewTab"]="Áttekintés", ["EquipmentTab"]="Felszerelés", ["SkillsTab"]="Skillek", ["StigmasTab"]="Stigmák", ["DaevanionTab"]="Daevanion", ["TalentsTab"]="Talentek", ["ArcanaTab"]="Arcana", ["StatsTab"]="Statok", ["LiveIdentity"]="ÉLŐ KARAKTERADAT", ["ProfileSourceHint"]="Ezek az adatok közvetlenül a jelenlegi játékmenetből származnak.", ["DetailedDataUnavailable"]="A részletes karakteradatok még nem érhetők el", ["EquipmentEmptyText"]="A felszerelésslotok készen állnak a valódi itemikonokra, ritkasági keretekre, fejlesztési szintekre és részletekre.", ["SkillsEmptyText"]="A skillikonok, szintek és leírások akkor jelennek meg, amikor elérhetők a karakter skilladatai.", ["StigmasEmptyText"]="A felszerelt stigmák, szintek és hatások akkor jelennek meg, amikor rendelkezésre állnak.", ["DaevanionEmptyText"]="Az interaktív fejlődési tábla készen áll a feloldott node-okra és a későbbi buildekre.", ["TalentsEmptyText"]="A talentágak és az elköltött pontok akkor jelennek meg, amikor megbízható adat érkezik.", ["ArcanaEmptyText"]="Az Arcana-kártyák, szintek és bónuszok akkor jelennek meg, amikor megbízható adat érkezik.", ["StatsEmptyText"]="A további harci és karakterstatisztikák akkor jelennek meg, amikor elérhetők.", ["NoItemsLoaded"]="Nincs betöltött felszerelésadat", ["NoSkillsLoaded"]="Nincs betöltött skilladat", ["NoStigmasLoaded"]="Nincs betöltött stigmaadat", ["NoProgressionLoaded"]="Nincs betöltött fejlődési adat", ["NoTalentsLoaded"]="Nincs betöltött talentadat", ["NoArcanaLoaded"]="Nincs betöltött Arcana-adat", ["NoStatsLoaded"]="Nincs betöltött további statisztika",
                ["EquipmentSlotWeapon"]="Főfegyver", ["EquipmentSlotOffhand"]="Másodlagos kéz", ["EquipmentSlotHead"]="Fej", ["EquipmentSlotChest"]="Mellkas", ["EquipmentSlotHands"]="Kezek", ["EquipmentSlotLegs"]="Lábak", ["EquipmentSlotFeet"]="Lábfej", ["EquipmentSlotShoulders"]="Váll", ["EquipmentSlotNecklace"]="Nyaklánc", ["EquipmentSlotEarring1"]="Fülbevaló I", ["EquipmentSlotEarring2"]="Fülbevaló II", ["EquipmentSlotRing1"]="Gyűrű I", ["EquipmentSlotRing2"]="Gyűrű II", ["EquipmentSlotBelt"]="Öv",
                ["CharacterSearchKicker"]="NEXFORGE KARAKTERHÁLÓZAT", ["CharacterSearchTitle"]="Karakterkereső", ["CharacterSearchSubtitle"]="Publikus karakterprofilok keresése, amikor az online címtár elkészül.", ["CharacterSearchWatermark"]="Karakternév…", ["Search"]="Keresés", ["SearchNotAvailable"]="A karakterkereső még készül. Innen egyelőre nem indul online keresés.", ["LeaderboardsKicker"]="NEXFORGE RANGLISTÁK", ["LeaderboardsTitle"]="Ranglisták", ["LeaderboardsSubtitle"]="A hitelesített harci rangsorok akkor jelennek meg, amikor a szolgáltatás elkészül.", ["LeaderboardPlayer"]="JÁTÉKOS", ["LeaderboardBoss"]="BOSS", ["LeaderboardDps"]="DPS", ["LeaderboardServer"]="SZERVER", ["LeaderboardsNotAvailable"]="A ranglisták még készülnek. Erről a képernyőről egyelőre nem kerül ki eredmény.",
            },
            ["de"] = new Dictionary<string, string>
            {
                ["NavAccount"]="Konto", ["NavEventTimers"]="Timer", ["NavMyCharacters"]="Meine Charaktere", ["NavCharacterSearch"]="Charaktersuche", ["NavLeaderboards"]="Ranglisten", ["BackToCharacters"]="Zurück zu meinen Charakteren",
                ["AccountPageKicker"]="NEXFORGE-KONTO", ["AccountPageTitle"]="Mein Profil", ["AccountPageSubtitle"]="Verwalte deine Nexforge-Verbindung und die damit verbundenen Online-Funktionen.", ["AccountNotConnectedTitle"]="Nexforge-Konto verbinden", ["AccountNotConnectedText"]="Lokale Kampfwerkzeuge funktionieren ohne Anmeldung. Ein Konto ergänzt Charakterprofile und zukünftige Online-Funktionen.", ["AccountConnectedTitle"]="Konto verbunden", ["OpenAccountCenter"]="Kontocenter öffnen", ["AccountBenefitsTitle"]="Mehr mit Nexforge", ["AccountBenefitsText"]="Charakterprofile, zukünftige Ranglisten und verbundene Dienste bleiben unter einem Konto zusammen.", ["AccountCharactersSummary"]="Erkannte Charaktere bleiben lokal verfügbar, auch wenn Nexforge offline ist.", ["OpenMyCharacters"]="Meine Charaktere öffnen", ["PairingCode"]="VERBINDUNGSCODE", ["PairingWaiting"]="Schließe die Verbindung im Browser ab. Der Companion fährt danach automatisch fort.",
                ["MyCharactersKicker"]="LOKAL ERKANNTE CHARAKTERE", ["MyCharactersTitle"]="Meine Charaktere", ["MyCharactersSubtitle"]="Charaktere, die zuverlässig aus deiner laufenden Spielsitzung erkannt wurden.", ["NoCharacterTitle"]="Kein Charakter erkannt", ["NoCharacterText"]="Starte AION 2 und aktiviere die Spielüberwachung, um deinen Charakter zu erkennen.", ["DetectedLocally"]="LOKAL ERKANNT", ["Unverified"]="NICHT BESTÄTIGT", ["UnverifiedHint"]="In deiner Spielsitzung erkannt. Der Besitz wurde noch nicht bestätigt.", ["ViewCharacter"]="Charakter öffnen", ["LevelValue"]="Stufe {0}", ["GearScoreValue"]="Ausrüstungswert {0}", ["UnknownLevel"]="Stufe unbekannt", ["UnknownGearScore"]="Ausrüstungswert unbekannt", ["ServerIdUnconfirmed"]="Server {0} · nicht bestätigt", ["UnknownServer"]="Server nicht bestätigt", ["Region"]="REGION", ["DetectedAt"]="Erkannt: {0}",
                ["CharacterDetailsKicker"]="CHARAKTERDETAILS", ["CharacterDetailsTitle"]="Charakterprofil", ["CharacterDetailsSubtitle"]="Visuelle Übersicht über Ausrüstung, Fähigkeiten und Fortschritt.", ["OverviewTab"]="Übersicht", ["EquipmentTab"]="Ausrüstung", ["SkillsTab"]="Fähigkeiten", ["StigmasTab"]="Stigmen", ["DaevanionTab"]="Daevanion", ["TalentsTab"]="Talente", ["ArcanaTab"]="Arcana", ["StatsTab"]="Werte", ["LiveIdentity"]="LIVE-CHARAKTERDATEN", ["ProfileSourceHint"]="Diese Angaben stammen direkt aus deiner aktuellen Spielsitzung.", ["DetailedDataUnavailable"]="Detaillierte Charakterdaten sind noch nicht verfügbar", ["EquipmentEmptyText"]="Die Ausrüstungsplätze sind für echte Symbole, Seltenheitsrahmen, Verbesserungsstufen und Details vorbereitet.", ["SkillsEmptyText"]="Fähigkeitssymbole, Stufen und Beschreibungen erscheinen, sobald Charakterdaten verfügbar sind.", ["StigmasEmptyText"]="Ausgerüstete Stigmen, Stufen und Effekte erscheinen, sobald sie verfügbar sind.", ["DaevanionEmptyText"]="Die interaktive Fortschrittstafel ist für freigeschaltete Knoten und zukünftige Builds vorbereitet.", ["TalentsEmptyText"]="Talentzweige und verteilte Punkte erscheinen, sobald verlässliche Daten vorliegen.", ["ArcanaEmptyText"]="Arcana-Karten, Stufen und Boni erscheinen, sobald verlässliche Daten vorliegen.", ["StatsEmptyText"]="Weitere Kampf- und Charakterwerte erscheinen, sobald sie verfügbar sind.", ["NoItemsLoaded"]="Keine Ausrüstungsdaten geladen", ["NoSkillsLoaded"]="Keine Fähigkeitsdaten geladen", ["NoStigmasLoaded"]="Keine Stigma-Daten geladen", ["NoProgressionLoaded"]="Keine Fortschrittsdaten geladen", ["NoTalentsLoaded"]="Keine Talentdaten geladen", ["NoArcanaLoaded"]="Keine Arcana-Daten geladen", ["NoStatsLoaded"]="Keine weiteren Werte geladen",
                ["EquipmentSlotWeapon"]="Hauptwaffe", ["EquipmentSlotOffhand"]="Nebenhand", ["EquipmentSlotHead"]="Kopf", ["EquipmentSlotChest"]="Brust", ["EquipmentSlotHands"]="Hände", ["EquipmentSlotLegs"]="Beine", ["EquipmentSlotFeet"]="Füße", ["EquipmentSlotShoulders"]="Schultern", ["EquipmentSlotNecklace"]="Halskette", ["EquipmentSlotEarring1"]="Ohrring I", ["EquipmentSlotEarring2"]="Ohrring II", ["EquipmentSlotRing1"]="Ring I", ["EquipmentSlotRing2"]="Ring II", ["EquipmentSlotBelt"]="Gürtel",
                ["CharacterSearchKicker"]="NEXFORGE-CHARAKTERNETZ", ["CharacterSearchTitle"]="Charaktersuche", ["CharacterSearchSubtitle"]="Durchsuche öffentliche Charakterprofile, sobald das Online-Verzeichnis verfügbar ist.", ["CharacterSearchWatermark"]="Charaktername…", ["Search"]="Suchen", ["SearchNotAvailable"]="Die Charaktersuche wird noch gebaut. Es wird noch keine Online-Suche ausgeführt.", ["LeaderboardsKicker"]="NEXFORGE-RANGLISTEN", ["LeaderboardsTitle"]="Ranglisten", ["LeaderboardsSubtitle"]="Bestätigte Kampfranglisten erscheinen, sobald der Dienst bereit ist.", ["LeaderboardPlayer"]="SPIELER", ["LeaderboardBoss"]="BOSS", ["LeaderboardDps"]="DPS", ["LeaderboardServer"]="SERVER", ["LeaderboardsNotAvailable"]="Die Ranglisten werden noch gebaut. Von diesem Bildschirm werden noch keine Ergebnisse veröffentlicht.",
            },
            ["pl"] = new Dictionary<string, string>
            {
                ["NavAccount"]="Konto", ["NavEventTimers"]="Timery", ["NavMyCharacters"]="Moje postacie", ["NavCharacterSearch"]="Wyszukiwanie postaci", ["NavLeaderboards"]="Rankingi", ["BackToCharacters"]="Wróć do moich postaci",
                ["AccountPageKicker"]="KONTO NEXFORGE", ["AccountPageTitle"]="Mój profil", ["AccountPageSubtitle"]="Zarządzaj połączeniem z Nexforge i powiązanymi funkcjami online.", ["AccountNotConnectedTitle"]="Połącz konto Nexforge", ["AccountNotConnectedText"]="Lokalne narzędzia walki działają bez logowania. Konto doda profile postaci i przyszłe funkcje online.", ["AccountConnectedTitle"]="Konto połączone", ["OpenAccountCenter"]="Otwórz centrum konta", ["AccountBenefitsTitle"]="Więcej z Nexforge", ["AccountBenefitsText"]="Profile postaci, przyszłe rankingi i połączone usługi pozostaną razem na jednym koncie.", ["AccountCharactersSummary"]="Wykryte postacie pozostają dostępne lokalnie, nawet gdy Nexforge jest offline.", ["OpenMyCharacters"]="Otwórz moje postacie", ["PairingCode"]="KOD POŁĄCZENIA", ["PairingWaiting"]="Dokończ łączenie konta w przeglądarce. Companion przejdzie dalej automatycznie.",
                ["MyCharactersKicker"]="LOKALNIE WYKRYTE POSTACIE", ["MyCharactersTitle"]="Moje postacie", ["MyCharactersSubtitle"]="Postacie niezawodnie wykryte z uruchomionej sesji gry.", ["NoCharacterTitle"]="Nie wykryto postaci", ["NoCharacterText"]="Uruchom AION 2 i włącz monitorowanie gry, aby wykryć postać.", ["DetectedLocally"]="WYKRYTO LOKALNIE", ["Unverified"]="NIEPOTWIERDZONA", ["UnverifiedHint"]="Wykryta w bieżącej sesji gry. Własność nie została jeszcze potwierdzona.", ["ViewCharacter"]="Otwórz postać", ["LevelValue"]="Poziom {0}", ["GearScoreValue"]="Wynik ekwipunku {0}", ["UnknownLevel"]="Poziom nieznany", ["UnknownGearScore"]="Wynik ekwipunku nieznany", ["ServerIdUnconfirmed"]="Serwer {0} · niepotwierdzony", ["UnknownServer"]="Serwer niepotwierdzony", ["Region"]="REGION", ["DetectedAt"]="Wykryto: {0}",
                ["CharacterDetailsKicker"]="SZCZEGÓŁY POSTACI", ["CharacterDetailsTitle"]="Profil postaci", ["CharacterDetailsSubtitle"]="Wizualny przegląd ekwipunku, umiejętności i rozwoju.", ["OverviewTab"]="Przegląd", ["EquipmentTab"]="Ekwipunek", ["SkillsTab"]="Umiejętności", ["StigmasTab"]="Stygmaty", ["DaevanionTab"]="Daevanion", ["TalentsTab"]="Talenty", ["ArcanaTab"]="Arcana", ["StatsTab"]="Statystyki", ["LiveIdentity"]="DANE Z GRY", ["ProfileSourceHint"]="Te dane pochodzą bezpośrednio z bieżącej sesji gry.", ["DetailedDataUnavailable"]="Szczegółowe dane postaci nie są jeszcze dostępne", ["EquipmentEmptyText"]="Miejsca ekwipunku są gotowe na prawdziwe ikony, ramki rzadkości, poziomy ulepszeń i szczegóły.", ["SkillsEmptyText"]="Ikony umiejętności, poziomy i opisy pojawią się, gdy dane postaci będą dostępne.", ["StigmasEmptyText"]="Założone stygmaty, poziomy i efekty pojawią się, gdy będą dostępne.", ["DaevanionEmptyText"]="Interaktywna plansza rozwoju jest gotowa na odblokowane węzły i przyszłe buildy.", ["TalentsEmptyText"]="Drzewka talentów i wydane punkty pojawią się po otrzymaniu wiarygodnych danych.", ["ArcanaEmptyText"]="Karty Arcana, poziomy i bonusy pojawią się po otrzymaniu wiarygodnych danych.", ["StatsEmptyText"]="Dalsze statystyki walki i postaci pojawią się, gdy będą dostępne.", ["NoItemsLoaded"]="Brak danych ekwipunku", ["NoSkillsLoaded"]="Brak danych umiejętności", ["NoStigmasLoaded"]="Brak danych stygmatów", ["NoProgressionLoaded"]="Brak danych rozwoju", ["NoTalentsLoaded"]="Brak danych talentów", ["NoArcanaLoaded"]="Brak danych Arcana", ["NoStatsLoaded"]="Brak dodatkowych statystyk",
                ["EquipmentSlotWeapon"]="Główna broń", ["EquipmentSlotOffhand"]="Druga ręka", ["EquipmentSlotHead"]="Głowa", ["EquipmentSlotChest"]="Klatka", ["EquipmentSlotHands"]="Dłonie", ["EquipmentSlotLegs"]="Nogi", ["EquipmentSlotFeet"]="Stopy", ["EquipmentSlotShoulders"]="Ramiona", ["EquipmentSlotNecklace"]="Naszyjnik", ["EquipmentSlotEarring1"]="Kolczyk I", ["EquipmentSlotEarring2"]="Kolczyk II", ["EquipmentSlotRing1"]="Pierścień I", ["EquipmentSlotRing2"]="Pierścień II", ["EquipmentSlotBelt"]="Pas",
                ["CharacterSearchKicker"]="SIEĆ POSTACI NEXFORGE", ["CharacterSearchTitle"]="Wyszukiwanie postaci", ["CharacterSearchSubtitle"]="Wyszukuj publiczne profile, gdy katalog online będzie dostępny.", ["CharacterSearchWatermark"]="Nazwa postaci…", ["Search"]="Szukaj", ["SearchNotAvailable"]="Wyszukiwanie postaci jest nadal tworzone. Zapytanie online nie jest jeszcze wykonywane.", ["LeaderboardsKicker"]="RANKINGI NEXFORGE", ["LeaderboardsTitle"]="Rankingi", ["LeaderboardsSubtitle"]="Potwierdzone rankingi walk pojawią się, gdy usługa będzie gotowa.", ["LeaderboardPlayer"]="GRACZ", ["LeaderboardBoss"]="BOSS", ["LeaderboardDps"]="DPS", ["LeaderboardServer"]="SERWER", ["LeaderboardsNotAvailable"]="Rankingi są nadal tworzone. Z tego ekranu nie są jeszcze publikowane wyniki.",
            },
        };

    internal static bool TryGet(string language, string key, out string? value)
    {
        if (Languages.TryGetValue(language, out IReadOnlyDictionary<string, string>? localized) &&
            localized.TryGetValue(key, out value))
            return true;

        return English.TryGetValue(key, out value);
    }
}
