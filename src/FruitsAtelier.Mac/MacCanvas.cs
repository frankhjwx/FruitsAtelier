using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FruitsAtelier.App.Rendering;
using R = FruitsAtelier.App.Rendering.Rect;

namespace FruitsAtelier.Mac;

internal sealed partial class ImageCache : IDisposable
{
    private readonly Dictionary<(string, uint, long, long), (Bitmap Bitmap, long Used)> images = [];
    private readonly string bookmarkToolbarPath = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "bookmarks", "toolbar-panel.png");
    private ((string, uint, long, long) Key, Bitmap Bitmap)? bookmarkToolbar;
    private ((string, uint, long, long) Key, Bitmap Bitmap)? beatmapBackground;
    private long clock;
    private long bytes;
    private readonly Dictionary<IReadOnlyList<System.Numerics.Vector2>, StreamGeometry> paths = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<IReadOnlyList<System.Numerics.Vector2>> pathOrder = new();
    private readonly Dictionary<(IReadOnlyList<System.Numerics.Vector2>, float), Geometry> sliderBorders = [];
    private readonly Queue<(IReadOnlyList<System.Numerics.Vector2>, float)> sliderBorderOrder = new();
    internal Geometry CachedSliderBorder(IReadOnlyList<System.Numerics.Vector2> points, float diameter)
    {
        var key = (points, diameter);
        if (sliderBorders.TryGetValue(key, out var cached)) return cached;
        if (sliderBorders.Count >= 128) sliderBorders.Remove(sliderBorderOrder.Dequeue());
        var path = CachedPolyline(points);
        var outer = path.GetWidenedGeometry(new Pen(Brushes.White, diameter * .921875f, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
        var inner = path.GetWidenedGeometry(new Pen(Brushes.White, diameter * .8125f, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round));
        var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        sliderBorders.Add(key, ring); sliderBorderOrder.Enqueue(key); return ring;
    }
    internal StreamGeometry CachedPolyline(IReadOnlyList<System.Numerics.Vector2> points)
    {
        if (paths.TryGetValue(points, out var existing)) return existing;
        if (paths.Count >= 128) paths.Remove(pathOrder.Dequeue());
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(points[0].X, points[0].Y), false);
            for (int i = 1; i < points.Count; i++) sink.LineTo(new Point(points[i].X, points[i].Y));
            sink.EndFigure(false);
        }
        paths.Add(points, geometry); pathOrder.Enqueue(points); return geometry;
    }
    private static readonly FruitsAtelier.Core.LibraryFileCache thumbnailCache = new("thumbnails-mac", "png-192x152-1");
    private readonly ThumbnailCache<Bitmap> thumbnails = new(path =>
    {
        var fingerprint = thumbnailCache.Fingerprint(path);
        if (thumbnailCache.Read(path) is { } cached)
        {
            try { using var input = new MemoryStream(cached); return new Bitmap(input); }
            catch (Exception) { /* A damaged thumbnail is regenerated from its source. */ }
        }
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 24 or > 32 * 1024 * 1024) return null;
        using var bitmap = new Bitmap(path);
        double scale = Math.Min(1, Math.Min(192d / bitmap.PixelSize.Width, 152d / bitmap.PixelSize.Height));
        var result = bitmap.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(bitmap.PixelSize.Width * scale)), Math.Max(1, (int)(bitmap.PixelSize.Height * scale))));
        using var output = new MemoryStream();
        result.Save(output); thumbnailCache.Write(path, fingerprint, output.ToArray());
        return result;
    });
    public Bitmap? Thumbnail(string path) => thumbnails.Get(path);
    public unsafe Bitmap? Get(string path, uint tint, bool background = false)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 32 * 1024 * 1024) return null;
        var key = (path, tint, file.LastWriteTimeUtc.Ticks, file.Length);
        bool isToolbar = path == bookmarkToolbarPath;
        if (background && beatmapBackground is { } bg && bg.Key == key) return bg.Bitmap;
        if (isToolbar && bookmarkToolbar is { } toolbar && toolbar.Key == key) return toolbar.Bitmap;
        if (!background && images.TryGetValue(key, out var existing)) { images[key] = (existing.Bitmap, ++clock); return existing.Bitmap; }
        using var decoded = new Bitmap(path);
        long size = (long)decoded.PixelSize.Width * decoded.PixelSize.Height * 4;
        if (size > 64 * 1024 * 1024) return null;
        while (!isToolbar && !background && images.Count > 0 && (bytes + size > 64 * 1024 * 1024 || images.Count >= 256))
        {
            var oldest = images.MinBy(entry => entry.Value.Used);
            images.Remove(oldest.Key);
            bytes -= (long)oldest.Value.Bitmap.PixelSize.Width * oldest.Value.Bitmap.PixelSize.Height * 4;
            oldest.Value.Bitmap.Dispose();
        }
        var result = new WriteableBitmap(decoded.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using (var buffer = result.Lock())
            {
                decoded.CopyPixels(buffer, AlphaFormat.Premul);
                for (int y = 0; y < buffer.Size.Height; y++)
                {
                    byte* row = (byte*)buffer.Address + y * buffer.RowBytes;
                    for (int x = 0; x < buffer.Size.Width; x++)
                    {
                        row[x * 4] = (byte)(row[x * 4] * (tint & 255) / 255);
                        row[x * 4 + 1] = (byte)(row[x * 4 + 1] * ((tint >> 8) & 255) / 255);
                        row[x * 4 + 2] = (byte)(row[x * 4 + 2] * ((tint >> 16) & 255) / 255);
                    }
                }
            }
            if (background)
            {
                beatmapBackground?.Bitmap.Dispose(); beatmapBackground = (key, result);
                beatmapBackgroundVersion = FruitsAtelier.App.Media.MediaFileVersion.Read(path);
            }
            else if (isToolbar) { bookmarkToolbar?.Bitmap.Dispose(); bookmarkToolbar = (key, result); }
            else { images.Add(key, (result, ++clock)); bytes += size; }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private void ClearImages()
    {
        beatmapBackground?.Bitmap.Dispose(); beatmapBackground = null;
        bookmarkToolbar?.Bitmap.Dispose(); bookmarkToolbar = null;
        foreach (var image in images.Values) image.Bitmap.Dispose(); images.Clear(); bytes = 0;
    }
    internal void ReleaseDifficultyResources()
    {
        ReleaseMedia();
        beatmapBackground?.Bitmap.Dispose(); beatmapBackground = null;
        ReleaseDifficultyGeometry();
    }
    internal void ReleaseDifficultyResources(bool preserveMedia)
    {
        if (!preserveMedia) { ReleaseDifficultyResources(); return; }
        ReleaseDifficultyGeometry(); ReuseMediaResources();
    }
    private void ReleaseDifficultyGeometry()
    {
        ClearPatternRasters();
        paths.Clear(); pathOrder.Clear(); sliderBorders.Clear(); sliderBorderOrder.Clear();
    }
    public void Dispose() { ReleaseMedia(); StoryboardPixels.Dispose(); ClearPatternRasters(); ClearImages(); thumbnails.Dispose(); paths.Clear(); pathOrder.Clear(); sliderBorders.Clear(); sliderBorderOrder.Clear(); }
}

