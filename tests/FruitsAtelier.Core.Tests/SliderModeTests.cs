using FruitsAtelier.Core;

internal static class SliderModeTests
{
    public static void EndpointOverhangs()
    {
        foreach (bool explicitControls in new[] { false, true })
        foreach (bool start in new[] { false, true })
        foreach (bool end in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 5000 };
            var track = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
            track.Nodes.Add(new() { TimeMs = 1000, X = 200, HandleOut = new(start ? -200 : 200, 20) });
            track.Nodes.Add(new() { TimeMs = 2000, X = 240, HandleIn = new(end ? 200 : -200, -20) });
            if (explicitControls)
            {
                track.Nodes[0].OutgoingCurve = new() { Kind = ControlCurveKind.Bezier };
                track.Nodes[0].OutgoingCurve!.Controls.Add(new() { Offset = track.Nodes[0].HandleOut });
                track.Nodes[0].OutgoingCurve!.Controls.Add(new() { Offset = new(1000 + track.Nodes[1].HandleIn.TimeMs, 20) });
            }
            map.Tracks.Add(track);
            Check(CurveMath.Validate(map).Count == 0, "Valid endpoint overhang rejected.");
            var points = ControlCurveMath.Points(track, 0);
            for (int i = 1; i < 1000; i++)
            {
                var point = ControlCurveMath.Bezier(points, i / 1000.0);
                if (point.TimeMs > 1000 && point.TimeMs < 2000)
                    Near(point.X, CurveMath.PositionAtTime(track, point.TimeMs), 1e-7);
            }
            var result = CatchStreamConverter.Convert(map);
            Check(result.Success && result.Objects.All(o => o.TimeMs >= 1000 && o.TimeMs <= 2000 && o.X is >= 0 and <= 512),
                "Overhang produced invalid catch events.");
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).ContentEquals(map), "Overhang did not persist.");
            Check(OsuBeatmapWriter.Serialize(map).ReadBack.ImportedSliders.Count == 1, "Overhang did not export.");
            if (explicitControls)
            {
                var before = map.DeepClone().Tracks[0];
                ControlCurveEditing.ConvertToPen(track, 0);
                for (int i = 0; i <= 100; i++) Near(CurveMath.PositionAtTime(before, 1000 + 10 * i), CurveMath.PositionAtTime(track, 1000 + 10 * i));
            }
            track.Nodes[0].HandleOut = new(2000, 20);
            track.Nodes[1].HandleIn = new(-2000, -20);
            Check(CurveMath.Validate(map).Count > 0 && !CatchStreamConverter.Convert(map).Success,
                "Ambiguous interior time reversal generated a slider.");
        }
    }

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
        EndpointCrossings();
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

    private static void EndpointCrossings()
    {
        foreach (bool head in new[] { true, false })
        {
            var history = new EditorHistory(ArcMap());
            var before = history.Document.DeepClone();
            var track = history.Document.Tracks[0];
            var vertices = SliderControlEditing.Vertices(track);
            Guid dragged = vertices[1].Id, replaced = vertices[head ? 0 : 2].Id;
            double time = head ? 750 : 1750;
            history.Begin("cross endpoint");
            Check(SliderControlEditing.TryMove(track, [dragged], new(time - 1250, 0), out var error), error);
            Check(track.Nodes[head ? 0 : track.Nodes.Count - 1].Id == dragged, "Dragged control did not become endpoint.");
            Check(SliderControlEditing.Vertices(track).Any(v => v.Id == replaced && v.Type is null), "Old endpoint did not become interior.");
            Near(head ? 750 : 1000, track.Nodes[0].TimeMs);
            Near(head ? 1500 : 1750, CurveMath.EndTimeMs(track));
            Check(CurveMath.Validate(history.Document).Count == 0, "Crossing produced invalid geometry.");
            history.Commit(); var after = history.Document.DeepClone();
            history.Undo(); Check(before.ContentEquals(history.Document), "Crossing undo changed geometry.");
            history.Redo(); Check(after.ContentEquals(history.Document), "Crossing redo changed geometry.");
            track = history.Document.Tracks[0];
            Check(SliderControlEditing.TryMove(track, [dragged], new(head ? -25 : 25, 5), out error), error);
            Check(track.Nodes[head ? 0 : track.Nodes.Count - 1].Id == dragged, "Continued editing lost endpoint identity.");
        }
        var ordinary = ArcMap(); var curve = ordinary.Tracks[0];
        var controls = SliderControlEditing.Vertices(curve);
        Check(SliderControlEditing.TryMove(curve, [controls[1].Id], new(10, 5), out var ordinaryError), ordinaryError);
        Check(curve.Nodes[0].Id == controls[0].Id && curve.Nodes[^1].Id == controls[^1].Id, "Ordinary dragging exchanged endpoints.");
        Near(1260, SliderControlEditing.Vertices(curve)[1].Point.TimeMs);
        Near(255, SliderControlEditing.Vertices(curve)[1].Point.X);
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

    public static void CompactArcConversion()
    {
        double maximumError = 0; int maximumAnchors = 0;
        foreach (double radius in new[] { 25d, 100d, 240d })
        foreach (double scale in new[] { .1, .4, 2 })
        foreach (int direction in new[] { -1, 1 })
        foreach (double sweep in new[] { Math.PI / 6, Math.PI / 2, Math.PI })
        {
            double extent = radius * Math.Sin(sweep / 2), endpointX = 256 + direction * radius * Math.Cos(sweep / 2);
            var map = new MapDocument { DurationMs = 10000 };
            var track = new CurveTrack { Kind = CurveKind.Bezier, CompensateTinyDroplets = false };
            var curve = new ControlCurve { Kind = ControlCurveKind.CircularArc, ReferenceScale = scale };
            curve.Controls.Add(new() { Offset = new(extent / scale, 256 + direction * radius - endpointX) });
            track.Nodes.AddRange([new() { TimeMs = 2000, X = endpointX, OutgoingCurve = curve },
                new() { TimeMs = 2000 + 2 * extent / scale, X = endpointX }]);
            map.Tracks.Add(track);
            var history = new EditorHistory(map); var edited = history.Document.Tracks[0];
            history.Begin("arc edit"); ControlCurveEditing.ConvertToPen(edited, 0);
            maximumAnchors = Math.Max(maximumAnchors, edited.Nodes.Count);
            Check(edited.Nodes.Count <= 9, "A simple circular arc acquired excessive pen anchors.");
            Check(edited.Nodes[0].Id == track.Nodes[0].Id && edited.Nodes[^1].Id == track.Nodes[^1].Id,
                "Compact arc conversion replaced authored endpoints.");
            for (int i = 0; i <= 2000; i++)
            {
                double y = extent * (i / 1000d - 1), time = 2000 + (y + extent) / scale;
                double expected = 256 + direction * Math.Sqrt(Math.Max(0, radius * radius - y * y));
                double error = Math.Abs(CurveMath.PositionAtTime(edited, time) - expected);
                maximumError = Math.Max(maximumError, error); Near(CurveMath.PositionAtTime(edited, time), expected, .001);
            }
            Check(CurveMath.Validate(history.Document).Count == 0, "Compact arc conversion produced invalid time geometry.");
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document)).ContentEquals(history.Document),
                "Compact arc geometry did not persist exactly.");
            if (CatchStreamConverter.Convert(map).Success)
                Check(OsuBeatmapWriter.Serialize(history.Document).ObjectSequenceMatches, "Compact arc geometry failed export read-back.");
            history.Commit(); var converted = history.Document.DeepClone();
            history.Undo(); Check(history.Document.ContentEquals(map), "Arc conversion undo did not restore exact source geometry.");
            history.Redo(); Check(history.Document.ContentEquals(converted), "Arc conversion redo changed compact geometry.");
        }
        Console.WriteLine($"Compact circular arcs: at most {maximumAnchors} anchors; maximum fixed-time X error={maximumError:F7}.");
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
