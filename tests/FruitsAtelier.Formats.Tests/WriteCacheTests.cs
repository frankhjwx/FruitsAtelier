using FruitsAtelier.Core;

internal static class WriteCacheTests
{
    public static void MatchesUncached()
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
