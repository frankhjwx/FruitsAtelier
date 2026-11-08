using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class FeedbackBugTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }

    public static void PlacementBounds()
    {
        foreach (float zoom in new[] { -120f, 0f, 120f })
        {
            var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 20000 });
            ui.View.Wheel(ui.Plot.X + 20, ui.Plot.Bottom - 20, zoom, false, false, true); ui.Paint();
            ui.Key('2');
            var field = ui.View.PlayfieldBounds;
            float y = ui.ScreenAt(1000, 256).Y;
            foreach (float x in new[] { field.X - 10, field.Right + 10 })
            {
                Check(ui.View.CanvasPlotBounds.Contains(x, y), "Fixture must click canvas padding");
                ui.View.PointerMove(x, y, false, false); ui.Paint();
                var ghost = typeof(EditorView).GetMethod("PlacementGhostPoint",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Check(ghost.Invoke(ui.View, null) is null, "Canvas padding must not show a placement preview");
                ui.Click(x, y);
                Check(ui.View.Document.Fruits.Count == 0 && !ui.View.IsDirty, "Canvas padding placed a fruit");
            }
            ui.Click(field.X + 1, y);
            Check(ui.View.Document.Fruits.Count == 1 && ui.View.Document.Fruits[0].X is >= 0 and <= 512,
                "Inside-edge placement must remain available");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.Fruits.Count == 0, "Fruit placement must undo once");
        }
        foreach (int direction in new[] { -1, 1 })
        {
            var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 20000 });
            ui.View.LibrarySettings.UniformCanvasScale = false;
            typeof(EditorView).GetMethod("SetCanvasScales",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(ui.View, [2d, ui.View.CanvasTimeZoom, ui.View.CanvasPlotBounds.Bottom]);
            ui.Paint();
            var plot = ui.View.CanvasPlotBounds;
            float centre = plot.X + plot.Width / 2, y = plot.Y + plot.Height / 2;
            ui.View.PointerDown(centre, y, 1, false, false);
            ui.View.PointerMove(centre + direction * 20000, y, false, false);
            ui.View.PointerUp(centre + direction * 20000, y, 1); ui.Paint();
            ui.Key('2');
            var field = ui.View.PlayfieldBounds;
            float outside = direction > 0 ? field.X - 5 : field.Right + 5;
            Check(plot.Contains(outside, y), "Panned edge padding must be visible");
            ui.Click(outside, y);
            Check(ui.View.Document.Fruits.Count == 0, "Panned padding placed a fruit");
            ui.Click(direction > 0 ? field.X + 1 : field.Right - 1, y);
            Check(ui.View.Document.Fruits.Count == 1, "Panned inside edge must allow placement");
        }
    }

    public static void HiddenPathDoubleClick()
    {
        foreach (bool visible in new[] { false, true })
        foreach (bool macSequence in new[] { false, true })
        {
            var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 20000 });
            ui.View.LibrarySettings.ShowSliderPaths = visible;
            ui.View.SetSliderEditingMode(SliderEditingMode.OsuLegacy);
            ui.Key('3'); ui.ClickMap(1000, 100); ui.ClickMap(1500, 250);
            var point = ui.ScreenAt(1500, 250);
            if (macSequence) ui.View.PointerDown(point.X, point.Y, 0, false, false);
            ui.View.PointerDoubleClick(point.X, point.Y, false, false);
            ui.View.PointerUp(point.X, point.Y, 0); ui.Paint();
            ui.ClickMap(2000, 100); ui.Key('1');
            var vertices = SliderControlEditing.Vertices(ui.View.Document.Tracks.Single());
            Check(vertices.Count == 3 && vertices[1].Type is not null,
                "Double-click must create a segment boundary without an extra control point");
            ui.Key('Z', ctrl: true);
            Check(ui.View.Document.Tracks.Count == 0, "Slider drawing must undo once");
            ui.Key('Y', ctrl: true);
            Check(SliderControlEditing.Vertices(ui.View.Document.Tracks.Single())[1].Type is not null,
                "Redo must retain the boundary");
        }
    }

    public static void TimelineEdges()
    {
        foreach (int frameMs in new[] { 10, 50 })
        foreach (bool cancel in new[] { false, true })
        foreach (bool playing in new[] { false, true })
        foreach (bool lazer in new[] { false, true })
        {
            var clock = new Clock();
            var map = new MapDocument { DurationMs = 20000 };
            map.Fruits.Add(new Fruit { TimeMs = 5000, X = 256 });
            map.Fruits.Add(new Fruit { TimeMs = 5500, X = 128 });
            var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
            ui.View.LibrarySettings.LazerEditing = lazer;
            ui.View.UpdateTransport(5000, 20000, true, playing, false, null, "fixture.wav"); ui.Paint();
            ui.View.RequestSeek = time => ui.View.UpdateTransport(time, 20000, true, playing, false, null, "fixture.wav");
            ui.Key('A', ctrl: true);
            var area = ui.View.ObjectTimelineBounds;
            float x = area.X + (float)((5000 - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
            float y = area.Y + 27;
            ui.View.PointerDown(x, y, 0, false, false);
            ui.View.SetModifiers(false, true);
            ui.View.PointerMove(area.Right, y, true, false); ui.Paint();
            Check(ui.View.TimelineDragScrollNeedsRedraw, "Edge drag must request frames while pointer is stationary");
            double before = ui.View.PlayheadMs, fruitBefore = ui.View.Document.Fruits[0].TimeMs;
            double viewBefore = ui.View.ViewStartMs;
            for (int elapsed = 0; elapsed < 500; elapsed += frameMs) { clock.Now += frameMs; ui.Paint(); }
            double delta = 300 / ui.View.ObjectTimelinePixelsPerMs;
            Near(delta, ui.View.PlayheadMs - before, "Timeline edge scroll speed");
            Near(delta, ui.View.Document.Fruits[0].TimeMs - fruitBefore, "Stationary pointer must keep moving the fruit");
            Near(500, ui.View.Document.Fruits[1].TimeMs - ui.View.Document.Fruits[0].TimeMs, "Group spacing must remain stable");
            Near(delta, ui.View.ViewStartMs - viewBefore, "Canvas must follow timeline scroll");
            ui.View.PointerMove(area.X + area.Width / 2, y, true, false); ui.Paint();
            Check(!ui.View.TimelineDragScrollNeedsRedraw, "Inside pointer must stop requesting frames");
            before = ui.View.PlayheadMs; clock.Now += 100; ui.Paint();
            Near(before, ui.View.PlayheadMs, "Leaving edge must stop scrolling");
            ui.View.PointerMove(area.X, y, true, false); ui.Paint();
            before = ui.View.PlayheadMs; clock.Now += 50; ui.Paint();
            Check(ui.View.PlayheadMs < before, "Left edge must scroll backwards");
            ui.View.Wheel(area.X + 1, y, 120 * 1000, false); ui.Paint();
            Near(0, ui.View.PlayheadMs, "Scroll must stop at map start");
            Check(!ui.View.TimelineDragScrollNeedsRedraw, "Map start must stop requesting frames");
            ui.View.PointerMove(area.Right, y, true, false);
            ui.View.Wheel(area.Right - 1, y, -120 * 1000, false); ui.Paint();
            Near(20000, ui.View.PlayheadMs, "Scroll must stop at map end");
            Check(!ui.View.TimelineDragScrollNeedsRedraw, "Map end must stop requesting frames");
            if (cancel) ui.View.CancelInteraction(); else ui.View.PointerUp(area.X, y, 0);
            ui.View.SetModifiers(false, false); ui.Paint();
            before = ui.View.PlayheadMs; clock.Now += 100; ui.Paint();
            Near(before, ui.View.PlayheadMs, "Ending gesture must stop scrolling");
            if (!cancel) ui.Key('Z', ctrl: true);
            Check(ui.View.Document.ContentEquals(map) && !ui.View.WantsCapture, "Cancel or one undo must restore content");
            Check(!ui.View.TimelineDragScrollNeedsRedraw, "Ended gesture must not request frames");
        }
    }

    private static void Near(double expected, double actual, string message)
        => Check(Math.Abs(expected - actual) < .01, $"{message}: expected {expected}, got {actual}");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
