using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Map.Pets;

namespace AION_2_Companion.Views.Overlays;

public partial class MapOverlayWindow
{
    public event Action? PetSearchRequested;

    public void ShowPetMenu()
    {
        var panel = this.FindControl<Border>("PetMenuPanel");

        if (panel is null)
            return;

        UpdatePetMenuCollectionStatus();
        UpdatePetMenuToggleVisual();
        if (panel.Opacity >= 0.5)
            return;
        panel.Opacity = 0;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible)
                return;

            // Magasság igazítása a képernyőhöz
            double availableHeight =
                MarkerCanvas.Bounds.Height - 205;

            panel.Height = Math.Clamp(
                availableHeight, 430, 640);

            // Ez indítja az AXAML fade animációt
            panel.Opacity = 1;

        }, DispatcherPriority.Background);
    }

    // The window remains click-through; the global mouse hook forwards clicks.
    public bool HandlePetMenuClick(int physicalX, int physicalY)
    {
        if (!IsVisible)
            return false;

        var panel = this.FindControl<Border>("PetMenuPanel");
        if (panel is null || !panel.IsVisible || panel.Opacity < 0.5)
            return false;

        double dpi = Math.Max(RenderScaling, 0.1);
        var mouse = new Point(
            (physicalX - Position.X) / dpi,
            (physicalY - Position.Y) / dpi);

        if (MenuControlContains("PetVisibilityToggle", mouse))
        {
            SetPetFarmVisibility(!PetFarmEnabled);
            return true;
        }

        if (MenuControlContains("PetSearchOpenToggle", mouse))
        {
            PetSearchRequested?.Invoke();
            return true;
        }

        return false;
    }

    private bool MenuControlContains(string name, Point mouse)
    {
        var control = this.FindControl<Border>(name);
        if (control is null || !control.IsVisible)
            return false;

        Point? topLeft = control.TranslatePoint(new Point(0, 0), this);
        return topLeft is Point p &&
            new Rect(p, control.Bounds.Size).Contains(mouse);
    }

    private void UpdatePetMenuToggleVisual()
    {
        var toggle = this.FindControl<Border>("PetVisibilityToggle");
        var state = this.FindControl<TextBlock>("PetVisibilityState");
        if (toggle is null || state is null)
            return;

        state.Text = PetFarmEnabled
            ? LocalizationService.Get("On")
            : LocalizationService.Get("Off");
        state.Foreground = PetFarmEnabled
            ? new SolidColorBrush(Color.Parse("#FF43C9FF"))
            : new SolidColorBrush(Color.Parse("#FFFF8FA3"));
        toggle.Background = PetFarmEnabled
            ? new SolidColorBrush(Color.Parse("#FF19364B"))
            : new SolidColorBrush(Color.Parse("#FF38202D"));
    }

    private void UpdatePetMenuCollectionStatus()
    {
        var code = this.FindControl<TextBlock>("PetSyncCode");
        var title = this.FindControl<TextBlock>("PetSyncTitle");
        var description = this.FindControl<TextBlock>("PetSyncDescription");
        var dot = this.FindControl<Ellipse>("PetSyncDot");
        if (code is null || title is null || description is null || dot is null)
            return;

        string color;
        switch (PetFarmCollectionStatus)
        {
            case PetCollectionSyncState.Ready:
                code.Text = "READY";
                title.Text = LocalizationService.Get("PetReadyTitle");
                description.Text = LocalizationService.Get("PetReadyText");
                color = "#58E0B2";
                break;

            case PetCollectionSyncState.Partial:
                code.Text = "PARTIAL";
                title.Text = LocalizationService.Get("PetPartialTitle");
                description.Text = LocalizationService.Get("PetPartialText");
                color = "#45DFFF";
                break;

            default:
                code.Text = "UNKNOWN";
                title.Text = LocalizationService.Get("PetUnknownTitle");
                description.Text = LocalizationService.Get("PetUnknownText");
                color = "#FFB454";
                break;
        }

        IBrush brush = new SolidColorBrush(Color.Parse(color));
        code.Foreground = brush;
        dot.Fill = brush;
    }

    public void HidePetMenu()
    {
        var panel = this.FindControl<Border>("PetMenuPanel");

        if (panel is not null)
            panel.Opacity = 0;
    }
}
