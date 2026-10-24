using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AION_2_Companion.Core.Map;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using CvSize = OpenCvSharp.Size;
using SdRectangle = System.Drawing.Rectangle;

namespace AION_2_Companion.Core.Map.V2;

// A reference JPG nyers pixelkoordinátájáról ABSZOLÚT fizikai képernyőpixelre:
// screenX = ImageX * Scale + OffsetX, screenY = ImageY * Scale + OffsetY.
public sealed record MapV2Transform(
    double A, double B, double C, double D,
    double OffsetX, double OffsetY, int Inliers,
    double InlierRatio, double MedianErrorPx)
{
    // A referencia képpontjára alkalmazott (általában majdnem forgatásmentes) affin transzformáció.
    public double Scale => Math.Sqrt(A * A + C * C);
    public (double X, double Y) Project(double imageX, double imageY) =>
        (imageX * A + imageY * B + OffsetX,
         imageX * C + imageY * D + OffsetY);
}

public sealed record MapV2Result(MapV2Transform Transform);

/// <summary>
/// REFERENCIA térkép -> képernyő képi illesztés.
/// Nem olvas játék memóriát, packetet és nem küld játék inputot.
/// A map megnyitását NEM ez dönti el: azt a meglévő VisibilityDetector végzi.
/// </summary>
public sealed class MapV2Engine : IDisposable
{
    private sealed class Level : IDisposable
    {
        public required double ReferenceScale { get; init; }
        public required KeyPoint[] Keypoints { get; init; }
        public required Mat Descriptors { get; init; }
        public void Dispose() => Descriptors.Dispose();
    }

    private readonly List<Level> _levels = new();
    private readonly SIFT _sift = SIFT.Create(nFeatures: 2600);
    private readonly FlannBasedMatcher _matcher = new();
    private readonly GameWindowCaptureService _capture;

    // Fontos: a SIFT natív objektumok NEM párhuzamosan használtak.
    // MapV2Tracker egyszerre legfeljebb egy frame-et küld ide.
    public MapV2Engine(string jpgPath, GameWindowCaptureService capture)
    {
        _capture = capture;
        if (!File.Exists(jpgPath))
            throw new FileNotFoundException("Map reference JPG missing", jpgPath);
        using var original = Cv2.ImRead(jpgPath, ImreadModes.Grayscale);
        if (original.Empty())
            throw new InvalidDataException($"Cannot decode map JPG: {jpgPath}");

        int mapSide = Math.Max(original.Width, original.Height);
        // Két lépték: robusztusabb, mint egyetlen referenciafelbontás.
        // Az előfeldolgozás egyszer történik induláskor, nem minden képkockán.
        foreach (int maxSide in new[] { 1800, 2800 })
        {
            double refScale = (double)maxSide / mapSide;
            using var scaled = new Mat();
            Cv2.Resize(original, scaled, new CvSize(), refScale, refScale,
                InterpolationFlags.Area);

            var desc = new Mat();
            _sift.DetectAndCompute(scaled, null, out KeyPoint[] points, desc);
            if (desc.Empty() || points.Length < 30)
            {
                desc.Dispose();
                continue;
            }
            _levels.Add(new Level
            {
                ReferenceScale = refScale,
                Keypoints = points,
                Descriptors = desc
            });
            Console.WriteLine($"[MAP V2] Reference {maxSide}px: features={points.Length}");
        }

        if (_levels.Count == 0)
            throw new InvalidDataException("Reference map has no usable features.");

        Console.WriteLine($"[MAP V2] Ready | map={original.Width}x{original.Height}");
    }

