using System;
using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Map.Pets;
using System.Collections.Generic;
using AION_2_Companion.Core.Map.V2;
using AION_2_Companion.ViewModels;

namespace AION_2_Companion.Views.Overlays;

/// <summary>Additional Pet Farm layer; the original V2 overlay stays unchanged.</summary>
public partial class MapOverlayWindow
{
    private PetFarmMapRenderControl? _petFarmLayer;
    private PetFarmMapPoint? _petFarmSelected;
    private Border? _petFarmTooltip;
    private Border? _petFarmTooltipOwnershipBadge;
    private TextBlock? _petFarmTooltipName;
    private TextBlock? _petFarmTooltipOwnership;
    private TextBlock? _petFarmTooltipInfo;
    private Rect _petFarmCloseHitbox; // default = empty hitbox
    private Rect _petFarmTooltipHitbox;
    private volatile TooltipPhysicalHitRegion? _petFarmTooltipPhysicalHitRegion;
    private Rect? _petFarmViewportOverride;
    private bool _petFarmEnabled = true;
    public bool PetFarmEnabled => _petFarmEnabled;
    public string[] PetFarmNames => _petFarmLayer?.PetNames ?? Array.Empty<string>();
    public string[] PetFarmCategories => _petFarmLayer?.Categories ?? Array.Empty<string>();
    public bool PetFarmCollectionReady => _petFarmLayer?.CollectionReady ?? false;
    public PetCollectionSyncState PetFarmCollectionStatus =>
        _petFarmLayer?.CollectionStatus ?? PetCollectionSyncState.Unknown;
    public PetFarmFilterSettings PetFarmFilters => _petFarmLayer?.CurrentFilters ?? new();
    public IReadOnlyList<PetFarmCatalogEntry> PetFarmCatalog =>
        _petFarmLayer?.GetCatalog() ?? Array.Empty<PetFarmCatalogEntry>();

    public void SetPetCollection(
        IReadOnlyList<PetFarmOwnedState>? snapshot,
        PetCollectionSyncState? status = null)
    {
        _petFarmLayer?.SetCollection(snapshot, status);
        _petFarmSelected = null;
        UpdatePetMenuCollectionStatus();
        UpdatePetFarmTooltip();
    }

    public void SetPetFarmFilters(PetFarmFilterSettings settings)
    {
        _petFarmLayer?.SetFarmFilters(settings);
        _petFarmSelected = null;
        UpdatePetFarmTooltip();
    }

    // Optional manual override. By default, use the responsive render area.
    public void SetPetFarmViewport(Rect rect)
    {
        _petFarmViewportOverride = rect;
        _petFarmLayer?.SetViewport(rect);
        UpdatePetFarmTooltip();
    }

    public void SetPetFarmVisibility(bool visible)
    {
        _petFarmEnabled = visible;
        if (_petFarmLayer is not null)
            _petFarmLayer.IsVisible = visible;

        if (!visible)
        {
            _petFarmSelected = null;
            _petFarmCloseHitbox = default;
            _petFarmTooltipHitbox = default;
            _petFarmTooltipPhysicalHitRegion = null;
        }
        UpdatePetFarmTooltip();
        UpdatePetMenuToggleVisual();
        Console.WriteLine($"[PET FARM] Markers {(visible ? "ON" : "OFF")}");
    }
    public void SetPetFarmFilter(string? petName = null, string? category = null)
    {
        _petFarmSelected = null;
        _petFarmLayer?.SetFilter(petName, category);
        UpdatePetFarmTooltip();
    }

    public void ConfigurePetFarm(IEnumerable records, string mapsDirectory)
    {
        // Executed once AFTER ForgeDataLoader has populated PetsDatabase.Pets.
        PetFarmImportResult imported = PetFarmMapImporter.FromRecords(records, mapsDirectory);
        if (_petFarmLayer is null)
        {
            _petFarmLayer = new PetFarmMapRenderControl();
            _petFarmLayer.ZIndex = 25;
            MarkerCanvas.Children.Add(_petFarmLayer);
            MarkerCanvas.SizeChanged += (_, args) =>
            {
                if (_petFarmLayer is not null)
                {
                    _petFarmLayer.Width = args.NewSize.Width;
                    _petFarmLayer.Height = args.NewSize.Height;
                }
            };
            _petFarmLayer.Width = MarkerCanvas.Bounds.Width;
            _petFarmLayer.Height = MarkerCanvas.Bounds.Height;
            CreatePetFarmTooltip();
        }
        _petFarmLayer.SetData(imported.Points);
        _petFarmLayer.SetCollection(
            PetCollectionBridge.Snapshot, PetCollectionBridge.Status);
        UpdatePetMenuCollectionStatus();
        if (_petFarmViewportOverride is Rect manualViewport)
            _petFarmLayer.SetViewport(manualViewport);
        _petFarmLayer.IsVisible = _petFarmEnabled;
        UpdatePetMenuToggleVisual();
    }

