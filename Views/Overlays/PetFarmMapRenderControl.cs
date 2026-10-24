using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AION_2_Companion.Core.Map.Pets;
using AION_2_Companion.Core.Map.V2;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace AION_2_Companion.Views.Overlays;

/// <summary>
/// One lightweight draw layer for THOUSANDS of spawn points.
/// No Border/Ellipse element is created per spawn; this control is click-through.
/// Its positions are generated from the EXISTING MapV2Transform.
/// </summary>
public sealed class PetFarmMapRenderControl : Control
{
    private PetFarmMapPoint[] _allPoints = Array.Empty<PetFarmMapPoint>();
    private PetFarmMapPoint[] _points = Array.Empty<PetFarmMapPoint>();
    private readonly Dictionary<int, Bitmap> _petIcons = new();
    private readonly HashSet<int> _missingPetIcons = new();
    private string? _selectedPetName;
    private string? _selectedCategory;
    private string _selectedMapId = PetFarmMapCatalog.VerteronId;
    private PetFarmFilterSettings _filterSettings = new();
    private readonly Dictionary<int, PetFarmOwnedState> _ownedPets = new();
    private PetCollectionSyncState _collectionStatus = PetCollectionSyncState.Unknown;

    public bool CollectionReady => _collectionStatus == PetCollectionSyncState.Ready;
    public PetCollectionSyncState CollectionStatus => _collectionStatus;
    public PetFarmFilterSettings CurrentFilters => _filterSettings;
    
    private string? _activeGameMapId;

