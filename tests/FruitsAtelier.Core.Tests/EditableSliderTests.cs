using FruitsAtelier.Core;

internal static class EditableSliderTests
{
    public static void MixedSegments()
    {
        var track = Mixed();
        var doc = With(track);
        True(CurveMath.Validate(doc).Count == 0, "Mixed trajectory was rejected.");
        True(CurveMath.SegmentKind(track, 0) == CurveKind.Bezier && CurveMath.SegmentKind(track, 1) == CurveKind.Linear
            && CurveMath.SegmentKind(track, 2) == CurveKind.Bezier && CurveMath.SegmentKind(track, 3) == CurveKind.Linear,
            "Per-anchor overrides did not alternate the interpolation type.");
        var sample = CurveMath.Evaluate(track, 0, 0.5);
        Near(sample.X, CurveMath.PositionAtTime(track, sample.TimeMs));
        Near(250, CurveMath.PositionAtTime(track, 1500));
        Near(200, CurveMath.PositionAtTime(track, 3500));
        var result = CatchStreamConverter.Convert(doc, false);
        Valid(result);
        True(result.Sliders.Count == 1 && result.Objects.Count(o => o.Kind == CatchObjectKind.Fruit) == 2,
            "Changing segment kind created extra parent sliders or anchor fruit.");
        foreach (var item in result.Objects.Where(o => o.Kind != CatchObjectKind.TinyDroplet))
            Near(CurveMath.PositionAtTime(track, item.TimeMs), item.X, CatchStreamConverter.AlignmentTolerance);
    }

    public static void MixedSplitAndValidation()
    {
        var track = Mixed();
        double[] before = Enumerable.Range(0, 401).Select(i => CurveMath.PositionAtTime(track, i * 10)).ToArray();
        CurveMath.Split(track, 1, 0.3);
        True(CurveMath.SegmentKind(track, 1) == CurveKind.Linear && CurveMath.SegmentKind(track, 2) == CurveKind.Linear,
            "Linear split inherited the track's Bezier default.");
        CurveMath.Split(track, 0, 0.4);
        True(CurveMath.SegmentKind(track, 0) == CurveKind.Bezier && CurveMath.SegmentKind(track, 1) == CurveKind.Bezier,
            "Bezier split changed one child's type.");
        for (int i = 0; i < before.Length; i++) Near(before[i], CurveMath.PositionAtTime(track, i * 10));
        True(CurveMath.Validate(With(track)).Count == 0, "Mixed split generated invalid handles.");

        var line = new CurveTrack { Kind = CurveKind.Bezier };
        line.Nodes.Add(new() { TimeMs = 0, X = 100, OutgoingKind = CurveKind.Linear, HandleOut = new(900, 800) });
        line.Nodes.Add(new() { TimeMs = 1000, X = 200, HandleIn = new(-900, -800) });
        True(CurveMath.Validate(With(line)).Count == 0, "Dormant Bezier control points constrained a straight segment.");
        line.Nodes[0].OutgoingKind = CurveKind.Bezier;
        True(CurveMath.Validate(With(line)).Count > 0, "Active Bezier controls escaped validation.");
        line.Nodes[0].OutgoingKind = (CurveKind)99;
        True(CurveMath.Validate(With(line)).Count > 0, "Unknown per-segment type was accepted.");
    }

