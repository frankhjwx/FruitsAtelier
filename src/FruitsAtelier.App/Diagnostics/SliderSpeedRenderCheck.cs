using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class SliderSpeedRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/slider-speed-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (760, 580) })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var map = new MapDocument { DurationMs = 5000, SliderMultiplier = 1 };
                var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
                track.Nodes.Add(new() { TimeMs = 100, X = 100 });
                track.Nodes.Add(new() { TimeMs = 300, X = 200 });
                map.Tracks.Add(track);
                var view = new EditorView(false); view.LoadDocument(map); view.CloseLibrary();
                Paint();
                float x = view.PlayfieldBounds.X + 100 * view.PlayfieldBounds.Width / 512;
                float y = view.CanvasPlotBounds.Bottom - (float)((100 - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Paint();
                if (view.SliderSpeedReadout is not { Sv: > 1, MaxDs: > 1 } || view.SliderSpeedReadoutBounds is not { } bounds
                    || bounds.Y < view.CanvasPlotBounds.Y || bounds.Bottom > view.CanvasPlotBounds.Bottom)
                    throw new InvalidOperationException("Native slider speed readout is missing or outside the canvas.");
                if (!map.ContentEquals(view.Document)) throw new InvalidOperationException("Native speed display changed the map.");
                Paint($"selected-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                view.LoadDocument(new MapDocument { DurationMs = 5000, SliderMultiplier = 1 }); Paint();
                view.SetSliderEditingMode(SliderEditingMode.PenTool); view.KeyDown(66, false, false); Paint();
                Click(375, 100); Click(500, 200);
                if (view.SliderSpeedReadout is not { Sv: > 1, MaxDs: > 1 })
                    throw new InvalidOperationException("Native draft speed readout is missing.");
                Paint($"draft-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                view.CancelInteraction();

                void Click(double time, double position)
                {
                    float px = view.PlayfieldBounds.X + (float)(position * view.PlayfieldBounds.Width / 512);
                    float py = view.CanvasPlotBounds.Bottom - (float)((time - view.ViewStartMs) * view.PixelsPerMs);
                    view.PointerDown(px, py, 0, false, true); view.PointerUp(px, py, 0); Paint();
                }
                void Paint(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, name + ".bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
        AppLog.Write("Slider speed readouts passed selected and draft checks at all sizes, languages and DPI values.");
    }
}
