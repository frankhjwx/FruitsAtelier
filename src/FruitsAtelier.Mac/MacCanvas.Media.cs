using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FruitsAtelier.App.Media;
using R = FruitsAtelier.App.Rendering.Rect;

namespace FruitsAtelier.Mac;

internal sealed partial class ImageCache
{
    internal StoryboardImageCache StoryboardPixels { get; private set; } = new(DecodeStoryboard);
    private readonly Dictionary<(string Path, uint Tint), (WriteableBitmap Bitmap, long Used)> storyboardTextures = [];
    private readonly Dictionary<(string Path, uint Tint), MediaFileVersion> storyboardTextureVersions = [];
    private long storyboardBytes, storyboardClock;
    private readonly List<string> storyboardDecodePaths = [];
    internal void PrepareStoryboardImages(IReadOnlyList<string> paths)
    {
        storyboardDecodePaths.Clear();
        foreach (string path in paths)
            if (!storyboardTextures.ContainsKey((path, 0xFFFFFF))) storyboardDecodePaths.Add(path);
        StoryboardPixels.Prepare(storyboardDecodePaths);
    }
    private readonly HashSet<string> storyboardRetainedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Path, uint Tint)> storyboardPins = [];
    private long storyboardPinnedBytes;
    internal long StoryboardRevision;
    internal long MediaGeneration = MediaResourceIdentity.Next();
    internal string? StoryboardCapacityWarning;
    internal bool MediaNeedsRedraw => Video?.NeedsRedraw(VideoSerial) == true || StoryboardPixels.Loading || StoryboardPixels.Revision != StoryboardRevision;
    internal VideoPlayback? Video;
    internal WriteableBitmap? VideoBitmap;
    internal long VideoSerial;
    internal float VideoLastOpacity;
    internal bool VideoPresentationPending;
    private MediaFileVersion beatmapBackgroundVersion;
    internal void RetainStoryboardImages(IReadOnlyList<string> paths)
    {
        if (storyboardRetainedPaths.SetEquals(paths)) return;
        storyboardRetainedPaths.Clear(); storyboardRetainedPaths.UnionWith(paths);
        storyboardPins.Clear(); storyboardPinnedBytes = 0;
        foreach (var texture in storyboardTextures.OrderByDescending(c => c.Value.Used))
        {
            long size = (long)texture.Value.Bitmap.PixelSize.Width * texture.Value.Bitmap.PixelSize.Height * 4;
            if (!storyboardRetainedPaths.Contains(texture.Key.Path) || storyboardPinnedBytes + size > StoryboardImageCache.Limit) continue;
            storyboardPins.Add(texture.Key); storyboardPinnedBytes += size;
        }
        while (storyboardBytes > StoryboardImageCache.Limit && EvictStoryboardTexture()) { }
    }
    private bool EvictStoryboardTexture()
    {
        var candidates = storyboardTextures.Where(c => !storyboardPins.Contains(c.Key));
        if (!candidates.Any()) return false;
        var oldest = candidates.MinBy(c => c.Value.Used);
        storyboardBytes -= (long)oldest.Value.Bitmap.PixelSize.Width * oldest.Value.Bitmap.PixelSize.Height * 4;
        oldest.Value.Bitmap.Dispose(); storyboardTextures.Remove(oldest.Key); storyboardTextureVersions.Remove(oldest.Key); return true;
    }
    private static unsafe StoryboardImageCache.Pixels? DecodeStoryboard(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 64 * 1024 * 1024) return null;
        using var input = new Bitmap(path);
        if ((long)input.PixelSize.Width * input.PixelSize.Height > 16 * 1024 * 1024) return null;
        using var result = new WriteableBitmap(input.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = result.Lock(); input.CopyPixels(buffer, AlphaFormat.Premul);
        int width = input.PixelSize.Width, height = input.PixelSize.Height;
        byte[] data = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++) Marshal.Copy(buffer.Address + y * buffer.RowBytes, data, y * width * 4, width * 4);
        return new(width, height, data);
    }
    internal unsafe WriteableBitmap? StoryboardTexture(string path, uint tint)
    {
        var key = (path, tint);
        if (storyboardTextures.TryGetValue(key, out var entry))
        {
            storyboardTextures[key] = (entry.Bitmap, ++storyboardClock); return entry.Bitmap;
        }
        if (StoryboardPixels.Get(path) is not { } pixels) return null;
        while (storyboardBytes + pixels.Data.Length > StoryboardImageCache.Limit + storyboardPinnedBytes)
        {
            if (!EvictStoryboardTexture()) return null;
        }
        var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
            for (int y = 0; y < pixels.Height; y++)
            {
                byte* row = (byte*)buffer.Address + y * buffer.RowBytes;
                for (int x = 0; x < pixels.Width; x++)
                {
                    int source = (y * pixels.Width + x) * 4, dest = x * 4;
                    row[dest] = (byte)(pixels.Data[source] * (tint & 255) / 255);
                    row[dest + 1] = (byte)(pixels.Data[source + 1] * (tint >> 8 & 255) / 255);
                    row[dest + 2] = (byte)(pixels.Data[source + 2] * (tint >> 16 & 255) / 255);
                    row[dest + 3] = pixels.Data[source + 3];
                }
            }
        storyboardTextures[key] = (bitmap, ++storyboardClock); storyboardTextureVersions[key] = StoryboardPixels.Version(path);
        storyboardBytes += pixels.Data.Length; return bitmap;
    }
    internal void StopVideo() { Video?.Dispose(); Video = null; VideoBitmap?.Dispose(); VideoBitmap = null; VideoSerial = 0; VideoLastOpacity = 0; VideoPresentationPending = false; }
    internal void ReuseMediaResources()
    {
        StoryboardCapacityWarning = null;
        StoryboardPixels.ReuseResources();
        foreach (var key in storyboardTextures.Keys.ToArray())
        {
            if (storyboardTextureVersions[key] == MediaFileVersion.Read(key.Path)) continue;
            var texture = storyboardTextures[key];
            if (storyboardPins.Remove(key)) storyboardPinnedBytes -= (long)texture.Bitmap.PixelSize.Width * texture.Bitmap.PixelSize.Height * 4;
            storyboardRetainedPaths.Remove(key.Path);
            storyboardBytes -= (long)texture.Bitmap.PixelSize.Width * texture.Bitmap.PixelSize.Height * 4;
            texture.Bitmap.Dispose(); storyboardTextures.Remove(key); storyboardTextureVersions.Remove(key);
        }
        while (storyboardBytes > StoryboardImageCache.Limit && EvictStoryboardTexture()) { }
        if (Video is not null && Video.Version != MediaFileVersion.Read(Video.Path)) StopVideo();
        else Video?.Suspend();
        if (beatmapBackground is { } background)
        {
            var version = MediaFileVersion.Read(background.Key.Item1);
            if (version != beatmapBackgroundVersion)
            { background.Bitmap.Dispose(); beatmapBackground = null; }
        }
    }
    internal void ReleaseMedia()
    {
        MediaGeneration = MediaResourceIdentity.Next(); StoryboardCapacityWarning = null;
        StopVideo(); StoryboardPixels.Dispose(); StoryboardPixels = new(DecodeStoryboard);
        foreach (var texture in storyboardTextures.Values) texture.Bitmap.Dispose();
        storyboardTextures.Clear(); storyboardBytes = 0;
        storyboardTextureVersions.Clear();
        storyboardRetainedPaths.Clear(); storyboardPins.Clear(); storyboardPinnedBytes = 0;
    }
}

