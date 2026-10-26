using System;
using System.Collections.Generic;
using Avalonia.Threading;
using AION_2_Companion.Views.Overlays;
using System.IO;
using AION_2_Companion.Core.Map.V2;
using AION_2_Companion.Core.Map.Pets;
using System.Collections;

namespace AION_2_Companion.Core.Map;

public sealed class MapOverlayManager : IDisposable
{
    // Automatikus térképfelismerés. Kikapcsolva, amíg a kézi toggle stabil.
    // Kalibrációkor a timer akkor is fut, ha ez false.
    private const bool AutoDetectEnabled = true;

    private readonly MapOverlayWindow _overlay = new();
    private readonly GameWindowCaptureService _gameCapture = new();
    private readonly MapVisibilityDetector _detector;
    private readonly GameMapInputService _mouse;
    private DateTime _lastGoodV2Frame = DateTime.MinValue;
    private PetFarmSearchWindow? _petSearch;
    private bool SearchVisible => _petSearch?.IsVisible == true;
    private MapV2Tracker? _v2Tracker;
    private DateTime _lastMapInputUtc = DateTime.MinValue;
    private DateTime _nextV2RequestUtc = DateTime.MinValue;
    private readonly DispatcherTimer _v2Timer;
    
    private readonly DispatcherTimer _visionTimer;
    private readonly DispatcherTimer _renderTimer;

    private MapReference? _pendingCalibration;
    private DateTime? _gameFocusedSince;

    private volatile bool _mapVisible;
    private bool _everShown;
    private bool _disposed;

    private double _pendingDx;
    private double _pendingDy;

    private readonly Queue<(int X, int Y, int Delta)>
        _pendingWheels = new();
    
    
    public void LoadPetFarmSpawns(IEnumerable records)
    {
        if (_disposed || records is null)
            return;

        string mapsDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Data", "Assets", "Maps");

        string referenceJpg = Path.Combine(
            mapsDirectory,
            "World_L_A.jpg");

        Console.WriteLine(
            $"[PET FARM] Maps directory: {mapsDirectory}");

        if (!Directory.Exists(mapsDirectory))
        {
            Console.WriteLine(
                $"[PET FARM ERROR] Maps directory missing: {mapsDirectory}");
            return;
        }

        if (!File.Exists(referenceJpg))
        {
            Console.WriteLine(
                $"[PET FARM ERROR] Reference JPG missing: {referenceJpg}");
            return;
        }

        try
        {
            // FONTOS: Az importer MAPPA útvonalat vár!
            _overlay.ConfigurePetFarm(records, mapsDirectory);

            Console.WriteLine(
                "[PET FARM] Map data loaded successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PET FARM ERROR] {ex}");
        }
    }



    public void FilterPetFarm(string? petName = null, string? category = null)
    {
        if (_disposed) return;
        _overlay.SetPetFarmFilter(petName, category);
    }
    public MapOverlayManager(string gameProcess = "AION2")
    {
        _detector = new MapVisibilityDetector(_gameCapture);
        _overlay.PetSearchRequested += OpenPetSearch;
        PetCollectionBridge.Changed += OnPetCollectionChanged;
        _overlay.SetPetCollection(
            PetCollectionBridge.Snapshot, PetCollectionBridge.Status);

        _mouse = new GameMapInputService(
            gameProcess,
            () => _mapVisible,
            _overlay.ShouldSuppressPetFarmClick);
        
        _mouse.FocusChanged += OnGameFocusChanged;
        _mouse.MouseDragged += (_, _) =>
        {
            _lastMapInputUtc = DateTime.UtcNow;
        };

        _mouse.MouseWheel += (_, _, _) =>
        {
            _lastMapInputUtc = DateTime.UtcNow;
        };
        _mouse.MouseClicked += (x, y) =>
        {
            if (_disposed || !_mapVisible || !_mouse.IsGameForeground())
                return;

            if (_overlay.HandlePetMenuClick(x, y))
                return;

            if (_overlay.HandlePetFarmClick(x, y))
                return;

        };
        _visionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _visionTimer.Tick += (_, _) =>
        {
            // Auto-detektálás vagy folyamatban lévő kalibráció esetén fut
            if (AutoDetectEnabled || _pendingCalibration is not null)
                UpdateMapVisibility();
        };

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (_, _) => UpdateMapTransform();

        _mouse.Start();
        _visionTimer.Start();
        _renderTimer.Start();
        
        try
        {
            
            string baseDir = AppContext.BaseDirectory;

            string jpg = Path.Combine(
                baseDir,
                "Data", "Assets", "Maps", "World_L_A.jpg");

            _v2Tracker = new MapV2Tracker(jpg, _gameCapture);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MAP V2 INIT] {ex.Message}");
        }

        _v2Timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _v2Timer.Tick += (_, _) => UpdateV2Tracking();
        _v2Timer.Start();
        
