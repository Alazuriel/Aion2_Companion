using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

/// <summary>Separate, position-persistent boss health / estimated kill overlay.</summary>
public partial class BossStatusOverlayWindow : Window
{
    private MeterSnapshot _snapshot;

    public BossStatusOverlayWindow() : this(new MeterSnapshot())
    {
    }

    public BossStatusOverlayWindow(MeterSnapshot snapshot)
    {
        _snapshot = snapshot;
        AvaloniaXamlLoader.Load(this);
        OverlayPlacementTracker.Attach(this, "boss-status-detached-v1");
        Opened += (_, _) => WindowClickThrough.Apply(this, enabled: false);
        LocalizationService.LanguageChanged += LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LanguageChanged;
        Refresh();
    }

    public void UpdateSnapshot(MeterSnapshot snapshot)
    {
        _snapshot = snapshot;
        Refresh();
    }

    private void LanguageChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        MeterSnapshot s = _snapshot;
        this.FindControl<TextBlock>("BossNameText")!.Text =
            string.IsNullOrWhiteSpace(s.MobTemplateName) ? "—" : s.MobTemplateName;
        double percent = Math.Clamp(s.BossHpPercent ?? 0, 0, 100);
        ProgressBar hpBar = this.FindControl<ProgressBar>("BossHpBar")!;
        hpBar.Value = percent;
        // Unknown HP percentage must not be rendered as a definite zero.
        hpBar.IsVisible = s.BossHpPercent.HasValue;
        this.FindControl<TextBlock>("BossHpText")!.Text = s.BossCurrentHp.HasValue
            ? s.BossHpPercent.HasValue && s.MobMaxHp > 0
                ? $"{Meter.Num(s.BossCurrentHp.Value)} / {Meter.Num(s.MobMaxHp)} · {percent:F1}%"
                : Meter.Num(s.BossCurrentHp.Value)
            : s.MobMaxHp > 0
                ? $"— / {Meter.Num(s.MobMaxHp)}"
                : LocalizationService.Get("BossHpWaiting");

        TextBlock enrage = this.FindControl<TextBlock>("EnrageText")!;
        if (s.EnrageRemainingSeconds is double remaining && double.IsFinite(remaining))
        {
            enrage.IsVisible = true;
            enrage.Text = remaining <= 0
                ? LocalizationService.Get("Enraged")
                : LocalizationService.Format("EnrageIn", FormatTime(remaining));
        }
        else
        {
            enrage.IsVisible = false;
            enrage.Text = string.Empty;
        }

        bool enabled = CompanionSettings.ShowBossDeathPrediction;
        this.FindControl<Button>("DeathPredictionToggle")!.Classes.Set("primary", enabled);
        this.FindControl<TextBlock>("DeathPredictionText")!.Text = !enabled
            ? LocalizationService.Get("Off")
            : s.EstimatedDeathSeconds is double estimate && double.IsFinite(estimate)
                ? FormatPredictionTime(estimate)
                : s.BossCurrentHp.HasValue
                    ? LocalizationService.Get("DeathPredictionCalculating")
                    : LocalizationService.Get("DeathPredictionUnavailable");
    }

    private static string FormatTime(double seconds)
    {
        int total = Math.Max(0, (int)seconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    private static string FormatPredictionTime(double seconds)
    {
        int total = Math.Max(0, (int)Math.Ceiling(seconds));
        if (total >= 3600)
            return $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}";
        return total >= 60 ? $"{total / 60}:{total % 60:00}" : $"{total}s";
    }

    private void DeathPredictionToggleClick(object? sender, RoutedEventArgs e)
    {
        CompanionSettings.ShowBossDeathPrediction = !CompanionSettings.ShowBossDeathPrediction;
        Refresh();
    }

    private void CloseClick(object? sender, RoutedEventArgs e)
    {
        // Closing the independent window disables auto-open until re-enabled from DPS HP.
        CompanionSettings.ShowBossStatusOverlay = false;
        Close();
    }

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            !OverlayStudioStore.IsLocked(OverlayStudioStore.BossStatus))
            BeginMoveDrag(e);
    }
}
