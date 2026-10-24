using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AION_2_Companion.Core.Map.Pets;

namespace AION_2_Companion.Views.Overlays;

// Focusable search window. The game-map overlay itself remains click-through.
public partial class PetFarmSearchWindow : Window
{
    private IReadOnlyList<PetFarmCatalogEntry> _catalog;
    private PetFarmFilterSettings _settings;
    private PetCollectionSyncState _collectionStatus;
    private bool _loading;
    private PetFarmCatalogEntry? _highlighted;
    private readonly Dictionary<int, Bitmap> _icons = new();

    private ComboBox _category = null!, _map = null!;
    private ListBox _pets = null!;
    private TextBox _search = null!;
    private CheckBox _markers = null!, _missing = null!, _lv1 = null!, _lv2 = null!, _lv3 = null!;
    private TextBlock _details = null!, _status = null!, _statusCode = null!,
        _statusTitle = null!, _count = null!, _active = null!;
    private Ellipse _statusDot = null!;
    private Image _selectedIcon = null!;
    private Button _only = null!;

    public event Action<PetFarmFilterSettings>? FiltersChanged;
    public event Action<bool>? VisibilityChanged;

    public PetFarmSearchWindow()
        : this(Array.Empty<PetFarmCatalogEntry>(),
            PetCollectionSyncState.Unknown,
            new PetFarmFilterSettings(),
            markersVisible: true)
    {
    }

    public PetFarmSearchWindow(
        IReadOnlyList<PetFarmCatalogEntry> catalog,
        PetCollectionSyncState collectionStatus,
        PetFarmFilterSettings settings,
        bool markersVisible)
    {
        _catalog = catalog;
        _collectionStatus = collectionStatus;
        _settings = settings;
        AvaloniaXamlLoader.Load(this);

        _category = this.FindControl<ComboBox>("CategorySelect")!;
        _map = this.FindControl<ComboBox>("MapSelect")!;
        _pets = this.FindControl<ListBox>("PetItems")!;
        _search = this.FindControl<TextBox>("PetSearchText")!;
        _markers = this.FindControl<CheckBox>("ShowMarkers")!;
        _missing = this.FindControl<CheckBox>("ShowMissing")!;
        _lv1 = this.FindControl<CheckBox>("ShowLevel1")!;
        _lv2 = this.FindControl<CheckBox>("ShowLevel2")!;
        _lv3 = this.FindControl<CheckBox>("ShowLevel3")!;
        _details = this.FindControl<TextBlock>("SelectedPetDetails")!;
        _status = this.FindControl<TextBlock>("CollectionStatus")!;
        _statusCode = this.FindControl<TextBlock>("CollectionStatusCode")!;
        _statusTitle = this.FindControl<TextBlock>("CollectionStatusTitle")!;
        _statusDot = this.FindControl<Ellipse>("CollectionStatusDot")!;
        _count = this.FindControl<TextBlock>("SearchCount")!;
        _active = this.FindControl<TextBlock>("ActiveFilterText")!;
        _only = this.FindControl<Button>("OnlyThisPet")!;
        _selectedIcon = this.FindControl<Image>("SelectedPetIcon")!;

        _loading = true;
        _markers.IsChecked = markersVisible;
        _missing.IsChecked = settings.ShowMissing;
        _lv1.IsChecked = settings.ShowLevel1;
        _lv2.IsChecked = settings.ShowLevel2;
        _lv3.IsChecked = settings.ShowLevel3;
        ReloadMaps();
        ReloadCategories();
        RebuildList();
        UpdateStatus();
        _loading = false;

        _category.SelectionChanged += (_, _) => ApplyFilterControls();
        _map.SelectionChanged += (_, _) => ChangeMap();
        foreach (var checkbox in new[] { _missing, _lv1, _lv2, _lv3 })
            checkbox.IsCheckedChanged += (_, _) => ApplyFilterControls();
        _markers.IsCheckedChanged += (_, _) => ChangeVisibility();

        _search.TextChanged += (_, _) => RebuildList();
        _pets.SelectionChanged += (_, _) => Highlight();
        _only.Click += (_, _) => SelectOnlyThisPet();
        this.FindControl<Button>("ClearPetSelection")!.Click += (_, _) =>
        {
            _settings = _settings with { SpecificPetId = null };
            ApplyAndUpdate();
        };
        this.FindControl<Button>("CloseSearch")!.Click += (_, _) => Close();
        this.FindControl<Button>("ApplyAndClose")!.Click += (_, _) => Close();

        Opened += (_, _) => _search.Focus();
        Closed += (_, _) =>
        {
            foreach (Bitmap icon in _icons.Values)
                icon.Dispose();
            _icons.Clear();
        };
    }

