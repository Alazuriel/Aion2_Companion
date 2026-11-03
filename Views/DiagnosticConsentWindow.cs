// LEGACY #21 / #24 - Nexforge diagnostics center. Local-first, per-report consent.
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AION_2_Companion.Core.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AION_2_Companion.Views;

public sealed class DiagnosticConsentWindow : Window
{
    private readonly DiagnosticReport _report;
    private readonly Window _owner;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _uploadState = new() { FontSize = 11, FontWeight = FontWeight.SemiBold };
    private readonly Button _send = new() { MinWidth = 142 };
    private readonly Button _cancelRetry = new() { MinWidth = 145 };
    private readonly DispatcherTimer _rateLimitRefresh = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _sending;
    private bool _sent;

    private static readonly IBrush Canvas = Ink("#080F1C");
    private static readonly IBrush Panel = Ink("#101E30");
    private static readonly IBrush PanelDark = Ink("#0B1625");
    private static readonly IBrush Line = Ink("#294056");
    private static readonly IBrush Primary = Ink("#F2F7FF");
    private static readonly IBrush Muted = Ink("#9BB0C5");
    private static readonly IBrush Cyan = Ink("#58D9F4");
    private static readonly IBrush Purple = Ink("#9B8CFF");
    private static readonly IBrush Danger = Ink("#F27F91");

    private static IBrush Ink(string color) => new SolidColorBrush(Color.Parse(color));

    public static string Translate(string en, string hu, string de, string pl)
    {
        var code = AION_2_Companion.Core.CompanionSettings.LanguageCode;
        return code switch { "hu" => hu, "de" => de, "pl" => pl, _ => en };
    }
    private static string L(string en, string hu, string de, string pl) => Translate(en, hu, de, pl);

    public static void ShowPending(Window owner)
    {
        var pending = DiagnosticStore.Pending();
        if (pending.Count > 0) new DiagnosticConsentWindow(owner, pending[0], pending.Count).Show(owner);
    }

    public static void CreateManual(Window owner)
    {
        try
        {
            var report = DiagnosticRuntime.MakeManualReport();
            DiagnosticStore.Save(report);
            new DiagnosticConsentWindow(owner, report, DiagnosticStore.Pending().Count).Show(owner);
        }
        catch { /* Never fall back to plaintext if DPAPI or validation is unavailable. */ }
    }

