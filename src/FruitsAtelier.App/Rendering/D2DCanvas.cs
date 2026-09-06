using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using DRect = Vortice.Mathematics.Rect;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace FruitsAtelier.App.Rendering;

public sealed class D2DCanvas : ICanvas, IDisposable
{
    private ID3D11Device? device;
    private ID3D11DeviceContext? immediateContext;
    private IDXGISwapChain1? swapChain;
    private ID2D1Factory1? factory;
    private ID2D1Device? drawingDevice;
    private ID2D1DeviceContext? context;
    private ID2D1Bitmap1? target;
    private IDWriteFactory? textFactory;
    private IWICImagingFactory? imagingFactory;
    private readonly Dictionary<uint, ID2D1SolidColorBrush> brushes = [];
    private readonly Dictionary<(float, bool), IDWriteTextFormat> formats = [];
    private readonly Dictionary<ImageKey, CachedImage> images = [];
    private readonly HashSet<ImageKey> failedImages = [];
    private readonly Dictionary<string, (long Version, long Length)> imageVersions = new(StringComparer.OrdinalIgnoreCase);
    private long imageBytes;
    private const long imageCacheLimit = 64 * 1024 * 1024;
    private readonly record struct ImageKey(string Path, uint Tint, long Version, long Length);
    private sealed record CachedImage(ID2D1Bitmap1 Bitmap, int Width, int Height);
    private int width, height, clipDepth;
    private float dpi;
    public string AdapterName { get; private set; } = "";
    public int LoadedImageCount => images.Count;