    /// <summary>Call from SetTrackedMap every time the existing matcher returns a frame.</summary>
    
    public void SetPetFarmFrame(MapV2Result? result)
    {
        if (_petFarmLayer is null)
            return;

        if (result is null)
            HidePetMenu();
        else
            ShowPetMenu();

        // Jelenleg a SIFT csak a Verteron referenciat ismeri.
        _petFarmLayer.SetActiveGameMap(
            result is null
                ? null
                : PetFarmMapCatalog.VerteronId);

        _petFarmLayer.SetFrame(
            result?.Transform,
            Position.X,
            Position.Y,
            RenderScaling);

        UpdatePetFarmTooltip();
    }


    public void HidePetFarm()
    {
        HidePetMenu();
        _petFarmLayer?.SetActiveGameMap(null);
        _petFarmLayer?.SetFrame(null, Position.X, Position.Y, RenderScaling);
        _petFarmSelected = null;
        if (_petFarmTooltip is not null) _petFarmTooltip.IsVisible = false;
        _petFarmCloseHitbox = default;
        _petFarmTooltipHitbox = default;
        _petFarmTooltipPhysicalHitRegion = null;
    }

    /// <summary>
    /// Call this from the EXISTING low-level click event (UI thread).
    /// The game receives the click too, as in the current test mob.
    /// </summary>
    public bool HandlePetFarmClick(int physicalX, int physicalY)
    {
        if (!_petFarmEnabled || _petFarmLayer is null || !IsVisible) return false;
        var pointer = new Point((physicalX - Position.X) / Math.Max(RenderScaling, 0.1),
                                (physicalY - Position.Y) / Math.Max(RenderScaling, 0.1));

        if (_petFarmSelected is not null && _petFarmCloseHitbox.Contains(pointer))
        {
            _petFarmSelected = null;
            UpdatePetFarmTooltip();
            return true;
        }

        // The low-level hook also swallows this click so it cannot activate
        // something underneath the tooltip in the game.
        if (_petFarmSelected is not null && _petFarmTooltipHitbox.Contains(pointer))
            return true;

        if (!_petFarmLayer.TryPick(physicalX, physicalY, out var hit) || hit is null)
            return false;

        _petFarmSelected = hit;
        PetFarmCatalogEntry? catalog = _petFarmLayer.GetCatalog()
            .FirstOrDefault(p => p.PetId == hit.PetId &&
                                 p.MapId.Equals(hit.MapId, StringComparison.OrdinalIgnoreCase));
        string mobName = hit.MonsterId is >= int.MinValue and <= int.MaxValue
            ? MainWindowViewModel._mobsDatabaseStatic?.GetById((int)hit.MonsterId)?.Name ?? LocalizationService.Get("UnknownMob")
            : LocalizationService.Get("UnknownMob");

        if (_petFarmTooltipName is not null)
            _petFarmTooltipName.Text = hit.PetName;

        if (_petFarmTooltipInfo is not null)
        {
            _petFarmTooltipInfo.Text =
                $"{hit.MapName.ToUpperInvariant()}  //  {hit.Category.ToUpperInvariant()}  //  PET #{hit.PetId}\n" +
                LocalizationService.Format("KnownSpawnCount", mobName, catalog?.SpawnCount ?? 0) + "\n" +
                $"World  {hit.WorldX:F0}  /  {hit.WorldY:F0}  /  {hit.WorldZ:F0}";
        }

        UpdatePetFarmTooltipOwnership(catalog);
        UpdatePetFarmTooltip();
        return true;
    }

