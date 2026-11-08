using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class FeedbackBugRenderCheck
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }

    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var clock = new Clock(); var view = new EditorView(false, clock);
                var map = new MapDocument { IsDemo = false, DurationMs = 20000 };
                map.Fruits.Add(new Fruit { TimeMs = 5000, X = 256 });
                view.LoadDocument(map); Paint();
                view.UpdateTransport(5000, 20000, true, false, false, null, null); Paint();
                view.KeyDown('2', false, false); Paint();
                var field = view.PlayfieldBounds;
                Click(field.X - 5, field.Y + field.Height / 2);
                Click(field.Right + 5, field.Y + field.Height / 2);
                Check(view.Document.ContentEquals(map), "Outside-playfield click changed content");
                view.KeyDown('1', false, false); Paint();
                var area = view.ObjectTimelineBounds;
                float x = area.X + (float)((5000 - view.ObjectTimelineStartMs) * view.ObjectTimelinePixelsPerMs);
                float y = area.Y + 27;
                view.PointerDown(x, y, 0, false, false);
                view.PointerMove(area.Right, y, false, false); Paint();
                Check(view.TimelineDragScrollNeedsRedraw, "Timeline edge did not request frames");
                double before = view.PlayheadMs;
                for (int i = 0; i < 10; i++) { clock.Now += 20; Paint(); }
                Check(view.PlayheadMs > before && view.Document.Fruits[0].TimeMs > 5000, "Timeline drag did not scroll");
                view.PointerUp(area.Right, y, 0); view.KeyDown('Z', true, false); Paint();
                Check(view.Document.ContentEquals(map) && !view.TimelineDragScrollNeedsRedraw, "Timeline undo or release failed");
                view.LibrarySettings.ShowSliderPaths = false;
                view.SetSliderEditingMode(SliderEditingMode.OsuLegacy);
                view.UpdateTransport(5000, 20000, true, false, false, null, null); Paint();
                view.KeyDown('3', false, false);
                var head = Point(5000, 100); Click(head.X, head.Y);
                var joint = Point(5125, 250); Click(joint.X, joint.Y);
                view.PointerDoubleClick(joint.X, joint.Y, false, false);
                view.PointerUp(joint.X, joint.Y, 0); Paint();
                var tail = Point(5250, 100); Click(tail.X, tail.Y);
                view.KeyDown('1', false, false); Paint();
                var vertices = SliderControlEditing.Vertices(view.Document.Tracks.Single());
                Check(vertices.Count == 3 && vertices[1].Type is not null, "Hidden-path double-click lost boundary");
                view.KeyDown('Z', true, false); Paint();
                Check(view.Document.ContentEquals(map), "Slider undo failed");
                void Paint() { canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End(); }
                void Click(float px, float py) { view.PointerDown(px, py, 0, false, false); view.PointerUp(px, py, 0); Paint(); }
                (float X, float Y) Point(double time, double px) =>
                    (view.PlayfieldBounds.X + (float)(px / 512 * view.PlayfieldBounds.Width),
                     view.CanvasPlotBounds.Bottom - (float)((time - view.ViewStartMs) * view.PixelsPerMs));
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