    private DiagnosticConsentWindow(Window owner, DiagnosticReport report, int pendingCount)
    {
        _owner = owner;
        _report = report;
        Title = "Nexforge Studios · Diagnostics Center";
        Width = 990;
        Height = 675;
        MinWidth = 740;
        MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Canvas;

        var outer = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new Border
        {
            Background = PanelDark, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(27, 19)
        };
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var monogram = new Border { Width = 50, Height = 50, Background = Ink("#273151"),
            BorderBrush = Purple, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15) };
        monogram.Child = new TextBlock { Text = "NX", Foreground = Primary, FontSize = 20,
            FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(monogram);
        var hTexts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0) };
        hTexts.Children.Add(Label("NEXFORGE  /  AION 2 COMPANION", 10, Cyan, FontWeight.Bold));
        hTexts.Children.Add(Label(L("Diagnostics Center", "Diagnosztikai központ", "Diagnosezentrum", "Centrum diagnostyki"), 25, Primary, FontWeight.Bold));
        Grid.SetColumn(hTexts, 1);
        headerRow.Children.Add(hTexts);
        var headerBadge = Badge("ISSUE #21", Purple);
        headerBadge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(headerBadge, 2);
        headerRow.Children.Add(headerBadge);
        header.Child = headerRow;
        outer.Children.Add(header);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("292,*"), ColumnSpacing = 18,
            Margin = new Thickness(24, 22, 24, 16) };
        var aside = new StackPanel { Spacing = 13 };
        aside.Children.Add(IncidentCard(report, pendingCount));
        aside.Children.Add(LocalSafetyCard());
        var nav = MakeButton(L("Next saved report", "Következő jelentés", "Nächster Bericht", "Następny raport"), false);
        nav.IsVisible = pendingCount > 1;
        nav.HorizontalAlignment = HorizontalAlignment.Stretch;
        nav.Click += (_, _) =>
        {
            if (_sending) return;
            var list = DiagnosticStore.Pending();
            if (list.Count <= 1) return;
            int idx = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].ReportId == _report.ReportId) { idx = i; break; }
            var next = list[(idx + 1) % list.Count];
            new DiagnosticConsentWindow(_owner, next, list.Count).Show(_owner);
            Close();
        };
        aside.Children.Add(nav);
        var asideScroll = new ScrollViewer { Content = aside,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        body.Children.Add(asideScroll);

        var main = new StackPanel { Spacing = 14 };
        var intro = new StackPanel { Spacing = 6, Margin = new Thickness(2, 2, 0, 5) };
        intro.Children.Add(Label(L("YOUR REPORT. YOUR DECISION.", "A JELENTÉSED. A DÖNTÉSED.",
            "DEIN BERICHT. DEINE ENTSCHEIDUNG.", "TWÓJ RAPORT. TWOJA DECYZJA."), 11, Cyan, FontWeight.Bold));
        intro.Children.Add(Label(L("Every crash leaves a clue.", "Minden hibának marad nyoma.",
            "Jeder Absturz hinterlässt Spuren.", "Każdy błąd zostawia ślad."), 21, Primary, FontWeight.SemiBold));
        intro.Children.Add(Description(L(
            "A protected local report is ready. Review the exact server payload before deciding whether to share it.",
            "Egy titkosított helyi jelentés készült. A beküldés előtt megnézheted a szerverre kerülő pontos adatokat.",
            "Ein verschlüsselter lokaler Bericht ist bereit. Prüfe die genauen Daten vor dem Senden.",
            "Zaszyfrowany raport lokalny jest gotowy. Sprawdź dane przed wysłaniem.")));
        main.Children.Add(intro);
        main.Children.Add(TimelineCard(report));
        main.Children.Add(StatusCard());
        var mainScroll = new ScrollViewer { Content = main,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(mainScroll, 1);
        body.Children.Add(mainScroll);
        Grid.SetRow(body, 1);
        outer.Children.Add(body);

        var footer = new Border { Background = PanelDark,
            BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 16) };
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        var review = MakeButton(L("Preview JSON", "JSON megtekintése", "JSON anzeigen", "Podgląd JSON"), false);
        review.Click += (_, _) => Review();
        controls.Children.Add(review);
        _send.Click += async (_, _) => await SendAsync();
        FormatButton(_send, true);
        controls.Children.Add(_send);
        _cancelRetry.Click += (_, _) =>
        {
            try
            {
                DiagnosticStore.CancelAuthorizedPending(_report.ReportId);
                RefreshButtons();
                SetStatus(L("Permission to retry was cancelled. Report remains local.",
                    "Az újraküldési engedély visszavonva. A jelentés helyben maradt.",
                    "Wiederholungsfreigabe widerrufen. Der Bericht bleibt lokal.",
                    "Zgoda na ponowienie została cofnięta. Raport pozostaje lokalnie."));
            }
            catch { SetStatus(L("Local status could not be changed.", "Nem sikerült megváltoztatni az állapotot.",
                "Status konnte nicht geändert werden.", "Nie można zmienić statusu.")); }
        };
        FormatButton(_cancelRetry, false);
        controls.Children.Add(_cancelRetry);
        var later = MakeButton(L("Not now", "Most nem", "Jetzt nicht", "Nie teraz"), false);
        later.Click += (_, _) => Close();
        controls.Children.Add(later);
        var delete = MakeButton(L("Delete", "Törlés", "Löschen", "Usuń"), false, true);
        delete.Click += (_, _) =>
        {
            if (_sending) return;
            try { DiagnosticStore.Delete(_report.ReportId); Close(); }
            catch { SetStatus(L("Could not delete local report.", "A jelentés törlése sikertelen.",
                "Bericht konnte nicht gelöscht werden.", "Nie można usunąć raportu.")); }
        };
        controls.Children.Add(delete);
        footer.Child = controls;
        Grid.SetRow(footer, 2);
        outer.Children.Add(footer);
        Content = outer;
        _rateLimitRefresh.Tick += (_, _) => RefreshButtons(); // Never uploads on timers.
        Closed += (_, _) => _rateLimitRefresh.Stop();
        _rateLimitRefresh.Start();
        RefreshButtons();
        if (DiagnosticStore.HasAuthorizedPending(_report.ReportId))
            SetStatus(L("Previous send was not confirmed. Retry requires another click.",
                "A korábbi küldés visszaigazolása hiányzik. Újraküldés csak újabb kattintással történik.",
                "Der letzte Versand wurde nicht bestätigt. Erneut senden nur per Klick.",
                "Poprzednia próba nie została potwierdzona. Ponowienie wymaga kliknięcia."));
    }

    private static TextBlock Label(string text, double size, IBrush color, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap
    };
    private static TextBlock Description(string text) => Label(text, 13, Muted);
    private static Border Card(StackPanel content) => new()
    {
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(17), Padding = new Thickness(18), Child = content
    };
    private static Border Badge(string label, IBrush color) => new()
    {
        Background = Ink("#1B2B42"), BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 6),
        Child = Label(label, 11, color, FontWeight.SemiBold)
    };
    private static string PrettyKind(string kind) => kind switch
    {
        "managed_crash" => L("Managed crash", "Alkalmazáshiba", "Anwendungsabsturz", "Awaria aplikacji"),
        "managed_error" => L("Managed exception", "Kezelt .NET hiba", ".NET-Fehler", "Błąd .NET"),
        "suspected_ui_hang" => L("UI unresponsive", "UI-nemválaszolás", "UI reagiert nicht", "UI nie odpowiada"),
        "unexpected_termination" => L("Unexpected exit", "Váratlan leállás", "Unerwartetes Ende", "Nieoczekiwane zamknięcie"),
        "manual_report" => L("Manual report", "Kézi jelentés", "Manueller Bericht", "Raport ręczny"),
        _ => L("Unknown incident", "Ismeretlen esemény", "Unbekanntes Ereignis", "Nieznane zdarzenie")
    };
    private static string PrettyConfidence(string confidence) => confidence switch
    {
        "confirmed" => L("Confirmed", "Megerősített", "Bestätigt", "Potwierdzony"),
        "suspected" => L("Suspected · not proven", "Gyanús · nem bizonyított", "Vermutet", "Podejrzewany"),
        "uncertain" => L("Cause unknown", "Ismeretlen ok", "Ursache unbekannt", "Przyczyna nieznana"),
        _ => L("Local / manual", "Helyi / kézi", "Lokal / manuell", "Lokalny / ręczny")
    };

    private static Border IncidentCard(DiagnosticReport report, int pendingCount)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(Label("INCIDENT / " + report.ReportId[^8..].ToUpperInvariant(), 11, Cyan, FontWeight.Bold));
        content.Children.Add(Label(PrettyKind(report.IncidentKind), 22, Primary, FontWeight.SemiBold));
        content.Children.Add(Badge(PrettyConfidence(report.Confidence), Purple));
        content.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 2), Height = 1 });
        content.Children.Add(Label(L("RECORDED", "RÖGZÍTVE", "ERFASST", "ZAPISANO"), 10, Muted, FontWeight.Bold));
        content.Children.Add(Label(report.CreatedUtc.ToLocalTime().ToString("yyyy.MM.dd. HH:mm", CultureInfo.InvariantCulture), 13, Primary));
        content.Children.Add(Label(L("SOURCE", "FORRÁS", "QUELLE", "ŹRÓDŁO"), 10, Muted, FontWeight.Bold));
        content.Children.Add(Label(report.Subsystem == "phaseward" ? "PHASEWARD / INTEGRATION" :
            report.Subsystem.ToUpperInvariant(), 13, Primary, FontWeight.SemiBold));
        content.Children.Add(Label(L($"Saved reports: {pendingCount}", $"Mentett jelentések: {pendingCount}",
            $"Gespeicherte Berichte: {pendingCount}", $"Zapisane raporty: {pendingCount}"), 12, Muted));
        return Card(content);
    }
    private static Border LocalSafetyCard()
    {
        var content = new StackPanel { Spacing = 9 };
        content.Children.Add(Label("PRIVACY // LOCAL FIRST", 11, Cyan, FontWeight.Bold));
        content.Children.Add(Description(L("Encrypted on this PC. Sending requires your click. No account needed.",
            "Ezen a gépen titkosítva. Küldés csak kattintásra. Fiók nem szükséges.",
            "Lokal verschlüsselt. Versand nur per Klick. Kein Konto erforderlich.",
            "Zaszyfrowany lokalnie. Wysyłka tylko po kliknięciu. Konto nie jest wymagane.")));
        content.Children.Add(Label(L("Never collected:", "Nem gyűjtjük:", "Nie erfasst:", "Nie gromadzimy:"), 11, Primary, FontWeight.Bold));
        content.Children.Add(Description(L("Packets, chat text, names, passwords, tokens, private paths and memory dumps.",
            "Csomagok, chat, nevek, jelszavak, tokenek, privát útvonalak és memóriadumpok.",
            "Pakete, Chats, Namen, Passwörter, Tokens, Pfade und Speicherabbilder.",
            "Pakiety, czaty, nazwy, hasła, tokeny, ścieżki i zrzuty pamięci.")));
        return Card(content);
    }
    private static Border TimelineCard(DiagnosticReport report)
    {
        var stack = new StackPanel { Spacing = 11 };
        stack.Children.Add(Label(L("LOCAL EVENT TIMELINE", "HELYI ESEMÉNY-IDŐVONAL", "LOKALE EREIGNISSE", "LOKALNA OŚ ZDARZEŃ"),
            11, Cyan, FontWeight.Bold));
        var events = report.Breadcrumbs.Where(x => DiagnosticSchema.AllowedEvents.Contains(x.Code))
            .OrderByDescending(x => x.Utc).Take(6).ToArray();
        if (events.Length == 0)
            stack.Children.Add(Description(L("No event details were retained. This can happen after a hard exit.",
                "Nem maradt eseményrészlet. Hirtelen leállásnál ez előfordulhat.",
                "Keine Ereignisse erfasst.", "Brak zapisanych zdarzeń.")));
        foreach (var entry in events)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            row.Children.Add(new Border { Width = 7, Height = 7, Background = entry.Code.StartsWith("phaseward_", StringComparison.Ordinal) ? Purple : Cyan,
                CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center });
            string name = entry.Code.StartsWith("phaseward_", StringComparison.Ordinal)
                ? "PHASEWARD  /  " + entry.Code["phaseward_".Length..].Replace('_', ' ').ToUpperInvariant()
                : entry.Code.Replace('_', ' ').ToUpperInvariant();
            var label = Label(name + "   ·   " + entry.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), 12, Primary);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            stack.Children.Add(row);
        }
        stack.Children.Add(Label(L("Local context is not necessarily part of the V42 upload. Preview JSON for exact outgoing data.",
            "A helyi idővonal nem feltétlenül része a V42 beküldésnek. A pontos adatokat a JSON-nézet mutatja.",
            "Lokale Ereignisse sind nicht zwingend Teil des Uploads. JSON-Vorschau zeigt die genauen Daten.",
            "Lokalne zdarzenia nie muszą być wysłane. Dokładne dane w podglądzie JSON."), 11, Muted));
        return Card(stack);
    }
    private Border StatusCard()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(Label(L("TRANSMISSION", "BEKÜLDÉSI ÁLLAPOT", "ÜBERTRAGUNG", "PRZESYŁANIE"), 11, Cyan, FontWeight.Bold));
        _uploadState.Foreground = Purple;
        stack.Children.Add(_uploadState);
        _status.Foreground = Muted;
        stack.Children.Add(_status);
        return Card(stack);
    }
    private static Button MakeButton(string text, bool prominent, bool danger = false)
    {
        var btn = new Button { Content = text, MinWidth = 105 };
        FormatButton(btn, prominent, danger);
        return btn;
    }
    private static void FormatButton(Button button, bool prominent, bool danger = false)
    {
        button.Background = prominent ? Ink("#654CE2") : Panel;
        button.Foreground = danger ? Danger : Primary;
        button.BorderBrush = prominent ? Purple : Line;
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(10);
        button.Padding = new Thickness(15, 11);
        button.Margin = new Thickness(0, 0, 9, 7);
        button.FontWeight = FontWeight.SemiBold;
        button.FontSize = 12;
    }
    private void SetStatus(string text) => _status.Text = text;
    private void RefreshButtons()
    {
        int backoff = DiagnosticStore.GetRetryAfterSeconds(_report.ReportId);
        bool retry = DiagnosticStore.HasAuthorizedPending(_report.ReportId);
        _send.IsEnabled = !_sending && !_sent && backoff == 0;
        _send.Content = backoff > 0
            ? L("Retry later", "Újrapróbálás később", "Später erneut", "Spróbuj później")
            : retry ? L("Retry send", "Újraküldés", "Erneut senden", "Wyślij ponownie")
            : L("Send report", "Jelentés küldése", "Bericht senden", "Wyślij raport");
        _cancelRetry.Content = L("Cancel retry", "Újraküldés visszavonása", "Wiederholung widerrufen", "Anuluj ponawianie");
        _cancelRetry.IsVisible = !_sent && retry;
        _cancelRetry.IsEnabled = !_sending;
        _uploadState.Text = _sent ? L("RECEIVED BY NEXFORGE", "A NEXFORGE ÁTVETTE", "EMPFANGEN", "ODEBRANO")
            : _sending ? L("SENDING…", "KÜLDÉS…", "SENDEN…", "WYSYŁANIE…")
            : backoff > 0 ? L($"RATE LIMIT · {backoff}s", $"KÜLDÉSI KORLÁT · {backoff} mp",
                $"SENDESPERRE · {backoff}s", $"LIMIT · {backoff}s")
            : retry ? L("AUTHORIZED · WAITING FOR RETRY", "ENGEDÉLYEZETT · ÚJRAKÜLDÉSRE VÁR",
                "FREIGEGEBEN · WARTET", "ZATWIERDZONY · OCZEKUJE")
            : L("LOCAL ONLY · NOT SENT", "CSAK HELYBEN · NINCS ELKÜLDVE",
                "NUR LOKAL · NICHT GESENDET", "TYLKO LOKALNIE · NIE WYSŁANO");
    }

    private void Review()
    {
        try
        {
            // Exact byte-identical V42 HTTP JSON, including the pinned payload on retry.
            string payload = DiagnosticStore.PreviewUpload(_report.ReportId);
            var view = new Window { Title = "Nexforge Diagnostics · V42 JSON", Width = 770, Height = 630,
                MinWidth = 540, MinHeight = 420, Background = Canvas,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22) };
            var title = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 14) };
            title.Children.Add(Label("API V42 / EXACT PAYLOAD", 11, Cyan, FontWeight.Bold));
            title.Children.Add(Label(L("These are the exact bytes we would send.", "Pontosan ezeket az adatokat küldenénk el.",
                "Dies sind die zu sendenden Daten.", "To dokładne dane do wysłania."), 14, Primary));
            panel.Children.Add(title);
            var viewer = new TextBox { Text = payload, IsReadOnly = true, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"),
                FontSize = 12, Foreground = Ink("#D3E8FC"), Background = PanelDark,
                BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(15) };
            Grid.SetRow(viewer, 1);
            panel.Children.Add(viewer);
            var close = MakeButton(L("Close preview", "Előnézet bezárása", "Vorschau schließen", "Zamknij podgląd"), false);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += (_, _) => view.Close();
            Grid.SetRow(close, 2);
            panel.Children.Add(close);
            view.Content = panel;
            view.Show(this);
        }
        catch
        {
            SetStatus(L("Unable to safely preview the exact report payload.",
                "Nem sikerült biztonságosan megjeleníteni a beküldendő adatokat.",
                "Vorschau nicht verfügbar.", "Podgląd jest niedostępny."));
        }
    }

    private async Task SendAsync()
    {
        // Only this button handler can send HTTP. No background or startup upload.
        if (_sending || _sent) return;
        int backoff = DiagnosticStore.GetRetryAfterSeconds(_report.ReportId);
        if (backoff > 0)
        {
            SetStatus(L($"Please wait another {backoff} seconds.", $"Még {backoff} másodpercet várni kell.",
                $"Noch {backoff} Sekunden warten.", $"Poczekaj {backoff} sekund."));
            return;
        }
        _sending = true;
        RefreshButtons();
        try
        {
            string body = DiagnosticStore.PinUpload(_report.ReportId);
            DiagnosticStore.MarkAuthorizedPending(_report.ReportId);
            var result = await DiagnosticApiClient.SendWithConsentAsync(body);
            if (result.Status == DiagnosticUploadStatus.Received && result.ReportRef != null)
            {
                DiagnosticStore.CompleteSent(_report.ReportId, result.ReportRef);
                _sent = true;
                SetStatus(L("The server acknowledged the report. Thank you!", "A szerver visszaigazolta a jelentést. Köszönjük!",
                    "Der Bericht wurde bestätigt. Danke!", "Serwer potwierdził raport. Dziękujemy!"));
                return;
            }
            if (result.Status == DiagnosticUploadStatus.RateLimited)
                DiagnosticStore.SetRetryAfter(_report.ReportId, result.RetryAfterSeconds);
            SetStatus(result.Status switch
            {
                DiagnosticUploadStatus.Disabled => L("Server intake is currently disabled. The local report is safe.",
                    "A szerver jelenleg nem fogad jelentéseket. A helyi másolat megmaradt.",
                    "Serverempfang deaktiviert. Lokal gespeichert.", "Odbiór wyłączony. Zapisano lokalnie."),
                DiagnosticUploadStatus.RateLimited => L("Server limit reached. Retry manually later.",
                    "Szerveroldali limit. Később kézzel próbálhatod újra.", "Sendelimit erreicht. Später manuell wiederholen.",
                    "Limit osiągnięty. Spróbuj później."),
                DiagnosticUploadStatus.ValidationRejected => L("Server rejected the format. Report retained locally.",
                    "A szerver elutasította a formátumot. A jelentés megmaradt.", "Format abgelehnt. Lokal behalten.",
                    "Odrzucono format. Raport pozostaje lokalnie."),
                DiagnosticUploadStatus.Conflict => L("Duplicate ID conflict. No local data was deleted.",
                    "Jelentésazonosító-ütközés. Nem töröltünk helyi adatot.", "ID-Konflikt. Lokal behalten.",
                    "Konflikt ID. Dane pozostały lokalnie."),
                DiagnosticUploadStatus.Unauthorized => L("Authorization refused. No anonymous fallback attempted.",
                    "Jogosultság elutasítva. Nem volt automatikus anonim újrapróbálás.",
                    "Autorisierung abgelehnt. Kein anonymer Versuch.", "Brak autoryzacji. Bez anonimowej próby."),
                DiagnosticUploadStatus.NetworkFailure => L("Connection failed. Local copy preserved; no auto-retry.",
                    "Hálózati hiba. Helyi másolat megmaradt; nincs automatikus újraküldés.",
                    "Verbindungsfehler. Lokal gespeichert.", "Błąd połączenia. Lokalnie zapisano."),
                _ => L("No confirmed server receipt. Report remains local.",
                    "Nincs szervernyugta. A jelentés helyben maradt.", "Kein bestätigter Empfang. Lokal behalten.",
                    "Brak potwierdzenia. Raport lokalny.")
            });
        }
        catch
        {
            SetStatus(L("Safe upload preparation or sending failed. Local copy preserved.",
                "A biztonságos előkészítés vagy küldés sikertelen. Helyben megmaradt.",
                "Übertragung fehlgeschlagen. Lokal behalten.", "Wysyłanie nieudane. Lokalnie zapisano."));
        }
        finally { _sending = false; RefreshButtons(); }
    }
}
