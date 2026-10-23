using Avalonia;
using Avalonia.Media.Imaging;
using FruitsAtelier.App.Rendering;
using R = FruitsAtelier.App.Rendering.Rect;

namespace FruitsAtelier.Mac;

internal sealed partial class ImageCache
{
    private const long patternRasterLimit = 16L * 1024 * 1024;
    private readonly record struct PatternRasterKey(object Identity, float Width, float Height, double Scale);
    private sealed record PatternRaster(RenderTargetBitmap Bitmap, long Bytes)
    {
        public long Used { get; set; }
    }
    private readonly Dictionary<PatternRasterKey, PatternRaster> patternRasters = [];
    private long patternRasterBytes, patternRasterClock;
    internal long CachedPatternRasterBytes => patternRasterBytes;
    internal int PatternRasterBuildCount { get; private set; }

    internal RenderTargetBitmap? PatternPreview(object key, R bounds, double renderScale, Action<ICanvas, R> draw)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var cacheKey = new PatternRasterKey(key, bounds.Width, bounds.Height, renderScale);
        if (!patternRasters.TryGetValue(cacheKey, out var cached))
        {
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * renderScale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * renderScale));
            long bytes = (long)pixelWidth * pixelHeight * 4;
            if (bytes > patternRasterLimit) return null;
            while (patternRasters.Count > 0 && (patternRasterBytes + bytes > patternRasterLimit || patternRasters.Count >= 128))
            {
                var oldest = patternRasters.MinBy(pair => pair.Value.Used);
                patternRasterBytes -= oldest.Value.Bytes; oldest.Value.Bitmap.Dispose(); patternRasters.Remove(oldest.Key);
            }
            var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96 * renderScale, 96 * renderScale));
            try
            {
                using (var recording = bitmap.CreateDrawingContext())
                using (var canvas = new MacCanvas(recording, this, renderScale))
                    draw(canvas, new(0, 0, bounds.Width, bounds.Height));
                cached = new(bitmap, bytes);
                patternRasters.Add(cacheKey, cached); patternRasterBytes += bytes; PatternRasterBuildCount++;
            }
            catch { bitmap.Dispose(); throw; }
        }
        cached.Used = ++patternRasterClock;
        return cached.Bitmap;
    }

    private void ClearPatternRasters()
    {
        foreach (var raster in patternRasters.Values) raster.Bitmap.Dispose();
        patternRasters.Clear(); patternRasterBytes = 0;
    }
}

internal sealed partial class MacCanvas
{
    public void CachedPreview(object key, R bounds, Action<ICanvas, R> draw)
    {
        if (images.PatternPreview(key, bounds, renderScale, draw) is { } bitmap)
            context.DrawImage(bitmap, new Avalonia.Rect(bitmap.Size), Convert(bounds));
        else draw(this, bounds);
    }
}
