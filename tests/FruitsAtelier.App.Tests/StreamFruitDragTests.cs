using FruitsAtelier.Core;

internal static class StreamFruitDragTests
{
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
        ui.DownMap(pointerTime, last.X);
        ui.MoveMap(pointerTime + 100, last.X + 80);
        ui.UpMap(pointerTime + 100, last.X + 80);
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
        childUi.DownMap(pointerTime, last.X);
        childUi.MoveMap(pointerTime, last.X + 80);
        childUi.UpMap(pointerTime, last.X + 80);
        var changed = childUi.View.Conversion.Objects.Where(item => item.SourceId == track.Id).ToArray();
        Check(Math.Abs(changed[^1].X - (last.X + 80)) < .001,
            "Box movement did not translate the final stream fruit.");
        Check(Math.Abs(changed[^1].TimeMs - last.TimeMs) < .001,
            "Box movement changed event time during a horizontal drag.");
        Check(Math.Abs(changed[^2].X - preceding.X - 80) < .001,
            "Box movement did not translate the preceding fruit.");
        childUi.Key('Z', ctrl: true);
        Check(map.ContentEquals(childUi.View.Document), "Undo did not restore the stream box move.");
    }

    private static bool OuterRing(Ui ui, (float X, float Y) position)
        => ui.Canvas.Circles.Any(circle => !circle.Filled && circle.Color == 0xE7EBF2
            && Math.Abs(circle.X - position.X) < 1 && Math.Abs(circle.Y - position.Y) < 1);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