    public void RefreshCatalog(IReadOnlyList<PetFarmCatalogEntry> catalog, PetCollectionSyncState collectionStatus)
    {
        _catalog = catalog;
        _collectionStatus = collectionStatus;
        int? selectedId = _highlighted?.PetId;
        _loading = true;
        ReloadMaps();
        ReloadCategories();
        RebuildList();
        if (selectedId is int id)
            _pets.SelectedItem = (_pets.ItemsSource as IEnumerable<PetSearchRow>)?.FirstOrDefault(p => p.Pet.PetId == id);
        _loading = false;
        UpdateStatus();
        Highlight(); // Refresh selected pet details after an incremental event.
    }

    private void ReloadCategories()
    {
        string[] cats = _catalog
            .Where(p => p.MapId.Equals(_settings.MapId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Category)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s).ToArray();
        var items = new List<string> { "Minden kategória" };
        items.AddRange(cats);
        _category.ItemsSource = items;
        _category.SelectedItem = _settings.Category is string wanted && items.Contains(wanted)
            ? wanted : items[0];
    }

    private void ReloadMaps()
    {
        MapChoice[] maps = _catalog
            .GroupBy(p => p.MapId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MapChoice(group.Key, group.First().MapName))
            .OrderBy(choice => PetFarmMapCatalog.Supported
                .Select((map, index) => (map.Id, index))
                .FirstOrDefault(item => item.Id.Equals(choice.Id, StringComparison.OrdinalIgnoreCase)).index)
            .ThenBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _map.ItemsSource = maps;
        MapChoice? selected = maps.FirstOrDefault(choice =>
            choice.Id.Equals(_settings.MapId, StringComparison.OrdinalIgnoreCase)) ?? maps.FirstOrDefault();
        _map.SelectedItem = selected;
        if (selected is not null && !selected.Id.Equals(_settings.MapId, StringComparison.OrdinalIgnoreCase))
            _settings = _settings with { MapId = selected.Id };
    }

    private void ChangeMap()
    {
        if (_loading || _map.SelectedItem is not MapChoice selected)
            return;

        _loading = true;
        _settings = _settings with
        {
            MapId = selected.Id,
            Category = null,
            SpecificPetId = null
        };
        _highlighted = null;
        _selectedIcon.Source = null;
        _details.Text = "Válassz egy petet a listából.";
        _only.IsEnabled = false;
        ReloadCategories();
        RebuildList();
        _loading = false;
        ApplyAndUpdate();
    }

    private void ApplyFilterControls()
    {
        if (_loading) return;
        string? category = _category.SelectedItem as string;
        _settings = _settings with
        {
            Category = category == "Minden kategória" ? null : category,
            ShowMissing = _missing.IsChecked == true,
            ShowLevel1 = _lv1.IsChecked == true,
            ShowLevel2 = _lv2.IsChecked == true,
            ShowLevel3 = _lv3.IsChecked == true
        };
        ApplyAndUpdate();
    }

    private void ApplyAndUpdate()
    {
        FiltersChanged?.Invoke(_settings);
        UpdateStatus();
    }

    private void ChangeVisibility()
    {
        if (!_loading)
            VisibilityChanged?.Invoke(_markers.IsChecked == true);
    }

