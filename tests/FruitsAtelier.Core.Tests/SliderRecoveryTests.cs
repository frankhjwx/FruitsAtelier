using FruitsAtelier.Core;

internal static class SliderRecoveryTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 500000, DerandomizeFSliderDroplets = true, SliderTickRate = 2 };
        var random = new Random(713);
        for (int i = 0; i < 72; i++)
        {
            double duration = 700 + random.NextDouble() * 1000;
            var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = i % 3 + 1, CompensateTinyDroplets = true };
            double x = 100 + random.NextDouble() * 150;
            track.Nodes.Add(new() { TimeMs = 1000 + i * 6000 + .37, X = x });
            track.Nodes.Add(new() { TimeMs = track.Nodes[0].TimeMs + duration, X = x + 30 + random.NextDouble() * 100 });
            if (i % 4 != 0)
            {
                var curve = new ControlCurve { Kind = i % 4 == 1 ? ControlCurveKind.CircularArc : ControlCurveKind.Bezier, ReferenceScale = .3 + random.NextDouble() };
                curve.Controls.Add(new() { Offset = new(duration * (.25 + random.NextDouble() * .5), 120 + random.NextDouble() * 60) });
                if (i % 4 == 3) curve.Controls.Add(new() { Offset = new(duration * .85, -30) });
                track.Nodes[0].OutgoingCurve = curve;
            }
            map.Tracks.Add(track);
            map.Fruits.Add(new() { TimeMs = CurveMath.EndTimeMs(track) + 120, X = Math.Round(track.Nodes[^1].X, MidpointRounding.AwayFromZero) });
        }
        map.TimingPoints.AddRange([
            new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 150000, BeatLengthMs = 400 },
            new() { TimeMs = 300000, BeatLengthMs = 650 }
        ]);
        // Keep unrelated objects in the RNG stream and HR position history.
        map.Fruits.AddRange([new() { TimeMs = 900, X = 256 }, new() { TimeMs = 490000, X = 128 }]);
        var exported = OsuBeatmapWriter.Serialize(map);
        var imported = OsuBeatmapReader.Read(exported.Text);
        var sourceIds = imported.ImportedSliders.ToDictionary(s => s.Id, s => exported.ObjectSources[s.SourceOrder]);
        var before = OsuBeatmapWriter.Serialize(imported);
        var initialized = SliderImportEditing.Initialize(imported, true);
        Check(initialized.Success, string.Join("; ", initialized.Diagnostics));
        var history = new EditorHistory(imported);
        var snapshot = imported.DeepClone();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { ImportedSliderEditing.ConvertAll(history.Document, cancelled.Token); throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { Check(history.Document.ContentEquals(snapshot), "Cancellation changed the document."); }
        history.Begin("Convert");
        var converted = ImportedSliderEditing.ConvertAll(history.Document);
        history.Commit();
        Check(converted.Tracks.Count + history.Document.ImportedSliders.Count == map.Tracks.Count, "A parent was lost.");
        var after = OsuBeatmapWriter.Serialize(history.Document);
        Equivalent(before.PlayableObjects, after.PlayableObjects);
        Equivalent(before.PlayableHardRockObjects, after.PlayableHardRockObjects);
        int restored = converted.Tracks.Count(t => t.Nodes.Count <= 2 && Error(map.Tracks.Single(a => a.Id == sourceIds[t.Id]), t) <= 4);
        Console.WriteLine($"Synthetic recovery: {restored}/{map.Tracks.Count} sparse paths within 4px; {converted.Tracks.Count} converted.");
        foreach (var group in converted.Tracks.GroupBy(t => map.Tracks.FindIndex(a => a.Id == sourceIds[t.Id]) % 4))
            Console.WriteLine($"  shape {group.Key}: {group.Count(t => t.Nodes.Count <= 2 && Error(map.Tracks.Single(a => a.Id == sourceIds[t.Id]), t) <= 4)}/{group.Count()}");
        Check(restored >= map.Tracks.Count * .8, "General curve recovery fell below 80%.");
        var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
        Check(saved.ContentEquals(history.Document), "Recovered curves changed after saving.");
        history.Undo(); Check(history.Document.ContentEquals(snapshot), "Undo lost Legacy baselines.");
        history.Redo(); Check(history.Document.ContentEquals(saved), "Redo changed recovered curves.");

        // A sharp multi-corner path must not be flattened merely because its endpoints agree.
        var corners = new[] { new MapPoint(0, 256), new MapPoint(100, 64), new MapPoint(200, 448),
            new MapPoint(300, 64), new MapPoint(400, 448), new MapPoint(500, 256) };
        var fitted = new CurveTrack();
        PlaybackCurveFitter.Fit(fitted, corners, .8, default);
        Check(corners.All(p => Math.Abs(CurveMath.PositionAtTime(fitted, p.TimeMs) - p.X) <= .8), "Sharp reversals were flattened.");
        Check(CurveMath.Validate(new MapDocument { Tracks = { fitted } }).Count == 0, "Recovered controls are invalid.");
        var reversal = Enumerable.Range(0, 9).Select(i => new MapPoint(i * 100, i <= 4 ? 100 + i * 60 : 340 - (i - 4) * 60)).ToArray();
        fitted = new();
        PlaybackCurveFitter.Fit(fitted, reversal, .8, default);
        Check(fitted.Nodes.Count == 3 && fitted.Nodes[1].TimeMs == 400, "A genuine droplet reversal lost its corner.");
        for (int i = 0; i <= 800; i++)
            Check(Math.Abs(CurveMath.PositionAtTime(fitted, i) - (i <= 400 ? 100 + i * .6 : 340 - (i - 400) * .6)) <= .8,
                "A reversal was rounded between droplets.");
        foreach (int spans in new[] { 1, 2, 3, 6 })
        foreach (double tickRate in new[] { 1d, 2d, 4d })
        {
            var reversing = new MapDocument { DurationMs = 20000, SliderTickRate = tickRate, DerandomizeFSliderDroplets = true };
            var path = new CurveTrack { Kind = CurveKind.Linear, SpanCount = spans, CompensateTinyDroplets = true };
            path.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 400 },
                new() { TimeMs = 2000, X = 100 }]);
            reversing.Tracks.Add(path);
            var read = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(reversing).Text);
            var source = OsuBeatmapWriter.Serialize(read);
            Check(SliderImportEditing.Initialize(read, true).Success, "Reversing import failed.");
            var result = ImportedSliderEditing.ConvertAll(read);
            Check(result.Tracks.Count == 1 && result.Tracks[0].SpanCount == spans, "Repeat conversion failed.");
            // Partial repeat compensation retains playback, which need not reveal the original target curve.
            if (CatchStreamConverter.Convert(reversing).Sliders.Single().TinyCompensationSucceeded)
                Check(Error(path, result.Tracks[0]) <= 4, $"Droplet-corner recovery changed the path ({spans} spans, tick rate {tickRate}).");
            var roundtrip = OsuBeatmapWriter.Serialize(read);
            Equivalent(source.PlayableObjects, roundtrip.PlayableObjects);
            Equivalent(source.PlayableHardRockObjects, roundtrip.PlayableHardRockObjects);
        }
        var protectedMap = OsuBeatmapReader.Read(exported.Text);
        Check(SliderImportEditing.Initialize(protectedMap, true).Success, "Guard fixture import failed.");
        var protectedSlider = protectedMap.ImportedSliders[0];
        foreach (var node in protectedSlider.RandomizationTrack!.Nodes) node.X += 100;
        var protectedSnapshot = protectedMap.DeepClone();
        var rejected = SliderImportEditing.ConvertFollowingMap(protectedMap, [protectedSlider.Id]);
        Check(rejected.Tracks.Count == 0 && rejected.Failures.Count == 1 && protectedMap.ContentEquals(protectedSnapshot),
            "Out-of-tolerance conversion did not retain the Legacy source.");
    }

    internal static double Error(CurveTrack original, CurveTrack recovered)
    {
        double max = 0;
        for (int i = 0; i <= 2000; i++)
        {
            double u = i / 2000d;
            double a = CurveMath.PositionAtTime(original, original.Nodes[0].TimeMs + u * (original.Nodes[^1].TimeMs - original.Nodes[0].TimeMs));
            double b = CurveMath.PositionAtTime(recovered, recovered.Nodes[0].TimeMs + u * (recovered.Nodes[^1].TimeMs - recovered.Nodes[0].TimeMs));
            max = Math.Max(max, Math.Abs(a - b));
        }
        return max;
    }

    internal static void Equivalent(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after)
    {
        Check(before.Count == after.Count, "Object count changed.");
        Check(before.Zip(after).All(p => p.First.Kind == p.Second.Kind && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= 1e-6
            && Math.Abs(p.First.X - p.Second.X) <= 4), "Exported playback exceeded 4px or changed event timing.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
