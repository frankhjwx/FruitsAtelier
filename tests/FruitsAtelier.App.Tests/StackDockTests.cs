using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class StackDockTests
{
    private static MapDocument Fixture(bool stack = false)
    {
        var map = new MapDocument { DurationMs = 6000 };
        var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = stack ? 4 : null, Stack = stack ? new() : null };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 2000, X = 256 }]);
        map.Tracks.Add(track); return map;
    }
    private static Ui Open(MapDocument map)
    {
        var ui = new Ui(false); ui.LoadDocument(map); ui.SelectTrack(map.Tracks[0].Id);
        ui.Key('F', ctrl: true, shift: true); return ui;
    }
    private static void Locales(Action run)
    {
        string language = L.Language;
        try { foreach (string locale in new[] { "en", "zh-CN" }) { L.SetLanguage(locale); run(); } }
        finally { L.SetLanguage(language); }
    }
    public static void Run() => Locales(() =>
    {
        foreach (int kind in new[] { 0, 1, 2 })
        {
            var map = Fixture(kind == 2);
            if (kind == 1) map.Tracks[0].StreamSnapDivisor = 6;
            var ui = Open(map);
            Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0
                && (ui.View.StackGraphBounds.Width > 0) == (kind == 2), "Shortcut focuses the matching live pattern controls");
            if (kind == 0)
            {
                Check(ui.View.Document.Tracks[0].StreamSnapDivisor == 4, "Ordinary slider converts to Stream");
                ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Initial conversion undoes atomically");
                ui.SelectTrack(map.Tracks[0].Id); ui.Key('F', ctrl: true, shift: true);
            }
            else Check(ui.View.Document.ContentEquals(map), "Existing patterns retain their mode and division");
            ui.ConversionMode(true);
            Check(ui.View.Document.Tracks[0].Stack is not null && ui.View.StackGraphBounds.Width > 0, "Stack mode applies immediately");
            var baseline = ui.View.Document.DeepClone();
            var graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.X + graph.Width * .25f, graph.Bottom - graph.Height * .25f, 0, false, false);
            ui.View.PointerMove(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * 1.5f, false, false);
            ui.Paint(); Check(ui.View.Document.ContentEquals(baseline), "Graph gesture retains a local preview until release");
            ui.View.PointerUp(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * 1.5f, 0); ui.Paint();
            Check(!ui.View.WantsCapture && ui.View.Document.Tracks[0].Stack!.Points.Any(p => p.Distance == 32), "Release commits the bounded envelope");
            var accepted = ui.View.Document.DeepClone();
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(accepted)).ContentEquals(accepted), "Envelope persists");
            Check(OsuBeatmapWriter.Serialize(accepted).ObjectSequenceMatches, "Envelope export matches gameplay");
            var positions = ui.View.Conversion.Objects.Select(o => o.X).ToArray();
            ui.Key('H', ctrl: true);
            Check(ui.View.Conversion.Objects.Select(o => o.X).Zip(positions).All(p => Math.Abs(p.First - (512 - p.Second)) < .001), "Mirror reflects the center path and alternating Stack sides");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(accepted), "Mirror undo preserves the envelope");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(baseline), "Graph edit undoes in one step");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(accepted), "Graph edit redoes");
            ui.SelectTrack(map.Tracks[0].Id); ui.Key('F', ctrl: true, shift: true);
            graph = ui.View.StackGraphBounds;
            ui.View.PointerDown(graph.Right, graph.Bottom, 0, false, false);
            ui.View.PointerMove(graph.Right, graph.Bottom - graph.Height * .25f, false, false);
            ui.View.CancelInteraction(); ui.Paint();
            Check(ui.View.Document.ContentEquals(accepted), "Lost capture cancels the graph gesture");
            ui.ConversionMode(false);
            Check(ui.View.Document.Tracks[0].Stack is null, "Stream mode clears the envelope");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(accepted), "Mode undo restores the envelope");
            ui.SelectTrack(map.Tracks[0].Id); ui.Key('F', ctrl: true, shift: true);
            var expected = OsuBeatmapWriter.Serialize(accepted).Text;
            ui.ClickText(L.Get("stream.breakFruits"));
            Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.Fruits.Count > 0
                && OsuBeatmapWriter.Serialize(ui.View.Document).Text == expected, "Breaking preserves exported events");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(accepted), "Break undo restores the managed pattern");
        }
    });
    public static void ManualFruits() => Locales(() =>
    {
        foreach (int spans in new[] { 1, 2 })
        {
            var map = Fixture(true); map.Tracks[0].SpanCount = spans; var ui = Open(map);
            ui.ClickText(L.Get("ui.sliderPathCurves"));
            if (ui.View.DistanceSnapEnabled) ui.Key('Y');
            if (ui.View.EditorGridSettings.Enabled) ui.Key('T');
            var before = ui.View.Conversion.Objects.ToArray();
            var fruit = before[2]; ui.ClickMap(fruit.TimeMs, fruit.X);
            ui.DownMap(fruit.TimeMs, fruit.X); ui.MoveMap(fruit.TimeMs, fruit.X + 32); ui.UpMap(fruit.TimeMs, fruit.X + 32);
            var after = ui.View.Conversion.Objects.ToArray();
            Check(after.Length == before.Length && after[2].TimeMs == fruit.TimeMs && Math.Abs(after[2].X - fruit.X - 32) < .001,
                "Canvas Stack fruit drag changes X while retaining time");
            Check(after.Where((f, i) => i != 2).Select(f => (f.TimeMs, f.X)).SequenceEqual(before.Where((f, i) => i != 2).Select(f => (f.TimeMs, f.X))),
                "Manual adjustment preserves neighbours");
            var edited = ui.View.Document.DeepClone();
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(edited)).ContentEquals(edited), "Manual adjustment persists");
            ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Manual adjustment undoes atomically");
            ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(edited), "Manual adjustment redoes");
            ui.DownMap(fruit.TimeMs, after[2].X); ui.MoveMap(fruit.TimeMs, after[2].X - 15); ui.Key(27); ui.UpMap(fruit.TimeMs, after[2].X - 15);
            Check(ui.View.Document.ContentEquals(edited), "Cancelled fruit drag preserves accepted content");
            Check(OsuBeatmapWriter.Serialize(edited).ObjectSequenceMatches, "Manual Stack adjustment exports the accepted event sequence");
        }
    });
    public static void Numeric() => Locales(() =>
    {
        var ui = Open(Fixture(true));
        var graph = ui.View.StackGraphBounds;
        Check(ui.Canvas.Lines.Count(line => Math.Abs(line.X1 - graph.X) < .01 && Math.Abs(line.X2 - graph.Right) < .01
            && line.Y1 == line.Y2 && line.Y1 >= graph.Y && line.Y1 <= graph.Bottom) == 33, "Graph retains 32 divisions");
        ui.Click(graph.X + graph.Width * .02f, graph.Bottom - graph.Height * 24 / 32);
        void Value(bool percent, string text)
        {
            var r = percent ? ui.View.StackPercentFieldBounds : ui.View.StackDistanceFieldBounds;
            ui.Click(r.X + 10, r.Y + 12); ui.Key('A', ctrl: true); ui.Type(text); ui.Key(13); ui.Paint();
        }
        Value(true, "20.5"); Value(false, "26.25");
        Check(ui.View.Document.Tracks[0].Stack!.Points.Any(p => Math.Abs(p.Progress - .205) < 1e-8 && p.Distance == 26.25),
            "Numeric controls commit exact percentage and pixels");
        var saved = ui.View.Document.DeepClone();
        Value(true, "100"); Check(ui.View.IsEditingText && ui.View.Document.Tracks[0].Stack!.IsValid, "Out-of-order time is rejected while retaining valid live input"); ui.Key(27);
        Value(false, "33"); Check(ui.View.IsEditingText && ui.View.Document.Tracks[0].Stack!.IsValid, "Out-of-range distance is rejected while retaining valid live input"); ui.Key(27);
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(saved)).ContentEquals(saved), "Numeric edits persist");
    });
    public static void DraftHistory() => Locales(() =>
    {
        var map = Fixture(true); var ui = Open(map); var graph = ui.View.StackGraphBounds;
        ui.Click(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * .25f);
        var added = ui.View.Document.DeepClone();
        Check(added.Tracks[0].Stack!.Points.Count == map.Tracks[0].Stack!.Points.Count + 1, "Graph click commits one control point");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Graph addition uses document undo");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(added), "Graph addition redoes");
        ui.SelectTrack(map.Tracks[0].Id); ui.Key('F', ctrl: true, shift: true);
        graph = ui.View.StackGraphBounds;
        ui.View.PointerDown(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * .25f, 2, false, false);
        ui.View.PointerUp(graph.X + graph.Width * .3f, graph.Bottom - graph.Height * .25f, 2); ui.Paint();
        Check(ui.View.Document.ContentEquals(map), "Right click removes the control point");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(added), "Removal undo restores the point");
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(map), "Undo returns to the original envelope");
        ui.Key('Z', ctrl: true, shift: true); Check(ui.View.Document.ContentEquals(added), "Shift-Z redoes the graph edit");
    });
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