    public void SetActiveGameMap(string? mapId)
    {
        string? normalized = NormalizeMapId(mapId);

        // Ne szűrjünk újra minden SIFT-frame után!
        if (string.Equals(
                _activeGameMapId,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            return;

        _activeGameMapId = normalized;
        RebuildFilter();
    }

    private static string? NormalizeMapId(string? mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
            return null;

        return mapId
            .Replace('\\', '/')
            .Trim('/')
            .Split('/')
            .Last();
    }
    

    // List of unique pets for the search window; source is loaded NFG data, read-only.
    public IReadOnlyList<PetFarmCatalogEntry> GetCatalog() => _allPoints
        .GroupBy(p => (p.MapId, p.PetId))
        .Select(g =>
        {
            PetFarmMapPoint first = g.First();
            _ownedPets.TryGetValue(g.Key.PetId, out var owned);
            return new PetFarmCatalogEntry(g.Key.PetId, first.PetName, first.Category,
                g.Count(), owned?.Level, owned?.SoulProgress ?? 0,
                first.MapId, first.MapName);
        })
        .OrderBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void SetCollection(
        IReadOnlyList<PetFarmOwnedState>? snapshot,
        PetCollectionSyncState? status = null)
    {
        _ownedPets.Clear();
        // Default preserves callers from the former full-snapshot-only API.
        _collectionStatus = status ?? (snapshot is null
            ? PetCollectionSyncState.Unknown : PetCollectionSyncState.Ready);
        if (snapshot is not null)
            foreach (var pet in snapshot)
                _ownedPets[pet.PetId] = pet;
        RebuildFilter();
    }

    public void SetFarmFilters(PetFarmFilterSettings state)
    {
        _filterSettings = state;
        _selectedCategory = NormalizeFilter(state.Category);
        _selectedMapId = state.MapId;
        _selectedPetName = null;
        RebuildFilter();
    }

    private void LoadPetIcons()
    {
        string folder = Path.Combine(
            AppContext.BaseDirectory,
            "Data", "Assets", "Pets");

        foreach (int petId in _allPoints
                     .Select(x => x.PetId)
                     .Distinct())
        {
            if (_petIcons.ContainsKey(petId) ||
                _missingPetIcons.Contains(petId))
                continue;

            string path = Path.Combine(
                folder, $"{petId}.webp");

            if (!File.Exists(path))
            {
                _missingPetIcons.Add(petId);
                continue;
            }

            try
            {
                _petIcons[petId] = new Bitmap(path);
            }
            catch (Exception ex)
            {
                _missingPetIcons.Add(petId);
                Console.WriteLine(
                    $"[PET ICON ERROR] {petId}: {ex.Message}");
            }
        }

        Console.WriteLine(
            $"[PET ICONS] Loaded: {_petIcons.Count}");
    }

    public string[] PetNames => _allPoints.Select(p => p.PetName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s).ToArray();

    public string[] Categories => _allPoints.Select(p => p.Category)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s).ToArray();
    private MapV2Transform? _transform;
    private double _desktopLeft, _desktopTop, _dpi = 1;

    private static readonly IBrush DotFill = new SolidColorBrush(Color.Parse("#EAA22CFF"));
    private static readonly IPen DotOutline = new Pen(Brushes.White, 0.8);

    // Shared styles; never allocate thousands of brushes per frame.
    private static readonly IBrush PetIconBackground =
        new SolidColorBrush(Color.Parse("#E80D1727"));
    private static readonly IPen PetIconBorder =
        new Pen(new SolidColorBrush(Color.Parse("#FF38C9FF")), 1.4);

    // Fine-tuned for the 1920x1078 map screenshot:
    // top ~105, left ~327, right ~25, bottom ~12 logical pixels.
    // Fractions keep the same usable area on other window sizes.
    private const double MapLeftFraction = 0.17;
    private const double MapTopFraction = 0.097;
    private const double MapRightFraction = 0.013;
    private const double MapBottomFraction = 0.011;

    private Rect? _viewportOverride;

    public void SetViewport(Rect? viewport)
    {
        _viewportOverride = viewport;
        InvalidateVisual();
    }

    public Rect PetRenderArea => GetPetRenderArea();

    private Rect GetPetRenderArea()
    {
        if (_viewportOverride is Rect custom)
            return custom;

        double w = Bounds.Width;
        double h = Bounds.Height;
        double left = w * MapLeftFraction;
        double top = h * MapTopFraction;
        double right = w * MapRightFraction;
        double bottom = h * MapBottomFraction;

        return new Rect(
            left,
            top,
            Math.Max(0, w - left - right),
            Math.Max(0, h - top - bottom));
    }

    public PetFarmMapRenderControl()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public void SetData(PetFarmMapPoint[] points)
    {
        _allPoints = points ?? Array.Empty<PetFarmMapPoint>();

        LoadPetIcons();
        RebuildFilter();
    }

    // null, "", or "All" means no restriction in that dimension.
    public void SetFilter(string? petName = null, string? category = null)
    {
        _selectedPetName = NormalizeFilter(petName);
        _selectedCategory = NormalizeFilter(category);
        _filterSettings = _filterSettings with {
            SpecificPetId = null, Category = _selectedCategory
        };
        RebuildFilter();
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "All", StringComparison.OrdinalIgnoreCase)
            ? null : value.Trim();

    
    private void RebuildFilter()
    {
        string? selectedMap = NormalizeMapId(_selectedMapId);

        // Csak akkor rajzolunk, ha a kiválasztott map
        // megegyezik a játékban ténylegesen felismert mappal.
        if (_activeGameMapId is null ||
            selectedMap is null ||
            !string.Equals(
                selectedMap,
                _activeGameMapId,
                StringComparison.OrdinalIgnoreCase))
        {
            _points = Array.Empty<PetFarmMapPoint>();
            InvalidateVisual();
            return;
        }

        _points = _allPoints.Where(p =>
        {
            // 1. A spawnnak a megfelelő maphoz kell tartoznia.
            if (!string.Equals(
                    NormalizeMapId(p.MapId),
                    selectedMap,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            // 2. Konkrét pet kiválasztása.
            // Ez felülírja a kategória- és szintszűrőket,
            // de SOHA nem írja felül a mapellenőrzést.
            if (_filterSettings.SpecificPetId is int specific)
                return p.PetId == specific;

            // 3. Petnév szűrése.
            if (_selectedPetName is not null &&
                !p.PetName.Equals(
                    _selectedPetName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            // 4. Kategória szűrése.
            if (_selectedCategory is not null &&
                !p.Category.Equals(
                    _selectedCategory,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            // 5. Még nincs meg a pet.
            if (!_ownedPets.TryGetValue(p.PetId, out var owned))
            {
                // Teljes lista esetén tényleg hiányzó.
                // Részleges vagy ismeretlen lista esetén
                // ideiglenesen megjelenítjük.
                return _collectionStatus == PetCollectionSyncState.Ready
                    ? _filterSettings.ShowMissing
                    : true;
            }

            // 6. Megszerzett pet szintje.
            return owned.Level switch
            {
                1 => _filterSettings.ShowLevel1,
                2 => _filterSettings.ShowLevel2,
                3 => _filterSettings.ShowLevel3,
                _ => false
            };
        }).ToArray();

        Console.WriteLine(
            $"[PET FARM FILTER] " +
            $"Status={_collectionStatus} | " +
            $"Selected={_filterSettings.SpecificPetId?.ToString() ?? "all"} | " +
            $"Map={selectedMap} | " +
            $"ActiveMap={_activeGameMapId} | " +
            $"Category={_selectedCategory ?? "all"} | " +
            $"Spawns={_points.Length}");

        InvalidateVisual();
    }


    public void SetFrame(MapV2Transform? transform, double desktopLeft, double desktopTop, double dpi)
    {
        _transform = transform;
        _desktopLeft = desktopLeft;
        _desktopTop = desktopTop;
        _dpi = Math.Max(0.1, dpi);
        InvalidateVisual();
    }

    
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_transform is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        Rect viewport = GetPetRenderArea();

        // Critical: an icon near the edge is also clipped, not merely its center.
        using (context.PushClip(viewport))
        {
            foreach (PetFarmMapPoint marker in _points)
            {
                var (absoluteX, absoluteY) =
                    _transform.Project(marker.ImageX, marker.ImageY);

                double x = (absoluteX - _desktopLeft) / _dpi;
                double y = (absoluteY - _desktopTop) / _dpi;
                var position = new Point(x, y);

                if (!viewport.Contains(position))
                    continue;

                if (_petIcons.TryGetValue(marker.PetId, out var icon))
                {
                    const double imageSize = 30;
                    const double frameSize = 36;

                    var frameRect = new Rect(
                        x - frameSize / 2, y - frameSize / 2,
                        frameSize, frameSize);

                    context.DrawRectangle(
                        PetIconBackground, PetIconBorder, frameRect, 7, 7);

                    var imageRect = new Rect(
                        x - imageSize / 2, y - imageSize / 2,
                        imageSize, imageSize);

                    context.DrawImage(icon, imageRect);
                }
                else
                {
                    context.DrawEllipse(DotFill, DotOutline, position, 4.5, 4.5);
                }
            }
        }
    }


    public bool TryPick(int physicalX, int physicalY, out PetFarmMapPoint? picked)
    {
        picked = null;
        if (_transform is null) return false;
        double mx = (physicalX - _desktopLeft) / _dpi;
        double my = (physicalY - _desktopTop) / _dpi;
        Rect viewport = GetPetRenderArea();

        if (!viewport.Contains(new Point(mx, my)))
            return false;
        double bestDistSquared = 20 * 20; // logical pixels, 36x36 frame
        foreach (PetFarmMapPoint marker in _points)
        {
            var (px, py) = _transform.Project(marker.ImageX, marker.ImageY);
            double markerX = (px - _desktopLeft) / _dpi;
            double markerY = (py - _desktopTop) / _dpi;
            if (!viewport.Contains(new Point(markerX, markerY)))
                continue;

            double dx = markerX - mx;
            double dy = markerY - my;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared < bestDistSquared)
            {
                bestDistSquared = distanceSquared;
                picked = marker;
            }
        }
        return picked is not null;
    }

    public bool TryGetLogicalPosition(PetFarmMapPoint p, out Point logical)
    {
        logical = default;
        if (_transform is null) return false;
        var (px, py) = _transform.Project(p.ImageX, p.ImageY);
        double x = (px - _desktopLeft) / _dpi;
        double y = (py - _desktopTop) / _dpi;
        if (!GetPetRenderArea().Contains(new Point(x, y))) return false;
        logical = new Point(x, y);
        return true;
    }
}
