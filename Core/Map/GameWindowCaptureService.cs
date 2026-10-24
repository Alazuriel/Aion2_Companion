using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WgcSharp;

namespace AION_2_Companion.Core.Map;

/// <summary>
/// Kizárólag az AION2 játék HWND-jét rögzíti Windows.Graphics.Capture-rel.
/// Nem használ DXGI desktop fallback-et és nem ír képet lemezre.
/// Egy háttérszál frissíti a legutolsó frame-et; a UI/SIFT csak képrészletet kér.
/// </summary>
public sealed class GameWindowCaptureService : IDisposable
{
    private const string GameProcessName = "AION2";
    private static readonly TimeSpan MaxFrameAge = TimeSpan.FromMilliseconds(1300);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private Bitmap? _latest;
    private Rectangle _latestBounds;
    private DateTime _latestAtUtc;
    private bool _disposed;
    private DateTime _lastErrorLogUtc;

    // A WgcSharp képenként új sessiont nyit: ezért 120ms-es újraindítás helyett
    // egyetlen háttér worker fut. Később tartós WGC frame poolra cserélhető.
    public GameWindowCaptureService()
    {
        _worker = Task.Run(() => CaptureLoopAsync(_stop.Token));
    }

    /// <summary>
    /// A kért terület fizikai ASZTAL koordinátában van, ugyanúgy mint a régi CopyFromScreen-nél.
    /// Az eredmény a hívó tulajdona; using-gal kötelező Dispose-olni.
    /// Nincs desktop/BitBlt fallback, az overlay nem kerülhet a SIFT frame-be.
    /// </summary>
    public bool TryGetRegion(Rectangle screenRegion, out Bitmap? region)
    {
        region = null;
        if (screenRegion.Width <= 0 || screenRegion.Height <= 0) return false;

        lock (_gate)
        {
            if (_disposed || _latest is null ||
                DateTime.UtcNow - _latestAtUtc > MaxFrameAge ||
                !_latestBounds.Contains(screenRegion))
                return false;

            var src = new Rectangle(
                screenRegion.Left - _latestBounds.Left,
                screenRegion.Top - _latestBounds.Top,
                screenRegion.Width, screenRegion.Height);

            if (src.Left < 0 || src.Top < 0 ||
                src.Right > _latest.Width || src.Bottom > _latest.Height)
                return false;

            region = _latest.Clone(src, PixelFormat.Format32bppArgb);
            return true;
        }
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Bitmap? next = null;
            try
            {
                IntPtr hwnd = FindForegroundGameWindow();
                if (hwnd == IntPtr.Zero)
                {
                    ClearFrame();
                }
                else
                {
                    // KRITIKUS: WgcOnly! Az Auto mód desktop-duplicationre eshet vissza.
                    next = WindowCapture.CaptureWindow(
                        hwnd, CaptureStrategy.WgcOnly, timeoutMs: 1200);

                    if (next is not null && next.Width > 0 && next.Height > 0)
                    {
                        if (TryDetermineCapturedBounds(hwnd, next.Size, out var bounds))
                        {
                            Publish(next, bounds);
                            next = null; // tulajdonjog átadva a cache-nek
                        }
                        else if (DateTime.UtcNow - _lastErrorLogUtc > TimeSpan.FromSeconds(10))
                        {
                            _lastErrorLogUtc = DateTime.UtcNow;
                            Console.WriteLine(
                                $"[MAP WGC] Window/screenshot size mismatch: {next.Width}x{next.Height}; " +
                                "check DPI mode and borderless window size.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow - _lastErrorLogUtc > TimeSpan.FromSeconds(10))
                {
                    _lastErrorLogUtc = DateTime.UtcNow;
                    Console.WriteLine($"[MAP WGC ERROR] {ex.GetType().Name}: {ex.Message}");
                }
            }
            finally
            {
                next?.Dispose();
            }

            try { await Task.Delay(180, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        ClearFrame();
    }

    private void Publish(Bitmap bitmap, Rectangle bounds)
    {
        Bitmap? old;
        lock (_gate)
        {
            if (_disposed) { bitmap.Dispose(); return; }
            old = _latest;
            _latest = bitmap;
            _latestBounds = bounds;
            _latestAtUtc = DateTime.UtcNow;
        }
        old?.Dispose();
    }

    private void ClearFrame()
    {
        Bitmap? old;
        lock (_gate)
        {
            old = _latest;
            _latest = null;
            _latestBounds = Rectangle.Empty;
            _latestAtUtc = DateTime.MinValue;
        }
        old?.Dispose();
    }

    private static IntPtr FindForegroundGameWindow()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return IntPtr.Zero;
        try
        {
            using Process p = Process.GetProcessById((int)pid);
            return string.Equals(p.ProcessName, GameProcessName,
                    StringComparison.OrdinalIgnoreCase)
                ? hwnd : IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or
                                  InvalidOperationException or
                                  System.ComponentModel.Win32Exception)
        {
            return IntPtr.Zero;
        }
    }

    // WGC HWND frame többnyire a DWM extended-frame boundsnak felel meg.
    // A képméretnek egyeznie kell valamelyik hiteles Win32-recttel.
    // Eltérő DPI/border esetén inkább nincs találat, mint rossz SIFT eltolás.
    private static bool TryDetermineCapturedBounds(
        IntPtr hwnd, Size size, out Rectangle result)
    {
        result = Rectangle.Empty;
        if (DwmGetWindowAttribute(hwnd, 9, out RECT ext, Marshal.SizeOf<RECT>()) == 0)
        {
            Rectangle b = ext.ToRectangle();
            if (Matches(b, size))
            {
                result = new Rectangle(b.Location, size);
                return true;
            }
        }
        if (GetWindowRect(hwnd, out RECT raw))
        {
            Rectangle b = raw.ToRectangle();
            if (Matches(b, size))
            {
                result = new Rectangle(b.Location, size);
                return true;
            }
        }
        // Ismeretlen pixel-origin esetén ne adjunk ki hamis képet.
        return false;
    }

    private static bool Matches(Rectangle rect, Size size) =>
        Math.Abs(rect.Width - size.Width) <= 2 &&
        Math.Abs(rect.Height - size.Height) <= 2;

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _stop.Cancel();
        // A worker max 1200 ms-es WGC hívásban lehet.
        try { _worker.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        ClearFrame();
        // Ha a WGC nem állt meg időben, a worker fut még a tokennel;
        // a CTS Dispose-t a végleges leállásig halasztjuk.
        if (_worker.IsCompleted) _stop.Dispose();
        else _ = _worker.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint id);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int attribute, out RECT value, int valueSize);
}
