using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class CanvasBookmarkTests
{
    public static void AxisColorsAndLines()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.Add(new() { TimeMs = 1500, BeatLengthMs = 500 });
        SongSetup.Set(map, "Editor", "Bookmarks", "2000");
        var ui = new Ui(); ui.LoadDocument(map);
        var plot = ui.View.CanvasPlotBounds;
        var labels = ui.Canvas.Texts.Where(label => label.X < plot.X && label.Y >= plot.Y && label.Y < plot.Bottom).ToArray();
        Check(labels.Any(label => label.Value == "00:01:500" && label.Color == 0xFF7F8D),
            "Timing point did not color its time label red.");
        Check(labels.Any(label => label.Value == "00:02:000" && label.Color == 0x4B9EF5),
            "Bookmark did not color its time label blue.");
        Check(labels.Any(label => label.Value == "00:03:000" && label.Color != 0x4B9EF5 && label.Color != 0xFF7F8D),
            "Ordinary time label inherited a marker color.");
        foreach (uint color in new uint[] { 0xFF7F8D, 0x4B9EF5 })
        {
            Check(ui.Canvas.Lines.Any(line => line.Color == color && line.X1 == plot.X && line.X2 == plot.Right
                && line.Y1 == line.Y2 && line.Width == 1.5f),
                "Marker line does not span the same canvas width as the playhead.");
            Check(!ui.Canvas.Lines.Any(line => line.Color == color && line.X1 < plot.X && line.X2 <= plot.X),
                "Marker still has a short tick beside its time label.");
        }
    }

    public static void DenseAxisMarks()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.Add(new() { TimeMs = 1500, BeatLengthMs = 500 });
        map.TimingPoints.Add(new() { TimeMs = 2500, BeatLengthMs = 500 });
        map.TimingPoints.Add(new() { TimeMs = 2501, BeatLengthMs = 500 });
        SongSetup.Set(map, "Editor", "Bookmarks", string.Join(',', Enumerable.Range(1000, 5000)));
        var ui = new Ui(); ui.LoadDocument(map);
        var original = ui.View.Document.DeepClone();
        var axis = ui.View.CanvasPlotBounds;
        var blueLines = BlueLines();
        Check(blueLines.Length > 0 && blueLines.Length <= Math.Ceiling(axis.Height) + 2,
            "Dense bookmarks were not bounded to visible pixel rows.");
        Check(ui.Canvas.Lines.Any(line => line.Color == 0x4B9EF5 && line.Opacity < .3f
            && line.X1 == axis.X && line.X2 == axis.Right),
            "Bookmark lines were not extended into the canvas.");
        Check(ui.Canvas.Lines.Any(line => line.Color == 0xFF7F8D && line.X1 == axis.X
            && line.X2 == axis.Right), "Red timing line is missing from the canvas.");
        Check(ui.Canvas.Texts.Any(label => label.Color == 0xFF7F8D && label.Value == "00:01:500"),
            "Red timing label is missing from the left axis.");
        var labels = ui.Canvas.Texts.Where(label => (label.Color == 0xFF7F8D || label.Color == 0x4B9EF5)
            && label.X < axis.X && label.Y >= axis.Y && label.Y < axis.Bottom)
            .OrderBy(label => label.Y).ToArray();
        Check(labels.Zip(labels.Skip(1)).All(pair => pair.First.Y + 14 <= pair.Second.Y),
            "Dense axis labels overlap.");

        var cluster = blueLines.First();
        ui.View.PointerMove(axis.X - 5, cluster.Y1, false, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(label => label.Value.Contains(L.Get("axis.bookmarkCount", 2).Split(' ')[^1])
            && label.Value.Contains('–')), "Cluster hover did not show its count and time range.");
        Check(original.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Rendering or hover changed bookmark data.");
        float redClusterY = axis.Bottom - (float)((2500 - ui.View.ViewStartMs) * ui.View.PixelsPerMs);
        ui.View.PointerMove(axis.X - 20, redClusterY, false, false); ui.Paint();
        Check(ui.Canvas.Texts.Any(label => label.Value.Contains(L.Get("axis.timingCount", 2).Split(' ')[^1])
            && label.Value.Contains('–')), "Timing cluster hover did not show its count and time range.");

        ui.View.UpdateTransport(6500, 10000, true, false, false, null, null);
        ui.Key(66, ctrl: true);
        Check(OsuTimeline.Bookmarks(ui.View.Document).Count == 5001, "Adding a bookmark merged stored entries.");
        var saved = ui.View.Document.DeepClone();
        ui.Key(90, ctrl: true);
        Check(original.ContentEquals(ui.View.Document), "Bookmark edit did not undo completely.");
        ui.Key(89, ctrl: true);
        Check(saved.ContentEquals(ui.View.Document), "Bookmark edit did not redo completely.");
        ui.LoadDocument(saved);
        Check(OsuTimeline.Bookmarks(ui.View.Document).Count == 5001, "Reload lost individual bookmarks.");
        ui.LoadDocument(map);
        Check(OsuTimeline.Bookmarks(ui.View.Document).Count == 5000, "Reload retained the prior bookmark cache.");

        RecordingCanvas.Segment[] BlueLines() => ui.Canvas.Lines.Where(line => line.Color == 0x4B9EF5
            && line.X1 == axis.X && line.X2 == axis.Right && line.Y1 >= axis.Y && line.Y1 <= axis.Bottom).ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
