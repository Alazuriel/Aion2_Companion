/*
 * TODO
 *      A Map csak akkor látszodjon ha
 *          1. A játék látható (Ne érzékelje máshogy az M betűt!)
 *          2. Az 1. pont működjön!
 * TODO
 *  borderless fullscreen használatával jó fixen, máshogy nem! 
    Bele kell ezt írni majd readme.be illetve beállításokba is ha bekapcsolja a helper-t kis I (információs tooltip gomb bizbasz
    Hogy Lehet nemjó!
 */

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Diagnostics;
using AION_2_Companion.Core;

namespace AION_2_Companion.Views.Overlays;

public partial class MapOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int DefaultZoom = 1;
    private const int MinZoom = 0;
    private const int MaxZoom = 3;
    


// Ideiglenes érték, később kimérjük!
    private double ZoomAnimationMs = 250;

    private readonly DispatcherTimer _zoomAnimationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };

    private long _zoomStartedAt;

    private double _zoomFromX;
    private double _zoomFromY;
    private double _zoomFromScale;

    private double _zoomToX;
    private double _zoomToY;
    private double _zoomToScale;

    private int _currentZoom = DefaultZoom;

    private static readonly double[] ZoomScales =
    {
        2.516, // 0
        1.000, // 1
        0.500, // 2
        0.327  // 3
    };

    private int _wheelRemainder;
    
    private const long WS_EX_TRANSPARENT = 0x20;
    private const long WS_EX_LAYERED = 0x80000;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private Screens? _registeredScreens;
    private Border? _petMenuPanel;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr after,
        int x, int y,
        int width, int height,
        uint flags);

    public MapOverlayWindow()
    {
        InitializeComponent();
        _zoomAnimationTimer.Tick += (_, _) =>
            UpdateZoomAnimation();
        // Az első Show() előtt!
        ShowActivated = false;
        Focusable = false;
        
        Opened += OnOverlayOpened;
        Closing += OnOverlayClosing;
        LocalizationService.LanguageChanged += LanguageChanged;
    }


    private void OnOverlayOpened(object? sender, EventArgs e)
    {
        // A Screens objektumot csak egyszer kérjük le.
        if (_registeredScreens is null)
        {
            _registeredScreens = Screens;
            _registeredScreens.Changed += OnScreensChanged;
        }

        UpdateScreenBounds();

        if (OperatingSystem.IsWindows())
            EnableClickThrough();

        Console.WriteLine(
            "[MAP OVERLAY] Window initialized.");
    }



    private void OnScreensChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Ha az ablakot bezártuk, már nincs mit frissíteni.
            if (_registeredScreens is null)
                return;

            UpdateScreenBounds();
        });
    }

    private void UpdateScreenBounds()
    {
        // NEM a Window.Screens property-t hívjuk!
        var screen = _registeredScreens?.Primary;

        if (screen is null)
            return;

        var bounds = screen.Bounds;
        var scaling = screen.Scaling;

        Position = new PixelPoint(
            bounds.X,
            bounds.Y);

        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;

        Console.WriteLine(
            $"[MAP OVERLAY] " +
            $"{bounds.Width}x{bounds.Height} | " +
            $"Scale={scaling:F2} | " +
            $"Position={bounds.X},{bounds.Y}");
    }


    private void EnableClickThrough()
    {
        var hwnd = TryGetPlatformHandle()?.Handle
                   ?? IntPtr.Zero;

        if (hwnd == IntPtr.Zero)
            return;

        long style = GetWindowLongPtr(
            hwnd, GWL_EXSTYLE).ToInt64();

        style |= WS_EX_TRANSPARENT;
        style |= WS_EX_LAYERED;
        style |= WS_EX_NOACTIVATE;

        SetWindowLongPtr(
            hwnd,
            GWL_EXSTYLE,
            new IntPtr(style));

        const uint flags =
            0x0001 | // SWP_NOSIZE
            0x0002 | // SWP_NOMOVE
            0x0004 | // SWP_NOZORDER
            0x0010 | // SWP_NOACTIVATE
            0x0020;  // SWP_FRAMECHANGED

        if (!SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            flags))
        {
            Console.WriteLine("map",
                "[MAP OVERLAY] WARNING: " +
                "Click-through style update failed.");
        }
    }

    private void OnOverlayClosing(
        object? sender,
        WindowClosingEventArgs e)
    {
        LocalizationService.LanguageChanged -= LanguageChanged;
        if (_registeredScreens is not null)
        {
            _registeredScreens.Changed -= OnScreensChanged;
            _registeredScreens = null;
        }

        Console.WriteLine(
            "[MAP OVERLAY] Window closing.");
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        UpdatePetMenuToggleVisual();
        UpdatePetMenuCollectionStatus();
        UpdatePetFarmTooltipOwnership(_petFarmSelected is null
            ? null
            : _petFarmLayer?.GetCatalog().FirstOrDefault(p =>
                p.PetId == _petFarmSelected.PetId &&
                p.MapId.Equals(_petFarmSelected.MapId, StringComparison.OrdinalIgnoreCase)));
    }

    private double _mapOffsetX = 0;
    private double _mapOffsetY = 0;
    private double _mapZoom = 1.0;

    public void SetMapView(
        double offsetX,
        double offsetY,
        double zoom)
    {
        if (zoom <= 0)
            return;

        _mapOffsetX = offsetX;
        _mapOffsetY = offsetY;
        _mapZoom = zoom;

    }
    
    public void ResetMapView()
    {
        _zoomAnimationTimer.Stop();
        _currentZoom = DefaultZoom;
        _wheelRemainder = 0;

        _mapOffsetX = 0;
        _mapOffsetY = 0;
        _mapZoom = ZoomScales[DefaultZoom];

    }

    private long _lastPanTimestamp;
    private double _smoothedPanSpeed;
    
    public void PanByPixels(double dx, double dy)
    {
        long now = Stopwatch.GetTimestamp();

        double elapsed = _lastPanTimestamp == 0
            ? 1.0 / 60.0
            : Stopwatch.GetElapsedTime(
                _lastPanTimestamp, now).TotalSeconds;

        _lastPanTimestamp = now;

        // Új húzásnál ne maradjon meg
        // az előző húzás sebessége.
        if (elapsed > 0.12)
            _smoothedPanSpeed = 0;

        double dt = Math.Clamp(
            elapsed, 0.008, 0.05);

        // Fizikai pixel / másodperc.
        double distance = Math.Sqrt(
            dx * dx + dy * dy);

        double speed = distance / dt;

        // Sebesség kisimítása, hogy ne ugráljon.
        _smoothedPanSpeed +=
            (speed - _smoothedPanSpeed) * 0.35;

        double sensitivity = GetPanSensitivity(
            _smoothedPanSpeed);

        double dpi = Math.Max(RenderScaling, 0.1);

        SetMapView(
            _mapOffsetX + dx / dpi * sensitivity,
            _mapOffsetY + dy / dpi * sensitivity,
            _mapZoom);
    }

    private static double GetPanSensitivity(double speed)
    {
        // Lassú: 0.90x
        if (speed <= 250)
            return 0.90;

        // Lassú -> normál: 0.90x -> 1.00x
        if (speed <= 900)
        {
            double t = (speed - 250) / 650;
            return 0.90 + t * 0.10;
        }

        // Normál -> gyors: 1.00x -> 1.20x
        if (speed <= 2500)
        {
            double t = (speed - 900) / 1600;
            return 1.00 + t * 0.20;
        }

        // Nagyon gyors húzás
        return 1.20;
    }



    

    public void ZoomAtScreenPoint(
        int pixelX,
        int pixelY,
        int wheelDelta)
    {
        // Amíg az előző zoom animáció fut,
        // minden új görgetést figyelmen kívül hagyunk.
        if (_zoomAnimationTimer.IsEnabled)
        {
            _wheelRemainder = 0;
            return;
        }

        _wheelRemainder += wheelDelta;

        int steps = _wheelRemainder / 120;
        _wheelRemainder -= steps * 120;

        if (steps == 0)
            return;

        // Egy animáció = maximum egy zoomszint.
        int direction = Math.Sign(steps);

        int previousZoom = _currentZoom;

        int nextZoom = Math.Clamp(
            _currentZoom - direction,
            MinZoom,
            MaxZoom);

        _wheelRemainder = 0;

        
        if (nextZoom == previousZoom)
            return;

        ZoomAnimationMs = (previousZoom, nextZoom) switch
        {
            (0, 1) => 150,
            (1, 0) => 325,
            (1, 2) or (2, 1) => 250,
            (2, 3) or (3, 2) => 1000,
            _ => 250
        };

        double factor =
            ZoomScales[nextZoom] /
            ZoomScales[previousZoom];

        double dpi = Math.Max(RenderScaling, 0.1);

        double mouseX =
            (pixelX - Position.X) / dpi;

        double mouseY =
            (pixelY - Position.Y) / dpi;

        double pivotX =
            mouseX - MarkerCanvas.Bounds.Width / 2;

        double pivotY =
            mouseY - MarkerCanvas.Bounds.Height / 2;

        // Kezdőállapot
        _zoomFromX = _mapOffsetX;
        _zoomFromY = _mapOffsetY;
        _zoomFromScale = _mapZoom;

        // Célállapot
        _zoomToX =
            pivotX + (_mapOffsetX - pivotX) * factor;

        _zoomToY =
            pivotY + (_mapOffsetY - pivotY) * factor;

        _zoomToScale = _mapZoom * factor;

        _currentZoom = nextZoom;

        _zoomStartedAt = Stopwatch.GetTimestamp();

        _zoomAnimationTimer.Start();

        Console.WriteLine(
            $"[MAP ZOOM] {previousZoom} -> {nextZoom} | " +
            $"Factor={factor:F3} | " +
            $"Animation={ZoomAnimationMs}ms");
    }

    
    private void UpdateZoomAnimation()
    {
        double elapsed = Stopwatch
            .GetElapsedTime(_zoomStartedAt)
            .TotalMilliseconds;

        double t = Math.Clamp(
            elapsed / ZoomAnimationMs,
            0.0,
            1.0);

        // LINEAR: nincs easing, egyenletes változás.
        double x =
            _zoomFromX +
            (_zoomToX - _zoomFromX) * t;

        double y =
            _zoomFromY +
            (_zoomToY - _zoomFromY) * t;

        double zoom =
            _zoomFromScale +
            (_zoomToScale - _zoomFromScale) * t;

        SetMapView(x, y, zoom);

        if (t >= 1.0)
        {
            _zoomAnimationTimer.Stop();

            SetMapView(
                _zoomToX,
                _zoomToY,
                _zoomToScale);

            Console.WriteLine(
                $"[MAP ZOOM] Completed | " +
                $"Level={_currentZoom} | " +
                $"Duration={elapsed:F0}ms");
        }
    }



}
