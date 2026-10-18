using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class OsuDragSnapTests
{
    public static void Run()
    {
        foreach (bool previousSlider in new[] { false, true })
        foreach (bool slider in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 500, SliderMultiplier = 1.4, OsuDistanceSpacing = .6 };
            if (previousSlider)
            {
                var previous = Slider(750, 32, 192, 172, 192); previous.SliderVelocityMultiplier = 2;
                map.ImportedSliders.Add(previous);
            }
            else map.Fruits.Add(new() { TimeMs = 1000, X = 128, Y = 192 });
            double expectedX = previousSlider ? 340 : 212;
            Guid id;
            if (slider)
            {
                var source = Slider(1500, 300, 256, 400, 256); map.ImportedSliders.Add(source); id = source.Id;
            }
            else { var source = new Fruit { TimeMs = 1500, X = 300, Y = 256 }; map.Fruits.Add(source); id = source.Id; }
            var ui = NewUi(map); if (!ui.View.DistanceSnapEnabled) ui.Key('Y');
            var before = ui.View.Document.DeepClone();
            double grabX = slider ? 350 : 300;
            var from = ui.View.OsuCanvasScreen(grabX, 256); var to = ui.View.OsuCanvasScreen(grabX + 100, 192);
            ui.View.PointerDown(from.X, from.Y, 0, false, false);
            ui.View.PointerMove(to.X, to.Y, false, false); ui.Paint();
            CheckHead(expectedX, 192);
            ui.View.PointerUp(to.X, to.Y, 0); ui.Paint(); CheckHead(expectedX, 192);
            if (slider)
            {
                var moved = ui.View.Document.ImportedSliders.Single(s => s.Id == id);
                Near(100, moved.PixelLength); Near(1, moved.SliderVelocityMultiplier!.Value);
                Near(expectedX + 100, moved.ControlPoints[^1].X); Near(192, moved.ControlPoints[^1].GeometryY);
            }
            Check(ui.View.Document.TimingPoints.Count == before.TimingPoints.Count, "Dragging inserted green timing.");
            ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "DS dragging did not undo atomically.");
            from = ui.View.OsuCanvasScreen(grabX, 256);
            ui.View.PointerDown(from.X, from.Y, 0, false, false);
            ui.View.PointerMove(to.X, to.Y, false, false); ui.View.CancelInteraction(); ui.Paint();
            Check(before.ContentEquals(ui.View.Document), "Cancelled DS dragging retained changes.");
            void CheckHead(double x, double y)
            {
                if (slider) { var s = ui.View.Document.ImportedSliders.Single(s => s.Id == id); Near(x, s.X); Near(y, s.Y); }
                else { var f = ui.View.Document.Fruits.Single(f => f.Id == id); Near(x, f.X); Near(y, f.Y); }
            }
        }
        ObjectTargets();
        SliderTailDrag();
        GroupDrag();
    }

    private static void ObjectTargets()
    {
        var map = new MapDocument { DurationMs = 8000 };
        map.Fruits.Add(new() { TimeMs = 1300, X = 200, Y = 192 });
        var moving = new Fruit { TimeMs = 1500, X = 80, Y = 256 }; map.Fruits.Add(moving);
        map.Fruits.Add(new() { TimeMs = 6000, X = 160, Y = 300 });
        map.ImportedSliders.Add(Slider(1400, 300, 192, 400, 192));
        var ui = NewUi(map);
        var preferences = (MapEditingPreferences)typeof(EditorView).GetProperty("EditingPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        ui.View.OpenDistanceSnapDialog(); ui.Paint();
        var toggle = ui.View.ObjectSnapToggleBounds;
        ui.Click(toggle.X + 10, toggle.Y + 10);
        Check(ui.View.ObjectSnapEarlierPlusBounds.Width == 0 && ui.View.ObjectSnapLaterPlusBounds.Width == 0,
            "osu Object Snap must not expose Catch time-window controls.");
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("library.apply"));
        Check(preferences.ObjectSnap, "osu Object Snap configuration did not enable snapping.");
        preferences.ObjectSnapEarlierBeats = preferences.ObjectSnapLaterBeats = 0;
        foreach (double x in new[] { 200d, 300, 400 })
        {
            ui.Key('2'); var p = ui.View.OsuCanvasScreen(x + 1, 193); var target = ui.View.OsuCanvasScreen(x, 192);
            ui.View.PointerMove(p.X, p.Y, false, false); ui.Paint();
            Check(ui.Canvas.Circles.Any(c => c.Filled && Math.Abs(c.X - target.X) < .05 && Math.Abs(c.Y - target.Y) < .05),
                "Object snap preview missed an active circle or slider endpoint.");
            ui.Click(p.X, p.Y); Near(x, ui.View.Document.Fruits.Last().X); Near(192, ui.View.Document.Fruits.Last().Y);
            ui.Key('Z', ctrl: true); ui.Paint();
        }
        ui.Key('2'); var inactive = ui.View.OsuCanvasScreen(161, 301); ui.Click(inactive.X, inactive.Y);
        Near(161, ui.View.Document.Fruits.Last().X); Near(301, ui.View.Document.Fruits.Last().Y);
        ui.Key('Z', ctrl: true); ui.Paint(); ui.Key('1');
        var from = ui.View.OsuCanvasScreen(80, 256); var to = ui.View.OsuCanvasScreen(82, 257);
        ui.View.PointerDown(from.X, from.Y, 0, false, false); ui.View.PointerMove(to.X, to.Y, false, false);
        ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
        var moved = ui.View.Document.Fruits.Single(f => f.Id == moving.Id); Near(82, moved.X); Near(257, moved.Y);
        ui.Key('Y');
        from = ui.View.OsuCanvasScreen(82, 257); to = ui.View.OsuCanvasScreen(201, 193);
        ui.View.PointerDown(from.X, from.Y, 0, false, false); ui.View.PointerMove(to.X, to.Y, false, false);
        ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
        moved = ui.View.Document.Fruits.Single(f => f.Id == moving.Id); Near(200, moved.X); Near(192, moved.Y);
    }

    private static void SliderTailDrag()
    {
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new() { TimeMs = 1300, X = 200, Y = 192 });
        var slider = Slider(1500, 80, 256, 180, 256); map.ImportedSliders.Add(slider);
        var ui = NewUi(map);
        var preferences = (MapEditingPreferences)typeof(EditorView).GetProperty("EditingPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        preferences.ObjectSnap = true;
        var from = ui.View.OsuCanvasScreen(130, 256); var to = ui.View.OsuCanvasScreen(149, 193);
        ui.View.PointerDown(from.X, from.Y, 0, false, false); ui.View.PointerMove(to.X, to.Y, false, false);
        ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
        var result = ui.View.Document.ImportedSliders.Single();
        Near(100, result.X); Near(192, result.Y); Near(200, result.ControlPoints[^1].X); Near(192, result.ControlPoints[^1].GeometryY);
    }

    private static void GroupDrag()
    {
        var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 500, SliderMultiplier = 1.4, OsuDistanceSpacing = .6 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 128, Y = 192 });
        var first = new Fruit { TimeMs = 1500, X = 300, Y = 256 };
        var last = new Fruit { TimeMs = 2000, X = 360, Y = 300 }; map.Fruits.AddRange([first, last]);
        var ui = NewUi(map); ui.Key('Y');
        typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, [new[] { first.Id, last.Id }, first.Id]);
        ui.Paint();
        var from = ui.View.OsuCanvasScreen(300, 256); var to = ui.View.OsuCanvasScreen(400, 192);
        ui.View.PointerDown(from.X, from.Y, 0, false, false); ui.View.PointerMove(to.X, to.Y, false, false);
        ui.View.PointerUp(to.X, to.Y, 0); ui.Paint();
        var a = ui.View.Document.Fruits.Single(f => f.Id == first.Id); var b = ui.View.Document.Fruits.Single(f => f.Id == last.Id);
        Near(212, a.X); Near(192, a.Y); Near(60, b.X - a.X); Near(44, b.Y - a.Y);
    }

    private static ImportedSlider Slider(double time, double x, double y, double endX, double endY)
    {
        var slider = new ImportedSlider { TimeMs = time, X = x, Y = y, PixelLength = Math.Sqrt(Math.Pow(endX - x, 2) + Math.Pow(endY - y, 2)), PathType = 'L', SliderVelocityMultiplier = 1 };
        slider.ControlPoints.AddRange([new(x, y), new(endX, endY)]); return slider;
    }
    private static Ui NewUi(MapDocument map)
    {
        var ui = new Ui(false); ui.LoadDocument(map); ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(1500, 8000, true, false, false, null, null); ui.Paint(); ui.Key('1');
        if (ui.View.DistanceSnapEnabled) ui.Key('Y');
        return ui;
    }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
