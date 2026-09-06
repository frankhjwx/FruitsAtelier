using FruitsAtelier.Core;

internal static class RequestedInteractionTests
{
    public static void SnapDivisors()
    {
        var ui = new Ui();
        foreach (int divisor in new[] { 4, 5, 6, 7, 8, 9, 12, 16 })
        {
            ui.SetSnapDivisor(divisor);
            Check(ui.View.SnapDivisor == divisor, $"Snap slider did not select 1/{divisor}.");
        }
        var slider = ui.View.SnapSliderBounds;
        ui.View.PointerDown(slider.X + 7, slider.Y + 15, 0, false, false);
        ui.View.PointerMove(slider.Right, slider.Y + 15, false, false);
        ui.View.PointerUp(slider.Right, slider.Y + 15, 0);
        Check(ui.View.SnapDivisor == 16, "Dragging the snap control to its right edge did not select 1/16.");

        ui.Resize(980, 620);
        Check(ui.Canvas.Texts.Any(item => item.Value == "香蕉雨  N")
            && ui.Canvas.Texts.Any(item => item.Value == "1/16"),
            "The Banana tool or snap divisor disappeared at the minimum window width.");
        Check(ui.Canvas.Texts.All(item => (item.Value is not "皮肤…" and not "Tiny 贴合") && !item.Value.StartsWith("Tick ×")),
            "Optional toolbar controls were not collapsed at the minimum window width.");
    }

    public static void DoubleClickEditing()
    {
        var map = new MapDocument { DurationMs = 10000 };
        var first = Track("First", 1000, 100, 2500, 140);
        var second = Track("Second", 3500, 360, 5000, 400);
        var fruit = new Fruit { TimeMs = 4500, X = 220 };
        map.Tracks.Add(first); map.Tracks.Add(second); map.Fruits.Add(fruit);
        var ui = Load(map);
        ui.Key(36);

        DoubleClick(ui, 1000, 100);
        Check(ui.View.ActiveTool == "Slider", "Double-clicking a Slider did not enter anchor edit mode.");
        ui.ClickMap(fruit.TimeMs, fruit.X);
        Check(ui.View.ActiveTool == "Select" && ui.View.SelectedObjectIds.SequenceEqual([fruit.Id]),
            $"Clicking another object did not leave Slider editing and select that object (tool={ui.View.ActiveTool}, selected={string.Join(',', ui.View.SelectedObjectIds)}).");

        DoubleClick(ui, 1000, 100);
        ui.ClickMap(3000, 480);
        Check(ui.View.ActiveTool == "Select" && ui.View.SelectedObjectIds.Count == 0,
            "Clicking blank canvas did not leave Slider editing.");

        DoubleClick(ui, 1000, 100);
        ui.ClickMap(3500, 360);
        Check(ui.View.ActiveTool == "Select" && ui.View.SelectedObjectIds.SequenceEqual([second.Id]),
            "Clicking a second Slider did not directly select its parent.");
    }

