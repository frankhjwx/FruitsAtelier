using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FruitsAtelier.App.Rendering;
using R = FruitsAtelier.App.Rendering.Rect;

namespace FruitsAtelier.Mac;

internal sealed class ImageCache : IDisposable
{
    private readonly Dictionary<(string, uint, long, long), Bitmap> images = [];
    private long bytes;
    private readonly ThumbnailCache<Bitmap> thumbnails = new(path =>
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 24 or > 32 * 1024 * 1024) return null;
        using var bitmap = new Bitmap(path);
        double scale = Math.Min(1, Math.Min(192d / bitmap.PixelSize.Width, 152d / bitmap.PixelSize.Height));
        return bitmap.CreateScaledBitmap(new PixelSize(Math.Max(1, (int)(bitmap.PixelSize.Width * scale)), Math.Max(1, (int)(bitmap.PixelSize.Height * scale))));
    });
    public Bitmap? Thumbnail(string path) => thumbnails.Get(path);
    public unsafe Bitmap? Get(string path, uint tint)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 32 * 1024 * 1024) return null;
        var key = (path, tint, file.LastWriteTimeUtc.Ticks, file.Length);
        if (images.TryGetValue(key, out var existing)) return existing;
        using var decoded = new Bitmap(path);
        long size = (long)decoded.PixelSize.Width * decoded.PixelSize.Height * 4;
        if (size > 64 * 1024 * 1024) return null;
        if (bytes + size > 64 * 1024 * 1024 || images.Count >= 256) ClearImages();
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
            images.Add(key, result); bytes += size;
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private void ClearImages() { foreach (var image in images.Values) image.Dispose(); images.Clear(); bytes = 0; }
    public void Dispose() { ClearImages(); thumbnails.Dispose(); }
}

internal sealed class MacCanvas(DrawingContext context, ImageCache images) : ICanvas, IDisposable
{
    private readonly Stack<DrawingContext.PushedState> clips = [];
    private static Avalonia.Rect Convert(R r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
    private static SolidColorBrush Brush(uint c) => new(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c));
    public void Fill(R r, uint color, float radius = 0, float opacity = 1)
    {
        using var state = context.PushOpacity(opacity);
        context.DrawRectangle(Brush(color), null, Convert(r), radius, radius);
    }
    public void Stroke(R r, uint color, float width = 1, float radius = 0) => context.DrawRectangle(null, new Pen(Brush(color), width), Convert(r), radius, radius);
    public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1)
    {
        using var state = context.PushOpacity(opacity);
        context.DrawLine(new Pen(Brush(color), width), new Point(x1, y1), new Point(x2, y2));
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
    {
        if (maxWidth <= 0 || text.Length == 0) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Arial, PingFang SC", FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, Brush(color));
        using var clip = context.PushClip(new Avalonia.Rect(x, y, maxWidth, size * 1.8));
        context.DrawText(formatted, new Point(x, y));
    }
    public bool Image(string filePath, R destination, uint tint = 0xFFFFFF, R? source = null, float opacity = 1)
    {
        try
        {
            var bitmap = images.Get(filePath, tint);
            if (bitmap is null) return false;
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