internal sealed partial class MacCanvas
{
    private bool videoFrameRequested;
    public long MediaGeneration => images.MediaGeneration;
    public string? MediaError => images.Video?.ReadError() is { } error ? $"{System.IO.Path.GetFileName(images.Video.Path)}: {error}" : images.StoryboardCapacityWarning ?? images.StoryboardPixels.Error;
    public void PrepareStoryboardImages(IReadOnlyList<string> paths) => images.PrepareStoryboardImages(paths);
    public void RetainStoryboardImages(IReadOnlyList<string> paths) => images.RetainStoryboardImages(paths);
    public bool StoryboardImagesReady(IReadOnlyList<string> paths)
    {
        if (images.StoryboardPixels.ExceedsCapacity(paths))
        {
            images.StoryboardCapacityWarning = "Visible storyboard images exceed the 256 MiB / 512 image cache; unavailable images are omitted.";
            return true;
        }
        bool ready = true;
        foreach (string path in paths)
            if (images.StoryboardTexture(path, 0xFFFFFF) is null && !images.StoryboardPixels.IsUnavailable(path)) ready = false;
        return ready;
    }
    public void BeginStoryboard() { images.StoryboardRevision = images.StoryboardPixels.Revision; }
    public void EndStoryboard() { }
    public void StoryboardSprite(string path, float x, float y, float scaleX, float scaleY, uint tint,
        float opacity, float rotationRadians, bool additive, bool flipX, bool flipY, float originX, float originY)
    {
        if (opacity <= 0 || images.StoryboardTexture(path, tint) is not { } bitmap) return;
        var matrix = System.Numerics.Matrix3x2.CreateTranslation(-bitmap.PixelSize.Width * originX, -bitmap.PixelSize.Height * originY)
            * System.Numerics.Matrix3x2.CreateScale(scaleX * (flipX ? -1 : 1), scaleY * (flipY ? -1 : 1))
            * System.Numerics.Matrix3x2.CreateRotation(rotationRadians) * System.Numerics.Matrix3x2.CreateTranslation(x, y);
        using var transform = context.PushTransform(new Matrix(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32));
        using var alpha = context.PushOpacity(opacity);
        using var blend = context.PushRenderOptions(new RenderOptions { BitmapBlendingMode = additive ? BitmapBlendingMode.Plus : BitmapBlendingMode.SourceOver });
        context.DrawImage(bitmap, new Avalonia.Rect(bitmap.Size), new Avalonia.Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height));
    }
    public bool PrepareVideoImage(string path, double positionMs, bool playing, double rate)
        => PrepareVideoFrame(path, positionMs, playing, rate, retainBitmap: true)
            || images.Video?.ContainsTime(positionMs) == false || images.Video?.Error is not null;

    private bool PrepareVideoFrame(string path, double positionMs, bool playing, double rate, bool retainBitmap)
    {
        if (images.Video?.Path != path)
        {
            if (retainBitmap) { images.Video?.Dispose(); images.Video = null; images.VideoSerial = 0; }
            else images.StopVideo();
            images.Video = new(path); videoFrameRequested = false; images.VideoPresentationPending = true;
        }
        if (!images.Video.ContainsTime(positionMs)) return false;
        bool ready = false;
        if (!videoFrameRequested) ready = images.Video.WithFrame(positionMs, playing, rate, frame =>
        {
            if (images.VideoBitmap is null || images.VideoBitmap.PixelSize != new PixelSize(frame.Width, frame.Height))
            {
                images.VideoBitmap?.Dispose(); images.VideoSerial = 0;
                images.VideoBitmap = new(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            if (images.VideoSerial != frame.Serial)
            {
                using var buffer = images.VideoBitmap.Lock();
                for (int y = 0; y < frame.Height; y++) Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes, frame.Width * 4);
                images.VideoSerial = frame.Serial;
            }
            images.VideoLastOpacity = images.Video!.Opacity(positionMs);
            images.VideoPresentationPending = false;
        });
        videoFrameRequested = true;
        return ready;
    }
    public bool VideoImage(string path, R destination, double positionMs, bool playing, double rate)
    {
        PrepareVideoFrame(path, positionMs, playing, rate, retainBitmap: false);
        return !images.VideoPresentationPending && images.Video?.ContainsTime(positionMs) == true && RetainedVideoImage(destination);
    }
    public bool RetainedVideoImage(R destination)
    {
        if (images.VideoBitmap is not { } bitmap) return false;
        int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
        float scale = Math.Max(destination.Width / width, destination.Height / height);
        float w = destination.Width / scale, h = destination.Height / scale;
        using var opacity = context.PushOpacity(images.VideoLastOpacity);
        context.DrawImage(bitmap, new Avalonia.Rect((width - w) / 2, (height - h) / 2, w, h), Convert(destination));
        return true;
    }
    public void StopVideo() => images.StopVideo();
    public void RetainVideo(string? path, bool preparing = false)
    {
        if (!preparing || path is not null && !string.Equals(images.Video?.Path, path, StringComparison.Ordinal)) images.StopVideo();
        else images.Video?.Suspend();
    }
    public void ReleaseStoryboardResources() => images.ReleaseMedia();
}
