using FruitsAtelier.Core;

internal static class WriteCacheTests
{
    public static void FractionalFruitBesideSlider()
    {
        foreach (bool stream in new[] { false, true })
        {
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n300,192,1000.75,1,0,0:0:0:0:\n");
            SliderMultiplierEditing.Apply(map, 2.94);
            var history = new EditorHistory(map);
            var cache = new OsuWriteCache();
            history.Begin("Place slider");
            var track = new CurveTrack { Kind = CurveKind.Linear, SourceOrder = 1,
                StreamSnapDivisor = stream ? 4 : null };
            track.Nodes.AddRange([new() { TimeMs = 1000.75, X = 100 }, new() { TimeMs = 1501, X = 200 }]);
            history.Document.Tracks.Add(track);
            history.Commit();
            Check(); Check();
            history.Undo(); Check(); history.Redo(); Check();

            void Check()
            {
                var document = history.Document;
                var snapshot = document.DeepClone();
                var baseline = document.DeepClone(); baseline.SliderMultiplierOverride = null;
                var original = OsuBeatmapWriter.Serialize(baseline);
                var output = OsuBeatmapWriter.Serialize(document, cache: cache);
                var full = OsuBeatmapWriter.Serialize(document);
                var restored = OsuBeatmapWriter.Serialize(ProjectSerializer.Read(ProjectSerializer.Serialize(document)));
                if (!original.ObjectSequenceMatches || !output.ObjectSequenceMatches
                    || output.Text != full.Text || output.Text != restored.Text
                    || !output.PlayableObjects.SequenceEqual(full.PlayableObjects)
                    || !output.PlayableHardRockObjects.SequenceEqual(full.PlayableHardRockObjects)
                    || !document.ContentEquals(snapshot))
                    throw new Exception("Placing a slider beside a fractional imported fruit blocked export or changed cached playback.");
                foreach (var pair in new[] { (original.PlayableObjects, output.PlayableObjects), (original.PlayableHardRockObjects, output.PlayableHardRockObjects) })
                {
                    var expected = pair.Item1.ToDictionary(o => (o.SourceId, o.EventIndex));
                    if (expected.Count != pair.Item2.Count || pair.Item2.Any(o =>
                        !expected.TryGetValue((o.SourceId, o.EventIndex), out var before) || o.Kind != before.Kind
                        || Math.Abs(o.TimeMs - before.TimeMs) >= .001 || Math.Abs(o.X - before.X) >= .001))
                        throw new Exception("SV compensation changed fractional fruit or nested slider playback.");
                }
                if (document.Tracks.Count > 0 && !stream
                    && output.PlayableObjects[0].SourceId != document.Tracks[0].Id)
                    throw new Exception("Playable mapping did not retain actual read-back order.");
            }
        }
    }

