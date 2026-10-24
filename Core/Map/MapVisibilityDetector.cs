
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace AION_2_Companion.Core.Map;

public enum MapReference
{
    Open,
    Closed
}

public sealed class MapVisibilityDetector : IDisposable
{
    private const string GameProcess = "AION2";

    // IDEIGLENES mintaterület:
    // A játékablak jobb felső részén.
    // Később a valódi map UI-hoz kalibráljuk!
    private const double RegionX = 0.78;
    private const double RegionY = 0.06;
    private const double RegionW = 0.18;
    private const double RegionH = 0.12;

    private readonly string _directory =
        Path.Combine(AppContext.BaseDirectory, "MapCalibration");

    private readonly GameWindowCaptureService _capture;
    private SD.Bitmap? _openReference;
    private SD.Bitmap? _closedReference;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr hwnd, out RECT rect);

    public MapVisibilityDetector(GameWindowCaptureService capture)
    {
        _capture = capture;
        Directory.CreateDirectory(_directory);

        LoadReferences();
    }

    public bool TryGetGameBounds(out SD.Rectangle bounds)
    {
        bounds = SD.Rectangle.Empty;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(hwnd, out uint pid);

        if (pid == 0)
            return false;

        try
        {
            using var process =
                Process.GetProcessById((int)pid);

            if (!string.Equals(
                process.ProcessName,
                GameProcess,
                StringComparison.OrdinalIgnoreCase))
                return false;

            if (!GetWindowRect(hwnd, out RECT rect))
                return false;

            bounds = SD.Rectangle.FromLTRB(
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom);

            return bounds.Width > 0 && bounds.Height > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private SD.Bitmap Capture(SD.Rectangle area)
    {
        if (_capture.TryGetRegion(area, out SD.Bitmap? bitmap) && bitmap is not null)
            return bitmap;

        throw new InvalidOperationException(
            "Nincs friss játékablak-kép (WGC). Asztal-capture fallback letiltva.");
    }

    private static SD.Rectangle GetRegion(
        SD.Rectangle gameBounds)
    {
        return new SD.Rectangle(
            gameBounds.Left + (int)(gameBounds.Width * RegionX),
            gameBounds.Top + (int)(gameBounds.Height * RegionY),
            Math.Max(1, (int)(gameBounds.Width * RegionW)),
            Math.Max(1, (int)(gameBounds.Height * RegionH)));
    }

    public void CaptureReference(
        MapReference state,
        SD.Rectangle gameBounds)
    {
        string name = state == MapReference.Open
            ? "map_open"
            : "map_closed";

        // Teljes screenshot csak kalibráláskor!
        using (var full = Capture(gameBounds))
        {
            full.Save(
                Path.Combine(_directory, $"{name}_full.png"),
                SDI.ImageFormat.Png);
        }

        // A tényleges felismeréshez csak kis ROI kell.
        using var sample = Capture(GetRegion(gameBounds));

        sample.Save(
            Path.Combine(_directory, $"{name}.png"),
            SDI.ImageFormat.Png);

        LoadReferences();

        Console.WriteLine(
            $"[MAP VISION] Reference saved: {name}");
    }

    private void LoadReferences()
    {
        _openReference?.Dispose();
        _closedReference?.Dispose();

        _openReference = LoadBitmap("map_open.png");
        _closedReference = LoadBitmap("map_closed.png");
    }

    private SD.Bitmap? LoadBitmap(string name)
    {
        string path = Path.Combine(_directory, name);

        return File.Exists(path)
            ? new SD.Bitmap(path)
            : null;
    }

    private static double Difference(
        SD.Bitmap current,
        SD.Bitmap reference)
    {
        if (current.Width != reference.Width ||
            current.Height != reference.Height)
            return double.PositiveInfinity;

        long total = 0;
        long count = 0;

        // Nem kell minden pixelt vizsgálni.
        for (int y = 0; y < current.Height; y += 4)
        {
            for (int x = 0; x < current.Width; x += 4)
            {
                var a = current.GetPixel(x, y);
                var b = reference.GetPixel(x, y);

                total += Math.Abs(a.R - b.R);
                total += Math.Abs(a.G - b.G);
                total += Math.Abs(a.B - b.B);

                count += 3;
            }
        }

        return count == 0
            ? double.PositiveInfinity
            : (double)total / count;
    }

    public bool IsMapOpen(
        SD.Rectangle gameBounds,
        out double openScore,
        out double closedScore)
    {
        openScore = double.PositiveInfinity;
        closedScore = double.PositiveInfinity;

        if (_openReference is null ||
            _closedReference is null)
            return false;

        using var current = Capture(GetRegion(gameBounds));

        openScore = Difference(current, _openReference);
        closedScore = Difference(current, _closedReference);

        // Első tesztküszöbök, később kalibráljuk.
        return openScore < 30 &&
               openScore + 5 < closedScore;
    }

    public void Dispose()
    {
        _openReference?.Dispose();
        _closedReference?.Dispose();
    }
}
