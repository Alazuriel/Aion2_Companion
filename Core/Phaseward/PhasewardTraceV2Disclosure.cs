using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AION_2_Companion.Core;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// A single, player-facing, versioned consent step for detailed boss-research uploads.
/// It does not alter upload mechanics or consent enforcement in PhasewardTraceV2Sync.
/// </summary>
public static class PhasewardTraceV2Disclosure
{
    // New version intentionally displays the redesigned notice once after this update.
    public const string NoticeVersion = "phaseward-diagnostic-v2-raw-2";

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static string Translate(string en, string hu, string de, string pl)
    {
        string language = CompanionSettings.LanguageCode ?? string.Empty;
        if (language.StartsWith("hu", StringComparison.OrdinalIgnoreCase)) return hu;
        if (language.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return de;
        if (language.StartsWith("pl", StringComparison.OrdinalIgnoreCase)) return pl;
        return en;
    }

    public static Task<bool> ShowAsync(Window owner)
    {
        string title = Translate("Make Phaseward better together", "Tegyünk együtt egy jobb Phasewardért!",
            "Gemeinsam Phaseward verbessern", "Ulepszajmy Phaseward razem");
        string description = Translate(
            "Help us discover boss abilities! With your permission, Companion automatically sends detailed data from your future completed boss fights to Nexforge's servers.",
            "Segíts felfedezni a bossok képességeit! Ha engedélyezed, a Companion a következő lezárt bossfightjaid részletes adatait automatikusan elküldi a Nexforge szerverére.",
            "Hilf uns, Boss-Fähigkeiten zu entdecken! Mit deiner Zustimmung sendet Companion detaillierte Daten künftiger abgeschlossener Bosskämpfe automatisch an die Nexforge-Server.",
            "Pomóż nam odkrywać umiejętności bossów! Za Twoją zgodą Companion automatycznie przesyła szczegółowe dane przyszłych zakończonych walk na serwery Nexforge.");
        string privacy = Translate(
            "The upload includes raw game network data, which may contain character IDs, location data or other personal information. You can stop sharing any time in Settings.",
            "A feltöltés nyers játékhálózati adatokat is tartalmaz, amelyekben karakterazonosító, helyadat vagy más személyes információ is lehet. A megosztást a Beállításokban bármikor kikapcsolhatod.",
            "Der Upload enthält auch rohe Spiel-Netzwerkdaten, die Charakterkennungen, Standortdaten oder andere personenbezogene Informationen enthalten können. Du kannst die Freigabe jederzeit in den Einstellungen beenden.",
            "Przesyłane są również surowe dane sieciowe gry, które mogą zawierać identyfikatory postaci, dane lokalizacji lub inne dane osobowe. Możesz wyłączyć udostępnianie w Ustawieniach.");
        string checkText = Translate(
            "I agree to automatically share detailed boss-fight data with Nexforge.",
            "Hozzájárulok a részletes bossfight-adatok automatikus megosztásához a Nexforge-dzsal.",
            "Ich stimme der automatischen Übermittlung detaillierter Bosskampf-Daten an Nexforge zu.",
            "Zgadzam się na automatyczne udostępnianie Nexforge szczegółowych danych walk z bossami.");

        var window = new Window
        {
            Title = "Nexforge Phaseward",
            Width = 630,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#091321")
        };

        var body = new StackPanel { Spacing = 16 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var logo = new Border
        {
            Width = 51, Height = 51,
            CornerRadius = new CornerRadius(13),
            Background = Brush("#15364D"),
            BorderBrush = Brush("#317A91"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "N", FontSize = 26, FontWeight = FontWeight.Bold,
                Foreground = Brush("#5BE1EE"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(logo);

        var heading = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "NEXFORGE  /  PHASEWARD",
            FontSize = 10, FontWeight = FontWeight.Bold,
            Foreground = Brush("#58C9E1")
        });
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0F7FF")
        });
        header.Children.Add(heading);
        body.Children.Add(header);

        body.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#C7D9E7")
        });

        // Required disclosure, kept readable without showing packet protocol jargon.
        var notice = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Brush("#132638"),
            BorderBrush = Brush("#28455A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13, 11),
            Child = new TextBlock
            {
                Text = privacy, FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#A9C3D5")
            }
        };
        body.Children.Add(notice);

        var consent = new CheckBox
        {
            IsChecked = false,
            Content = new TextBlock
            {
                Text = checkText,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#EAF6FF"),
                MaxWidth = 500
            },
            Padding = new Thickness(5, 7)
        };
        body.Children.Add(consent);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var later = new Button
        {
            Content = Translate("Not now", "Most nem", "Jetzt nicht", "Nie teraz"),
            MinWidth = 112, Height = 42,
            Padding = new Thickness(16, 8),
            Background = Brush("#25374A"),
            Foreground = Brush("#E2EDF6")
        };
        var enable = new Button
        {
            Content = Translate("Enable sharing", "Megosztás engedélyezése", "Freigabe aktivieren", "Włącz udostępnianie"),
            MinWidth = 205, Height = 42,
            Padding = new Thickness(16, 8),
            FontWeight = FontWeight.SemiBold,
            Background = Brush("#1687B9"),
            Foreground = Brush("#FFFFFF"),
            IsEnabled = false
        };
        consent.IsCheckedChanged += (_, _) => enable.IsEnabled = consent.IsChecked == true;
        later.Click += (_, _) => window.Close(false);
        enable.Click += (_, _) => window.Close(consent.IsChecked == true);
        footer.Children.Add(later);
        footer.Children.Add(enable);
        body.Children.Add(footer);

        window.Content = new Border
        {
            Padding = new Thickness(27, 25),
            Background = Brush("#0C1C2E"),
            BorderBrush = Brush("#2C4960"),
            BorderThickness = new Thickness(1),
            Child = body
        };
        return window.ShowDialog<bool>(owner);
    }
}