    private void CreatePetFarmTooltip()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _petFarmTooltipName = new TextBlock
        {
            Text = "Pet", FontWeight = FontWeight.SemiBold,
            FontSize = 17, Foreground = new SolidColorBrush(Color.Parse("#F4F7FF"))
        };
        header.Children.Add(_petFarmTooltipName);
        var close = new TextBlock
        {
            Text = "×", Foreground = new SolidColorBrush(Color.Parse("#8EA3B8")),
            FontSize = 18, Margin = new Thickness(9, 0, 2, 0)
        };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        _petFarmTooltipInfo = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#9EB2C5")),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 10.5,
            LineHeight = 17
        };

        _petFarmTooltipOwnership = new TextBlock
        {
            Text = LocalizationService.Get("UnknownPetBadge"),
            Foreground = new SolidColorBrush(Color.Parse("#FFB454")),
            FontSize = 10,
            FontWeight = FontWeight.Bold
        };
        _petFarmTooltipOwnershipBadge = new Border
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(9, 4),
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Color.Parse("#402C2418")),
            BorderBrush = new SolidColorBrush(Color.Parse("#99FFB454")),
            BorderThickness = new Thickness(1),
            Child = _petFarmTooltipOwnership
        };

        var contents = new StackPanel { Spacing = 8 };
        contents.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("PetLocation"),
            Foreground = new SolidColorBrush(Color.Parse("#45DFFF")),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1.2
        });
        contents.Children.Add(header);
        contents.Children.Add(_petFarmTooltipOwnershipBadge);
        contents.Children.Add(_petFarmTooltipInfo);
        contents.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#30475E"))
        });
        contents.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("PossibleSpawnWarning"),
            Foreground = new SolidColorBrush(Color.Parse("#607A91")),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 8.5
        });
        _petFarmTooltip = new Border
        {
            Width = 350,
            Padding = new Thickness(16, 14),
            Background = new SolidColorBrush(Color.Parse("#F20A1422")),
            BorderBrush = new SolidColorBrush(Color.Parse("#6645DFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            BoxShadow = BoxShadows.Parse("0 16 42 0 #85000000"),
            IsHitTestVisible = false,
            IsVisible = false,
            Child = contents
        };
        _petFarmTooltip.ZIndex = 1200;
        MarkerCanvas.Children.Add(_petFarmTooltip);
    }

    private void UpdatePetFarmTooltipOwnership(PetFarmCatalogEntry? pet)
    {
        if (_petFarmTooltipOwnership is null ||
            _petFarmTooltipOwnershipBadge is null)
        {
            return;
        }

        string text;
        string foreground;
        string background;
        if (pet?.Level is int level)
        {
            text = LocalizationService.Format("OwnedLevelBadge", level) +
                   (pet.SoulProgress > 0 ? $"  ·  SOUL {pet.SoulProgress}" : string.Empty);
            foreground = level == 3 ? "#FFB454" : "#58E0B2";
            background = level == 3 ? "#403A2817" : "#40213A34";
        }
        else if (PetFarmCollectionStatus == PetCollectionSyncState.Ready)
        {
            text = LocalizationService.Get("MissingPetBadge");
            foreground = "#FF8FA3";
            background = "#4038202D";
        }
        else
        {
            text = LocalizationService.Get("UnknownPetBadge");
            foreground = "#FFB454";
            background = "#402C2418";
        }

        var brush = new SolidColorBrush(Color.Parse(foreground));
        _petFarmTooltipOwnership.Text = text;
        _petFarmTooltipOwnership.Foreground = brush;
        _petFarmTooltipOwnershipBadge.BorderBrush = brush;
        _petFarmTooltipOwnershipBadge.Background =
            new SolidColorBrush(Color.Parse(background));
    }

    private void UpdatePetFarmTooltip()
    {
        if (!_petFarmEnabled || _petFarmTooltip is null || _petFarmLayer is null || _petFarmSelected is null ||
            !_petFarmLayer.TryGetLogicalPosition(_petFarmSelected, out Point at))
        {
            if (_petFarmTooltip is not null) _petFarmTooltip.IsVisible = false;
            _petFarmCloseHitbox = default;
            _petFarmTooltipHitbox = default;
            _petFarmTooltipPhysicalHitRegion = null;
            return;
        }

        Rect area = _petFarmLayer.PetRenderArea;
        double x = Math.Clamp(at.X + 17, area.Left, Math.Max(area.Left, area.Right - 355));
        double y = Math.Clamp(at.Y - 22, area.Top, Math.Max(area.Top, area.Bottom - 190));
        Canvas.SetLeft(_petFarmTooltip, x);
        Canvas.SetTop(_petFarmTooltip, y);
        _petFarmCloseHitbox = new Rect(x + 312, y + 7, 33, 30);
        const double tooltipWidth = 350;
        double tooltipHeight = Math.Max(190, _petFarmTooltip.Bounds.Height);
        _petFarmTooltipHitbox = new Rect(x, y, tooltipWidth, tooltipHeight);
        double dpi = Math.Max(RenderScaling, 0.1);
        int physicalLeft = Position.X + (int)Math.Floor(x * dpi);
        int physicalTop = Position.Y + (int)Math.Floor(y * dpi);
        _petFarmTooltipPhysicalHitRegion = new TooltipPhysicalHitRegion(
            physicalLeft,
            physicalTop,
            physicalLeft + (int)Math.Ceiling(tooltipWidth * dpi),
            physicalTop + (int)Math.Ceiling(tooltipHeight * dpi));
        _petFarmTooltip.IsVisible = true;
    }

    public bool ShouldSuppressPetFarmClick(int physicalX, int physicalY)
    {
        TooltipPhysicalHitRegion? region = _petFarmTooltipPhysicalHitRegion;
        return region is not null && region.Contains(physicalX, physicalY);
    }

    private sealed record TooltipPhysicalHitRegion(int Left, int Top, int Right, int Bottom)
    {
        public bool Contains(int x, int y) =>
            x >= Left && x <= Right && y >= Top && y <= Bottom;
    }
}