    public static int BenchmarkMultiplierEdits()
    {
        var map = new MapDocument { DurationMs = 2010000, SliderMultiplierOverride = 2 };
        for (int i = 0; i < 1000; i++)
        {
            var slider = new ImportedSlider { TimeMs = i * 2000 + 500, X = 100, Y = 192,
                PathType = 'B', PixelLength = 200, SpanCount = 2, SourceOrder = i };
            slider.ControlPoints.AddRange([new(100, 192), new(200, 100), new(300, 192)]);
            map.ImportedSliders.Add(slider);
            if (i % 25 == 0) map.TimingPoints.Add(new() { TimeMs = i * 2000, BeatLengthMs = i % 50 == 0 ? 500 : 400 });
        }
        map.BananaShowers.Add(new() { TimeMs = 100, EndTimeMs = 400 });
        map.Fruits.Add(new() { TimeMs = 1500, X = 200 });
        var cache = new OsuWriteCache();
        var samples = new List<object>();
        for (int i = 0; i < 12; i++)
        {
            map.Fruits[0].X++;
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var actual = OsuBeatmapWriter.Serialize(map, cache: cache);
            samples.Add(new { phase = i == 0 ? "cold" : "fruit-edit", elapsedMs = watch.Elapsed.TotalMilliseconds,
                bytes = GC.GetAllocatedBytesForCurrentThread() - bytes });
            var expected = OsuBeatmapWriter.Serialize(map);
            if (actual.Text != expected.Text || !actual.PlayableObjects.SequenceEqual(expected.PlayableObjects)
                || !actual.PlayableHardRockObjects.SequenceEqual(expected.PlayableHardRockObjects))
                throw new Exception("SV edit differs from full export.");
        }
        long baselineBytes = GC.GetAllocatedBytesForCurrentThread();
        var baselineWatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) _ = cache.MultiplierBaseline(map, true);
        samples.Add(new { phase = "100-baseline-lookups", elapsedMs = baselineWatch.Elapsed.TotalMilliseconds,
            bytes = GC.GetAllocatedBytesForCurrentThread() - baselineBytes });
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(samples, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    public static void StableParentOrder()
    {
        var map = new MapDocument { DurationMs = 12000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 220 });
        var stream = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4 };
        stream.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
        map.Tracks.Add(stream);
        var imported = new ImportedSlider { TimeMs = 1000, X = 300, Y = 192, PathType = 'L', PixelLength = 70 };
        imported.ControlPoints.AddRange([new(300, 192), new(370, 192)]);
        map.ImportedSliders.Add(imported);
        map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 1250 });
        var history = new EditorHistory(map);
        map = history.Document; stream = map.Tracks[0];
        var cache = new OsuWriteCache();
        Check(); Check();
        history.Begin("Change source order");
        stream.SourceOrder = -1;
        history.Commit(); Check();
        history.Undo(); map = history.Document; Check();
        history.Redo(); map = history.Document; Check();
        map.ImportedSliders.Clear(); map.BananaShowers.Clear();
        map.Tracks[0].SourceOrder = int.MaxValue;
        map.Fruits[0].TimeMs += .75;
        foreach (var node in map.Tracks[0].Nodes) node.TimeMs += .75;
        Check();

        void Check()
        {
            var before = map.DeepClone();
            var converted = CatchStreamConverter.Convert(map);
            var actual = OsuBeatmapWriter.Serialize(map, cache: cache);
            var expected = OsuBeatmapWriter.Serialize(map);
            if (!actual.ObjectSequenceMatches || !expected.ObjectSequenceMatches
                || !actual.PlayableObjects.Select(o => (o.SourceId, o.EventIndex, o.Kind))
                    .SequenceEqual(converted.Objects.Select(o => (o.SourceId, o.EventIndex, o.Kind)))
                || actual.Text != expected.Text || !actual.PlayableObjects.SequenceEqual(expected.PlayableObjects)
                || !actual.PlayableHardRockObjects.SequenceEqual(expected.PlayableHardRockObjects)
                || !map.ContentEquals(before))
                throw new Exception("Equal-time/order parent export diverged from conversion or changed source content.");
        }
    }

    public static void MatchesUncached()
    {
        MatchesUncached(false);
        MatchesUncached(true);
    }

    private static void MatchesUncached(bool useOverride)
    {
        var velocityQuery = typeof(OsuBeatmapWriter).GetMethod("EmittedSliderVelocityAt",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var timingFixture = new MapDocument();
        var random = new Random(423);
        for (int i = 0; i < 80; i++)
        {
            timingFixture.TimingPoints.Add(new()
            {
                TimeMs = random.Next(12) * 100, SourceOrder = random.Next(3),
                Uninherited = i % 3 == 0, BeatLengthMs = i % 7 == 0 ? double.NaN : i % 3 == 0 ? 500 : -25 - i
            });
            var lookup = new TimingMap.Lookup(timingFixture);
            foreach (double time in new[] { -1d, 0, 99.999, 100, 100.001, 500, 900, 1100, 1200 })
                if ((double)velocityQuery.Invoke(null, [timingFixture.TimingPoints, time])! != lookup.At(time).SliderVelocityMultiplier)
                    throw new Exception("Mutable emitted SV query diverged from the full timing lookup.");
        }
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,192,500,8,0,900,0:0:0:0:\n100,192,1000,2,0,B|200:50|300:192,2,300\n256,192,3000,1,0,0:0:0:0:\n");
        var cache = new OsuWriteCache();
        if (useOverride) map.SliderMultiplierOverride = 2;
        Check(); Check();
        map.Fruits.Add(new() { TimeMs = 1200.5, X = 123.6 }); Check();
        map.Fruits[^1].X = 400; Check();
        map.Fruits[^1].TimeMs = 250; Check();
        map.BananaShowers[0].EndTimeMs += 200; Check();
        map.BananaShowers[0].TimeMs = 1000; map.BananaShowers[0].SourceOrder = 10; Check();
        map.BananaShowers[0].SourceOrder = -1; Check();
        map.TimingPoints[0].BeatLengthMs = 450; Check();
        map.SliderTickRate = 2; Check();
        var imported = map.ImportedSliders[0];
        imported.OriginalLine = null; imported.ControlPoints[1] = new(300, 80); imported.PixelLength = 400; Check();
        imported.SpanCount = 3; Check();
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 5000, X = 100 }, new() { TimeMs = 6000, X = 250 }]);
        map.Tracks.Add(track); map.DurationMs = 10000; Check();
        map.TimingPoints.Add(new() { TimeMs = -1, BeatLengthMs = 500 });
        Check();
        map.TimingPoints.RemoveAt(map.TimingPoints.Count - 1);
        map.TimingPoints.Add(new() { TimeMs = 5000.5, BeatLengthMs = 500 });
        bool rejected = false;
        try { OsuBeatmapWriter.Serialize(map, cache: cache); }
        catch (InvalidDataException) { rejected = true; }
        finally { map.TimingPoints.RemoveAt(map.TimingPoints.Count - 1); }
        if (!rejected) throw new Exception("Invalid timing unexpectedly populated the export cache.");
        Check();
        var saved = map.DeepClone();
        map.Fruits.Add(new() { TimeMs = 4500, X = 200 }); Check(); Check();
        track.Nodes[^1].X += 25; Check();
        track.SpanCount = 2; Check();
        map.TimingPoints.Add(new() { TimeMs = 4000, BeatLengthMs = -50, Uninherited = false }); Check();
        map.TimingPoints[^1].Volume = 45; Check();
        map = saved.DeepClone(); track = map.Tracks.Single(); Check();
        imported = map.ImportedSliders.Single(); imported.TimeMs += 1; Check();
        track.StreamSnapDivisor = 4; Check();
        map = map.DeepClone(); Check();
        map.ImportedSliders.Clear(); map.BananaShowers.Clear(); Check();

        void Check()
        {
            var before = map.DeepClone();
            _ = cache.Convert(map);
            var actual = OsuBeatmapWriter.Serialize(map, cache: cache);
            var expected = OsuBeatmapWriter.Serialize(map);
            if (actual.Text != expected.Text || actual.ObjectSequenceMatches != expected.ObjectSequenceMatches
                || !actual.Diagnostics.SequenceEqual(expected.Diagnostics)
                || !actual.PlayableObjects.SequenceEqual(expected.PlayableObjects)
                || !actual.PlayableHardRockObjects.SequenceEqual(expected.PlayableHardRockObjects)
                || !actual.PlayableEndTimes.OrderBy(p => p.Key).SequenceEqual(expected.PlayableEndTimes.OrderBy(p => p.Key)))
                throw new Exception("Cached export diverged from uncached text, diagnostics, events or end times.");
            var read = OsuBeatmapReader.Read(actual.Text);
            if (read.DurationMs != actual.ReadBack.DurationMs || !map.ContentEquals(before))
                throw new Exception("Cached export changed source content or inferred duration.");
        }
    }
}
