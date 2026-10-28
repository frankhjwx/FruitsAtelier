using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;

internal static class PatternPlacementTests
{
    public static void Run()
    {
        TempoAndStack();
        TimingBoundaries();
        CurveBoundsAndHistory();
        LegacyAndPreview();
        PreviewDifficulty();
        GeometrySharing();
        PreparationCost();
    }

    private static void GeometrySharing()
    {
        var arcMap = new MapDocument { IsDemo = false, DurationMs = 5000, BeatLengthMs = 500 };
        var arcTrack = Track(1000, 1500, 200, 300);
        arcTrack.Nodes[0].OutgoingCurve = new() { Kind = ControlCurveKind.CircularArc, ReferenceScale = .4 };
        arcTrack.Nodes[0].OutgoingCurve!.Controls.Add(new() { Offset = new(250, 80) }); arcMap.Tracks.Add(arcTrack);
        var arcSnapshot = Capture(arcMap);
        var sharedArc = PatternClipboard.Decode(PatternClipboard.Encode(arcSnapshot));
        Check(sharedArc.HasSameGeometry(arcSnapshot), "circular arc geometry round trip");
        var arcBefore = PatternPlacement.Prepare(arcSnapshot, new(), 3000).CreateDocument();
        var arcAfter = PatternPlacement.Prepare(sharedArc, new(), 3000).CreateDocument();
        for (int i = 0; i <= 20; i++)
        {
            var a = ControlCurveMath.Evaluate(arcBefore.Tracks[0], 0, i / 20.0); var b = ControlCurveMath.Evaluate(arcAfter.Tracks[0], 0, i / 20.0);
            Near(a.TimeMs, b.TimeMs, "arc time round trip"); Near(a.X, b.X, "arc X round trip");
        }
        for (int i = 0; i < 16; i++)
        {
            var source = new MapDocument { IsDemo = false, DurationMs = 10000, BeatLengthMs = 333.33 + i * 13.7, SliderMultiplier = 1.4 };
            var track = Track(1000.125, 1456.875, 120.25, 335.125);
            track.Kind = CurveKind.Bezier;
            track.Nodes[0].HandleOut = new(123.456, 31.25); track.Nodes[1].HandleIn = new(-145.678, -23.125);
            source.Tracks.Add(track);
            var slider = new ImportedSlider { TimeMs = 2000.375, X = 100.25, Y = 192, PathType = 'L', PixelLength = 205.125,
                SliderVelocityMultiplier = .4 + i * .37, SourceOrder = 1 };
            slider.ControlPoints.AddRange([new(100.25, 192), new(315.375, 192)]); source.ImportedSliders.Add(slider);
            var snapshot = Capture(source);
            var shared = PatternClipboard.Decode(PatternClipboard.Encode(snapshot));
            Check(snapshot.HasSameGeometry(shared), $"fractional native and Bezier geometry round trip {i}");
            var target = new MapDocument { BeatLengthMs = source.BeatLengthMs, SliderMultiplier = source.SliderMultiplier };
            var before = PatternPlacement.Prepare(snapshot, target, 3000).CreateDocument();
            var after = PatternPlacement.Prepare(shared, target, 3000).CreateDocument();
            Near(ImportedSliderConverter.DurationMs(before, before.ImportedSliders[0]), ImportedSliderConverter.DurationMs(after, after.ImportedSliders[0]), "native duration round trip");
            for (int j = 0; j <= 20; j++)
            {
                var a = CurveMath.Evaluate(before.Tracks[0], 0, j / 20.0); var b = CurveMath.Evaluate(after.Tracks[0], 0, j / 20.0);
                Near(a.TimeMs, b.TimeMs, "Bezier time round trip"); Near(a.X, b.X, "Bezier X round trip");
            }
        }
    }