    public static void RepeatAuthoring()
    {
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 3 };
        track.Nodes.Add(new() { TimeMs = 100, X = 100 });
        track.Nodes.Add(new() { TimeMs = 200, X = 200 });
        var doc = With(track); doc.SliderMultiplier = 5; doc.SliderTickRate = 10;
        var result = CatchStreamConverter.Convert(doc, false);
        Valid(result);
        True(result.Sliders.Count == 1 && result.Sliders[0].SpanCount == 3, "Repeat was split into separate sliders.");
        Near(300, result.Sliders[0].DurationMs); Near(400, CurveMath.EndTimeMs(track));
        Near(175, CurveMath.FirstSpanTime(track, 225)); Near(175, CurveMath.PositionAtTime(track, 225));
        Near(100, CurveMath.PositionAtTime(track, 0)); Near(200, CurveMath.PositionAtTime(track, 1000));
        var fruits = result.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
        True(fruits.Length == 4, "Repeat boundary has duplicate tail/head fruit.");
        double[] xs = [100, 200, 100, 200];
        for (int i = 0; i < fruits.Length; i++) { Near(100 + i * 100, fruits[i].TimeMs); Near(xs[i], fruits[i].X); }
        var ticks = result.Objects.Where(o => o.Kind == CatchObjectKind.Droplet).ToArray();
        True(ticks.Length == 3, "Repeated ticks were lost.");
        for (int i = 0; i < ticks.Length; i++) { Near(150 + i * 100, ticks[i].TimeMs); Near(150, ticks[i].X); }
        doc.DurationMs = 399;
        True(CurveMath.Validate(doc).Count > 0, "Total repeated duration escaped map bounds.");
        track.SpanCount = 0;
        True(CurveMath.Validate(doc).Count > 0, "Zero spans were accepted.");
    }

    public static void RepeatTinyConflict()
    {
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 3 };
        track.Nodes.Add(new() { TimeMs = 0, X = 256 }); track.Nodes.Add(new() { TimeMs = 1000, X = 256 });
        var doc = With(track);
        var actual = CatchStreamConverter.Convert(doc, true);
        var plain = CatchStreamConverter.Convert(doc, false);
        Valid(actual); Valid(plain);
        True(!actual.Sliders[0].TinyCompensationApplied, "Conflicting repeat offsets were reported as compensated.");
        True(actual.Diagnostics.Count == 0, "A compatible repeat fallback exposed an internal compensation warning.");
        True(actual.Objects.SequenceEqual(plain.Objects), "Repeat fallback fabricated tiny positions or changed the RNG sequence.");
        track.CompensateTinyDroplets = true;
        var required = CatchStreamConverter.Convert(doc, true);
        Valid(required);
        True(required.Objects.SequenceEqual(plain.Objects), "Saved tiny alignment must allow repeat fallback.");
        True(track.CompensateTinyDroplets == true, "Generation mutated the saved preference.");
        var export = OsuBeatmapWriter.Serialize(doc, true);
        True(export.ReadBack.ImportedSliders.Single().SpanCount == 3, "Fallback export lost repeats.");
        track.CompensateTinyDroplets = false;
        var preserved = CatchStreamConverter.Convert(doc, true);
        Valid(preserved);
        True(preserved.Objects.SequenceEqual(plain.Objects) && preserved.Diagnostics.Count == 0, "Track-level tiny preference did not override the global default.");
        var edited = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
        edited.Nodes.Add(new Anchor { TimeMs = 241992.33333333317, X = 186.2392051021486, HandleIn = new(0, 0), HandleOut = new(0, 0), OutgoingKind = CurveKind.Bezier });
        edited.Nodes.Add(new Anchor { TimeMs = 242251.09021210903, X = 371.344827803147, HandleIn = new(-110.59651961948839, 0.27939038317435916), HandleOut = new(128.47108038316946, -8.715352438245418), OutgoingKind = CurveKind.Bezier });
        edited.Nodes.Add(new Anchor { TimeMs = 242472.6132794908, X = 178.00397011848435, HandleIn = new(-20.890448022575583, 82.3523498366423), HandleOut = new(0, 0), OutgoingKind = CurveKind.Linear });
        edited.Nodes.Add(new Anchor { TimeMs = 242575.66666666648, X = 440.2050634076596, HandleIn = new(0, 0), HandleOut = new(0, 0), OutgoingKind = null });
        var fractional = new MapDocument { DurationMs = 360742, BeatLengthMs = 333.333333333333, SliderMultiplier = 1.9, SliderTickRate = 2 };
        fractional.Tracks.Add(edited);
        foreach (int spans in new[] { 1, 2, 3, 4 })
        {
            edited.SpanCount = spans;
            var generated = CatchStreamConverter.Convert(fractional); Valid(generated);
            True(generated.MaxTickError <= CatchStreamConverter.AlignmentTolerance, "Repeat fallback changed fruit/droplet targets.");
            True(Math.Abs(generated.Sliders.Single().DurationMs - (edited.Nodes[^1].TimeMs - edited.Nodes[0].TimeMs) * spans) < 1e-6, "Repeat fallback changed duration.");
            OsuBeatmapWriter.Serialize(fractional);
        }
    }

    public static void ImportToEditable()
    {
        foreach (char type in new[] { 'L', 'B', 'P', 'C' })
        {
            var doc = new MapDocument { SliderMultiplier = 5, SliderTickRate = 2 };
            var slider = new ImportedSlider { TimeMs = 100, X = 100, Y = 100, PathType = type, SpanCount = 1,
                PixelLength = 300, SourceOrder = 2, OriginalLine = "retained source samples" };
            slider.ControlPoints.AddRange([new(100, 100), new(200, 180), new(300, 100)]);
            doc.ImportedSliders.Add(slider);
            doc.BananaShowers.Add(new() { TimeMs = 0, EndTimeMs = 50, SourceOrder = 1 });
            var before = CatchStreamConverter.Convert(doc);
            Valid(before);
            var converted = ImportedSliderEditing.ConvertToTrack(doc, slider.Id);
            True(doc.ImportedSliders.Count == 0 && doc.Tracks.Single() == converted.Track, "Editable replacement was not atomic.");
            True(converted.Track.Id == slider.Id && converted.Track.SourceOrder == slider.SourceOrder
                && converted.Track.OriginalLine == slider.OriginalLine && converted.Track.SpanCount == 1, "Imported identity or source metadata was lost.");
            True(converted.Track.Nodes.All(n => n.OutgoingKind is CurveKind.Linear or CurveKind.Bezier), "Imported path must use editable mixed segments.");
            True(converted.Diagnostics.Count == 0 && converted.Track.CompensateTinyDroplets == true,
                "A successful Legacy conversion exposed internal metrics or lost the FSlider alignment policy.");
            var after = CatchStreamConverter.Convert(doc);
            Valid(after);
            CompareSequence(before.Objects, after.Objects);
            True(after.Objects.Where(item => item.SourceId == slider.Id)
                .All(item => Math.Abs(item.X - CurveMath.PositionAtTime(converted.Track, item.TimeMs)) <= CatchStreamConverter.AlignmentTolerance),
                "Converted FSlider left an object away from its target path.");
            var anchor = converted.Track.Nodes[1];
            True(CurveMath.TryMoveAnchor(converted.Track, anchor.Id, anchor.TimeMs, anchor.X - 5, out string error), error);
            Valid(CatchStreamConverter.Convert(doc));
        }
    }

    public static void ConversionHistoryAndFailure()
    {
        var doc = new MapDocument();
        var slider = new ImportedSlider { X = 100, Y = 100, PixelLength = 200, SpanCount = 1, PathType = 'L', SourceOrder = 3 };
        slider.ControlPoints.AddRange([new(100, 100), new(200, 200)]); doc.ImportedSliders.Add(slider);
        var history = new EditorHistory(doc);
        history.Begin("Edit imported slider");
        var converted = ImportedSliderEditing.ConvertToTrack(history.Document, slider.Id);
        converted.Track.Nodes[0].OutgoingKind = CurveKind.Bezier;
        history.Commit();
        var clone = history.Document.DeepClone();
        True(clone.ContentEquals(history.Document), "New span/segment/tiny fields were not deep-cloned.");
        clone.Tracks[0].Nodes[0].OutgoingKind = CurveKind.Linear;
        True(!clone.ContentEquals(history.Document), "Changing a segment kind did not dirty the document.");
        history.Undo();
        True(!history.IsDirty && history.Document.Tracks.Count == 0 && history.Document.ImportedSliders[0].Id == slider.Id,
            "Undo failed to restore the original imported slider.");
        history.Redo();
        True(history.Document.Tracks[0].SpanCount == 1 && history.Document.Tracks[0].Nodes[0].OutgoingKind == CurveKind.Bezier
            && history.Document.Tracks[0].CompensateTinyDroplets == true, "Redo lost editable fields.");

        var repeated = new MapDocument { SliderMultiplier = 5, SliderTickRate = 2 };
        var conflicting = new ImportedSlider { TimeMs = 100, X = 100, Y = 100, PathType = 'L', SpanCount = 3, PixelLength = 300 };
        conflicting.ControlPoints.AddRange([new(100, 100), new(300, 100)]);
        repeated.ImportedSliders.Add(conflicting);
        var repeatedBefore = repeated.DeepClone();
        var approximate = ImportedSliderEditing.ConvertToTrack(repeated, conflicting.Id).Track;
        True(repeated.ImportedSliders.Count == 0 && approximate.CompensateTinyDroplets == false, "Conflicting repeat must produce an approximate editable track.");
        var repeatedOutput = CatchStreamConverter.Convert(repeated); Valid(repeatedOutput);
        True(Math.Abs(repeatedOutput.Sliders.Single().DurationMs - ImportedSliderConverter.DurationMs(repeatedBefore, conflicting)) < .000001,
            "Approximate repeat changed its total duration.");

        var invalid = new MapDocument();
        var zero = new ImportedSlider { X = 100, Y = 100, PathType = 'L' }; zero.ControlPoints.Add(new(100, 100)); invalid.ImportedSliders.Add(zero);
        var unchanged = invalid.DeepClone();
        try { ImportedSliderEditing.ConvertToTrack(invalid, zero.Id); throw new Exception("Zero-length slider unexpectedly converted."); }
        catch (InvalidOperationException) { }
        True(invalid.ContentEquals(unchanged), "A failed conversion modified the source document.");
    }

    public static void RealFixtures()
    {
        string fixtureRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "beatmaps");
        foreach (string mapName in new[] { "Vidro Moyou", "Oriental Blossom" })
        {
            string path = Directory.EnumerateFiles(fixtureRoot, "*.osu", SearchOption.AllDirectories).Single(p => p.Contains(mapName));
            var original = OsuBeatmapReader.ReadFile(path);
            var candidates = original.ImportedSliders.GroupBy(s => (s.PathType, Repeats: s.SpanCount > 1))
                .Select(g => g.OrderByDescending(s => s.ControlPoints.Count).First()).ToArray();
            foreach (var source in candidates)
            {
                var doc = original.DeepClone();
                var before = CatchStreamConverter.Convert(doc);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = ImportedSliderEditing.ConvertToTrack(doc, source.Id);
                watch.Stop();
                var after = CatchStreamConverter.Convert(doc);
                Valid(after);
                var beforeSlider = before.Sliders.Single(s => s.SourceId == source.Id);
                var afterSlider = after.Sliders.Single(s => s.SourceId == source.Id);
                True(beforeSlider.StartTimeMs == afterSlider.StartTimeMs && beforeSlider.SpanCount == afterSlider.SpanCount
                    && Math.Abs(beforeSlider.DurationMs - afterSlider.DurationMs) < .000001, "Real fixture duration changed.");
                double error = Enumerable.Range(0, 501).Select(i =>
                {
                    double time = source.TimeMs + beforeSlider.DurationMs * i / 500;
                    return Math.Abs(ImportedSliderConverter.PositionAtTime(original, source, time) - CurveMath.PositionAtTime(result.Track, time));
                }).Max();
                Console.WriteLine($"EDIT-FIXTURE {mapName} {source.PathType} spans={source.SpanCount} anchors={result.Track.Nodes.Count} X={error:R} convertMs={watch.Elapsed.TotalMilliseconds:F1}");
            }
        }
    }

    private static CurveTrack Mixed()
    {
        var track = new CurveTrack { Kind = CurveKind.Bezier };
        track.Nodes.Add(new() { TimeMs = 0, X = 100, HandleOut = new(100, 100) });
        track.Nodes.Add(new() { TimeMs = 1000, X = 300, HandleIn = new(-300, -20), OutgoingKind = CurveKind.Linear });
        track.Nodes.Add(new() { TimeMs = 2000, X = 200, HandleOut = new(300, -70), OutgoingKind = CurveKind.Bezier });
        track.Nodes.Add(new() { TimeMs = 3000, X = 300, HandleIn = new(-100, 70), OutgoingKind = CurveKind.Linear });
        track.Nodes.Add(new() { TimeMs = 4000, X = 100 });
        return track;
    }
    private static MapDocument With(CurveTrack track) { var doc = new MapDocument(); doc.Tracks.Add(track); return doc; }
    private static void Compare(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after, double xTolerance)
    {
        True(before.Count == after.Count, "Imported edit changed full-map object count.");
        for (int i = 0; i < before.Count; i++)
        {
            True(before[i].SourceId == after[i].SourceId && before[i].EventIndex == after[i].EventIndex && before[i].Kind == after[i].Kind,
                "Imported edit changed source order, event identity or kind.");
            Near(before[i].TimeMs, after[i].TimeMs, 0.000001); Near(before[i].X, after[i].X, xTolerance);
            Near(before[i].RandomOffset, after[i].RandomOffset);
        }
    }

    private static void CompareSequence(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after)
    {
        True(before.Count == after.Count, "FSlider conversion changed the full-map object count.");
        for (int i = 0; i < before.Count; i++)
        {
            True(before[i].SourceId == after[i].SourceId && before[i].EventIndex == after[i].EventIndex && before[i].Kind == after[i].Kind,
                "FSlider conversion changed source order, event identity or kind.");
            Near(before[i].TimeMs, after[i].TimeMs, 0.000001);
            Near(before[i].RandomOffset, after[i].RandomOffset);
        }
    }
    private static void Valid(CatchConversionResult result) => True(result.Success, string.Join("; ", result.Diagnostics));
    private static void Near(double expected, double actual, double tolerance = 1e-7)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected:R}, got {actual:R}."); }
    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
}