    /// <summary>
    /// Hívás háttérszálról. GameBounds a meglévő TryGetGameBounds fizikai pixel-RECT-je.
    /// Null eredmény = nincs megbízható illesztés -> a markereket rejtsük el!
    /// </summary>
    public MapV2Result? MatchFrame(SdRectangle gameBounds)
    {
        if (gameBounds.Width < 600 || gameBounds.Height < 400)
            return null;

        // A térkép bal oldali listája + felső menüsávja ne szennyezze a match-et.
        // A screenshotokat NEM a saját map marker layer alapján illesztjük.
        var region = new SdRectangle(
            gameBounds.Left + (int)(gameBounds.Width * 0.17),
            gameBounds.Top + (int)(gameBounds.Height * 0.12),
            (int)(gameBounds.Width * 0.75),
            (int)(gameBounds.Height * 0.76));

        using var screenshot = CaptureGrayscale(region);
        if (screenshot.Empty())
            return null;

        // A screenshot kis felbontása CPU-t spórol; a koordinátákat visszaskálázzuk.
        double queryScale = Math.Min(1.0, 1100.0 / screenshot.Width);
        using var query = new Mat();
        Cv2.Resize(screenshot, query, new CvSize(), queryScale, queryScale,
            InterpolationFlags.Area);

        using var queryDescriptors = new Mat();
        _sift.DetectAndCompute(query, null, out KeyPoint[] queryPoints, queryDescriptors);
        if (queryDescriptors.Empty() || queryPoints.Length < 25)
        {
            Console.WriteLine($"[MAP V2 MISS] queryFeatures={queryPoints.Length} | too few screen features");
            return null;
        }

        MapV2Transform? best = null;
        foreach (var level in _levels)
        {
            var candidate = TryMatchLevel(queryPoints, queryDescriptors,
                level, queryScale, region);
            if (candidate is not null &&
                (best is null || candidate.Inliers > best.Inliers))
                best = candidate;

            // Ha az elso referencian mar eros a talalat,
            // ne futtassuk a kovetkezo draga FLANN keresest.
            if (best is { Inliers: >= 45, InlierRatio: >= 0.55, MedianErrorPx: <= 2.5 })
                break;
        }

        // A 0-s zoomnal a matcher szigorubb feltetelei miatt
        // NULL lehet az eredmeny: diagnosztika, nem talalgatas.
        if (best is null)
            Console.WriteLine($"[MAP V2 MISS] queryFeatures={queryPoints.Length} | levels={_levels.Count} | no confident transform");

        return best is null ? null : new MapV2Result(best);
    }

    private MapV2Transform? TryMatchLevel(
        KeyPoint[] queryPoints, Mat queryDesc, Level level,
        double queryScale, SdRectangle region)
    {
        DMatch[][] pairs;
        try { pairs = _matcher.KnnMatch(queryDesc, level.Descriptors, 2); }
        catch (OpenCVException) { return null; }

        // Lowe ratio test; egy referenciapontot egyszer használunk.
        DMatch[] good = pairs
            .Where(p => p.Length == 2 && p[0].Distance < 0.72 * p[1].Distance)
            .Select(p => p[0])
            .GroupBy(m => m.TrainIdx)
            .Select(g => g.OrderBy(x => x.Distance).First())
            .ToArray();
        if (good.Length < 25)
            return null;

        Point2f[] src = good.Select(m => level.Keypoints[m.TrainIdx].Pt).ToArray();
        Point2f[] dst = good.Select(m => queryPoints[m.QueryIdx].Pt).ToArray();

        using var inlierMask = new Mat();
        using var input = InputArray.Create(src);
        using var output = InputArray.Create(dst);
        using var a = Cv2.EstimateAffinePartial2D(
            input, output, inlierMask,
            RobustEstimationAlgorithms.RANSAC, 3.0, 2000, 0.995, 10);

        if (a is null || a.Empty())
            return null;

        double xx = a.At<double>(0, 0);
        double yx = a.At<double>(1, 0);
        double tx = a.At<double>(0, 2);
        double ty = a.At<double>(1, 2);
        double localScale = Math.Sqrt(xx * xx + yx * yx);

        // A game-map normál esetben NEM forog, csak eltolódik és nagyítódik.
        if (xx <= 0 || localScale < 0.10 || localScale > 15.0 ||
            Math.Abs(yx) > localScale * 0.018)
            return null;

        int inliers = Cv2.CountNonZero(inlierMask);
        double ratio = (double)inliers / good.Length;
        if (inliers < 20 || ratio < 0.35)
            return null;

        var errors = new List<double>(inliers);
        double loX = double.MaxValue, hiX = double.MinValue;
        double loY = double.MaxValue, hiY = double.MinValue;
        for (int i = 0; i < src.Length; i++)
        {
            if (inlierMask.At<byte>(i, 0) == 0)
                continue;
            // A forgatás pici maradhat a RANSAC-ban, de a mi overlayünk
            // sima uniform scale-t használ. Ezért a torzítást is ellenőrizzük.
            double px = xx * src[i].X - yx * src[i].Y + tx;
            double py = yx * src[i].X + xx * src[i].Y + ty;
            errors.Add(Math.Sqrt(Math.Pow(px - dst[i].X, 2) +
                                 Math.Pow(py - dst[i].Y, 2)));
            loX = Math.Min(loX, dst[i].X); hiX = Math.Max(hiX, dst[i].X);
            loY = Math.Min(loY, dst[i].Y); hiY = Math.Max(hiY, dst[i].Y);
        }
        if (errors.Count == 0 || hiX - loX < 100 || hiY - loY < 70)
            return null;
        errors.Sort();
        double medianError = errors[errors.Count / 2];
        if (medianError > 3.0)
            return null;

        // Reference ORIGINAL map pixels -> DESKTOP physical pixels.
        double factor = level.ReferenceScale / queryScale;
        return new MapV2Transform(
            A: a.At<double>(0, 0) * factor,
            B: a.At<double>(0, 1) * factor,
            C: a.At<double>(1, 0) * factor,
            D: a.At<double>(1, 1) * factor,
            OffsetX: tx / queryScale + region.Left,
            OffsetY: ty / queryScale + region.Top,
            Inliers: inliers,
            InlierRatio: ratio,
            MedianErrorPx: medianError / queryScale);
    }