    public D2DCanvas(nint hwnd, int width, int height, float dpi)
    {
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [Vortice.Direct3D.FeatureLevel.Level_11_0], out device, out immediateContext).CheckError();
            using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            AdapterName = adapter.Description.Description;
            using var dxgiFactory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
            swapChain = dxgiFactory.CreateSwapChainForHwnd(device, hwnd, new SwapChainDescription1
            {
                Width = (uint)width, Height = (uint)height, Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Ignore
            });
            dxgiFactory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
            factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
            drawingDevice = factory.CreateDevice(dxgiDevice);
            context = drawingDevice.CreateDeviceContext(DeviceContextOptions.None);
            textFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
            Resize(width, height, dpi);
        }
        catch { Dispose(); throw; }
    }

    public void Resize(int newWidth, int newHeight, float newDpi)
    {
        if (newWidth <= 0 || newHeight <= 0) return;
        if (width == newWidth && height == newHeight && dpi == newDpi) return;
        // Back-buffer references must be released before DXGI can resize it.
        context!.Target = null;
        target?.Dispose(); target = null;
        swapChain!.ResizeBuffers(2, (uint)newWidth, (uint)newHeight, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        width = newWidth; height = newHeight; dpi = newDpi;
        using var surface = swapChain.GetBuffer<IDXGISurface>(0);
        target = context.CreateBitmapFromDxgiSurface(surface,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore),
                dpi, dpi, BitmapOptions.Target | BitmapOptions.CannotDraw));
        context.Target = target;
        context.SetDpi(dpi, dpi);
        context.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
    }

    public void Begin()
    {
        context!.BeginDraw();
        context.Clear(new Color4(0.07f, 0.085f, 0.11f, 1));
        clipDepth = 0;
        imageVersions.Clear();
    }

    public void End()
    {
        while (clipDepth > 0) Unclip();
        context!.EndDraw().CheckError();
        swapChain!.Present(1, PresentFlags.None).CheckError();
    }

    private ID2D1SolidColorBrush Brush(uint color, float opacity = 1)
    {
        if (!brushes.TryGetValue(color, out var brush))
        {
            brush = context!.CreateSolidColorBrush(new Color4(((color >> 16) & 255) / 255f,
                ((color >> 8) & 255) / 255f, (color & 255) / 255f, 1));
            brushes.Add(color, brush);
        }
        brush.Opacity = opacity;
        return brush;
    }

    private static DRect Convert(Rect r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
    public void Fill(Rect r, uint color, float radius = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (radius > 0) context!.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(r.X, r.Y, r.Width, r.Height), radius, radius), Brush(color));
        else context!.FillRectangle(Convert(r), Brush(color));
    }
    public void Stroke(Rect r, uint color, float width = 1, float radius = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        if (radius > 0) context!.DrawRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(r.X, r.Y, r.Width, r.Height), radius, radius), Brush(color), width);
        else context!.DrawRectangle(Convert(r), Brush(color), width);
    }
    public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1)
        => context!.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), Brush(color, opacity), width);
    public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1)
    {
        var ellipse = new Ellipse(new Vector2(x, y), radius, radius);
        if (filled) context!.FillEllipse(ellipse, Brush(color));
        else context!.DrawEllipse(ellipse, Brush(color), width);
    }
    public float MeasureText(string text, float size, bool bold = false)
    {
        using var format = textFactory!.CreateTextFormat("Segoe UI", null, bold ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle.Normal, FontStretch.Normal, size, "zh-CN");
        format.WordWrapping = WordWrapping.NoWrap;
        using var layout = textFactory.CreateTextLayout(text, format, 10000, size * 2);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }
    public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false)
    {
        if (maxWidth <= 0 || string.IsNullOrEmpty(text)) return;
        if (!formats.TryGetValue((size, bold), out var format))
        {
            format = textFactory!.CreateTextFormat("Segoe UI", null, bold ? FontWeight.SemiBold : FontWeight.Normal,
                FontStyle.Normal, FontStretch.Normal, size, "zh-CN");
            format.WordWrapping = WordWrapping.NoWrap;
            formats.Add((size, bold), format);
        }
        context!.DrawText(text, format, new DRect(x, y, maxWidth, size * 1.8f), Brush(color), DrawTextOptions.Clip);
    }
    public void Clip(Rect r) { context!.PushAxisAlignedClip(Convert(r), AntialiasMode.PerPrimitive); clipDepth++; }
    public void Unclip() { if (clipDepth > 0) { context!.PopAxisAlignedClip(); clipDepth--; } }

    public bool Image(string filePath, Rect destination, uint tint = 0xFFFFFF, Rect? source = null)
    {
        if (!ValidRectangle(destination)) return false;
        ImageKey key = default;
        try
        {
            if (!imageVersions.TryGetValue(filePath, out var version))
            {
                var file = new FileInfo(filePath);
                version = file.Exists ? (file.LastWriteTimeUtc.Ticks, file.Length) : (0, 0);
                imageVersions[filePath] = version;
            }
            if (version.Length is < 24 or > 32 * 1024 * 1024) return false;
            key = new(filePath, tint & 0xFFFFFF, version.Version, version.Length);
            if (failedImages.Contains(key)) return false;
            if (!images.TryGetValue(key, out var image))
            {
                image = LoadImage(filePath, key.Tint);
                long bytes = (long)image.Width * image.Height * 4;
                if (imageBytes + bytes > imageCacheLimit || images.Count >= 128) ClearImages();
                images.Add(key, image);
                imageBytes += bytes;
            }
            var region = source ?? new Rect(0, 0, image.Width, image.Height);
            if (!ValidRectangle(region) || region.X < 0 || region.Y < 0 || region.Right > image.Width || region.Bottom > image.Height) return false;
            context!.DrawBitmap(image.Bitmap, Convert(destination), 1, Vortice.Direct2D1.BitmapInterpolationMode.Linear, Convert(region));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or OverflowException or SharpGen.Runtime.SharpGenException)
        {
            if (failedImages.Count >= 256) failedImages.Clear();
            if (failedImages.Add(key)) AppLog.Write($"Skin image unavailable: {filePath}: {ex.Message}");
            return false;
        }
    }

    private CachedImage LoadImage(string path, uint tint)
    {
        imagingFactory ??= new IWICImagingFactory();
        using var decoder = imagingFactory.CreateDecoderFromFileName(path, FileAccess.Read, DecodeOptions.CacheOnLoad);
        using var frame = decoder.GetFrame(0);
        frame.GetSize(out uint imageWidth, out uint imageHeight);
        if (imageWidth is < 1 or > 4096 || imageHeight is < 1 or > 4096) throw new ArgumentException("PNG dimensions exceed the supported 4096 pixel limit.");
        using var converter = imagingFactory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA).CheckError();
        var properties = new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.None);
        if (tint == 0xFFFFFF)
            return new(context!.CreateBitmapFromWicBitmap(converter, properties), (int)imageWidth, (int)imageHeight);

        uint stride = checked(imageWidth * 4);
        var pixels = new byte[checked((int)(stride * imageHeight))];
        converter.CopyPixels(stride, pixels);
        uint red = (tint >> 16) & 255, green = (tint >> 8) & 255, blue = tint & 255;
        // Multiply premultiplied colour channels without changing alpha, so tinted edges remain transparent.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)((pixels[i] * blue + 127) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * green + 127) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * red + 127) / 255);
        }
        using var tinted = imagingFactory.CreateBitmapFromMemory(imageWidth, imageHeight, Vortice.WIC.PixelFormat.Format32bppPBGRA, pixels, stride);
        return new(context!.CreateBitmapFromWicBitmap(tinted, properties), (int)imageWidth, (int)imageHeight);
    }

    private static bool ValidRectangle(Rect rect) => float.IsFinite(rect.X) && float.IsFinite(rect.Y)
        && float.IsFinite(rect.Width) && float.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0;

    private void ClearImages()
    {
        foreach (var image in images.Values) image.Bitmap.Dispose();
        images.Clear();
        imageBytes = 0;
    }

    public void Dispose()
    {
        ClearImages();
        imagingFactory?.Dispose(); imagingFactory = null;
        foreach (var format in formats.Values) format.Dispose(); formats.Clear();
        foreach (var brush in brushes.Values) brush.Dispose(); brushes.Clear();
        if (context is not null) context.Target = null;
        target?.Dispose(); target = null;
        context?.Dispose(); context = null;
        drawingDevice?.Dispose(); drawingDevice = null;
        factory?.Dispose(); factory = null;
        textFactory?.Dispose(); textFactory = null;
        swapChain?.Dispose(); swapChain = null;
        immediateContext?.Dispose(); immediateContext = null;
        device?.Dispose(); device = null;
    }
}