internal sealed partial class MacCanvas(DrawingContext context, ImageCache images, double renderScale = 1) : ICanvas, IDisposable
{
    public void ReleaseDifficultyResources() => images.ReleaseDifficultyResources();
    public void ReleaseDifficultyResources(bool preserveMedia) => images.ReleaseDifficultyResources(preserveMedia);
    private readonly Stack<DrawingContext.PushedState> clips = [];
    private static Avalonia.Rect Convert(R r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
    private static SolidColorBrush Brush(uint c) => new(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c));
    public void Fill(R r, uint color, float radius = 0, float opacity = 1)
    {
        using var state = context.PushOpacity(opacity);
        context.DrawRectangle(Brush(color), null, Convert(r), radius, radius);
    }
    public void Stroke(R r, uint color, float width = 1, float radius = 0)
        => StrokeOpacity(r, color, width, radius, 1);
    public void StrokeOpacity(R r, uint color, float width, float radius, float opacity)
    {
        using var state = context.PushOpacity(opacity);
        context.DrawRectangle(null, new Pen(Brush(color), width), Convert(r), radius, radius);
    }
    public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1)
    {
        using var state = context.PushOpacity(opacity);
        context.DrawLine(new Pen(Brush(color), width), new Point(x1, y1), new Point(x2, y2));
    }
    public void Polyline(IReadOnlyList<System.Numerics.Vector2> points, float x, float y, float scale, uint color, float width, float opacity)
    {
        if (points.Count < 2 || scale <= 0 || opacity <= 0) return;
        using var transform = context.PushTransform(new Matrix(scale, 0, 0, scale, x, y));
        using var state = context.PushOpacity(opacity);
        context.DrawGeometry(null, new Pen(Brush(color), width / scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), images.CachedPolyline(points));
    }
    public void SliderBody(IReadOnlyList<System.Numerics.Vector2> points, float x, float y, float scale,
        float diameter, uint border, uint accent, float opacity)
    {
        if (points.Count < 2 || diameter <= 0 || scale <= 0 || opacity <= 0) return;
        using var transform = context.PushTransform(new Matrix(scale, 0, 0, scale, x, y));
        using (context.PushOpacity(opacity)) context.DrawGeometry(Brush(border), null, images.CachedSliderBorder(points, diameter));
        using var alpha = context.PushOpacity(opacity * .7f);
        var geometry = images.CachedPolyline(points);
        for (int i = 0; i < SliderTrackGradient.Steps; i++)
            context.DrawGeometry(null, new Pen(Brush(SliderTrackGradient.Colour(accent, i)), diameter * .8125f * (1 - i / (float)SliderTrackGradient.Steps),
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
    public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1)
    {
        using var state = context.PushOpacity(opacity);
        context.DrawEllipse(filled ? Brush(color) : null, filled ? null : new Pen(Brush(color), width), new Point(x, y), radius, radius);
    }
    public float MeasureText(string text, float size, bool bold = false)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Arial, PingFang SC", FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, Brush(0xFFFFFF));
        return (float)formatted.WidthIncludingTrailingWhitespace;
    }
    public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false)
        => TextOpacity(text, x, y, size, color, maxWidth, bold, 1);
    public float MeasureStyledText(string text, float size, bool bold, bool italic)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Arial, PingFang SC", italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, Brush(0xFFFFFF));
        return (float)formatted.WidthIncludingTrailingWhitespace;
    }
    public void StyledText(string text, float x, float y, float size, uint color, float maxWidth, bool bold, bool italic)
    {
        if (maxWidth <= 0 || text.Length == 0) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Arial, PingFang SC", italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, Brush(color));
        using var clip = context.PushClip(new Avalonia.Rect(x, y, maxWidth, size * 1.8));
        context.DrawText(formatted, new Point(x, y));
    }
    public void TextOpacity(string text, float x, float y, float size, uint color, float maxWidth, bool bold, float opacity)
    {
        if (maxWidth <= 0 || text.Length == 0) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Arial, PingFang SC", FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, Brush(color));
        using var state = context.PushOpacity(opacity);
        using var clip = context.PushClip(new Avalonia.Rect(x, y, maxWidth, size * 1.8));
        context.DrawText(formatted, new Point(x, y));
    }
    public bool Image(string filePath, R destination, uint tint = 0xFFFFFF, R? source = null, float opacity = 1)
        => DrawImage(filePath, destination, tint, source, opacity, false);

    public bool BackgroundImage(string filePath, R destination)
        => DrawImage(filePath, destination, 0xFFFFFF, null, 1, true);

    private bool DrawImage(string filePath, R destination, uint tint, R? source, float opacity, bool fill)
    {
        try
        {
            var bitmap = images.Get(filePath, tint, fill);
            if (bitmap is null) return false;
            if (fill)
            {
                double scale = Math.Max(destination.Width / bitmap.Size.Width, destination.Height / bitmap.Size.Height);
                float w = (float)(destination.Width / scale), h = (float)(destination.Height / scale);
                source = new((float)(bitmap.Size.Width - w) / 2, (float)(bitmap.Size.Height - h) / 2, w, h);
            }
            using var state = context.PushOpacity(opacity);
            context.DrawImage(bitmap, source is R s ? Convert(s) : new Avalonia.Rect(bitmap.Size), Convert(destination));
            return true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { return false; }
    }
    public bool CatcherImage(string filePath, R destination, uint tint, float opacity, bool additive, bool flipHorizontal)
    {
        using var transform = context.PushTransform(flipHorizontal
            ? new Matrix(-1, 0, 0, 1, destination.X * 2 + destination.Width, 0) : Matrix.Identity);
        return additive ? AdditiveImage(filePath, destination, tint, opacity) : Image(filePath, destination, tint, opacity: opacity);
    }
    public bool SpriteImage(string filePath, R destination, uint tint, R source, float opacity, float rotation, bool additive)
    {
        double angle = rotation * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        double x = destination.X + destination.Width / 2, y = destination.Y + destination.Height / 2;
        using var transform = context.PushTransform(new Matrix(c, s, -s, c, x - x * c + y * s, y - x * s - y * c));
        using var blend = context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = additive
            ? Avalonia.Media.Imaging.BitmapBlendingMode.Plus : Avalonia.Media.Imaging.BitmapBlendingMode.SourceOver });
        return Image(filePath, destination, tint, source, opacity);
    }
    public bool AdditiveImage(string filePath, R destination, uint tint, float opacity)
    {
        using var state = context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = Avalonia.Media.Imaging.BitmapBlendingMode.Plus });
        return Image(filePath, destination, tint, opacity: opacity);
    }
    public bool Thumbnail(string filePath, R destination)
    {
        if (images.Thumbnail(filePath) is not { } bitmap) return false;
        double scale = Math.Min(destination.Width / bitmap.Size.Width, destination.Height / bitmap.Size.Height);
        var fitted = new Avalonia.Rect(destination.X + (destination.Width - bitmap.Size.Width * scale) / 2, destination.Y + (destination.Height - bitmap.Size.Height * scale) / 2, bitmap.Size.Width * scale, bitmap.Size.Height * scale);
        context.DrawImage(bitmap, new Avalonia.Rect(bitmap.Size), fitted);
        return true;
    }
    public void Clip(R r) => clips.Push(context.PushClip(Convert(r)));
    public void Unclip() { if (clips.TryPop(out var clip)) clip.Dispose(); }
    public void Dispose() { while (clips.Count > 0) Unclip(); }
}
