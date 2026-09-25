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
            throw new Exception($"The lower half of the final stream fruit did not select its slider: selected={string.Join(',', ui.View.SelectedObjectIds)}, status={ui.View.StatusMessage}, radius={CatchSize.FruitRadius(map.CircleSize) * ui.Plot.Width / 512}, pixelsPerMs={ui.View.PixelsPerMs}.");
        ui.DownMap(pointerTime, last.X);
        ui.MoveMap(pointerTime, last.X + 80);
        ui.UpMap(pointerTime, last.X + 80);

        var changed = ui.View.Conversion.Objects.Where(item => item.SourceId == track.Id).ToArray();
        if (Math.Abs(changed[^1].X - (last.X + 80)) > .001)
            throw new Exception($"Dragging the lower half of the final stream fruit did not move it: {changed[^1].X} (expected {last.X + 80}).");
        if (Math.Abs(changed[^2].X - preceding.X) > .001)
            throw new Exception($"Dragging the final stream fruit moved the preceding fruit: {changed[^2].X} (expected {preceding.X}).");
        ui.Key('Z', ctrl: true);
        if (!map.ContentEquals(ui.View.Document))
            throw new Exception("Undo did not restore the slider stream.");

        ui.DownMap(last.TimeMs, last.X);
        ui.MoveMap(last.TimeMs + 100, last.X);
        ui.UpMap(last.TimeMs + 100, last.X);
        var moved = ui.View.Document.Tracks.Single();
        if (Math.Abs(moved.Nodes[0].TimeMs - 1100) > .001 || moved.StreamSnapDivisor != 16)
            throw new Exception("Dragging a selected stream vertically did not move the whole stream on the time axis.");
        ui.Key('Z', ctrl: true);
        if (!map.ContentEquals(ui.View.Document))
            throw new Exception("Undo did not restore the vertically moved slider stream.");
    }
}
