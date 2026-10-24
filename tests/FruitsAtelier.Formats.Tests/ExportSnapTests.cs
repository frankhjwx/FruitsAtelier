using FruitsAtelier.Core;

internal static class ExportSnapTests
{
    public static void Run()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:2.02\nSliderTickRate:2\n[TimingPoints]\n670,254.237288135593,3,2,1,85,1,0\n12873,254.237288135593,4,2,1,85,1,0\n[HitObjects]\n215,192,33212,5,0,2:0:0:0:\n100,192,38297,5,8,2:0:0:0:\n442,192,55585,5,4,1:2:0:0:\n");
        var before = map.DeepClone();
        var cache = new OsuWriteCache();
        var source = cache.Convert(map);
        var output = OsuBeatmapWriter.Serialize(map, cache: cache);
        var again = OsuBeatmapWriter.Serialize(map, cache: cache);
        var full = OsuBeatmapWriter.Serialize(map);
        var expected = new[] { 33211d, 38296, 55584 };
        Check(source.Objects.Select(o => o.TimeMs).SequenceEqual(expected), "Internal conversion corrects the active red-line grid");
        Check(output.ReadBack.Fruits.Select(f => f.TimeMs).SequenceEqual(expected), "Export corrects retained integer source lines");
        Check(output.ObjectSequenceMatches && output.MaxConvertedTimeErrorMs == 0
            && output.MaxTimeQuantizationMs == 1
            && output.Text == again.Text && output.Text == full.Text, "Cached and full export agree with conversion");
        Check(OsuBeatmapWriter.Serialize(output.ReadBack).Text == output.Text, "Correction is idempotent");
        Check(map.ContentEquals(before), "Export and preview retain source and undo content");

        var controls = new MapDocument { BeatLengthMs = 500, DurationMs = 5000 };
        controls.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1001, X = 200 },
            new() { TimeMs = 1007, X = 300 }, new() { TimeMs = 1125, X = 400 }]);
        var untouched = OsuBeatmapWriter.Serialize(controls);
        Check(untouched.ReadBack.Fruits.Select(f => f.TimeMs).SequenceEqual(new[] { 1000d, 1001, 1007, 1125 }),
            "Correction preserves close distinct heads, off-grid notes and already snapped notes");
        controls.Fruits.Clear();
        controls.Fruits.AddRange([new() { TimeMs = 999, X = 100 }, new() { TimeMs = 1001, X = 200 }]);
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.Fruits.Select(f => f.TimeMs).SequenceEqual(new[] { 999d, 1001 }),
            "Two distinct heads cannot converge onto one grid point");

        var streamMap = new MapDocument { BeatLengthMs = 500, DurationMs = 40000 };
        var stream = new CurveTrack { StreamSnapDivisor = 3 };
        stream.Nodes.AddRange([new() { TimeMs = 0, X = 100 }, new() { TimeMs = 33000, X = 100 }]);
        streamMap.Tracks.Add(stream);
        var events = SliderFruitStream.Convert(streamMap, stream);
        Check(events[195].TimeMs == 32500, "Stream subdivisions retain exact integer grid times");
        var exported = OsuBeatmapWriter.Serialize(streamMap);
        Check(exported.ReadBack.Fruits[195].TimeMs == 32500 && exported.ObjectSequenceMatches,
            "Stream export and internal grid agree");

        var samples = new MapDocument { BeatLengthMs = 500, DurationMs = 5000 };
        samples.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 500, Uninherited = false, BeatLengthMs = -100, Volume = 20 },
            new() { TimeMs = 950, Uninherited = false, BeatLengthMs = -100, Volume = 20, SampleSet = 2 },
            new() { TimeMs = 1500, Uninherited = false, BeatLengthMs = -100, Volume = 40, SampleSet = 3 },
            new() { TimeMs = 2003, Uninherited = false, BeatLengthMs = -100, Volume = 60, SampleSet = 3 }]);
        var slider = new CurveTrack { CompensateTinyDroplets = false };
        slider.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 300 }]);
        samples.Tracks.Add(slider);
        var sampleExport = OsuBeatmapWriter.Serialize(samples, false);
        Check(!sampleExport.ReadBack.TimingPoints.Any(p => p.TimeMs == 500), "Unused sample sections are omitted");
        Check(sampleExport.ReadBack.TimingPoints.Any(p => p.TimeMs == 950), "The sample state needed by the slider head is retained");
        Check(sampleExport.ReadBack.TimingPoints.Any(p => p.TimeMs == 1500)
            && sampleExport.ReadBack.TimingPoints.Any(p => p.TimeMs == 2003), "Body samples and five-ms tail sample changes are retained");
        var sourceEvents = CatchStreamConverter.Convert(samples, false).Objects;
        var sourceSamples = new HitsoundResolver(samples, sourceEvents);
        var exportedSamples = new HitsoundResolver(sampleExport.ReadBack, sampleExport.PlayableObjects);
        Check(sourceEvents.Zip(sampleExport.PlayableObjects).All(p => sourceSamples.Describe(p.First) == exportedSamples.Describe(p.Second)),
            "Pruning preserves every event's sample settings");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
}
