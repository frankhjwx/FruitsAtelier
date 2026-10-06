using FruitsAtelier.Core;

internal static class StreamFruitDragTests
{
    public static void EditEvents()
    {
        foreach (int spans in new[] { 1, 2 })
        foreach (double selectedTime in spans == 1 ? new[] { 1400d } : new[] { 1400d, 2200d })
        {
            var map = Fixture(spans);
            var ui = Prepare(map, selectedTime);
            var before = ui.View.Conversion.Objects.Where(o => o.SourceId == map.Tracks[0].Id).ToArray();
            ui.DownMap(selectedTime, 200); ui.MoveMap(selectedTime, 280); ui.UpMap(selectedTime, 280);
            var after = ui.View.Conversion.Objects.Where(o => o.SourceId == map.Tracks[0].Id).ToArray();
            Check(after.Length == before.Length && after.Select((o, i) =>
                Math.Abs(o.TimeMs - before[i].TimeMs) < .001
                && Math.Abs(o.X - (Math.Abs(CurveMath.FirstSpanTime(map.Tracks[0], o.TimeMs) - 1400) < .001 ? 280 : before[i].X)) < .001).All(v => v),
                "Middle stream fruit dragging changed neighbouring samples or event times.");
            Check(ui.View.Document.Tracks[0].StreamSnapDivisor == 2 && ui.View.Document.Tracks[0].SpanCount == spans,
                "Stream fruit editing lost its subdivision or repeats.");
            var edited = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Stream fruit edit did not undo atomically.");
            ui.Key('Y', ctrl: true); Check(edited.ContentEquals(ui.View.Document), "Stream fruit edit did not redo.");
        }
        foreach (bool head in new[] { true, false })
        foreach (bool timeDrag in new[] { false, true })
        {
            var map = Fixture(1);
            double time = head ? 1000 : 1800, movedTime = timeDrag ? head ? 900 : 1900 : time;
            var ui = Prepare(map, time);
            ui.DownMap(time, 200); ui.MoveMap(movedTime, 240); ui.UpMap(movedTime, 240);
            var track = ui.View.Document.Tracks.Single();
            var endpoint = head ? track.Nodes[0] : track.Nodes[^1];
            var fixedEndpoint = head ? track.Nodes[^1] : track.Nodes[0];
            Check(Math.Abs(endpoint.TimeMs - movedTime) < .001 && Math.Abs(endpoint.X - 240) < .001
                && fixedEndpoint.TimeMs == (head ? 1800 : 1000) && fixedEndpoint.X == 200 && track.StreamSnapDivisor == 2,
                "Stream endpoint drag did not edit time and X while preserving the opposite endpoint.");
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Stream endpoint edit did not undo atomically.");
            ui.DownMap(time, 200); ui.MoveMap(movedTime, 240); ui.Key(27);
            Check(map.ContentEquals(ui.View.Document), "Cancelled stream endpoint drag retained changes.");
        }
        {
            var map = Fixture(2);
            var ui = Prepare(map, 2600);
            ui.DownMap(2600, 200); ui.MoveMap(2800, 240); ui.UpMap(2800, 240);
            var track = ui.View.Document.Tracks.Single();
            Check(CurveMath.EndTimeMs(track) == 2800 && track.Nodes[^1].TimeMs == 1900 && track.Nodes[0].X == 240,
                "Repeated stream tail did not resize its span or move the final traversal endpoint.");
            ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Repeated stream tail edit did not undo.");
        }

        static MapDocument Fixture(int spans)
        {
            var map = new MapDocument { DurationMs = 6000, BeatLengthMs = 400, IsDemo = false };
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 2, SpanCount = spans };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 200 }, new() { TimeMs = 1800, X = 200 }]);
            map.Tracks.Add(track); return map;
        }
        static Ui Prepare(MapDocument map, double time)
        {
            var ui = new Ui(false); ui.LoadDocument(map);
            ui.ClickMap(time, 200);
            ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.sliderPathCurves"));
            return ui;
        }
    }

    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 400 };
        var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 16 };
        track.Nodes.AddRange([
            new Anchor { TimeMs = 1000, X = 200 },
            new Anchor { TimeMs = 1075, X = 200 },
            new Anchor { TimeMs = 1100, X = 200 }
        ]);
        map.Tracks.Add(track);

        var ui = new Ui();
        ui.LoadDocument(map);
        var fruits = ui.View.Conversion.Objects.Where(item => item.SourceId == track.Id).ToArray();
        var last = fruits[^1];
        var preceding = fruits[^2];
        if (last.Kind != CatchObjectKind.Fruit || preceding.Kind != CatchObjectKind.Fruit)
            throw new Exception("Stream fixture did not produce adjacent fruits.");

        // This point is inside the final fruit's lower half and nearer the preceding fruit.
        double pointerTime = last.TimeMs - (last.TimeMs - preceding.TimeMs) * .55;
        ui.ClickMap(pointerTime, last.X + 20);
        if (!ui.View.SelectedObjectIds.Contains(track.Id))
            throw new Exception("The first click did not select the stream parent.");
        var originalPosition = ui.ScreenAt(last.TimeMs, last.X);
        Check(!OuterRing(ui, originalPosition), "Selecting the stream parent highlighted an individual fruit.");

        ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.sliderPathCurves"));
        var parentBox = ui.View.SelectionTransformBounds;
        float parentX = parentBox.X + 2, parentY = parentBox.Y + 2;
        float parentMovedX = parentX + ui.View.PlayfieldBounds.Width * 80 / 512;
        float parentMovedY = parentY - (float)(100 * ui.View.PixelsPerMs);
        ui.View.PointerDown(parentX, parentY, 0, false, false);
        ui.View.PointerMove(parentMovedX, parentMovedY, false, false);
        ui.View.PointerUp(parentMovedX, parentMovedY, 0); ui.Paint();
        var moved = ui.View.Document.Tracks.Single();
        Check(moved.StreamSnapDivisor == 16 && moved.Nodes.Select((node, index) =>
            Math.Abs(node.TimeMs - map.Tracks[0].Nodes[index].TimeMs - 100) < .001
            && Math.Abs(node.X - map.Tracks[0].Nodes[index].X - 80) < .001).All(matched => matched),
            "Dragging the selected stream did not move the whole parent in time and X.");
        ui.Key('Z', ctrl: true);
        Check(map.ContentEquals(ui.View.Document), "Undo did not restore the whole stream move.");

        var childUi = new Ui(); childUi.LoadDocument(map);
        childUi.ClickMap(pointerTime, last.X + 20);
        childUi.ClickText(FruitsAtelier.Localization.Strings.Get("ui.sliderPathCurves"));
        childUi.ClickMap(pointerTime, last.X);
        Check(OuterRing(childUi, childUi.ScreenAt(last.TimeMs, last.X)),
            "The second click did not highlight the selected stream fruit.");
        var box = childUi.View.SelectionTransformBounds;
        float boxX = box.X + 2, boxY = box.Y + 2;
        float movedX = boxX + childUi.View.PlayfieldBounds.Width * 80 / 512;
        childUi.View.PointerDown(boxX, boxY, 0, false, false);
        childUi.View.PointerMove(movedX, boxY, false, false);
        childUi.View.PointerUp(movedX, boxY, 0); childUi.Paint();
        var changed = childUi.View.Conversion.Objects.Where(item => item.SourceId == track.Id).ToArray();
        Check(Math.Abs(changed[^1].X - (last.X + 80)) < .001,
            "Box movement did not translate the final stream fruit.");
        Check(Math.Abs(changed[^1].TimeMs - last.TimeMs) < .001,
            "Box movement changed event time during a horizontal drag.");
        Check(Math.Abs(changed[^2].X - preceding.X - 80) < .001,
            "Box movement did not translate the preceding fruit.");
        Check(changed.Length == fruits.Length && changed.Select((item, index) =>
            Math.Abs(item.X - fruits[index].X - 80) < .001 && Math.Abs(item.TimeMs - fruits[index].TimeMs) < .001).All(matched => matched),
            "Box movement did not translate every stream fruit without changing its time.");
        var translated = childUi.View.Document.DeepClone();
        childUi.Key('Z', ctrl: true);
        Check(map.ContentEquals(childUi.View.Document), "Undo did not restore the stream box move.");
        childUi.Key('Y', ctrl: true);
        Check(translated.ContentEquals(childUi.View.Document), "Redo did not restore the complete stream box move.");
    }

    private static bool OuterRing(Ui ui, (float X, float Y) position)
        => ui.Canvas.Circles.Any(circle => !circle.Filled && circle.Color == 0xE7EBF2
            && Math.Abs(circle.X - position.X) < 1 && Math.Abs(circle.Y - position.Y) < 1);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