        Console.WriteLine("[MAP] Manager started (manual toggle).");
    }
    private void UpdateV2Tracking()
    {
        if (_disposed || _v2Tracker is null || SearchVisible) return;
        
        DateTime now = DateTime.UtcNow;

        bool interacting =
            now - _lastMapInputUtc <
            TimeSpan.FromMilliseconds(1200);

        TimeSpan requestInterval = interacting
            ? TimeSpan.FromMilliseconds(120)
            : TimeSpan.FromMilliseconds(350);

        if (now < _nextV2RequestUtc)
            return;

        _nextV2RequestUtc = now + requestInterval;


        // A MAI, már működő mapnyitás logikát használjuk.
        if (!_mapVisible || !_mouse.IsGameForeground())
        {
            _overlay.HideTrackedMap();
            return;
        }

        if (!_detector.TryGetGameBounds(out var bounds))
        {
            _overlay.HideTrackedMap();
            return;
        }

        _v2Tracker.RequestFrame(bounds, result =>
        {
            // Az eredmény elkészülése közben akár be is záródhatott a map!
            if (_disposed || !_mapVisible || !_mouse.IsGameForeground())
            {
                _overlay.HideTrackedMap();
                return;
            }

            if (result is not null)
            {
                _lastGoodV2Frame = DateTime.UtcNow;
                _overlay.SetTrackedMap(result);
            }
            else if (DateTime.UtcNow - _lastGoodV2Frame >
                     TimeSpan.FromSeconds(2))
            {
                _overlay.HideTrackedMap();
            }
        });
    }
    private void OnPetCollectionChanged(IReadOnlyList<PetFarmOwnedState>? snapshot)
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            // Read the newest snapshot and status together on the UI dispatcher.
            _overlay.SetPetCollection(
                PetCollectionBridge.Snapshot, PetCollectionBridge.Status);
            _petSearch?.RefreshCatalog(
                _overlay.PetFarmCatalog, _overlay.PetFarmCollectionStatus);
        });
    }

    private void OpenPetSearch()
    {
        if (_disposed) return;
        if (SearchVisible)
        {
            _petSearch!.Activate();
            return;
        }

        var dialog = new PetFarmSearchWindow(
            _overlay.PetFarmCatalog,
            _overlay.PetFarmCollectionStatus,
            _overlay.PetFarmFilters,
            _overlay.PetFarmEnabled);

        _petSearch = dialog;
        dialog.FiltersChanged += settings => _overlay.SetPetFarmFilters(settings);
        dialog.VisibilityChanged += enabled => _overlay.SetPetFarmVisibility(enabled);
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_petSearch, dialog)) _petSearch = null;
        };

        // A regular, focusable window is required for TextBox keyboard input.
        // The map window itself remains focusless and click-through.
        dialog.Show(_overlay);
        dialog.Activate();
        Console.WriteLine("[PET SEARCH] Opened.");
    }

    private void ToggleOverlay()
    {
        if (_disposed)
            return;

        if (_mapVisible)
        {
            HideOverlay();
            return;
        }

        ShowOverlay();
    }

    private void ShowOverlay()
    {
        if (_disposed || (_mapVisible && _overlay.IsVisible))
            return;
        if (!_overlay.IsVisible)
        {
            _overlay.Show();

            _overlay.DisableCaptureExclusion();

            _everShown = true;
        }

        _mapVisible = true;
        Console.WriteLine("[MAP] Overlay visible.");
    }

    private void OnGameFocusChanged(bool focused)
    {
        // Ha kilépsz a játékból, az overlay ne maradjon az asztalon
        if (!focused && !SearchVisible)
            HideOverlay();
    }

    private void UpdateMapVisibility()
    {
        if (_disposed || SearchVisible)
            return;

        var detector = _detector;

        if (!detector.TryGetGameBounds(out var bounds))
        {
            _gameFocusedSince = null;
            HideOverlay();
            return;
        }

        if (_pendingCalibration is { } reference)
        {
            _gameFocusedSince ??= DateTime.UtcNow;

            if ((DateTime.UtcNow -
                 _gameFocusedSince.Value).TotalSeconds < 3)
                return;

            try
            {
                detector.CaptureReference(reference, bounds);
                Console.WriteLine(
                    $"[MAP VISION] Reference saved: {reference}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[MAP VISION ERROR] {ex.Message}");
            }

            _pendingCalibration = null;
            _gameFocusedSince = null;
            return;
        }

        if (!AutoDetectEnabled)
            return;

        bool detected;

        try
        {
            detected = detector.IsMapOpen(
                bounds,
                out double openScore,
                out double closedScore);

            if (detected != _mapVisible)
            {
                Console.WriteLine(
                    $"[MAP VISION] Open={detected} | " +
                    $"OpenScore={openScore:F1} | " +
                    $"ClosedScore={closedScore:F1}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MAP VISION ERROR] {ex.Message}");

            HideOverlay();
            return;
        }

        if (detected)
            ShowOverlay();
        else
            HideOverlay();
    }

    private void UpdateMapTransform()
    {
        if (!_mapVisible || !_mouse.IsGameForeground())
        {
            ClearPendingInput();
        }
    }

    private void ClearPendingInput()
    {
        _pendingDx = 0;
        _pendingDy = 0;
        _pendingWheels.Clear();
    }

    private void HideOverlay()
    {
        // Closing the game map must also close the interactive browser.
        if (_petSearch is not null)
        {
            var search = _petSearch;
            _petSearch = null;
            search.Close();
        }
        _overlay.HidePetMenu();
        if (_overlay.IsVisible)
            _overlay.Hide();

        _mapVisible = false;
        _overlay.HideTrackedMap();
        ClearPendingInput();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PetCollectionBridge.Changed -= OnPetCollectionChanged;
        _overlay.PetSearchRequested -= OpenPetSearch;
        _petSearch?.Close();
        _petSearch = null;

        _visionTimer.Stop();
        _renderTimer.Stop();
        _v2Timer.Stop();
        _v2Tracker?.Dispose();
        _mouse.Dispose();
        _detector.Dispose();
        _gameCapture.Dispose();

        if (_everShown)
            _overlay.Close();
    }
}
