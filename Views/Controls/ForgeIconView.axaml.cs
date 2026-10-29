using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AION_2_Companion.Core.Assets;

namespace AION_2_Companion.Views.Controls;

public partial class ForgeIconView : UserControl
{
    public static readonly StyledProperty<string?> IconSourceProperty =
        AvaloniaProperty.Register<ForgeIconView, string?>(nameof(IconSource));

    public static readonly StyledProperty<string?> TooltipTextProperty =
        AvaloniaProperty.Register<ForgeIconView, string?>(nameof(TooltipText));

    public static readonly StyledProperty<string> AccentColorProperty =
        AvaloniaProperty.Register<ForgeIconView, string>(nameof(AccentColor), "#3B5874");

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<ForgeIconView, bool>(nameof(IsSelected));

    private CancellationTokenSource? _loadCancellation;

    public string? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public string? TooltipText
    {
        get => GetValue(TooltipTextProperty);
        set => SetValue(TooltipTextProperty, value);
    }

    public string AccentColor
    {
        get => GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    static ForgeIconView()
    {
        IconSourceProperty.Changed.AddClassHandler<ForgeIconView>((view, _) => view.ReloadIcon());
        TooltipTextProperty.Changed.AddClassHandler<ForgeIconView>((view, _) => view.UpdateTooltip());
        AccentColorProperty.Changed.AddClassHandler<ForgeIconView>((view, _) => view.UpdateFrame());
        IsSelectedProperty.Changed.AddClassHandler<ForgeIconView>((view, _) => view.UpdateFrame());
    }

    public ForgeIconView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => CancelLoad();
        UpdateFrame();
        UpdateTooltip();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void ReloadIcon()
    {
        CancelLoad();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken token = _loadCancellation.Token;

        try
        {
            Bitmap? bitmap = await CompanionImageService.LoadAsync(IconSource, 160, token);
            if (token.IsCancellationRequested)
                return;

            Image image = this.FindControl<Image>("IconImage")!;
            image.Source = bitmap;
            image.IsVisible = bitmap is not null;
            this.FindControl<Grid>("FallbackVisual")!.IsVisible = bitmap is null;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateFrame()
    {
        if (this.FindControl<Border>("Frame") is not Border frame)
            return;

        try
        {
            frame.BorderBrush = Brush.Parse(AccentColor);
        }
        catch (FormatException)
        {
            frame.BorderBrush = Brush.Parse("#3B5874");
        }

        this.FindControl<Border>("SelectionGlow")!.IsVisible = IsSelected;
    }

    private void UpdateTooltip() => ToolTip.SetTip(this, TooltipText);

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }
}
