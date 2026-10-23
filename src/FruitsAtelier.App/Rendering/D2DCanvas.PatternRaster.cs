using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace FruitsAtelier.App.Rendering;

public sealed partial class D2DCanvas
{
    private const long patternRasterLimit = 16L * 1024 * 1024;
    private readonly record struct PatternRasterKey(object Identity, float Width, float Height, float Dpi);
    private sealed record PatternRaster(ID2D1Bitmap1 Bitmap, int Width, int Height)
    {
        public long Used { get; set; }
        public long Bytes => (long)Width * Height * 4;
    }
    private readonly Dictionary<PatternRasterKey, PatternRaster> patternRasters = [];
    private long patternRasterBytes, patternRasterClock;
    internal int PatternRasterBuildCount { get; private set; }
    internal long CachedPatternRasterBytes => patternRasterBytes;

    public void CachedPreview(object key, Rect bounds, Action<ICanvas, Rect> draw)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var cacheKey = new PatternRasterKey(key, bounds.Width, bounds.Height, dpi);
        if (!patternRasters.TryGetValue(cacheKey, out var cached))
        {
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * dpi / 96));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * dpi / 96));
            long bytes = (long)pixelWidth * pixelHeight * 4;
            if (bytes > patternRasterLimit) { draw(this, bounds); return; }
            while (patternRasters.Count > 0 && (patternRasterBytes + bytes > patternRasterLimit || patternRasters.Count >= 128))
            {
                var oldest = patternRasters.MinBy(pair => pair.Value.Used);
                patternRasterBytes -= oldest.Value.Bytes; oldest.Value.Bitmap.Dispose(); patternRasters.Remove(oldest.Key);
            }
            // A separate context keeps the window's active drawing and clip stack intact.
            using var recording = drawingDevice!.CreateDeviceContext(DeviceContextOptions.None);
            var bitmap = recording.CreateBitmap(new SizeI(pixelWidth, pixelHeight), nint.Zero, 0,
                new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                    dpi, dpi, BitmapOptions.Target));
            var windowContext = context;
            int windowClipDepth = clipDepth;
            try
            {
                recording.Target = bitmap; recording.SetDpi(dpi, dpi);
                recording.BeginDraw(); recording.Clear(new Color4(0, 0, 0, 0));
                context = recording; clipDepth = 0;
                try { draw(this, new(0, 0, bounds.Width, bounds.Height)); }
                finally
                {
                    while (clipDepth > 0) Unclip();
                    context = windowContext; clipDepth = windowClipDepth;
                }
                recording.EndDraw().CheckError(); recording.Target = null;
                cached = new(bitmap, pixelWidth, pixelHeight);
                patternRasters.Add(cacheKey, cached); patternRasterBytes += bytes; PatternRasterBuildCount++;
            }
            catch
            {
                context = windowContext; clipDepth = windowClipDepth;
                recording.Target = null; bitmap.Dispose(); throw;
            }
        }
        cached.Used = ++patternRasterClock;
        context!.DrawBitmap(cached.Bitmap, Convert(bounds), 1, BitmapInterpolationMode.Linear,
            new Vortice.Mathematics.Rect(0, 0, cached.Width * 96 / dpi, cached.Height * 96 / dpi));
    }

    private void ClearPatternRasters()
    {
        foreach (var raster in patternRasters.Values) raster.Bitmap.Dispose();
        patternRasters.Clear(); patternRasterBytes = 0;
    }
}
