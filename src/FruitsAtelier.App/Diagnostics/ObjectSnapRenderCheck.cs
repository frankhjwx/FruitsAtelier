using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class ObjectSnapRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/object-snap-native");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var map = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
                map.Fruits.Add(new() { TimeMs = 100, X = 256.25 });
                var view = new EditorView(false); view.LoadDocument(map); view.CloseLibrary(); Paint();
                view.KeyDown(70, false, false); Paint();
                float px = view.PlayfieldBounds.X + 258 * view.PlayfieldBounds.Width / 512;
                float py = view.CanvasPlotBounds.Bottom - (float)((250 - view.ViewStartMs) * view.PixelsPerMs);
                view.PointerMove(px, py, false, false); Paint($"guide-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                if (view.ObjectSnapTargetPoint is not { X: 256.25 })
                    throw new InvalidOperationException("Native placement did not find the object snap target.");
                view.PointerDown(px, py, 0, false, false); view.PointerUp(px, py, 0); Paint();
                if (!view.Document.Fruits.Any(f => Math.Abs(f.TimeMs - 250) < .01 && f.X == 256.25))
                    throw new InvalidOperationException("Native object snap changed time or failed to align X.");
                view.KeyDown(90, true, false); Paint();
                if (!view.Document.ContentEquals(map)) throw new InvalidOperationException("Native snapped placement failed undo.");
                view.PointerMove(0, 0, false, false); view.OpenDistanceSnapDialog(); Paint();
                if (view.ObjectSnapSettings != (true, 2, 1) || view.ObjectSnapLaterPlusBounds.Bottom > size.Item2)
                    throw new InvalidOperationException("Native object snap defaults or dialog bounds are invalid.");
                Paint($"configure-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                var plus = view.ObjectSnapLaterPlusBounds;
                view.PointerDown(plus.X + 12, plus.Y + 12, 0, false, false);
                view.PointerUp(plus.X + 12, plus.Y + 12, 0); Paint();
                view.KeyDown(27, false, false); Paint();
                if (view.ObjectSnapSettings != (true, 2, 1) || !view.Document.ContentEquals(map))
                    throw new InvalidOperationException("Native configuration cancellation changed state.");
                view.NewProject();

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
        AppLog.Write("Object snap placement, undo and configuration passed native rendering checks.");
    }
}
