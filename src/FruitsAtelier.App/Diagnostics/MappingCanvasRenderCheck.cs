using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class MappingCanvasRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/mapping-canvas-native");
        Directory.CreateDirectory(folder);
        string image = Path.Combine(folder, "background.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), image, true);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620), (760, 580) })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var view = new EditorView();
                view.Document.SourcePath = Path.Combine(folder, "map.osu");
                view.LibrarySettings.MappingCanvasBackgroundDim = 40;
                view.LibrarySettings.CanvasGridColour = 0x66DDFF;
                view.LibrarySettings.CanvasCentreLineColour = 0xFFE088;
                view.LibrarySettings.CanvasGridOpacity = 65;
                view.DropLibraryFiles([image]);
                Paint("canvas");
                int decodes = canvas.ImageDecodeCount;
                var plot = view.CanvasPlotBounds;
                view.Wheel(plot.X + 10, plot.Y + 10, -240, false, false, true);
                for (int i = 0; i < 20; i++) Paint();
                if (canvas.ImageDecodeCount != decodes)
                    throw new InvalidOperationException("Zoom repeatedly decoded the mapping background.");
                Paint("zoom");
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
}
