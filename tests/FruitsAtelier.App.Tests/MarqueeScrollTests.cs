using FruitsAtelier.Core;

internal static class MarqueeScrollTests
{
    private sealed class Clock : TimeProvider
    {
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => now;
        public void Advance(int ms) => now += ms;
    }

    public static void WheelAndEdges()
    {
        foreach (bool timeline in new[] { false, true })
        foreach (bool playing in new[] { false, true })
        foreach (int frameMs in new[] { 10, 50 })
        {
            var clock = new Clock();
            var map = new MapDocument { DurationMs = 20000, CircleSize = 10 };
            map.Fruits.AddRange([new Fruit { TimeMs = 4200, X = 80 }, new Fruit { TimeMs = 4800, X = 80 }]);
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
            ui.View.UpdateTransport(5000, 20000, true, playing, false, null, "fixture.wav"); ui.Paint();
            var original = ui.View.Document.DeepClone();
            var area = timeline ? ui.View.ObjectTimelineBounds : ui.View.CanvasPlotBounds;
            float X(double time) => timeline
                ? area.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs)
                : ui.Plot.X + 30f / 512 * ui.Plot.Width;
            float Y(double time) => timeline ? area.Y + 3
                : area.Bottom - (float)((time - ui.View.ViewStartMs) * ui.View.PixelsPerMs);
            float startX = X(4000), startY = Y(4000);
            float endX = timeline ? X(4500) : ui.Plot.X + 130f / 512 * ui.Plot.Width;
            float endY = timeline ? area.Y + 45 : Y(4500);
            ui.View.PointerDown(startX, startY, 0, false, false);
            ui.View.PointerMove(endX, endY, false, false); ui.Paint();
            Check(ui.View.SelectedObjectIds.Contains(map.Fruits[0].Id), "Initial box missed the first fruit");
            double before = ui.View.PlayheadMs;
            ui.View.Wheel(endX, endY, -120, false); ui.Paint();
            Near(before + (playing ? 500 : 125), ui.View.PlayheadMs);
            Check(ui.View.WantsCapture, "Wheel released the selection drag");

            float forwardX = timeline ? area.Right : endX;
            float forwardY = timeline ? endY : area.Y;
            ui.View.PointerMove(forwardX, forwardY, false, false); ui.Paint();
            Check(ui.View.MarqueeScrollNeedsRedraw, "Edge did not request continued redraws");
            before = ui.View.PlayheadMs;
            for (int elapsed = 0; elapsed < 1000; elapsed += frameMs) { clock.Advance(frameMs); ui.Paint(); }
            double scale = timeline ? ui.View.ObjectTimelinePixelsPerMs : ui.View.PixelsPerMs;
            Near(100 / scale, ui.View.PlayheadMs - before);
            Check(ui.View.SelectedObjectIds.Contains(map.Fruits[0].Id)
                && ui.View.SelectedObjectIds.Contains(map.Fruits[1].Id), "Scrolling lost the anchored selection range");

            ui.View.PointerMove(timeline ? area.X + area.Width / 2 : endX,
                timeline ? endY : area.Y + area.Height / 2, false, false); ui.Paint();
            Check(!ui.View.MarqueeScrollNeedsRedraw, "Scrolling continued away from the edge");
            before = ui.View.PlayheadMs; clock.Advance(1000); ui.Paint(); Near(before, ui.View.PlayheadMs);

            float backwardX = timeline ? area.X : endX;
            float backwardY = timeline ? endY : area.Bottom;
            ui.View.PointerMove(backwardX, backwardY, false, false); ui.Paint();
            for (int elapsed = 0; elapsed < 1000; elapsed += frameMs) { clock.Advance(frameMs); ui.Paint(); }
            Near(-100 / scale, ui.View.PlayheadMs - before);
            ui.View.Wheel(timeline ? area.X + 1 : endX, timeline ? endY : area.Bottom - 1, 120 * 1000, false); ui.Paint();
            Near(0, ui.View.PlayheadMs);
            Check(!ui.View.MarqueeScrollNeedsRedraw, "Scrolling did not stop at the map start");
            ui.Key(27);
            Check(!ui.View.WantsCapture && ui.View.SelectedObjectIds.Count == 0, "Cancel did not restore selection");
            Check(original.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Box scrolling edited content");

            ui.View.PointerDown(startX, startY, 0, false, false);
            ui.View.PointerMove(forwardX, forwardY, false, false); ui.Paint();
            ui.View.Wheel(timeline ? area.Right - 1 : endX, timeline ? endY : area.Y + 1, -120 * 1000, false); ui.Paint();
            Near(20000, ui.View.PlayheadMs);
            Check(!ui.View.MarqueeScrollNeedsRedraw, "Scrolling did not stop at the map end");
            ui.View.PointerUp(forwardX, forwardY, 0); ui.Paint();
            before = ui.View.PlayheadMs; clock.Advance(1000); ui.Paint(); Near(before, ui.View.PlayheadMs);
            Check(!ui.View.WantsCapture && !ui.View.MarqueeScrollNeedsRedraw, "Release retained edge scrolling");
        }
    }

    private static void Near(double expected, double actual)
        => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