    private void RebuildList()
    {
        if (_pets is null) return;
        string term = (_search.Text ?? "").Trim();
        var filtered = _catalog.Where(p =>
            p.MapId.Equals(_settings.MapId, StringComparison.OrdinalIgnoreCase) &&
            (term.Length == 0 ||
             p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
             p.PetId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PetSearchRow(p, _collectionStatus, GetPetIcon(p.PetId)))
            .ToArray();

        _pets.ItemsSource = filtered;
        _count.Text = $"Találatok: {filtered.Length} / {_catalog.Count} pet";
    }

    private void Highlight()
    {
        if (_loading) return;
        _highlighted = (_pets.SelectedItem as PetSearchRow)?.Pet;
        _only.IsEnabled = _highlighted is not null;
        _selectedIcon.Source = _highlighted is null
            ? null
            : GetPetIcon(_highlighted.PetId);
        _details.Text = _highlighted is PetFarmCatalogEntry pet
            ? $"{pet.Name}\n" +
              $"#{pet.PetId}  //  {pet.Category}\n" +
              $"Térkép: {pet.MapName}\n" +
              $"Tulajdon: {OwnershipText(pet, _collectionStatus)}\n" +
              $"Soul progress: {pet.SoulProgress}\n" +
              $"Ismert spawnpont: {pet.SpawnCount}"
            : "Válassz egy petet a listából.";
    }

    private void SelectOnlyThisPet()
    {
        if (_highlighted is null)
            return;

        _settings = _settings with
        {
            MapId = _highlighted.MapId,
            SpecificPetId = _highlighted.PetId
        };

        ApplyAndUpdate();

        Console.WriteLine(
            $"[PET FILTER] Selected={_highlighted.PetId} | " +
            $"Map={_highlighted.MapId}");
    }

    private void UpdateStatus()
    {
        if (_status is null) return;
        (_statusCode.Text, _statusTitle.Text, _status.Text) = _collectionStatus switch
        {
            PetCollectionSyncState.Ready =>
                ("READY", "Teljes lista",
                 "Megérkezett a 00 90; a Hiányzó / Lv. 1 / Lv. 2 / Lv. 3 szűrés teljesen használható."),
            PetCollectionSyncState.Partial =>
                ("PARTIAL", "Részleges adat",
                 "Megszereztél egy petet, és a 09 90-ből már tudjuk, hogy megvan, de a többiek továbbra is ismeretlenek."),
            _ =>
                ("UNKNOWN", "Teljes lista nélkül",
                 "A petek tulajdonosi állapota nem ismert. Az ismeretleneket ideiglenesen mutatjuk.")
        };
        string color = _collectionStatus switch
        {
            PetCollectionSyncState.Ready => "#58E0B2",
            PetCollectionSyncState.Partial => "#45DFFF",
            _ => "#FFB454"
        };
        _statusCode.Foreground = Brush.Parse(color);
        _statusDot.Fill = Brush.Parse(color);
        string activeMap = (_map.SelectedItem as MapChoice)?.Name ?? _settings.MapId;
        _active.Text = _settings.SpecificPetId is int id
            ? $"AKTÍV: {activeMap} • CSAK #{id}  •  Ez elsőbbséget élvez a kategória- és szintszűrőkkel szemben."
            : $"AKTÍV: {activeMap} • {(_settings.Category ?? "Összes kategória")} • " +
              $"Hiányzó={_settings.ShowMissing} | L1={_settings.ShowLevel1} | L2={_settings.ShowLevel2} | L3={_settings.ShowLevel3}";
    }

    private Bitmap? GetPetIcon(int petId)
    {
        if (_icons.TryGetValue(petId, out Bitmap? cached))
            return cached;

        string path = System.IO.Path.Combine(AppContext.BaseDirectory,
            "Data", "Assets", "Pets", $"{petId}.webp");
        if (!File.Exists(path))
            return null;

        try
        {
            using FileStream stream = File.OpenRead(path);
            var icon = Bitmap.DecodeToWidth(
                stream, 64, BitmapInterpolationMode.HighQuality);
            _icons[petId] = icon;
            return icon;
        }
        catch
        {
            return null;
        }
    }

    private static string OwnershipText(
        PetFarmCatalogEntry pet,
        PetCollectionSyncState collectionStatus) =>
        pet.Level is int level
            ? $"Lv. {level}"
            : collectionStatus == PetCollectionSyncState.Ready
                ? "Nincs meg"
                : "Ismeretlen";

    private sealed class PetSearchRow
    {
        public PetFarmCatalogEntry Pet { get; }
        public string Name => Pet.Name;
        public string Meta => $"#{Pet.PetId}  //  {Pet.Category}  //  {Pet.SpawnCount} spawn";
        public string Status { get; }
        public string StatusColor { get; }
        public Bitmap? Icon { get; }

        public PetSearchRow(
            PetFarmCatalogEntry pet,
            PetCollectionSyncState collectionStatus,
            Bitmap? icon)
        {
            Pet = pet;
            Icon = icon;
            Status = OwnershipText(pet, collectionStatus).ToUpperInvariant();
            StatusColor = pet.Level switch
            {
                3 => "#FFB454",
                1 or 2 => "#58E0B2",
                _ when collectionStatus == PetCollectionSyncState.Ready => "#FF8FA3",
                _ => "#8EA3B8"
            };
        }
    }

    private sealed record MapChoice(string Id, string Name)
    {
        public override string ToString() => Name.ToUpperInvariant();
    }

    private void HeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
