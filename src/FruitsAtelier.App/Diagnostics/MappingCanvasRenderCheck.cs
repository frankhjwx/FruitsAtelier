using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class MappingCanvasRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int milliseconds) => ticks += milliseconds;
    }

    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/mapping-canvas-native");
        Directory.CreateDirectory(folder);
        string image = Path.Combine(folder, "background.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), image, true);
        try
        {
            bool overlaysOnly = Environment.GetCommandLineArgs().Contains("--canvas-overlay-render-check");
            if (!overlaysOnly)
            {
                CheckBackgroundPreviewToggle(canvas, folder);
                CheckBackgroundDimEdges(canvas, folder);
            }
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620), (760, 580) })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var clock = new Clock();
                var view = new EditorView(timeProvider: clock);
                view.Document.SourcePath = Path.Combine(folder, "map.osu");
                view.LibrarySettings.MappingCanvasBackgroundDim = 40;
                view.LibrarySettings.CanvasGridColour = 0x66DDFF;
                view.LibrarySettings.CanvasCentreLineColour = 0xFFE088;
                view.LibrarySettings.CanvasGridOpacity = 65;
                view.DropLibraryFiles([image]);
                Paint("canvas");
                view.SetModifiers(false, true); Paint("grid-snap"); view.SetModifiers(false, false);
                int decodes = canvas.ImageDecodeCount;
                var dimBounds = view.CanvasBackgroundDimBounds;
                var plot = view.CanvasPlotBounds;
                view.Wheel(plot.X + 10, plot.Y + 10, -240, false, false, true);
                for (int i = 0; i < 20; i++) Paint();
                if (canvas.ImageDecodeCount != decodes)
                    throw new InvalidOperationException("Zoom repeatedly decoded the mapping background.");
                Paint("zoom");
                if (view.CanvasBackgroundDimBounds != dimBounds) throw new InvalidOperationException("Zoom moved the native dim control.");
                var original = view.Document.DeepClone();
                view.PointerMove(dimBounds.X + 10, dimBounds.Y + 10, false, false);
                clock.Advance(75); Paint("dim-hover-half");
                clock.Advance(75); Paint("dim-hover");
                var dimSlider = view.CanvasBackgroundDimSliderBounds;
                view.PointerDown(dimSlider.X + dimSlider.Width * .75f, dimSlider.Y + 12, 0, false, false);
                view.PointerUp(dimSlider.X + dimSlider.Width * .75f, dimSlider.Y + 12, 0); Paint("dim-slider");
                if (view.LibrarySettings.MappingCanvasBackgroundDim != 75 || !original.ContentEquals(view.Document))
                    throw new InvalidOperationException("Native dim drag changed content or did not apply its value.");
                view.PointerMove(plot.X + 10, plot.Y + 10, false, false);
                clock.Advance(999); Paint("dim-delay");
                if (view.CanvasBackgroundDimOpacity != 1) throw new InvalidOperationException("Native dim control faded before one second.");
                clock.Advance(76); Paint("dim-fade");
                clock.Advance(75); Paint("dim-hidden");
                if (view.CanvasBackgroundDimOpacity != 0 || view.CanvasBackgroundDimNeedsRedraw)
                    throw new InvalidOperationException("Native dim control did not finish hiding.");
                var panel = view.CanvasControlsPanelBounds;
                view.PointerMove(panel.X + 8, panel.Y + panel.Height / 2, false, false);
                clock.Advance(150); Paint("panel-compact");
                view.PointerDown(panel.X + 8, panel.Y + panel.Height / 2, 0, false, false);
                view.PointerMove(panel.X + 48, panel.Y + panel.Height / 2 - 80, false, false);
                view.PointerUp(panel.X + 48, panel.Y + panel.Height / 2 - 80, 0); Paint("panel-moved");
                panel = view.CanvasControlsPanelBounds;
                view.PointerDown(panel.Right, panel.Bottom, 0, false, false);
                view.PointerMove(panel.X, panel.Y, false, false);
                view.PointerUp(panel.X, panel.Y, 0); Paint("panel-half");
                if (view.LibrarySettings.CanvasControlsScale != .5f || view.WantsCapture || !original.ContentEquals(view.Document))
                    throw new InvalidOperationException("Native panel resize did not retain 50% scale or changed content.");
                var halfSlider = view.CanvasBackgroundDimSliderBounds;
                Click(halfSlider.X + halfSlider.Width / 2, halfSlider.Y + halfSlider.Height / 2);
                if (view.LibrarySettings.MappingCanvasBackgroundDim != 50)
                    throw new InvalidOperationException("Native half-size slider hit region was not scaled.");
                var media = view.CanvasMediaToggleBounds;
                bool enabled = view.LibrarySettings.EnableVideoStoryboard;
                Click(media.X + media.Width / 2, media.Y + media.Height / 2);
                if (view.LibrarySettings.EnableVideoStoryboard == enabled)
                    throw new InvalidOperationException("Native half-size media toggle did not respond.");
                Paint("panel-half-controls");
                view.ShowDiscussionPanel(); Paint();
                panel = view.CanvasControlsPanelBounds;
                var annotations = view.CanvasDiscussionToggleBounds;
                if (annotations.Y != panel.Y || view.CanvasBackgroundDimBounds.Y != annotations.Bottom
                    || view.CanvasMediaToggleBounds.Bottom != panel.Bottom)
                    throw new InvalidOperationException("Discussion toggle was not grouped with the media controls.");
                view.PointerMove(panel.X + 8, panel.Y + panel.Height / 2, false, false);
                clock.Advance(150); Paint("panel-half-discussion");
                bool showAnnotations = view.LibrarySettings.ShowCanvasDiscussionAnnotations;
                Click(annotations.X + annotations.Width / 2, annotations.Y + annotations.Height / 2);
                if (view.LibrarySettings.ShowCanvasDiscussionAnnotations == showAnnotations || !original.ContentEquals(view.Document))
                    throw new InvalidOperationException("Native half-size annotation toggle failed or edited content.");
                if (size.Item1 >= 980)
                {
                    var bookmarks = view.BookmarkToolbarBounds;
                    view.PointerMove(bookmarks.X + 20, bookmarks.Bottom + 15, false, false);
                    clock.Advance(499); Paint("bookmark-pending");
                    clock.Advance(1);
                    if (!view.BookmarkToolbarNeedsRedraw) throw new InvalidOperationException("Native bookmark hover missed its deadline.");
                    Paint("bookmark-visible");
                    var handle = view.BookmarkToolbarGrip;
                    view.PointerDown(handle.X + 7, handle.Y + 17, 0, false, false);
                    view.PointerMove(handle.X + 37, handle.Y - 63, false, false);
                    view.PointerUp(handle.X + 37, handle.Y - 63, 0);
                    view.PointerMove(handle.X + 37, handle.Y + 17, false, false); Paint("bookmark-moved");
                    if (view.BookmarkToolbarBounds.X != bookmarks.X + 30 || view.BookmarkToolbarBounds.Y != bookmarks.Y
                        || !original.ContentEquals(view.Document))
                        throw new InvalidOperationException("Native bookmark drag failed or edited content.");
                }
                if (overlaysOnly) continue;
                view.OpenSettings(); Paint();
                var settings = view.SettingsBounds;
                Click(settings.X + 40, settings.Y + 78 + 5 * 48 + 16);
                Paint("settings");
                Click(settings.X + 230 + 180, settings.Y + 144);
                Paint("settings-dim");
                view.Wheel(settings.X + 300, settings.Y + 200, -1200, false); Paint("grid-settings");
                float scroll = Math.Max(0, 424 - (settings.Height - 208));
                Click(settings.X + 230 + 100, settings.Y + 448 - scroll + 16);
                Paint("grid-colour");
                view.KeyDown(27, false, false); Paint();
                view.KeyDown(27, false, false); Paint();
                view.DropLibraryFiles([image]); Paint("replace");
                if (!view.DiscardConfirmationVisible) throw new InvalidOperationException("Native replacement did not open confirmation.");
                view.KeyDown(27, false, false);

                void Click(float x, float y)
                { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint(); }
                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}x{size.Item2}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Mapping canvas native backgrounds, zoom cache, settings and replacement checks passed.");
    }

    private static void CheckBackgroundPreviewToggle(D2DCanvas canvas, string folder)
    {
        foreach (var imageSize in new[] { (1902, 1199), (1901, 1188), (1199, 1902), (1188, 1901) })
        {
            string image = Path.Combine(folder, $"crop-{imageSize.Item1}x{imageSize.Item2}.bmp");
            WriteBitmap(image, imageSize.Item1, imageSize.Item2,
                Enumerable.Repeat((byte)255, imageSize.Item1 * imageSize.Item2 * 4).ToArray());
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            {
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var document = new MapDocument { SourcePath = Path.Combine(folder, "crop.osu") };
                OsuTimeline.SetBackgroundFilename(document, Path.GetFileName(image));
                var view = new EditorView(loadDemo: false);
                view.LoadDocument(document);
                view.LibrarySettings.MappingCanvasBackgroundDim = 0;
                int decodes = 0;
                for (int frame = 0; frame < 4; frame++)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: () =>
                    {
                        var pixels = canvas.CapturePixels();
                        int offset = ((200 * dpi / 96) * pixels.Width + 26 * dpi / 96) * 4;
                        if (frame < 2 && dpi == 96 && size.Item1 == 1440)
                            WriteBitmap(Path.Combine(folder, $"preview-crop-{imageSize.Item1}x{imageSize.Item2}-{frame}.bmp"),
                                pixels.Width, pixels.Height, pixels.Pixels);
                        if (pixels.Pixels[offset] != 255 || pixels.Pixels[offset + 1] != 255 || pixels.Pixels[offset + 2] != 255)
                            throw new InvalidOperationException($"Background disappeared: {imageSize}, {size}, {dpi} DPI, preview={view.CatchPreviewVisible}.");
                    });
                    if (frame == 0) decodes = canvas.ImageDecodeCount;
                    else if (canvas.ImageDecodeCount != decodes)
                        throw new InvalidOperationException("Preview toggling decoded the background again.");
                    var toggle = view.PreviewToggleBounds;
                    view.PointerDown(toggle.X + 10, toggle.Y + 10, 0, false, false);
                    view.PointerUp(toggle.X + 10, toggle.Y + 10, 0);
                    if (view.CatchPreviewVisible != (frame % 2 == 0))
                        throw new InvalidOperationException("Background regression did not toggle the preview.");
                }
                if (view.IsDirty || !document.ContentEquals(view.Document) || view.LibrarySettings.MappingCanvasBackgroundDim != 0)
                    throw new InvalidOperationException("Preview toggling changed background preferences or map content.");
            }
        }
    }

    private static void CheckBackgroundDimEdges(D2DCanvas canvas, string folder)
    {
        string image = Path.Combine(folder, "dim-white.bmp");
        WriteBitmap(image, 32, 32, Enumerable.Repeat((byte)255, 32 * 32 * 4).ToArray());
        foreach (int dpi in new[] { 96, 120, 144, 168, 192 })
        foreach (float fraction in new[] { .25f, .5f, .75f })
        {
            canvas.Resize(320, 240, dpi);
            var bounds = new Rect(20 + fraction, 20 + fraction, 100, 80);
            canvas.Begin();
            canvas.Fill(new(0, 0, 320, 240), 0);
            if (!canvas.BackgroundImage(image, bounds)) throw new InvalidOperationException("Dim fixture did not load.");
            canvas.DimBackground(bounds, 0, .9f);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                WriteBitmap(Path.Combine(folder, $"dim-edge-{dpi}-{fraction}.bmp"), frame.Width, frame.Height, frame.Pixels);
                int left = (int)Math.Floor(bounds.X * dpi / 96), right = (int)Math.Ceiling(bounds.Right * dpi / 96);
                int top = (int)Math.Floor(bounds.Y * dpi / 96), bottom = (int)Math.Ceiling(bounds.Bottom * dpi / 96);
                for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int offset = (y * frame.Width + x) * 4;
                    if (frame.Pixels[offset] > 27 || frame.Pixels[offset + 1] > 27 || frame.Pixels[offset + 2] > 27)
                        throw new InvalidOperationException($"Background dim leaked at ({x}, {y}), {dpi} DPI, fraction={fraction}.");
                }
            });
        }
    }

    private static void WriteBitmap(string path, int width, int height, byte[] pixels)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(-height);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(pixels.Length);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(pixels);
    }
}