    private Mat CaptureGrayscale(SdRectangle region)
    {
        // Csak a játék HWND-jéből származó, overlaymentes WGC-kép.
        if (!_capture.TryGetRegion(region, out Bitmap? bitmap) || bitmap is null)
            return new Mat();

        using (bitmap)
        {
            var bits = bitmap.LockBits(
                new SdRectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                using var bgra = Mat.FromPixelData(bitmap.Height, bitmap.Width,
                    MatType.CV_8UC4, bits.Scan0, bits.Stride);
                var gray = new Mat();
                Cv2.CvtColor(bgra, gray, ColorConversionCodes.BGRA2GRAY);
                return gray;
            }
            finally { bitmap.UnlockBits(bits); }
        }
    }

    public void Dispose()
    {
        foreach (var level in _levels) level.Dispose();
        _matcher.Dispose();
        _sift.Dispose();
    }
}

/// <summary>
/// Nem blokkolja a UI thread-et; legfeljebb egy frame fut egyszerre.
/// </summary>
public sealed class MapV2Tracker : IDisposable
{
    private readonly string _jpgPath;
    private readonly GameWindowCaptureService _capture;
    private MapV2Engine? _engine;
    private bool _initializationFailed;
    private int _busy;
    private bool _disposed;
    private Task? _running;

    public MapV2Tracker(string jpgPath, GameWindowCaptureService capture)
    {
        _capture = capture;
        if (!File.Exists(jpgPath))
            throw new FileNotFoundException("Map JPG missing", jpgPath);
        _jpgPath = jpgPath;
    }

    // Az eredmény Callbackje mindig az Avalonia UI thread-en fut.
    public void RequestFrame(SdRectangle gameBounds, Action<MapV2Result?> onResult)
    {
        if (_disposed || _initializationFailed ||
            Interlocked.Exchange(ref _busy, 1) == 1)
            return;
        _running = Task.Run(() =>
        {
            MapV2Result? result = null;
            try
            {
                // A sok képi feature előfeldolgozása a háttérszálon történjen!
                _engine ??= new MapV2Engine(_jpgPath, _capture);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                result = _engine.MatchFrame(gameBounds);
                watch.Stop();
                if (watch.ElapsedMilliseconds >= 800)
                    Console.WriteLine($"[MAP V2 PERF] Match={watch.ElapsedMilliseconds}ms | found={result is not null}");
            }
            catch (Exception ex)
            {
                if (_engine is null) _initializationFailed = true;
                Console.WriteLine($"[MAP V2 ERROR] {ex.GetType().Name}: {ex.Message}");
            }
            finally { Interlocked.Exchange(ref _busy, 0); }

            if (!_disposed)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed) onResult(result);
                });
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // UI kilépéskor elkerüljük a natív OpenCV objektumok
        // felszabadítását egy éppen futó matcher alatt.
        try { _running?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }
        if (_running is null || _running.IsCompleted)
            _engine?.Dispose();
        else
            _ = _running.ContinueWith(_ => _engine?.Dispose());
    }
}
