using System.Numerics;
using FruitsAtelier.App.Media;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;

namespace FruitsAtelier.App.Rendering;

public sealed partial class D2DCanvas
{
    private StoryboardImageCache? storyboardPixels;
    private readonly Dictionary<string, MediaFileVersion> storyboardTextureVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ID2D1Bitmap1 Bitmap, int Width, int Height, long Used)> storyboardTextures = new(StringComparer.OrdinalIgnoreCase);
    private long storyboardTextureBytes, storyboardTextureClock;
    private readonly HashSet<string> storyboardPins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> storyboardRetainedPaths = new(StringComparer.OrdinalIgnoreCase);
    private long storyboardPinnedBytes;
    private int storyboardUploadBytes;
    private IReadOnlyList<string> storyboardPreparedPaths = Array.Empty<string>();
    private long storyboardRevision;
    private bool storyboardPendingUpload;
    private long mediaGeneration = MediaResourceIdentity.Next();
    public long MediaGeneration => mediaGeneration;
    private string? storyboardCapacityWarning;
    public bool MediaNeedsRedraw => video?.NeedsRedraw(videoSerial) == true || storyboardPendingUpload
        || (storyboardPixels is not null && (storyboardPixels.Loading || storyboardPixels.Revision != storyboardRevision));
    public string? MediaError => video?.ReadError() is { } error ? $"{Path.GetFileName(video.Path)}: {error}" : storyboardCapacityWarning ?? storyboardPixels?.Error;
    private ID2D1DeviceContext3? spriteContext;
    private ID2D1SpriteBatch? spriteBatch;
    private ID2D1Bitmap1? batchTexture;
    private bool batchAdditive;
    private int batchCount;
    private readonly RawRectF[] spriteDestinations = new RawRectF[2048];
    private readonly Color4[] spriteColors = new Color4[2048];
    private readonly Matrix3x2[] spriteTransforms = new Matrix3x2[2048];
    internal int StoryboardDrawCalls { get; private set; }
    internal int StoryboardSprites { get; private set; }
    internal long StoryboardTextureBytes => storyboardTextureBytes;
    internal int StoryboardDecodeCount => storyboardPixels?.DecodeCount ?? 0;
    internal int StoryboardUploadCount { get; private set; }
    private VideoPlayback? video;
    private ID2D1Bitmap1? videoBitmap;
    private long videoSerial;
    private int videoWidth, videoHeight;
    private float videoLastOpacity;
    private bool videoFrameRequested;
    private bool videoPresentationPending;
    private MediaFileVersion beatmapBackgroundVersion;

    public void PrepareStoryboardImages(IReadOnlyList<string> paths)
    {
        storyboardPixels ??= new(DecodeStoryboardImage);
        storyboardPreparedPaths = paths;
        storyboardPixels.Prepare(paths);
    }
    public bool StoryboardImagesReady(IReadOnlyList<string> paths)
    {
        if (storyboardPixels?.ExceedsCapacity(paths) == true)
        {
            storyboardCapacityWarning = "Visible storyboard images exceed the 256 MiB / 512 image cache; unavailable images are omitted.";
            return true;
        }
        bool ready = true;
        foreach (string path in paths)
            if (StoryboardTexture(path, out _, out _) is null && storyboardPixels?.IsUnavailable(path) != true) ready = false;
        return ready;
    }
    public void RetainStoryboardImages(IReadOnlyList<string> paths)
    {
        if (storyboardRetainedPaths.SetEquals(paths)) return;
        storyboardRetainedPaths.Clear(); storyboardRetainedPaths.UnionWith(paths);
        storyboardPins.Clear(); storyboardPinnedBytes = 0;
        foreach (string path in paths)
            if (!storyboardPins.Contains(path) && storyboardTextures.TryGetValue(path, out var texture)
                && storyboardPinnedBytes + (long)texture.Width * texture.Height * 4 <= StoryboardImageCache.Limit)
            {
                storyboardPins.Add(path); storyboardPinnedBytes += (long)texture.Width * texture.Height * 4;
            }
        while (storyboardTextureBytes > StoryboardImageCache.Limit && EvictStoryboardTexture()) { }
    }
    private bool EvictStoryboardTexture()
    {
        KeyValuePair<string, (ID2D1Bitmap1 Bitmap, int Width, int Height, long Used)> oldest = default;
        foreach (var pair in storyboardTextures)
            if (!storyboardPins.Contains(pair.Key) && (oldest.Key is null || pair.Value.Used < oldest.Value.Used)) oldest = pair;
        if (oldest.Key is null) return false;
        FlushStoryboard();
        oldest.Value.Bitmap.Dispose(); storyboardTextures.Remove(oldest.Key);
        storyboardTextureVersions.Remove(oldest.Key);
        storyboardTextureBytes -= (long)oldest.Value.Width * oldest.Value.Height * 4;
        return true;
    }
    private static StoryboardImageCache.Pixels? DecodeStoryboardImage(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 64 * 1024 * 1024) return null;
        using var factory = new IWICImagingFactory();
        using var decoder = factory.CreateDecoderFromFileName(path);
        using var frame = decoder.GetFrame(0);
        frame.GetSize(out uint w, out uint h);
        if (w == 0 || h == 0 || (long)w * h > 16 * 1024 * 1024) return null;
        using var converter = factory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA).CheckError();
        byte[] data = new byte[checked((int)(w * h * 4))];
        converter.CopyPixels(w * 4, data);
        return new((int)w, (int)h, data);
    }
    public void BeginStoryboard()
    {
        storyboardPendingUpload = false; storyboardRevision = storyboardPixels?.Revision ?? 0;
        spriteContext ??= context!.QueryInterface<ID2D1DeviceContext3>();
        spriteBatch ??= spriteContext.CreateSpriteBatch();
        foreach (string path in storyboardPreparedPaths)
        {
            if (storyboardUploadBytes >= 8 * 1024 * 1024) { storyboardPendingUpload = true; break; }
            StoryboardTexture(path, out _, out _, demand: false);
        }
    }
    public void EndStoryboard() => FlushStoryboard();
    private unsafe ID2D1Bitmap1? StoryboardTexture(string path, out int width, out int height, bool demand = true)
    {
        if (storyboardTextures.TryGetValue(path, out var entry))
        {
            storyboardTextures[path] = (entry.Bitmap, entry.Width, entry.Height, ++storyboardTextureClock);
            width = entry.Width; height = entry.Height; return entry.Bitmap;
        }
        width = height = 0;
        if (storyboardPixels?.Get(path, demand) is not { } pixels) return null;
        if (storyboardUploadBytes > 0 && storyboardUploadBytes + pixels.Data.Length > 8 * 1024 * 1024) { storyboardPendingUpload = true; return null; }
        while (storyboardTextureBytes + pixels.Data.Length > StoryboardImageCache.Limit + storyboardPinnedBytes)
        {
            if (!EvictStoryboardTexture()) return null;
        }
        ID2D1Bitmap1 bitmap;
        fixed (byte* data = pixels.Data)
            bitmap = context!.CreateBitmap(new SizeI(pixels.Width, pixels.Height), (nint)data, (uint)(pixels.Width * 4),
                new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
        width = pixels.Width; height = pixels.Height;
        storyboardTextures[path] = (bitmap, width, height, ++storyboardTextureClock);
        storyboardTextureVersions[path] = storyboardPixels.Version(path);
        StoryboardUploadCount++;
        storyboardTextureBytes += pixels.Data.Length; storyboardUploadBytes += pixels.Data.Length;
        return bitmap;
    }
    public void StoryboardSprite(string path, float x, float y, float scaleX, float scaleY, uint tint,
        float opacity, float rotationRadians, bool additive, bool flipX, bool flipY, float originX, float originY)
    {
        if (opacity <= 0 || scaleX == 0 || scaleY == 0) return;
        var texture = StoryboardTexture(path, out int width, out int height);
        if (texture is null) return;
        if (batchCount > 0 && (batchTexture != texture || batchAdditive != additive || batchCount == spriteDestinations.Length)) FlushStoryboard();
        batchTexture = texture; batchAdditive = additive;
        spriteDestinations[batchCount] = new(0, 0, width, height);
        // Direct2D sprite colours use straight alpha even with premultiplied bitmap pixels.
        spriteColors[batchCount] = new((tint >> 16 & 255) / 255f, (tint >> 8 & 255) / 255f, (tint & 255) / 255f, opacity);
        spriteTransforms[batchCount] = Matrix3x2.CreateTranslation(-width * originX, -height * originY)
            * Matrix3x2.CreateScale(scaleX * (flipX ? -1 : 1), scaleY * (flipY ? -1 : 1))
            * Matrix3x2.CreateRotation(rotationRadians) * Matrix3x2.CreateTranslation(x, y);
        batchCount++; StoryboardSprites++;
    }
    private void FlushStoryboard()
    {
        if (batchCount == 0) return;
        var blend = context!.PrimitiveBlend; var antialias = context.AntialiasMode;
        try
        {
            context.PrimitiveBlend = batchAdditive ? PrimitiveBlend.Add : PrimitiveBlend.SourceOver;
            context.AntialiasMode = AntialiasMode.Aliased;
            spriteBatch!.Clear();
            spriteBatch.AddSprites((uint)batchCount, spriteDestinations, null!, spriteColors, spriteTransforms, 16, 0, 16, 24);
            spriteContext!.DrawSpriteBatch(spriteBatch, 0, (uint)batchCount, batchTexture!, BitmapInterpolationMode.Linear, SpriteOptions.ClampToSourceRectangle);
            StoryboardDrawCalls++;
        }
        finally { batchCount = 0; batchTexture = null; context.PrimitiveBlend = blend; context.AntialiasMode = antialias; }
    }
    public unsafe bool VideoImage(string path, Rect destination, double positionMs, bool playing, double rate)
    {
        if (video is null || !string.Equals(video.Path, path, StringComparison.OrdinalIgnoreCase)) { StopVideo(); video = new(path); }
        if (!video.ContainsTime(positionMs)) return false;
        bool ready = false;
        if (!videoFrameRequested) ready = video.WithFrame(positionMs, playing, rate, frame =>
        {
            if (videoBitmap is null || videoWidth != frame.Width || videoHeight != frame.Height)
            {
                videoBitmap?.Dispose(); videoWidth = frame.Width; videoHeight = frame.Height; videoSerial = 0;
                videoBitmap = context!.CreateBitmap(new SizeI(videoWidth, videoHeight), 0, 0,
                    new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96));
            }
            if (videoSerial != frame.Serial)
            {
                fixed (byte* data = frame.Pixels) videoBitmap.CopyFromMemory((nint)data, (uint)(frame.Width * 4)).CheckError();
                videoSerial = frame.Serial;
            }
            videoLastOpacity = video!.Opacity(positionMs);
            videoPresentationPending = false;
        });
        videoFrameRequested = true;
        if (videoPresentationPending || videoBitmap is null || !video.ContainsTime(positionMs)) return false;
        float scale = Math.Max(destination.Width / videoWidth, destination.Height / videoHeight);
        float w = destination.Width / scale, h = destination.Height / scale;
        context!.DrawBitmap(videoBitmap, Convert(destination), videoLastOpacity, BitmapInterpolationMode.Linear,
            new Vortice.Mathematics.Rect((videoWidth - w) / 2, (videoHeight - h) / 2, w, h));
        return ready || videoSerial != 0;
    }
    public void StopVideo() { video?.Dispose(); video = null; videoBitmap?.Dispose(); videoBitmap = null; videoSerial = 0; videoLastOpacity = 0; videoFrameRequested = videoPresentationPending = false; }
    public void RetainVideo(string? path, bool preparing = false)
    {
        if (!preparing || path is not null && !string.Equals(video?.Path, path, StringComparison.OrdinalIgnoreCase)) StopVideo();
        else video?.Suspend();
    }
    private void ReuseMediaResources()
    {
        mediaGeneration = MediaResourceIdentity.Next(); storyboardCapacityWarning = null;
        FlushStoryboard();
        storyboardPixels?.ReuseResources();
        foreach (string path in storyboardTextures.Keys.ToArray())
        {
            if (storyboardTextureVersions[path] == MediaFileVersion.Read(path)) continue;
            var texture = storyboardTextures[path]; texture.Bitmap.Dispose();
            storyboardTextureBytes -= (long)texture.Width * texture.Height * 4;
            storyboardTextures.Remove(path); storyboardTextureVersions.Remove(path);
        }
        storyboardPins.Clear(); storyboardRetainedPaths.Clear(); storyboardPinnedBytes = 0;
        storyboardPreparedPaths = Array.Empty<string>(); storyboardPendingUpload = false;
        while (storyboardTextureBytes > StoryboardImageCache.Limit && EvictStoryboardTexture()) { }
        if (video is not null && video.Version != MediaFileVersion.Read(video.Path)) StopVideo();
        else video?.Suspend();
        videoPresentationPending = video is not null;
        if (beatmapBackground is { } background)
        {
            var version = MediaFileVersion.Read(background.Key.Path);
            if (version != beatmapBackgroundVersion)
            { background.Image.Bitmap.Dispose(); beatmapBackground = null; }
        }
    }
    public void ReleaseStoryboardResources() => ReleaseMedia();
    private void ReleaseMedia()
    {
        mediaGeneration = MediaResourceIdentity.Next(); storyboardCapacityWarning = null;
        StopVideo(); batchCount = 0; batchTexture = null;
        storyboardPixels?.Dispose(); storyboardPixels = null;
        foreach (var value in storyboardTextures.Values) value.Bitmap.Dispose();
        storyboardTextures.Clear(); storyboardTextureBytes = 0;
        storyboardTextureVersions.Clear();
        storyboardPins.Clear(); storyboardRetainedPaths.Clear(); storyboardPinnedBytes = 0;
        storyboardPreparedPaths = Array.Empty<string>(); storyboardPendingUpload = false; storyboardRevision = 0;
        spriteBatch?.Dispose(); spriteBatch = null; spriteContext?.Dispose(); spriteContext = null;
    }
}
