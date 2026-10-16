using System.Buffers;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace FruitsAtelier.App.Rendering;

public sealed partial class D2DCanvas
{
    private const long sliderRasterLimit = 32L * 1024 * 1024;
    private const int sliderRasterMaximumPixels = 2 * 1024 * 1024;
    // Render snapshots are immutable; pan, zoom, and fade remain outside the cached pixels.
    private readonly record struct SliderRasterKey(IReadOnlyList<Vector2> Points, float Diameter, uint Border, uint Accent, Rect Bounds);
    private sealed record SliderRaster(ID2D1Bitmap1 Bitmap, int Width, int Height, bool Preview = false)
    {
        public long Used;
        public long Bytes => (long)Width * Height * 4;
    }
    private readonly Dictionary<SliderRasterKey, SliderRaster> sliderRasters = [];
    private readonly Dictionary<IReadOnlyList<Vector2>, Rect> sliderRasterExtents = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<IReadOnlyList<Vector2>> sliderRasterExtentOrder = new();
    private long sliderRasterBytes, sliderRasterClock;
    internal long CachedSliderRasterBytes => sliderRasterBytes;
    internal int SliderRasterBuildCount { get; private set; }
    private int sliderRasterPixelBudget = sliderRasterMaximumPixels;

    private bool sliderRasterDeferred;
    private int sliderRasterResizesRemaining;
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

    public void PrepareSliderBodies(int denseBodyCount, bool deferred = false)
    {
        // Active bodies must coexist: evicting another visible body would rebuild it every frame.
        int slots = 4;
        while (slots < denseBodyCount && slots < 1 << 20) slots *= 2;
        sliderRasterPixelBudget = (int)Math.Max(1, sliderRasterLimit / (4 * slots));
        sliderRasterDeferred = deferred;
        sliderRasterResizesRemaining = 1;
        CompleteSliderRaster();
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
        // Very long paths retain only a generously padded, quantized view window.
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
        if (sliderRasters.TryGetValue(key, out var oversized)
            && ((long)oversized.Width * oversized.Height > sliderRasterPixelBudget || !sliderRasterDeferred && oversized.Preview)
            && (!sliderRasterDeferred || sliderRasterResizesRemaining > 0))
        {
            // Spread working-set changes over frames instead of rebuilding every visible body at once.
            sliderRasterResizesRemaining--;
            sliderRasterBytes -= oversized.Bytes; oversized.Bitmap.Dispose(); sliderRasters.Remove(key);
        }
        if (!sliderRasters.TryGetValue(key, out var raster))
        {
            int budget = sliderRasterDeferred ? Math.Min(32768, sliderRasterPixelBudget) : sliderRasterPixelBudget;
            using var pixels = CreateSliderPixels(key, budget, CancellationToken.None);
            var desired = SliderRasterSize(bounds, sliderRasterPixelBudget);
            raster = UploadSliderRaster(pixels, preview: pixels.Width < desired.Width || pixels.Height < desired.Height);
            while (sliderRasters.Count > 0 && (sliderRasterBytes + raster.Bytes > sliderRasterLimit || sliderRasters.Count >= 128))
            {
                var oldest = sliderRasters.MinBy(pair => pair.Value.Used);
                sliderRasterBytes -= oldest.Value.Bytes; oldest.Value.Bitmap.Dispose(); sliderRasters.Remove(oldest.Key);
            }
            sliderRasters.Add(key, raster); sliderRasterBytes += raster.Bytes; SliderRasterBuildCount++;
        }
        if (sliderRasterDeferred && sliderRasterPending is null)
        {
            var desired = SliderRasterSize(bounds, sliderRasterPixelBudget);
            if (desired.Width > raster.Width || desired.Height > raster.Height)
            {
                sliderRasterPendingKey = key;
                var cancellation = new CancellationTokenSource();
                sliderRasterCancellation = cancellation;
                int budget = sliderRasterPixelBudget;
                sliderRasterPending = Task.Run(() => CreateSliderPixels(key, budget, cancellation.Token));
            }
        }
        raster.Used = ++sliderRasterClock;
        context!.DrawBitmap(raster.Bitmap, Convert(new Rect(x + bounds.X * scale, y + bounds.Y * scale,
            bounds.Width * scale, bounds.Height * scale)), opacity, Vortice.Direct2D1.BitmapInterpolationMode.Linear,
            new Vortice.Mathematics.Rect(0, 0, raster.Width, raster.Height));
        return true;
    }

    private static (int Width, int Height, double Density) SliderRasterSize(Rect bounds, int pixelBudget)
    {
        double density = Math.Min(4, Math.Min(2048d / Math.Max(bounds.Width, bounds.Height),
            Math.Sqrt(pixelBudget / ((double)bounds.Width * bounds.Height))));
        return (Math.Max(1, (int)Math.Floor(bounds.Width * density)),
            Math.Max(1, (int)Math.Floor(bounds.Height * density)), density);
    }

    private unsafe SliderRaster UploadSliderRaster(SliderRasterPixels pixels, bool preview = false)
    {
        var properties = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None);
        fixed (byte* source = pixels.Pixels)
            return new(context!.CreateBitmap(new SizeI(pixels.Width, pixels.Height), (nint)source,
                (uint)(pixels.Width * 4), properties), pixels.Width, pixels.Height, preview);
    }

    private void CompleteSliderRaster()
    {
        if (sliderRasterPending is not { IsCompleted: true } pending) return;
        var key = sliderRasterPendingKey;
        sliderRasterPending = null;
        if (sliderRasterPendingDiscarded) { sliderRasterPendingDiscarded = false; return; }
        sliderRasterCancellation!.Dispose(); sliderRasterCancellation = null;
        SliderRasterPixels result;
        try { result = pending.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        catch (Exception exception) { AppLog.Write(exception.ToString()); return; }
        using (result)
        {
            if (!sliderRasters.TryGetValue(key, out var previous)
                || (long)result.Width * result.Height > sliderRasterPixelBudget
                || result.Width <= previous.Width && result.Height <= previous.Height) return;
            var replacement = UploadSliderRaster(result);
            sliderRasterBytes -= previous.Bytes; previous.Bitmap.Dispose(); sliderRasters.Remove(key);
            while (sliderRasters.Count > 0 && sliderRasterBytes + replacement.Bytes > sliderRasterLimit)
            {
                var oldest = sliderRasters.MinBy(pair => pair.Value.Used);
                sliderRasterBytes -= oldest.Value.Bytes; oldest.Value.Bitmap.Dispose(); sliderRasters.Remove(oldest.Key);
            }
            replacement.Used = previous.Used;
            sliderRasters.Add(key, replacement); sliderRasterBytes += replacement.Bytes; SliderRasterBuildCount++;
        }
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

    private void ClearSliderRasters()
    {
        if (sliderRasterPending is { } pending && !sliderRasterPendingDiscarded)
        {
            // Keep the cancelled task registered until it stops, so Clear cannot overlap workers.
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
        foreach (var raster in sliderRasters.Values) raster.Bitmap.Dispose();
        sliderRasters.Clear(); sliderRasterBytes = 0;
        sliderRasterExtents.Clear(); sliderRasterExtentOrder.Clear();
    }
}
