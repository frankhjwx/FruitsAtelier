using FruitsAtelier.Core;

internal static class SliderModeTests
{
    internal static MapDocument ArcMap()
    {
        var map = new MapDocument { DurationMs = 8000, ApproachRate = 8 };
        var track = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
        var a = new Anchor { TimeMs = 1000, X = 200 };
        track.Nodes.Add(a); track.Nodes.Add(new() { TimeMs = 1500, X = 200 });
        a.OutgoingCurve = new() { Kind = ControlCurveKind.CircularArc, ReferenceScale = ControlCurveMath.ReferenceScale(8) };
        a.OutgoingCurve.Controls.Add(new() { Offset = new(250, 50) });
        map.Tracks.Add(track); return map;
    }

    public static void PersistenceAndAr()
    {
        var map = ArcMap(); var original = map.DeepClone(); var track = map.Tracks[0];
        var positions = Enumerable.Range(0, 101).Select(i => CurveMath.PositionAtTime(track, 1000 + i * 5)).ToArray();
        string serialized = ProjectSerializer.Serialize(map);
        Check(serialized.Contains("\"SchemaVersion\": 3"), "New geometry was saved under the old schema.");
        var restored = ProjectSerializer.Read(serialized);
        Check(restored.ContentEquals(map), "Shared segment definition did not round trip.");
        var project = BeatmapProject.FromDocuments([map]);
        Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Difficulties[0].Document.ContentEquals(map), "Multi-difficulty round trip failed.");
        map.ApproachRate = 3;
        for (int i = 0; i <= 100; i++) Near(positions[i], CurveMath.PositionAtTime(track, 1000 + i * 5), 1e-10);
        track.Nodes[0].OutgoingCurve!.Controls[0].Offset = new(250, 70);
        Check(original.Tracks[0].Nodes[0].OutgoingCurve!.Controls[0].Offset.X == 50 && !map.ContentEquals(original), "Nested clone aliases source.");
        Check(CurveMath.Validate(map).Count == 0, "Valid arc rejected.");
    }

    public static void SharedEditing()
    {
        var map = ArcMap(); var track = map.Tracks[0];
        var original = map.DeepClone(); var vertices = SliderControlEditing.Vertices(track);
        SliderControlEditing.Apply(track, vertices);
        Check(original.ContentEquals(map), "An unchanged projection changed stored content.");
        Check(SliderControlEditing.TryMove(track, [vertices[1].Id], new(0, 10), out _), "Arc point movement failed.");
        Near(60, track.Nodes[0].OutgoingCurve!.Controls[0].Offset.X);
        SliderControlEditing.Insert(track, 0, new(1375, 235));
        Check(track.Nodes[0].OutgoingCurve!.Kind == ControlCurveKind.Bezier, "Fourth point did not change an arc to Bezier.");
        var point = SliderControlEditing.Vertices(track)[1];
        SliderControlEditing.ToggleBoundary(track, point.Id);
        Check(track.Nodes.Count == 3, "Control did not become a segment boundary.");
        SliderControlEditing.ToggleBoundary(track, point.Id);
        Check(track.Nodes.Count == 2, "Boundary did not merge.");
        var before = map.DeepClone();
        Check(!SliderControlEditing.TryMove(track, [track.Nodes[0].Id], new(1000, 0), out _), "Backward-time edit accepted.");
        Check(map.ContentEquals(before), "Invalid edit partially applied.");
        Check(SliderControlEditing.Remove(track, [point.Id]), "Removing one control deleted the slider.");
        Check(CurveMath.Validate(map).Count == 0, "Control removal broke curve validation.");
    }

    public static void PenConversionAndHistory()
    {
        foreach (bool higher in new[] { false, true })
        {
            var map = ArcMap(); var track = map.Tracks[0];
            if (higher)
            {
                track.Nodes[0].OutgoingCurve!.Kind = ControlCurveKind.Bezier;
                track.Nodes[0].OutgoingCurve!.Controls.Clear();
                for (int i = 1; i <= 5; i++) track.Nodes[0].OutgoingCurve!.Controls.Add(new() { Offset = new(i * 500.0 / 6, i % 2 == 0 ? -50 : 80) });
            }
            var history = new EditorHistory(map); var before = history.Document.DeepClone(); var edited = history.Document.Tracks[0];
            history.Begin("pen handle");
            ControlCurveEditing.ConvertToPen(edited, 0);
            Check(edited.Nodes.All(n => n.OutgoingCurve is null), "Pen conversion left a custom segment.");
            Check(edited.Nodes[0].Id == track.Nodes[0].Id && edited.Nodes[^1].Id == track.Nodes[^1].Id, "Conversion changed endpoint identity.");
            for (int i = 0; i <= 2000; i++) Near(CurveMath.PositionAtTime(track, 1000 + i / 4.0), CurveMath.PositionAtTime(edited, 1000 + i / 4.0), 0.01);
            Check(CurveMath.Validate(history.Document).Count == 0, "Pen approximation is invalid.");
            history.Commit(); history.Undo();
            Check(before.ContentEquals(history.Document), "Undo did not restore exact control geometry.");
            history.Redo(); Check(history.Document.Tracks[0].Nodes.All(n => n.OutgoingCurve is null), "Redo lost converted geometry.");
        }
    }

    public static void SplitReverseAndConversion()
    {
        var map = ArcMap(); var track = map.Tracks[0]; var original = map.DeepClone().Tracks[0];
        CurveMath.Split(track, 0, 0.4);
        for (int i = 0; i <= 500; i++) Near(CurveMath.PositionAtTime(original, 1000 + i), CurveMath.PositionAtTime(track, 1000 + i), 1e-7);
        var removed = map.DeepClone();
        CurvePointEditing.Remove(removed.Tracks[0], removed.Tracks[0].Nodes[1].Id);
        Check(CurveMath.Validate(removed).Count == 0 && removed.Tracks[0].Nodes.All(n => n.OutgoingCurve is null),
            "Pen deletion did not convert the adjacent exact arc segments.");
        SliderControlEditing.Reverse(track);
        for (int i = 0; i <= 500; i++) Near(CurveMath.PositionAtTime(original, 1500 - i), CurveMath.PositionAtTime(track, 1000 + i), 1e-7);
        track.SpanCount = 3;
        var cache = new CatchConversionCache();
        var conversion = CatchStreamConverter.Convert(map, cache: cache);
        Check(conversion.Success && conversion.Sliders.Count == 1, "Shared curve did not generate a slider.");
        var exported = OsuBeatmapWriter.Serialize(map);
        Check(exported.ReadBack.ImportedSliders.Count == 1 && exported.ReadBack.ImportedSliders[0].SpanCount == 3,
            "Exact shared geometry did not export as one repeated osu slider.");
        var control = track.Nodes[0].OutgoingCurve!.Controls[0]; control.Offset = new(control.Offset.TimeMs, control.Offset.X + 2);
        var cached = CatchStreamConverter.Convert(map, cache: cache); var full = CatchStreamConverter.Convert(map);
        Check(cached.Success == full.Success && cached.Objects.SequenceEqual(full.Objects), "Segment edit left stale conversion cache.");
    }

    public static void RejectBadArcsAndControls()
    {
        var map = ArcMap(); var track = map.Tracks[0];
        track.Nodes[0].OutgoingCurve!.Controls[0].Offset = new(10, 250);
        Check(CurveMath.Validate(map).Count > 0, "Time-reversing arc accepted.");
        track.Nodes[0].OutgoingCurve!.Controls[0].Offset = new(250, 50);
        track.Nodes[0].OutgoingCurve!.ReferenceScale = double.NaN;
        Check(CurveMath.Validate(map).Count > 0, "Invalid reference scale accepted.");
        track.Nodes[0].OutgoingCurve!.ReferenceScale = 1;
        track.Nodes[0].OutgoingCurve!.Controls[0].Id = track.Id;
        Check(CurveMath.Validate(map).Count > 0, "Duplicate control identity accepted.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double a, double b, double tolerance = 1e-7) { if (Math.Abs(a - b) > tolerance) throw new Exception($"Expected {a}, got {b}."); }
}
