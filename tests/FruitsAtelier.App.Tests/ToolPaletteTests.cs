using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class ToolPaletteTests
{
    public static void PlacementHyperdash()
    {
        foreach (int key in new[] { 'F', 'B' })
        {
            var ui = Empty();
            var map = new MapDocument { DurationMs = 12000 };
            map.Fruits.Add(new() { TimeMs = 1000, X = 0 });
            map.Fruits.Add(new() { TimeMs = 1250, X = 0 });
            ui.LoadDocument(map); ui.Key(key);
            var before = ui.View.Document.DeepClone();
            ui.MoveMap(1125, 512);
            var previous = Screen(ui, 1000, 0); var ghost = Screen(ui, 1125, 512);
            Check(GlowAt(ui, previous.X, previous.Y, .7f), "Previous fruit did not acquire a placement hyperdash.");
            Check(GlowAt(ui, ghost.X, ghost.Y, .42f), "Placement ghost did not show its outgoing hyperdash.");
            ui.MoveMap(1125, 0);
            Check(!ui.Canvas.Circles.Any(c => c.Color == 0xFF0000), "Hover movement retained a stale hyperdash.");
            ui.MoveMap(1125, 512); ui.Key('1');
            Check(!ui.Canvas.Circles.Any(c => c.Color == 0xFF0000), "Leaving placement retained a phantom hyperdash.");
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Hover changed content or history.");
        }
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var ui = Empty();
            var map = new MapDocument { DurationMs = 12000 };
            map.Fruits.Add(new() { TimeMs = 1000, X = 0 });
            map.Fruits.Add(new() { TimeMs = 1500, X = 0 });
            ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.Key('B');
            ui.ClickMap(1125, 480, ctrl: true); ui.MoveMap(1375, 480);
            var previous = Screen(ui, 1000, 0); var tail = Screen(ui, 1375, 480);
            Check(GlowAt(ui, previous.X, previous.Y, .7f), $"{mode}: Slider draft did not update its preceding hyperdash. {ui.View.StatusMessage}");
            Check(GlowAt(ui, tail.X, tail.Y, .42f), "Slider draft tail did not preview its outgoing hyperdash.");
            ui.Key(27);
            Check(ui.View.Document.ContentEquals(map) && !ui.View.IsDirty, "Cancelling a slider preview changed the map.");
        }
    }

    private static bool GlowAt(Ui ui, float x, float y, float opacity) => ui.Canvas.Circles.Any(c =>
        c.Color == 0xFF0000 && Math.Abs(c.X - x) < 1 && Math.Abs(c.Y - y) < 1 && Math.Abs(c.Opacity - opacity) < .001);

    public static void PaletteAndGhost()
    {
        var ui = Empty();
        string[] names = ["Select", "Fruit", "Slider", "Banana"];
        for (int i = 0; i < 4; i++)
        {
            var b = ui.View.ToolButtonBounds[i]; ui.Click(b.X + b.Width / 2, b.Y + b.Height / 2);
            var images = ui.Canvas.Images.Where(t => t.Path.Contains(Path.Combine("icons", "tools"))).ToArray();
            Check(images.Length == 4 && images.Count(t => t.Opacity == 1) == 1 && images[i].Opacity == 1, "Palette is not exclusive.");
            Check(images.All(t => t.Bounds.Width == b.Width && t.Bounds.Height == b.Height), "Tool rectangles differ in size.");
            Check(ui.View.ActiveTool == names[i], "Palette selected the wrong tool.");
        }
        var imported = new ImportedSlider { TimeMs = 1000, X = 200, Y = 192, PathType = 'L', PixelLength = 200 };
        imported.ControlPoints.Add(new(200, 192)); imported.ControlPoints.Add(new(400, 192));
        var map = new MapDocument { DurationMs = 12000 }; map.ImportedSliders.Add(imported);
        ui.LoadDocument(map); ui.ClickMap(1000, 200);
        var baseline = ui.View.Document.DeepClone();
        var sliderButton = ui.View.ToolButtonBounds[2]; ui.Click(sliderButton.X + 20, sliderButton.Y + 20);
        Check(baseline.ContentEquals(ui.View.Document), "Selecting the FSlider placement tool converted an existing slider.");
        foreach (int key in new[] { 'F', 'B' })
        {
            ui.Key(key); ui.MoveMap(1234, 230);
            var ghost = ui.Canvas.Circles.Single(c => Math.Abs(c.Opacity - .6f) < .001);
            Near(Screen(ui, 1250, 230).Y, ghost.Y);
            Near(Screen(ui, 1250, 230).X, ghost.X);
        }
    }

    public static void FruitCombo()
    {
        var ui = Empty(); ui.Key('F');
        Right(ui, 1234, 230);
        Check(ui.View.NextFruitNewCombo && !ui.View.IsDirty, "Arming New combo modified the document.");
        Check(ui.Canvas.Texts.Any(t => t.Value == "NC" && t.Color == 0xF2C66D), "New Combo placement preview did not show NC.");
        ui.ClickMap(1234, 230);
        var fruit = ui.View.Document.Fruits.Single(); Near(1250, fruit.TimeMs);
        Check(!ui.View.NextFruitNewCombo && Combo(fruit), "Placed fruit lost its New combo flag.");
        var saved = ui.View.Document.DeepClone();
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(saved));
        Check(Combo(restored.Fruits.Single()), "Project round-trip lost New combo.");
        Check(Combo(OsuBeatmapWriter.Serialize(saved).ReadBack.Fruits.Single()), "osu export lost New combo.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Fruits.Count == 0, "Placement did not undo.");
        ui.Key('Y', ctrl: true); Check(saved.ContentEquals(ui.View.Document), "Redo lost combo or object identity.");
        Right(ui, 1250, 230); Check(ui.View.Document.Fruits.Count == 0 && !ui.View.NextFruitNewCombo, "Paused right-click failed to delete the fruit.");
        ui.Key('Z', ctrl: true);
        ui.ClickMap(1250, 230); Check(ui.View.Document.Fruits.Count == 2, "Fruit mode selected an existing object instead of placing.");
        Check(ui.View.Document.Fruits.Count(Combo) == 1, "New combo leaked to the next fruit.");
        ui.View.UpdateTransport(1250, 12000, true, true, false, null, "fixture.wav"); ui.Paint();
        Right(ui, 1250, 230);
        Check(ui.View.NextFruitNewCombo && ui.View.Document.Fruits.Count == 2, "Playing right-click should arm a combo without deleting.");
    }

    public static void ComboLabels()
    {
        var map = new MapDocument { DurationMs = 12000 };
        var left = new Fruit { TimeMs = 1000, X = 0 };
        var right = new Fruit { TimeMs = 1000, X = 512 };
        map.Fruits.AddRange([left, right, new Fruit { TimeMs = 1250, X = 256 }]);
        ObjectFlags.SetNewCombo(map, left.Id, true);
        ObjectFlags.SetNewCombo(map, right.Id, true);
        var ui = new Ui(); ui.LoadDocument(map);
        var plot = ui.View.CanvasPlotBounds;
        ui.View.Wheel(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, 120000, true);
        ui.View.UpdateTransport(1000, 12000, true, true, false, null, null);
        ui.View.UpdateTransport(1000, 12000, true, true, false, null, null);
        foreach (string language in new[] { "en", "zh-CN" })
        {
            FruitsAtelier.Localization.Strings.SetLanguage(language); ui.Paint();
            var labels = ui.Canvas.Texts.Where(t => t.Value == "NC" && t.Color == 0xF2C66D).OrderBy(t => t.X).ToArray();
            Check(labels.Length == 2, "Canvas must label only New Combo fruits with NC in every language.");
            float middle = ui.View.PlayfieldBounds.X + ui.View.PlayfieldBounds.Width / 2;
            Check(labels[0].X >= plot.X && labels[0].X < middle
                && labels[1].X > middle && labels[1].X < plot.Right,
                "NC labels were not placed beside their fruits inside the canvas.");
        }
        FruitsAtelier.Localization.Strings.SetLanguage("en");

        ui.Key('1'); ui.ClickFruit(map.Fruits[2].Id);
        var conversion = ui.View.Conversion;
        ui.Key('Q');
        Check(ReferenceEquals(conversion, ui.View.Conversion), "Toggling New Combo rebuilt the catch object stream.");
        Check(ui.Canvas.Texts.Count(t => t.Value == "NC" && t.Color == 0xF2C66D) == 3,
            "Toggling New Combo did not show NC immediately.");
        ui.Key('Q');
        Check(ReferenceEquals(conversion, ui.View.Conversion)
            && ui.Canvas.Texts.Count(t => t.Value == "NC" && t.Color == 0xF2C66D) == 2,
            "Removing New Combo rebuilt the stream or retained NC.");
        ui.Key('Z', ctrl: true);
        Check(ui.Canvas.Texts.Count(t => t.Value == "NC" && t.Color == 0xF2C66D) == 3,
            "Undo did not restore the NC label.");

        var slider = new ImportedSlider { TimeMs = 1000, X = 256, Y = 192, PathType = 'L', PixelLength = 200 };
        slider.ControlPoints.Add(new(256, 192)); slider.ControlPoints.Add(new(400, 192));
        var sliderMap = new MapDocument { DurationMs = 12000 };
        sliderMap.ImportedSliders.Add(slider);
        ObjectFlags.SetNewCombo(sliderMap, slider.Id, true);
        ui.LoadDocument(sliderMap);
        Check(ui.Canvas.Texts.Count(t => t.Value == "NC" && t.Color == 0xF2C66D) == 1,
            "Slider New Combo must label its head with NC once.");
    }

    public static void DraftRemovalAndStraight()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var ui = Empty(); ui.View.SetSliderEditingMode(mode); ui.Key('B');
            ui.ClickMap(1000, 100); ui.ClickMap(1500, 200); ui.ClickMap(2000, 180, ctrl: true);
            var track = ui.View.Document.Tracks.Single();
            Check(CurveMath.SegmentKind(track, track.Nodes.Count - 2) == CurveKind.Linear, "Ctrl placement did not produce a straight segment.");
            Right(ui, 2000, 180);
            Check(ui.View.Document.Tracks.Count == 1, "Removing a draft point deleted its parent.");
            ui.ClickMap(2250, 220); Right(ui, 3000, 260);
            Check(ui.View.ActiveTool == "Slider" && !ui.View.WantsCapture, "Finish did not retain Slider mode without capture.");
            Check(CurveMath.Validate(ui.View.Document).Count == 0, "Draft deletion/continuation produced invalid geometry.");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.Tracks.Count == 0, "Draft edits split the undo transaction.");
        }
    }

    public static void RepeatedPointAndWholeDelete()
    {
        var ui = Empty(); ui.Key('B');
        ui.ClickMap(1000, 100); ui.ClickMap(1500, 240); ui.ClickMap(1500, 240);
        ui.ClickMap(2000, 150); Right(ui, 2500, 260);
        var track = ui.View.Document.Tracks.Single();
        Check(track.Nodes.Count == 4, "Repeated click added a zero-length segment.");
        Check(CurveMath.Validate(ui.View.Document).Count == 0, "Repeated point produced invalid geometry.");
        ui.SelectTrack(track.Id); Right(ui, 1200, CurveMath.PositionAtTime(track, 1200));
        Check(ui.View.Document.Tracks.Count == 0, "Right-click on a whole selected slider did not delete it.");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.Tracks.Single().Id == track.Id, "Undo did not restore the complete slider.");
        ui.Key('N'); ui.ClickMap(4000, 250); Right(ui, 5000, 250);
        Check(ui.View.Document.BananaShowers.Single().EndTimeMs == 5000, "Banana right-click did not set the end time.");
    }

    public static void SelectedSliderControls()
    {
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var ui = Empty();
            var track = new CurveTrack { CompensateTinyDroplets = false };
            track.Nodes.Add(new() { TimeMs = 1000, X = 100, HandleOut = new(250, 30) });
            track.Nodes.Add(new() { TimeMs = 3000, X = 250, HandleIn = new(-250, -30), HandleOut = new(250, -20) });
            track.Nodes.Add(new() { TimeMs = 5000, X = 150, HandleIn = new(-250, 20) });
            var map = new MapDocument { DurationMs = 12000 }; map.Tracks.Add(track);
            ui.LoadDocument(map); ui.View.SetSliderEditingMode(mode); ui.SelectTrack(track.Id);
            var original = ui.View.Document.DeepClone();
            double bodyX = CurveMath.PositionAtTime(track, 2000);
            ui.ClickMap(800, 400);
            ui.DownMap(2000, bodyX); ui.MoveMap(2125, bodyX + 20); ui.UpMap(2125, bodyX + 20);
            for (int i = 0; i < 3; i++)
            {
                Near(original.Tracks[0].Nodes[i].TimeMs + 125, ui.View.Document.Tracks[0].Nodes[i].TimeMs);
                Near(original.Tracks[0].Nodes[i].X + 20, ui.View.Document.Tracks[0].Nodes[i].X);
            }
            ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "Body drag did not undo atomically.");
            track = ui.View.Document.Tracks[0]; ui.SelectTrack(track.Id);
            ui.DownMap(3000, 250); ui.MoveMap(3125, 270); ui.UpMap(3125, 270);
            Near(1000, ui.View.Document.Tracks[0].Nodes[0].TimeMs);
            Near(5000, ui.View.Document.Tracks[0].Nodes[^1].TimeMs);
            Near(3125, ui.View.Document.Tracks[0].Nodes[1].TimeMs);
            ui.Key('Z', ctrl: true);
            track = ui.View.Document.Tracks[0]; ui.SelectTrack(track.Id);
            var point = mode == SliderEditingMode.PenTool
                ? new SliderVertex(track.Nodes[1].Id, new(3000, 250), null)
                : SliderControlEditing.Vertices(track).First(v => v.Type is null && v.Point.TimeMs > 1000);
            ui.ClickMap(point.Point.TimeMs, point.Point.X, ctrl: true);
            track = ui.View.Document.Tracks[0];
            Check(mode == SliderEditingMode.PenTool ? !CurvePointEditing.IsCurved(track, point.Id)
                : SliderControlEditing.Vertices(track).Single(v => v.Id == point.Id).Type is not null, "Ctrl-click did not create a straight anchor.");
            var straight = ui.View.Document.DeepClone();
            var screen = Screen(ui, point.Point.TimeMs, point.Point.X);
            ui.View.PointerDoubleClick(screen.X, screen.Y, false, true); ui.View.PointerUp(screen.X, screen.Y, 0); ui.Paint();
            Check(straight.ContentEquals(ui.View.Document), "Repeated Ctrl-click toggled the straight point.");
            Right(ui, point.Point.TimeMs, point.Point.X);
            track = ui.View.Document.Tracks[0];
            Check(mode == SliderEditingMode.PenTool ? CurvePointEditing.IsCurved(track, point.Id)
                : SliderControlEditing.Vertices(track).Single(v => v.Id == point.Id).Type is null, "Right-click did not restore a curved anchor.");
            var curved = ui.View.Document.DeepClone();
            Right(ui, point.Point.TimeMs, point.Point.X);
            Check(!SliderControlEditing.Vertices(ui.View.Document.Tracks[0]).Any(v => v.Id == point.Id), "Right-click on a curved point did not delete it.");
            ui.Key('Z', ctrl: true); Check(curved.ContentEquals(ui.View.Document), "Point deletion undo changed the curve.");
            ui.Key('Z', ctrl: true); Check(straight.ContentEquals(ui.View.Document), "Curve restoration undo changed the straight anchor.");
            ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "Straightening undo lost original curve data.");
            track = ui.View.Document.Tracks[0]; ui.SelectTrack(track.Id);
            int count = mode == SliderEditingMode.PenTool ? track.Nodes.Count : SliderControlEditing.Vertices(track).Count;
            ui.ClickMap(3500, 400, ctrl: true);
            track = ui.View.Document.Tracks[0];
            Check((mode == SliderEditingMode.PenTool ? track.Nodes.Count : SliderControlEditing.Vertices(track).Count) == count + 1,
                "Ctrl-click away from existing anchors did not insert a point.");
            var added = mode == SliderEditingMode.PenTool
                ? track.Nodes.Select(n => new SliderVertex(n.Id, new(n.TimeMs, n.X), null)).ToList() : SliderControlEditing.Vertices(track);
            Check(added.Any(v => Math.Abs(v.Point.TimeMs - 3500) < .01 && Math.Abs(v.Point.X - 400) < .01), "Inserted anchor missed the pointer position.");
            if (mode == SliderEditingMode.PenTool)
                Check(CurvePointEditing.IsCurved(track, added.Single(v => Math.Abs(v.Point.TimeMs - 3500) < .01).Id), "New Ctrl-click anchor must be curved.");
            Check(CurveMath.Validate(ui.View.Document).Count == 0, "Selected-slider controls produced invalid geometry.");
            ui.Key('Z', ctrl: true); Check(original.ContentEquals(ui.View.Document), "Insertion did not undo atomically.");
        }
    }

    public static void NoDuplicateDraftGhost()
    {
        var fruitUi = Empty(); fruitUi.Key('F'); fruitUi.ClickMap(1250, 230);
        var fruitPoint = Screen(fruitUi, 1250, 230);
        Check(fruitUi.Canvas.Circles.Count(c => c.Filled && c.Radius > 8 && Math.Abs(c.X - fruitPoint.X) < 1 && Math.Abs(c.Y - fruitPoint.Y) < 1) == 1,
            "Placed fruit overlaps its placement ghost.");
        foreach (var mode in Enum.GetValues<SliderEditingMode>())
        {
            var ui = Empty(); ui.View.SetSliderEditingMode(mode); ui.Key('B');
            ui.ClickMap(1000, 100); ui.ClickMap(1500, 200);
            ui.MoveMap(mode == SliderEditingMode.OsuLegacy ? 2000 : 1500, 200);
            var point = Screen(ui, mode == SliderEditingMode.OsuLegacy ? 2000 : 1500, 200);
            var fruit = ui.Canvas.Circles.Where(c => c.Filled && c.Radius > 8
                && Math.Abs(c.X - point.X) < 1 && Math.Abs(c.Y - point.Y) < 1).ToArray();
            Check(fruit.Length == 1, "Draft endpoint has overlapping fruit previews.");
            if (mode == SliderEditingMode.OsuLegacy) Near(.6f, fruit[0].Opacity);
        }
    }

    private static bool Combo(Fruit fruit) => fruit.OriginalLine is { } line && (int.Parse(line.Split(',')[3]) & 4) != 0;
    private static Ui Empty() { var ui = new Ui(); ui.LoadDocument(new MapDocument { DurationMs = 12000 }); return ui; }
    private static (float X, float Y) Screen(Ui ui, double t, double x) =>
        (ui.Plot.X + (float)(x / 512) * ui.Plot.Width, ui.Plot.Bottom - (float)((t - ui.View.ViewStartMs) * ui.View.PixelsPerMs));
    private static void Right(Ui ui, double t, double x)
    {
        var p = Screen(ui, t, x); ui.View.PointerDown(p.X, p.Y, 2, false, false); ui.View.PointerUp(p.X, p.Y, 2); ui.Paint();
    }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
