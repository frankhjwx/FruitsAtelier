using System.Buffers;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace FruitsAtelier.App.Rendering;

public sealed partial class D2DCanvas
{
    private const long sliderRasterLimit = 32L * 1024 * 1024;
    private const long sliderRasterActiveLimit = 24L * 1024 * 1024;
    private const long sliderRasterTransitionLimit = sliderRasterLimit - sliderRasterActiveLimit;
    private const double sliderRasterTransitionMs = 120;
    // Render snapshots are immutable; pan, zoom, and fade remain outside the cached pixels.
    private readonly record struct SliderRasterKey(IReadOnlyList<Vector2> Points, float Diameter, uint Border, uint Accent, Rect Bounds);
    private sealed record SliderRaster(ID2D1Bitmap1 Bitmap, int Width, int Height, DpiCompensation Dpi, AffineTransform2D Transform, bool Preview = false) : IDisposable
    {
        public long Used, Frame;
        public long Bytes => (long)Width * Height * 4;
        public void Dispose() { Transform.Dispose(); Dpi.Dispose(); Bitmap.Dispose(); }
    }
    private sealed record SliderRasterTransition(SliderRaster Previous, CrossFade Blend, double Started) : IDisposable
    {
        public void Dispose()
        {
            Blend.Dispose(); Previous.Dispose();
        }
    }
    private readonly Dictionary<SliderRasterKey, SliderRaster> sliderRasters = [];
    private readonly Dictionary<SliderRasterKey, SliderRasterTransition> sliderRasterTransitions = [];
    private readonly Dictionary<IReadOnlyList<Vector2>, Rect> sliderRasterExtents = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<IReadOnlyList<Vector2>> sliderRasterExtentOrder = new();
    private long sliderRasterBytes, sliderRasterClock, sliderRasterFrame, sliderRasterPreparedFrame = -1;
    internal long CachedSliderRasterBytes => sliderRasterBytes;
    internal int SliderRasterBuildCount { get; private set; }
    internal int CachedSliderRasterTransitionCount => sliderRasterTransitions.Count;
    internal Func<double>? SliderRasterTimeOverride { get; set; }
    private double SliderRasterNow => SliderRasterTimeOverride?.Invoke()
        ?? System.Diagnostics.Stopwatch.GetTimestamp() * 1000d / System.Diagnostics.Stopwatch.Frequency;
    public bool SliderRasterNeedsRedraw
    {
        get
        {
            if (sliderRasterPending is not null || sliderRasterTransitions.Count > 0) return true;
            if (sliderRasterPreparedFrame == sliderRasterFrame)
                foreach (var raster in sliderRasters.Values)
                    if (raster.Preview && raster.Frame == sliderRasterFrame) return true;
            return false;
        }
    }
    private int sliderRasterPixelBudget = (int)(sliderRasterActiveLimit / 16);
    private bool sliderRasterDeferred;
    private Task<SliderRasterPixels>? sliderRasterPending;
    private SliderRasterKey sliderRasterPendingKey;
    private bool sliderRasterPendingDiscarded;
    internal bool PendingSliderRasterBuild => sliderRasterPending is not null;
    internal int CachedSliderRasterPreviewCount => sliderRasters.Values.Count(raster => raster.Preview);
    private CancellationTokenSource? sliderRasterCancellation;
    private sealed class SliderRasterPixels(int width, int height, byte[] pixels) : IDisposable
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Pixels { get; } = pixels;
        public void Dispose() => ArrayPool<byte>.Shared.Return(Pixels);
    }

    internal void BeginSliderRasterFrame()
    {
        sliderRasterFrame++;
        RetireSliderRasterTransitions();
        if (sliderRasterPreparedFrame < sliderRasterFrame - 1)
        {
            CancelSliderRaster();
            if (sliderRasterPendingDiscarded && sliderRasterPending is { IsCompleted: true })
            {
                sliderRasterPending = null; sliderRasterPendingDiscarded = false;
            }
        }
    }

    public void PrepareSliderBodies(int denseBodyCount, bool deferred = false)
    {
        int slots = 4;
        while (slots < denseBodyCount && slots < 1 << 20) slots *= 2;
        sliderRasterPixelBudget = (int)Math.Max(1, sliderRasterActiveLimit / (4 * slots));
        sliderRasterDeferred = deferred;
        sliderRasterPreparedFrame = sliderRasterFrame;
        RetireSliderRasterTransitions();
        CompleteSliderRaster();
    }

    private void RetireSliderRasterTransitions()
    {
        double now = SliderRasterNow;
        foreach (var pair in sliderRasterTransitions)
            if (now - pair.Value.Started >= sliderRasterTransitionMs)
            {
                sliderRasterBytes -= pair.Value.Previous.Bytes;
                pair.Value.Dispose(); sliderRasterTransitions.Remove(pair.Key);
            }
    }

    private bool MakeSliderRasterRoom(long bytes, SliderRasterKey? protectedKey = null)
    {
        while (sliderRasters.Count > 0 && (sliderRasterBytes + bytes > sliderRasterLimit
            || protectedKey is null && sliderRasters.Count >= 128))
        {
            KeyValuePair<SliderRasterKey, SliderRaster> oldest = default;
            foreach (var pair in sliderRasters)
                if (pair.Key != protectedKey && pair.Value.Frame < sliderRasterFrame - 1
                    && (oldest.Value is null || pair.Value.Used < oldest.Value.Used)) oldest = pair;
            if (oldest.Value is null) return false;
            sliderRasterBytes -= oldest.Value.Bytes; oldest.Value.Dispose(); sliderRasters.Remove(oldest.Key);
            if (sliderRasterTransitions.Remove(oldest.Key, out var transition))
            {
                sliderRasterBytes -= transition.Previous.Bytes; transition.Dispose();
            }
        }
        return sliderRasterBytes + bytes <= sliderRasterLimit;
    }

    private bool HasOversizedActiveSliderRaster
    {
        get
        {
            foreach (var raster in sliderRasters.Values)
                if (raster.Frame >= sliderRasterFrame - 1 && (long)raster.Width * raster.Height > sliderRasterPixelBudget) return true;
            return false;
        }
    }

    private bool TryDrawSliderRaster(IReadOnlyList<Vector2> points, float x, float y, float scale,
        float diameter, uint border, uint accent, float opacity)
    {
        if (points.Count <= 2048) return false;
        if (!sliderRasterExtents.TryGetValue(points, out var extent))
        {
            float minX = points[0].X, maxX = minX, minY = points[0].Y, maxY = minY;
            for (int i = 1; i < points.Count; i++)
            {
                minX = Math.Min(minX, points[i].X); maxX = Math.Max(maxX, points[i].X);
                minY = Math.Min(minY, points[i].Y); maxY = Math.Max(maxY, points[i].Y);
            }
            extent = new(minX, minY, maxX - minX, maxY - minY);
            if (sliderRasterExtents.Count >= 32) sliderRasterExtents.Remove(sliderRasterExtentOrder.Dequeue());
            sliderRasterExtents.Add(points, extent); sliderRasterExtentOrder.Enqueue(points);
        }
        float left = extent.X - diameter, top = extent.Y - diameter;
        float right = extent.Right + diameter, bottom = extent.Bottom + diameter;
        if (right - left > 4096 || bottom - top > 4096)
        {
            float screenWidth = width * 96 / dpi, screenHeight = height * 96 / dpi;
            left = Math.Max(left, -x / scale - diameter - 128);
            right = Math.Min(right, (screenWidth - x) / scale + diameter + 128);
            top = Math.Max(top, -y / scale - diameter - 128);
            bottom = Math.Min(bottom, (screenHeight - y) / scale + diameter + 128);
        }
        if (right <= left || bottom <= top) return true;
        left = MathF.Floor(left / 64) * 64; top = MathF.Floor(top / 64) * 64;
        right = MathF.Ceiling(right / 64) * 64; bottom = MathF.Ceiling(bottom / 64) * 64;
        var bounds = new Rect(left, top, right - left, bottom - top);
        var key = new SliderRasterKey(points, diameter, border, accent, bounds);
        if (!sliderRasters.TryGetValue(key, out var raster))
        {
            int budget = sliderRasterDeferred || HasOversizedActiveSliderRaster
                ? Math.Min(32768, Math.Max(1, sliderRasterPixelBudget / 8)) : sliderRasterPixelBudget;
            using var pixels = CreateSliderPixels(key, budget, CancellationToken.None);
            if (!MakeSliderRasterRoom((long)pixels.Width * pixels.Height * 4)) return false;
            var desired = SliderRasterSize(bounds, sliderRasterPixelBudget);
            raster = UploadSliderRaster(pixels, bounds, pixels.Width < desired.Width || pixels.Height < desired.Height);
            sliderRasters.Add(key, raster); sliderRasterBytes += raster.Bytes; SliderRasterBuildCount++;
        }
        var target = SliderRasterSize(bounds, sliderRasterPixelBudget);
        bool resize = (long)raster.Width * raster.Height > sliderRasterPixelBudget
            || raster.Preview && (target.Width > raster.Width || target.Height > raster.Height);
        bool oversized = (long)raster.Width * raster.Height > sliderRasterPixelBudget;
        if (resize && sliderRasterPending is null && !sliderRasterTransitions.ContainsKey(key)
            && (oversized || !HasOversizedActiveSliderRaster))
        {
            sliderRasterPendingKey = key;
            var cancellation = new CancellationTokenSource(); sliderRasterCancellation = cancellation;
            int budget = sliderRasterPixelBudget;
            sliderRasterPending = sliderRasterDeferred
                ? Task.Run(() => CreateSliderPixels(key, budget, cancellation.Token))
                : Task.FromResult(CreateSliderPixels(key, budget, CancellationToken.None));
            if (!sliderRasterDeferred) CompleteSliderRaster();
            raster = sliderRasters[key];
        }
        raster.Used = ++sliderRasterClock; raster.Frame = sliderRasterFrame;
        if (sliderRasterTransitions.TryGetValue(key, out var fade))
            DrawSliderRasterTransition(key, fade, x, y, scale, opacity);
        else DrawSliderRasterImage(key, raster.Transform, x, y, scale, opacity);
        return true;
    }

    private void DrawSliderRasterTransition(SliderRasterKey key, SliderRasterTransition fade,
        float x, float y, float scale, float opacity)
    {
        // Interpolate premultiplied pixels before applying the object's opacity once.
        fade.Blend.Weight = 1 - (float)Math.Clamp((SliderRasterNow - fade.Started) / sliderRasterTransitionMs, 0, 1);
        DrawSliderRasterImage(key, fade.Blend, x, y, scale, opacity);
    }

    private void DrawSliderRasterImage(SliderRasterKey key, ID2D1Effect image, float x, float y, float scale, float opacity)
    {
        var previous = context!.Transform;
        try
        {
            context.Transform = Matrix3x2.CreateScale(scale)
                * Matrix3x2.CreateTranslation(x + key.Bounds.X * scale, y + key.Bounds.Y * scale) * previous;
            context.PushLayer(new LayerParameters1 { ContentBounds = new Vortice.Mathematics.Rect(0, 0, key.Bounds.Width, key.Bounds.Height),
                MaskTransform = Matrix3x2.Identity, MaskAntialiasMode = AntialiasMode.PerPrimitive,
                Opacity = opacity, LayerOptions = LayerOptions1.None }, null!);
            try
            {
                using var output = image.Output;
                context.DrawImage(output, null, null, Vortice.Direct2D1.InterpolationMode.Linear, CompositeMode.SourceOver);
            }
            finally { context.PopLayer(); }
        }
        finally { context.Transform = previous; }
    }

    private static (int Width, int Height, double Density) SliderRasterSize(Rect bounds, int pixelBudget)
    {
        double density = Math.Min(4, Math.Min(2048d / Math.Max(bounds.Width, bounds.Height),
            Math.Sqrt(pixelBudget / ((double)bounds.Width * bounds.Height))));
        return (Math.Max(1, (int)Math.Floor(bounds.Width * density)),
            Math.Max(1, (int)Math.Floor(bounds.Height * density)), density);
    }

    private unsafe SliderRaster UploadSliderRaster(SliderRasterPixels pixels, Rect bounds, bool preview = false)
    {
        var properties = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None);
        ID2D1Bitmap1 bitmap;
        fixed (byte* source = pixels.Pixels)
            bitmap = context!.CreateBitmap(new SizeI(pixels.Width, pixels.Height), (nint)source,
                (uint)(pixels.Width * 4), properties);
        AffineTransform2D? transform = null;
        DpiCompensation? compensation = null;
        try
        {
            // Effect graphs need explicit compensation for cached 96-DPI bitmap inputs.
            compensation = new(context!) { BorderMode = BorderMode.Hard,
                InterpolationMode = DpiCompensationInterpolationMode.Linear };
            // The native property is VECTOR2; the pinned wrapper's scalar setter is incompatible.
            compensation.SetValue((uint)DpiCompensationProperties.InputDpi, new Vector2(96, 96));
            compensation.SetInput(0, bitmap, true);
            transform = new(context!) { TransformMatrix = Matrix3x2.CreateScale(bounds.Width / pixels.Width, bounds.Height / pixels.Height),
                InterPolationMode = AffineTransform2DInterpolationMode.Linear };
            transform.SetInputEffect(0, compensation);
            return new(bitmap, pixels.Width, pixels.Height, compensation, transform, preview);
        }
        catch { transform?.Dispose(); compensation?.Dispose(); bitmap.Dispose(); throw; }
    }

    private void FinishSliderRasterPending()
    {
        sliderRasterPending = null;
        sliderRasterCancellation?.Dispose(); sliderRasterCancellation = null;
    }

    private void CompleteSliderRaster()
    {
        if (sliderRasterPending is not { IsCompleted: true } pending) return;
        if (sliderRasterPendingDiscarded)
        {
            sliderRasterPending = null; sliderRasterPendingDiscarded = false; return;
        }
        SliderRasterPixels result;
        try { result = pending.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { FinishSliderRasterPending(); return; }
        catch (Exception exception) { FinishSliderRasterPending(); AppLog.Write(exception.ToString()); return; }
        var key = sliderRasterPendingKey;
        if (!sliderRasters.TryGetValue(key, out var previous)
            || result.Width == previous.Width && result.Height == previous.Height
            || (long)result.Width * result.Height > sliderRasterPixelBudget)
        {
            result.Dispose(); FinishSliderRasterPending(); return;
        }
        long transitionBytes = sliderRasterTransitions.Values.Sum(item => item.Previous.Bytes);
        if (sliderRasterTransitions.ContainsKey(key) || transitionBytes + previous.Bytes > sliderRasterTransitionLimit
            || !MakeSliderRasterRoom((long)result.Width * result.Height * 4, key))
        {
            // Shrink the active working set before an upgrade can occupy its transition reserve.
            if ((long)result.Width * result.Height > (long)previous.Width * previous.Height
                && HasOversizedActiveSliderRaster)
            {
                result.Dispose(); FinishSliderRasterPending();
            }
            return;
        }
        var replacement = UploadSliderRaster(result, key.Bounds);
        CrossFade? blend = null;
        try
        {
            blend = new(context!);
            blend.SetInputEffect(0, previous.Transform); blend.SetInputEffect(1, replacement.Transform);
            sliderRasterTransitions.Add(key, new(previous, blend, SliderRasterNow));
        }
        catch { blend?.Dispose(); replacement.Dispose(); throw; }
        replacement.Used = previous.Used; replacement.Frame = previous.Frame;
        sliderRasters[key] = replacement; sliderRasterBytes += replacement.Bytes; SliderRasterBuildCount++;
        result.Dispose(); FinishSliderRasterPending();
    }

    private static SliderRasterPixels CreateSliderPixels(SliderRasterKey key, int pixelBudget, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var (rasterWidth, rasterHeight, density) = SliderRasterSize(key.Bounds, pixelBudget);
        var points = key.Points; var bounds = key.Bounds;
        float diameter = key.Diameter; uint border = key.Border, accent = key.Accent;
        int count = checked(rasterWidth * rasterHeight);
        float[] distances = ArrayPool<float>.Shared.Rent(count);
        byte[] pixels = ArrayPool<byte>.Shared.Rent(checked(count * 4));
        bool transferred = false;
        try
        {
            Array.Fill(distances, float.PositiveInfinity, 0, count);
            RasterizeSliderCentreline(points, bounds, density, rasterWidth, rasterHeight, distances, cancellation);
            SliderDistanceTransform(distances, rasterWidth, rasterHeight, cancellation);
            float inverseDensity = (float)(1 / density), radius = diameter / 2, innerRadius = radius * .8125f, outerRadius = radius * .921875f;
            for (int i = 0; i < count; i++)
            {
                if ((i & 16383) == 0) cancellation.ThrowIfCancellationRequested();
                float distance = MathF.Sqrt(distances[i]) * inverseDensity;
                float coverage = Math.Clamp((outerRadius - distance) * (float)density + .5f, 0, 1);
                float borderWeight = Math.Clamp((distance - innerRadius) * (float)density + .5f, 0, 1);
                float bodyWeight = 1 - borderWeight;
                float alpha = (borderWeight + .7f * bodyWeight) * coverage;
                float progress = Math.Clamp(1 - distance / Math.Max(.001f, innerRadius), 0, 1);
                int offset = i * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    int shift = channel * 8;
                    float value = (accent >> shift) & 255;
                    float outer = value * .9f, inner = Math.Min(255, value * 1.125f + 63.75f);
                    float body = outer + (inner - outer) * progress;
                    float result = (((border >> shift) & 255) * borderWeight + body * .7f * bodyWeight) * coverage;
                    pixels[offset + channel] = (byte)Math.Clamp(MathF.Round(result), 0, 255);
                }
                pixels[offset + 3] = (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255);
            }
            transferred = true;
            return new(rasterWidth, rasterHeight, pixels);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(distances);
            if (!transferred) ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    private static void RasterizeSliderCentreline(IReadOnlyList<Vector2> points, Rect bounds, double density, int width, int height, float[] distances, CancellationToken cancellation)
    {
        var segments = new HashSet<ulong>();
        for (int i = 1; i < points.Count; i++)
        {
            if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            double x0 = (points[i - 1].X - bounds.X) * density - .5, y0 = (points[i - 1].Y - bounds.Y) * density - .5;
            double x1 = (points[i].X - bounds.X) * density - .5, y1 = (points[i].Y - bounds.Y) * density - .5;
            if (!ClipSliderSegment(ref x0, ref y0, ref x1, ref y1, width - 1, height - 1)) continue;
            int ax = (int)Math.Round(x0), ay = (int)Math.Round(y0), bx = (int)Math.Round(x1), by = (int)Math.Round(y1);
            uint first = (uint)(ay * width + ax), last = (uint)(by * width + bx);
            ulong key = (ulong)Math.Min(first, last) << 32 | Math.Max(first, last);
            if (segments.Contains(key)) continue;
            if (segments.Count < 32768) segments.Add(key);
            int dx = Math.Abs(bx - ax), sx = ax < bx ? 1 : -1, dy = -Math.Abs(by - ay), sy = ay < by ? 1 : -1, error = dx + dy;
            while (true)
            {
                distances[ay * width + ax] = 0;
                if (ax == bx && ay == by) break;
                int twice = 2 * error;
                if (twice >= dy) { error += dy; ax += sx; }
                if (twice <= dx) { error += dx; ay += sy; }
            }
        }
    }

    private static bool ClipSliderSegment(ref double x0, ref double y0, ref double x1, ref double y1, int maxX, int maxY)
    {
        int Code(double x, double y) => (x < 0 ? 1 : x > maxX ? 2 : 0) | (y < 0 ? 4 : y > maxY ? 8 : 0);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            int first = Code(x0, y0), last = Code(x1, y1);
            if ((first | last) == 0) return true;
            if ((first & last) != 0) return false;
            int outside = first != 0 ? first : last;
            double x, y;
            if ((outside & 8) != 0) { x = x0 + (x1 - x0) * (maxY - y0) / (y1 - y0); y = maxY; }
            else if ((outside & 4) != 0) { x = x0 + (x1 - x0) * -y0 / (y1 - y0); y = 0; }
            else if ((outside & 2) != 0) { y = y0 + (y1 - y0) * (maxX - x0) / (x1 - x0); x = maxX; }
            else { y = y0 + (y1 - y0) * -x0 / (x1 - x0); x = 0; }
            if (first != 0) { x0 = x; y0 = y; } else { x1 = x; y1 = y; }
        }
        return false;
    }

    private static void SliderDistanceTransform(float[] distances, int width, int height, CancellationToken cancellation)
    {
        for (int y = 0; y < height; y++)
        {
            if ((y & 31) == 0) cancellation.ThrowIfCancellationRequested();
            int row = y * width, nearest = -width;
            for (int x = 0; x < width; x++)
            {
                if (distances[row + x] == 0) nearest = x;
                else if (nearest >= 0) distances[row + x] = (x - nearest) * (x - nearest);
            }
            nearest = width * 2;
            for (int x = width - 1; x >= 0; x--)
            {
                if (distances[row + x] == 0) nearest = x;
                else if (nearest < width) distances[row + x] = Math.Min(distances[row + x], (x - nearest) * (x - nearest));
            }
        }
        float[] costs = ArrayPool<float>.Shared.Rent(height);
        int[] sites = ArrayPool<int>.Shared.Rent(height);
        double[] edges = ArrayPool<double>.Shared.Rent(height + 1);
        try
        {
            for (int x = 0; x < width; x++)
            {
                if ((x & 31) == 0) cancellation.ThrowIfCancellationRequested();
                for (int y = 0; y < height; y++) costs[y] = distances[y * width + x];
                int last = -1;
                for (int q = 0; q < height; q++)
                {
                    if (float.IsPositiveInfinity(costs[q])) continue;
                    double intersection = double.NegativeInfinity;
                    while (last >= 0)
                    {
                        int previous = sites[last];
                        intersection = (costs[q] + (double)q * q - costs[previous] - (double)previous * previous) / (2 * (q - previous));
                        if (intersection > edges[last]) break;
                        last--;
                    }
                    last++; sites[last] = q; edges[last] = last == 0 ? double.NegativeInfinity : intersection; edges[last + 1] = double.PositiveInfinity;
                }
                if (last < 0) continue;
                int site = 0;
                for (int y = 0; y < height; y++)
                {
                    while (edges[site + 1] < y) site++;
                    int delta = y - sites[site]; distances[y * width + x] = delta * delta + costs[sites[site]];
                }
            }
        }
        finally { ArrayPool<float>.Shared.Return(costs); ArrayPool<int>.Shared.Return(sites); ArrayPool<double>.Shared.Return(edges); }
    }

    private void CancelSliderRaster()
    {
        if (sliderRasterPending is not { } pending || sliderRasterPendingDiscarded) return;
        var cancellation = sliderRasterCancellation!;
        sliderRasterPendingDiscarded = true; sliderRasterCancellation = null;
        cancellation.Cancel();
        _ = pending.ContinueWith(completed =>
        {
            if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Dispose();
            else _ = completed.Exception;
            cancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void ClearSliderRasters()
    {
        CancelSliderRaster();
        sliderRasterPending = null; sliderRasterPendingKey = default; sliderRasterPendingDiscarded = false;
        foreach (var transition in sliderRasterTransitions.Values) transition.Dispose();
        sliderRasterTransitions.Clear();
        foreach (var raster in sliderRasters.Values) raster.Dispose();
        sliderRasters.Clear(); sliderRasterBytes = 0;
        sliderRasterExtents.Clear(); sliderRasterExtentOrder.Clear();
    }
}
