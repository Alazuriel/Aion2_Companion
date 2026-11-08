using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AION_2_Companion.Views.Overlays;

// Shared WYSIWYG visuals for LIVE and Studio DEMO. No access to capture or PhasewardEngine.
internal sealed record PhasewardHudBar(string Name, double Remaining, double Duration,
    Color Tint, bool Estimated = false);

internal static class PhasewardHudVisuals
{
    internal static int VisibleRows(double height, bool hasAlert, int maximum = 8)
    {
        // Never render more rows than can fit the user's actual on-screen HUD rectangle.
        double usable = Math.Max(0, height - 28 - (hasAlert ? 82 : 0));
        return Math.Clamp((int)(usable / 49), 1, maximum);
    }

    internal static Control Row(PhasewardHudBar bar, bool nearest)
    {
        bool urgent = bar.Remaining <= 5;
        Color accent = urgent ? Color.Parse("#FF5D72") : bar.Tint;
        var brush = new SolidColorBrush(accent);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = bar.Name, FontSize = nearest ? 14 : 12,
            FontWeight = nearest ? FontWeight.Bold : FontWeight.SemiBold,
            Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        });
        var time = new TextBlock
        {
            Text = Time(bar.Remaining), FontSize = nearest ? 16 : 13,
            FontWeight = FontWeight.Bold, Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(time, 1);
        header.Children.Add(time);

        var progress = new ProgressBar
        {
            Minimum = 0, Maximum = Math.Max(0.1, bar.Duration),
            Value = Math.Clamp(bar.Remaining, 0, Math.Max(0.1, bar.Duration)),
            Height = 4, Background = Brush.Parse("#45526878"), Foreground = brush,
            IsHitTestVisible = false
        };
        var body = new StackPanel { Spacing = 5, Children = { header, progress } };
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = brush, BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(9, 6),
            Background = Brush.Parse(urgent ? "#D2231320" : nearest ? "#CD13273A" : "#B30C1929"),
            Child = body, IsHitTestVisible = false
        };
    }

    internal static Control Alert(string title, string? detail, Color color)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 15, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(color), TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrWhiteSpace(detail))
            panel.Children.Add(new TextBlock
            {
                Text = detail.Length <= 100 ? detail : detail[..99] + "…",
                FontSize = 10, Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });
        return new Border
        {
            Child = panel, CornerRadius = new CornerRadius(6),
            Background = Brush.Parse("#CF251322"), Padding = new Thickness(6, 5),
            IsHitTestVisible = false
        };
    }

    internal static string Time(double seconds)
    {
        int total = Math.Max(0, (int)Math.Ceiling(seconds));
        return total >= 60 ? $"{total / 60}:{total % 60:00}" : $"{total}s";
    }
}