    private static void PreviewDifficulty()
    {
        var map = new MapDocument { ApproachRate = 9, CircleSize = 5 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 40 }, new() { TimeMs = 1100, X = 470 },
            new() { TimeMs = 1200, X = 460 }]);
        var saved = Capture(map);
        map.ApproachRate = 2; map.Fruits[1].X = 50;
        var preview = PatternPreview.Create(saved);
        Check(preview.ApproachRate == 9, "Preview retains snapshot AR after source edits");
        Check(preview.HyperdashStarts.SetEquals(HyperDashCalculator.GetHyperDashStarts(preview.Objects, preview.CircleSize))
            && preview.HyperdashStarts.Contains((preview.Objects[0].SourceId, preview.Objects[0].EventIndex))
            && !preview.HyperdashStarts.Contains((preview.Objects[1].SourceId, preview.Objects[1].EventIndex)),
            "Preview marks red departures using the complete saved sequence");
    }

    private static void TempoAndStack()
    {
        var source = new MapDocument { DurationMs = 5000, BeatLengthMs = 500, SliderTickRate = 4 };
        source.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        source.Fruits.Add(new() { TimeMs = 1500, X = 400 });
        var stream = Track(2000, 2500, 220, 280);
        stream.StreamSnapDivisor = 4; stream.Stack = new() { Points = [new(0, 12), new(1, 24)], FruitAdjustments = [new(.5, 6)] };
        stream.StreamNewComboOffsets.Add(250); stream.DropletMode = SliderDropletMode.Randomize;
        source.Tracks.Add(stream);
        source.HitsoundOverrides.Add(new(stream.Id, 1, new()));
        var snapshot = Capture(source); var baseline = snapshot.CreateDocument();
        var target = new MapDocument { BeatLengthMs = 1000.0 / 3, SliderTickRate = 2, DerandomizeFSliderDroplets = true };
        var prepared = PatternPlacement.Prepare(snapshot, target, 3000, true);
        var placed = prepared.CreateDocument(0, 10);
        Near(prepared.HorizontalScale, 2.0 / 3, "120 to 180 horizontal scale");
        Near(placed.Fruits[1].TimeMs, 3000 + 1000.0 / 3, "relative half-second is still one beat");
        Near(placed.Fruits[1].X, 300, "horizontal speed preserved");
        Check(placed.SliderTickRate == 2 && baseline.SliderTickRate == 4, "target STR and saved preview STR independent");
        var stack = placed.Tracks[0];
        Near(stack.Stack!.Points[1].Distance, 16, "stack width scales");
        Near(stack.Stack.FruitAdjustments[0].Offset, 4, "stack corrections scale");
        Near(stack.StreamNewComboOffsets[0], 500.0 / 3, "internal combo time scales");
        Check(stack.StreamSnapDivisor == 4 && stack.DropletMode == SliderDropletMode.FollowMap, "stream parameters retained and target mode followed");
        Check(placed.HitsoundOverrides.Single().SourceId == stack.Id && stack.Id != stream.Id, "hitsound parent identity replaced");
        Check(stack.Nodes.Zip(stream.Nodes).All(p => p.First.Id != p.Second.Id), "anchor identities replaced");
        Check(baseline.ContentEquals(snapshot.CreateDocument()), "placement does not mutate snapshot");
        var again = prepared.CreateDocument();
        Check(again.Tracks[0].Id != stack.Id && again.Tracks[0].Nodes[0].Id != stack.Nodes[0].Id, "each insertion gets independent identities");
        Throws(() => PatternPlacement.Prepare(snapshot, target, -1), "negative placement rejected");
        var clamped = prepared.CreateDocument(100000);
        Near(clamped.Fruits.Max(f => f.X), 512, "whole group clamps at edge");
        Near(clamped.Fruits[1].X - clamped.Fruits[0].X, 200, "clamping preserves spacing");
    }

    private static void TimingBoundaries()
    {
        var source = new MapDocument { DurationMs = 5000, BeatLengthMs = 500 };
        source.Fruits.AddRange([new() { TimeMs = 0, X = 100 }, new() { TimeMs = 1000, X = 200 }]);
        var target = new MapDocument { BeatLengthMs = 500 };
        target.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 500, BeatLengthMs = 250 }]);
        var placed = PatternPlacement.Prepare(Capture(source), target, 0).CreateDocument();
        Near(placed.Fruits[1].TimeMs, 750, "fruits integrate across target red line");
        Throws(() => PatternPlacement.Prepare(Capture(source), target, 0, true), "one horizontal scale cannot represent variable tempo ratios");
        source.Tracks.Add(Track(0, 1000, 100, 200));
        Throws(() => PatternPlacement.Prepare(Capture(source), target, 0), "non-affine curve timing explicitly rejected");
        source.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 500, BeatLengthMs = 250 }]);
        var compatible = PatternPlacement.Prepare(Capture(source), target, 0, true).CreateDocument();
        Near(compatible.Tracks[0].Nodes[1].TimeMs, 1000, "matching tempo boundaries preserve curve geometry");
    }

    private static void CurveBoundsAndHistory()
    {
        var source = new MapDocument { DurationMs = 5000 };
        var track = Track(1000, 2000, 200, 200);
        track.Kind = CurveKind.Bezier;
        track.Nodes[0].HandleOut = new(300, 200); track.Nodes[1].HandleIn = new(-300, 200);
        track.Nodes[0].OutgoingCurve = new() { Kind = ControlCurveKind.Bezier };
        track.Nodes[0].OutgoingCurve!.Controls.Add(new() { Offset = new(500, 300) });
        source.Tracks.Add(track);
        var snapshot = Capture(source);
        var shared = PatternClipboard.Decode(PatternClipboard.Encode(snapshot));
        Check(shared.HasSameGeometry(snapshot), "geometry clipboard retains exact control curves");
        var prepared = PatternPlacement.Prepare(shared, new(), 3000);
        Near(prepared.MaximumOffsetX, 162, "bounds include quadratic interior maximum");
        var placed = prepared.CreateDocument(10000);
        Near(CurveMath.Evaluate(placed.Tracks[0], 0, .5).X, 512, "interior extremum limits translation");
        Check(placed.Tracks[0].Nodes[0].OutgoingCurve!.Controls[0].Id != track.Nodes[0].OutgoingCurve!.Controls[0].Id, "exact controls receive fresh IDs");
        var history = new EditorHistory(new MapDocument());
        history.Begin("insert pattern"); history.Document.Tracks.AddRange(placed.Tracks); history.Commit();
        var saved = history.Document.DeepClone();
        history.Undo(); Check(history.Document.Tracks.Count == 0, "undo removes complete insertion");
        history.Redo(); Check(history.Document.ContentEquals(saved), "redo restores complete controls and identities");
        source.Fruits.AddRange([new() { TimeMs = 500, X = 0 }, new() { TimeMs = 2500, X = 512 }]);
        Throws(() => PatternPlacement.Prepare(Capture(source), new() { BeatLengthMs = 1000 }, 0, true), "too-wide group cannot silently distort");
    }

    private static void LegacyAndPreview()
    {
        var source = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,2,200\n256,192,3000,8,0,4000\n");
        var snapshot = Capture(source); var before = snapshot.CreateDocument();
        var target = new MapDocument { BeatLengthMs = 1000.0 / 3, SliderMultiplier = 1.92, DerandomizeFSliderDroplets = true };
        var prepared = PatternPlacement.Prepare(snapshot, target, 2000, true);
        var placed = prepared.CreateDocument();
        double oldDuration = ImportedSliderConverter.DurationMs(source, source.ImportedSliders[0]);
        Near(ImportedSliderConverter.DurationMs(placed, placed.ImportedSliders[0]), oldDuration * 2 / 3, "Legacy repeat duration retimed with per-object SV");
        var refreshed = SliderRepresentationEditing.RefreshLegacyDroplets(placed);
        Check(refreshed.Success, "target derandomization can refresh placed Legacy: " + string.Join(";", refreshed.Diagnostics));
        Check(placed.ImportedSliders[0].RandomizationTrack is not null, "target derandomization establishes reusable baseline");
        var refreshedSnapshot = Capture(placed);
        var sharedRefreshed = PatternClipboard.Decode(PatternClipboard.Encode(refreshedSnapshot));
        Check(sharedRefreshed.HasSameGeometry(refreshedSnapshot), "geometry clipboard retains the current Legacy path and duration");
        var randomTarget = new MapDocument { BeatLengthMs = 500, SliderMultiplier = 1.4, DerandomizeFSliderDroplets = false };
        var randomized = PatternPlacement.Prepare(refreshedSnapshot, randomTarget, 1000).CreateDocument();
        Check(randomized.ImportedSliders[0].RandomizationTrack?.DropletMode == SliderDropletMode.FollowMap, "saved baseline follows target mode");
        var refreshedAgain = SliderRepresentationEditing.RefreshLegacyDroplets(randomized);
        Check(refreshedAgain.Success, "saved compensated baseline retargets to randomized map: " + string.Join(";", refreshedAgain.Diagnostics));
        var preview = PatternPreview.Create(snapshot);
        var second = PatternPreview.Create(snapshot);
        Check(preview.Objects.SequenceEqual(second.Objects), "preview seed and events deterministic");
        Check(preview.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).All(o => o.X == o.PathX && o.RandomOffset == 0), "Gallery preview removes Legacy tiny randomization");
        Check(preview.Paths.Count == 2, "repeat slider preview has both traversals");
        Check(before.ContentEquals(snapshot.CreateDocument()), "preview and transform keep snapshot immutable");
        var outOfBounds = new MapDocument();
        var slider = new ImportedSlider { X = 100, Y = 192, TimeMs = 1000, PathType = 'L', PixelLength = 600 };
        slider.ControlPoints.AddRange([new(100, 192), new(700, 192)]); outOfBounds.ImportedSliders.Add(slider);
        Throws(() => PatternPlacement.Prepare(Capture(outOfBounds), target, 0), "raw Legacy geometry bounds checked before playable clamping");
    }

    private static CurveTrack Track(double start, double end, double x1, double x2)
    {
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = start, X = x1 }, new() { TimeMs = end, X = x2 }]);
        return track;
    }

    private static void PreparationCost()
    {
        var source = new MapDocument { DurationMs = 500000 };
        for (int i = 0; i < 500; i++) source.Fruits.Add(new() { TimeMs = 1000 + i * 500, X = 100 + i % 300 });
        for (int i = 0; i < 20; i++)
        {
            var track = Track(1100 + i * 500, 1200 + i * 500, 220, 260);
            track.StreamSnapDivisor = 4; track.Stack = new(); source.Tracks.Add(track);
            var slider = new ImportedSlider { TimeMs = 1300 + i * 500, X = 200, Y = 192, PathType = 'L', PixelLength = 20 };
            slider.ControlPoints.AddRange([new(200, 192), new(220, 192)]); source.ImportedSliders.Add(slider);
            source.BananaShowers.Add(new() { TimeMs = 1400 + i * 500, EndTimeMs = 1450 + i * 500 });
        }
        var snapshot = Capture(source);
        var smallTarget = new MapDocument { BeatLengthMs = 1000.0 / 3 };
        var largeTarget = new MapDocument { BeatLengthMs = smallTarget.BeatLengthMs, DurationMs = 100000000 };
        for (int i = 0; i < 100000; i++) largeTarget.Fruits.Add(new() { TimeMs = i * 500, X = 256 });
        _ = PatternPlacement.Prepare(snapshot, smallTarget, 1000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = PatternPlacement.Prepare(snapshot, smallTarget, 1000);
        long smallBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var prepared = PatternPlacement.Prepare(snapshot, largeTarget, 1000);
        watch.Stop();
        long largeBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(largeBytes <= smallBytes + 65536, "local preparation does not clone unrelated target objects");
        before = GC.GetAllocatedBytesForCurrentThread();
        double sum = 0;
        for (int i = 0; i < 100000; i++) sum += prepared.ClampOffsetX(i % 1000 - 500);
        long pointerBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sum);
        Check(pointerBytes == 0, "horizontal pointer updates allocate no geometry or object collections");
        Console.WriteLine($"Pattern preparation: {snapshot.ObjectCount} mixed parents into 100000-note map: {watch.Elapsed.TotalMilliseconds:F2} ms, {largeBytes} bytes; 100000 X updates: {pointerBytes} bytes.");
    }
    private static PatternSnapshot Capture(MapDocument map) => PatternSnapshot.Capture(map,
        map.Fruits.Select(f => f.Id).Concat(map.Tracks.Select(t => t.Id)).Concat(map.ImportedSliders.Select(s => s.Id)).Concat(map.BananaShowers.Select(s => s.Id)), "Test pattern");
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-4, $"{message}: {actual} != {expected}");
    private static void Throws(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
}