    public static void MultiObjectDrag()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 1100, BeatLengthMs = 333, Uninherited = true });
        var fruit = new Fruit { TimeMs = 1000, X = 100 };
        var track = Track("Group", 1500, 250, 2600, 300);
        map.Fruits.Add(fruit); map.Tracks.Add(track);
        var ui = Load(map);
        var baseline = ui.View.Document.DeepClone();

        Click(ui, fruit.TimeMs, fruit.X, false);
        Click(ui, track.Nodes[0].TimeMs, track.Nodes[0].X, true);
        Check(ui.View.SelectedObjectIds.ToHashSet().SetEquals([fruit.Id, track.Id]), "Fixture did not select both parent objects.");
        ui.DownMap(fruit.TimeMs, fruit.X);
        ui.MoveMap(1137, 130);
        ui.UpMap(1137, 130);

        var movedFruit = ui.View.Document.Fruits.Single(item => item.Id == fruit.Id);
        var movedTrack = ui.View.Document.Tracks.Single(item => item.Id == track.Id);
        Near(1100, movedFruit.TimeMs); Near(130, movedFruit.X);
        Near(1600, movedTrack.Nodes[0].TimeMs); Near(280, movedTrack.Nodes[0].X);
        Near(2700, movedTrack.Nodes[1].TimeMs); Near(330, movedTrack.Nodes[1].X);
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(baseline), "One undo did not restore the complete multi-object drag.");
    }

    public static void SingleSliderSnap()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        var track = Track("Snapped Slider", 1000, 200, 2000, 300);
        map.Tracks.Add(track);
        var ui = Load(map);

        ui.DownMap(1000, 200);
        ui.MoveMap(1137, 240);
        ui.UpMap(1137, 240);

        var moved = ui.View.Document.Tracks.Single();
        Near(1125, moved.Nodes[0].TimeMs); Near(240, moved.Nodes[0].X);
        Near(2125, moved.Nodes[1].TimeMs); Near(340, moved.Nodes[1].X);
    }

    public static void PlayfieldPadding()
    {
        var map = new MapDocument { DurationMs = 5000, CircleSize = 5 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 0 });
        map.Fruits.Add(new() { TimeMs = 2000, X = 512 });
        var ui = Load(map);
        var field = ui.View.PlayfieldBounds;
        var canvas = ui.View.CanvasPlotBounds;
        float padding = CatchSize.FruitRadius(0) * field.Width / 512;
        float radius = CatchSize.FruitRadius(map.CircleSize) * field.Width / 512;
        Near(padding, field.X - canvas.X);
        Near(padding, canvas.Right - field.Right);
        Near(54.4, CatchSize.FruitRadius(0));
        var fruits = ui.Canvas.Circles.Where(circle => circle.Filled && circle.Color == 0xFFFFFF
            && Math.Abs(circle.Radius - radius) < 0.001).ToArray();
        Check(fruits.Any(circle => Math.Abs(circle.X - field.X) < 0.001 && circle.X - circle.Radius >= canvas.X - 0.001),
            "The X=0 fruit is still clipped by the canvas edge.");
        Check(fruits.Any(circle => Math.Abs(circle.X - field.Right) < 0.001 && circle.X + circle.Radius <= canvas.Right + 0.001),
            "The X=512 fruit is still clipped by the canvas edge.");
        var timingLines = ui.Canvas.Lines.Where(line => Math.Abs(line.Y1 - line.Y2) < 0.001
            && Math.Abs(line.X1 - field.X) < 0.001 && Math.Abs(line.X2 - field.Right) < 0.001
            && line.Color is 0x343C49 or 0x222933 or 0x845460).ToArray();
        Check(timingLines.Length > 0, "The timing grid fixture did not draw horizontal lines.");
        foreach (var timingLine in timingLines)
            Check(!ui.Canvas.Lines.Any(line => Math.Abs(line.Y1 - timingLine.Y1) < 0.001 && Math.Abs(line.Y2 - timingLine.Y2) < 0.001
                && line.Color == timingLine.Color && (line.X1 < field.X - 0.001 || line.X2 > field.Right + 0.001)),
                "A timing grid line was drawn into the playfield padding.");
    }

    public static void BananaPlacement()
    {
        var ui = Load(new MapDocument { DurationMs = 10000 });
        ui.Key('N');
        ui.ClickMap(1010, 256);
        Check(ui.View.Document.BananaShowers.Count == 1 && ui.View.IsDirty,
            "Left-click did not begin a banana shower transaction.");
        Check(!ui.View.PrepareFileOperation() && !ui.Canvas.Texts.Any(item => item.Value == "开始时间  ms"),
            "An unfinished banana shower allowed file output or precise field editing.");
        var point = Screen(ui, 1990, 256);
        ui.View.PointerDown(point.X, point.Y, 2, false, false);
        ui.View.PointerUp(point.X, point.Y, 2);
        ui.Paint();
        var shower = ui.View.Document.BananaShowers.Single();
        Near(1000, shower.TimeMs); Near(2000, shower.EndTimeMs);
        Check(ui.View.ActiveTool == "Banana" && ui.View.Conversion.Objects.Any(item => item.SourceId == shower.Id),
            "Finishing banana placement did not keep the tool active or generate bananas.");
        ui.SetField("开始时间  ms", "1100");
        ui.SetField("结束时间  ms", "2100");
        Near(1100, shower.TimeMs); Near(2100, shower.EndTimeMs);
        ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true);
        Check(ui.View.Document.BananaShowers.Count == 0, "Banana placement and precise edits were not undoable.");

        var cancel = Load(new MapDocument { DurationMs = 10000 });
        cancel.Key('F'); cancel.ClickMap(500, 128);
        Guid fruitId = cancel.View.Document.Fruits.Single().Id;
        cancel.Key('N'); cancel.ClickMap(1000, 256); cancel.Key('Z', ctrl: true);
        Check(cancel.View.Document.BananaShowers.Count == 0 && cancel.View.Document.Fruits.Any(item => item.Id == fruitId),
            "Undoing an unfinished banana shower also undid the preceding committed object.");
    }

    public static void BananaRectangleEditing()
    {
        var map = new MapDocument { DurationMs = 5000 };
        map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 2000 });
        var overlap = Track("Overlap", 1400, 300, 1600, 350);
        map.Tracks.Add(overlap);
        var ui = Load(map);
        var field = ui.View.PlayfieldBounds;
        var range = ui.Canvas.Outlines.Single(outline => outline.Color == 0x8C7445);
        Near(field.X, range.Bounds.X);
        Near(field.Width, range.Bounds.Width);
        ui.ClickMap(1500, 325);
        Check(ui.View.SelectedObjectIds.Single() == overlap.Id,
            "The banana rectangle intercepted a Slider drawn over its body.");

        ui.Key('N');
        ui.DownMap(1500, 400);
        ui.MoveMap(1760, 400);
        ui.UpMap(1760, 400);
        var shower = ui.View.Document.BananaShowers.Single();
        Near(1250, shower.TimeMs);
        Near(2250, shower.EndTimeMs);
        Check(ui.View.Document.BananaShowers.Count == 1,
            "Dragging an existing banana range in the Banana tool created another shower.");

        ui.Key('Z', ctrl: true);
        shower = ui.View.Document.BananaShowers.Single();
        Near(1000, shower.TimeMs); Near(2000, shower.EndTimeMs);
        ui.ClickMap(1500, 400);
        Check(ui.Canvas.Outlines.Any(outline => outline.Color == 0xF2C66D
            && Math.Abs(outline.Bounds.X - field.X) < 0.001 && Math.Abs(outline.Bounds.Width - field.Width) < 0.001),
            "The selected banana range did not use the visible selected outline.");

        ui.DownMap(1000, 256);
        ui.MoveMap(1125, 256);
        ui.UpMap(1125, 256);
        shower = ui.View.Document.BananaShowers.Single();
        Near(1125, shower.TimeMs); Near(2000, shower.EndTimeMs);

        ui.DownMap(2000, 256);
        ui.MoveMap(2250, 256);
        ui.UpMap(2250, 256);
        shower = ui.View.Document.BananaShowers.Single();
        Near(1125, shower.TimeMs); Near(2250, shower.EndTimeMs);
        ui.Key('Z', ctrl: true);
        shower = ui.View.Document.BananaShowers.Single();
        Near(1125, shower.TimeMs); Near(2000, shower.EndTimeMs);

        ui.ClickMap(1500, 400);
        ui.DownMap(1125, 256);
        ui.MoveMap(1250, 256);
        ui.Key(27);
        ui.UpMap(1250, 256);
        shower = ui.View.Document.BananaShowers.Single();
        Near(1125, shower.TimeMs); Near(2000, shower.EndTimeMs);
    }

    public static void AudioExtendsEditableDuration()
    {
        var map = new MapDocument { DurationMs = 217892 };
        map.Fruits.Add(new() { TimeMs = 215892, X = 256 });
        var ui = Load(map);

        ui.View.UpdateTransport(233762, 421108.25, true, false, false, null, "audio.mp3");
        ui.Paint();
        Near(217892, ui.View.Document.DurationMs);
        Check(!ui.View.IsDirty, "Loading the longer audio track dirtied the imported map.");

        ui.Key('F');
        ui.ClickMap(233750, 128);
        Check(ui.View.Document.Fruits.Any(item => Math.Abs(item.TimeMs - 233750) < 0.001 && Math.Abs(item.X - 128) < 0.001),
            "Fruit placement after the imported object's old duration was still clamped.");
        Near(233750, ui.View.Document.DurationMs);

        ui.Key('N');
        ui.ClickMap(234000, 256);
        var end = Screen(ui, 235000, 256);
        ui.View.PointerDown(end.X, end.Y, 2, false, false);
        ui.View.PointerUp(end.X, end.Y, 2);
        ui.Paint();
        var shower = ui.View.Document.BananaShowers.Single();
        Near(234000, shower.TimeMs);
        Near(235000, shower.EndTimeMs);
        Near(235000, ui.View.Document.DurationMs);
    }

    private static CurveTrack Track(string name, double start, double startX, double end, double endX)
    {
        var track = new CurveTrack { Name = name, Kind = CurveKind.Linear, CompensateTinyDroplets = false };
        track.Nodes.Add(new() { TimeMs = start, X = startX });
        track.Nodes.Add(new() { TimeMs = end, X = endX });
        return track;
    }

    private static Ui Load(MapDocument map)
    {
        var ui = new Ui();
        ui.LoadDocument(map);
        ui.Paint();
        return ui;
    }

    private static void DoubleClick(Ui ui, double time, double x)
    {
        ui.ClickMap(time, x);
        var point = Screen(ui, time, x);
        ui.View.PointerDoubleClick(point.X, point.Y, false, false);
        ui.View.PointerUp(point.X, point.Y, 0);
        ui.Paint();
    }

    private static void Click(Ui ui, double time, double x, bool ctrl)
    {
        var point = Screen(ui, time, x);
        ui.View.PointerDown(point.X, point.Y, 0, false, ctrl);
        ui.View.PointerUp(point.X, point.Y, 0);
        ui.Paint();
    }

    private static (float X, float Y) Screen(Ui ui, double time, double x)
    {
        var field = ui.View.PlayfieldBounds;
        return (field.X + (float)(x / 512) * field.Width,
            field.Bottom - (float)((time - ui.View.ViewStartMs) * ui.View.PixelsPerMs));
    }

    private static void Near(double expected, double actual)
        => Check(double.IsFinite(actual) && Math.Abs(expected - actual) < 0.001, $"Expected {expected:R}, got {actual:R}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
