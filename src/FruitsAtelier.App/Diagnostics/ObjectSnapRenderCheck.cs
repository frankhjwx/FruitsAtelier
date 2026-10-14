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
                var group = new MapDocument { DurationMs = 8000, BeatLengthMs = 500 };
                var first = new Fruit { TimeMs = 100, X = 100 };
                var last = new Fruit { TimeMs = 250, X = 300 };
                group.Fruits.AddRange([first, last, new() { TimeMs = 0, X = 385.4 }]);
                view.LoadDocument(group); Paint(); view.KeyDown(49, false, false);
                ClickFruit(first, false); ClickFruit(last, true);
                var start = Position(first.TimeMs, first.X);
                var end = Position(first.TimeMs, 182);
                view.PointerDown(start.X, start.Y, 0, false, false);
                view.PointerMove(end.X, end.Y, false, false); Paint($"drag-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                if (view.ObjectSnapTargetPoint is not { } groupTarget || Math.Abs(groupTarget.X - 385.4) > .001)
                    throw new InvalidOperationException("Native group drag did not snap from its last fruit.");
                view.PointerUp(end.X, end.Y, 0); Paint();
                if (Math.Abs(view.Document.Fruits.Single(f => f.Id == first.Id).X - 185.4) > .001
                    || Math.Abs(view.Document.Fruits.Single(f => f.Id == last.Id).X - 385.4) > .001)
                    throw new InvalidOperationException("Native group snap did not preserve relative spacing.");
                view.KeyDown(90, true, false); Paint();
                if (!view.Document.ContentEquals(group)) throw new InvalidOperationException("Native group snap failed undo.");
                var ordered = new MapDocument { DurationMs = 8000, BeatLengthMs = 500, SliderMultiplier = 2 };
                var moving = new Fruit { TimeMs = 250, X = 450 };
                ordered.Fruits.AddRange([new() { TimeMs = 0, X = 100 }, moving,
                    new() { TimeMs = 500, X = 400 }, new() { TimeMs = 0, X = 280 }]);
                ordered.DistanceSnapRatios.AddRange([.5, 1, 1.8]);
                view.LoadDocument(ordered); Paint(); view.KeyDown(89, false, false);
                start = Position(moving.TimeMs, moving.X);
                view.PointerDown(start.X, start.Y, 0, false, false);
                end = Position(moving.TimeMs, 480);
                view.PointerMove(end.X, end.Y, false, false); Paint();
                double previousX = view.Document.Fruits.Single(f => f.Id == moving.Id).X;
                for (int x = 472; x >= 24; x -= 8)
                {
                    end = Position(moving.TimeMs, x);
                    view.PointerMove(end.X, end.Y, false, false); Paint();
                    double actual = view.Document.Fruits.Single(f => f.Id == moving.Id).X;
                    if (actual > previousX + .001) throw new InvalidOperationException("Native right-to-left snap drag reversed.");
                    previousX = actual;
                }
                Paint($"ordered-drag-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                view.PointerUp(end.X, end.Y, 0); Paint(); view.KeyDown(90, true, false); Paint();
                if (!ordered.ContentEquals(view.Document)) throw new InvalidOperationException("Native ordered drag failed undo.");
                view.LoadDocument(group); Paint();
                view.PointerMove(0, 0, false, false); view.OpenDistanceSnapDialog(); Paint();
                if (view.ObjectSnapSettings != (true, 2, 1) || view.ObjectSnapLaterPlusBounds.Bottom > size.Item2)
                    throw new InvalidOperationException("Native object snap defaults or dialog bounds are invalid.");
                Paint($"configure-{locale}-{size.Item1}x{size.Item2}-{dpi}");
                var plus = view.ObjectSnapLaterPlusBounds;
                view.PointerDown(plus.X + 12, plus.Y + 12, 0, false, false);
                view.PointerUp(plus.X + 12, plus.Y + 12, 0); Paint();
                view.KeyDown(27, false, false); Paint();
                if (view.ObjectSnapSettings != (true, 2, 1) || !view.Document.ContentEquals(group))
                    throw new InvalidOperationException("Native configuration cancellation changed state.");
                view.NewProject();

                (float X, float Y) Position(double time, double x) =>
                    (view.PlayfieldBounds.X + (float)(x * view.PlayfieldBounds.Width / 512),
                        view.CanvasPlotBounds.Bottom - (float)((time - view.ViewStartMs) * view.PixelsPerMs));
                void ClickFruit(Fruit fruit, bool ctrl)
                {
                    var p = Position(fruit.TimeMs, fruit.X);
                    view.PointerDown(p.X, p.Y, 0, false, ctrl); view.PointerUp(p.X, p.Y, 0); Paint();
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
        AppLog.Write("Object snap placement, undo and configuration passed native rendering checks.");
    }
}
