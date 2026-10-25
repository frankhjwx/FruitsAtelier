using FruitsAtelier.Core;

internal static class ExportSnapTests
{
    public static void Run()
    {
        Spinners();
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
        controls.Fruits.Clear(); controls.Fruits.Add(new() { TimeMs = 2168, X = 100 });
        controls.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 1000 }, new() { TimeMs = 2169, BeatLengthMs = 500 }]);
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.Fruits[0].TimeMs == 2168,
            "A nearby red line cannot pull a note across its active timing grid");
        controls.TimingPoints.Clear(); controls.Fruits[0].TimeMs = Math.BitDecrement(1000d);
        Check(CatchStreamConverter.Convert(controls).Objects[0].TimeMs == 1000
            && OsuBeatmapWriter.Serialize(controls).ReadBack.Fruits[0].TimeMs == 1000,
            "Floating-point noise below an integer grid point does not become a fractional one-ms offset internally");

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

    private static void Spinners()
    {
        foreach (int end in new[] { 2386, 2387 })
        {
            var map = OsuBeatmapReader.Read($"osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n387,500,4,1,0,100,1,0\n[HitObjects]\n256,192,386,8,4,{end},1:2:0:50:\n128,192,3000,1,0,0:0:0:0:\n100,192,4000,2,0,L|300:192,1,140\n");
            var before = map.DeepClone();
            var cache = new OsuWriteCache();
            var conversion = cache.Convert(map);
            var output = OsuBeatmapWriter.Serialize(map, cache: cache);
            var spinner = output.ReadBack.BananaShowers.Single();
            Check(spinner.TimeMs == 387 && spinner.EndTimeMs == 2387, "Spinner head and tail repair one-ms offsets, including before the first red line");
            Check(output.ObjectSequenceMatches && output.MaxConvertedTimeErrorMs == 0
                && conversion.Objects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(output.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X))),
                "Spinner preview, exported bananas and downstream slider RNG agree");
            Check(output.Text == OsuBeatmapWriter.Serialize(map, cache: cache).Text
                && output.Text == OsuBeatmapWriter.Serialize(map).Text
                && output.Text == OsuBeatmapWriter.Serialize(output.ReadBack).Text, "Spinner correction is cached and idempotent");
            Check(map.ContentEquals(before), "Spinner correction preserves authoring data and source lines");
            Check(output.Text.Contains("256,192,387,8,4,2387,1:2:0:50:"), "Corrected spinner retains position, sounds and samples");
        }

        var controls = new MapDocument { BeatLengthMs = 500, DurationMs = 5000 };
        controls.TimingPoints.Add(new() { TimeMs = 387, BeatLengthMs = 500 });
        controls.BananaShowers.Add(new() { TimeMs = 385, EndTimeMs = 2385 });
        var untouched = OsuBeatmapWriter.Serialize(controls).ReadBack.BananaShowers.Single();
        Check(untouched.TimeMs == 385 && untouched.EndTimeMs == 2385, "Spinner offsets larger than one ms stay authored");
        controls.BananaShowers[0].TimeMs = 386; controls.BananaShowers[0].EndTimeMs = 2387;
        controls.Fruits.Add(new() { TimeMs = 387, X = 100 });
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.BananaShowers[0].TimeMs == 386, "Spinner head cannot move onto an occupied object head");

        controls.TimingPoints.Clear(); controls.Fruits.Clear();
        controls.Fruits.Add(new() { TimeMs = 1001, X = 100 });
        controls.BananaShowers[0].TimeMs = 999; controls.BananaShowers[0].EndTimeMs = 2000;
        var collisions = OsuBeatmapWriter.Serialize(controls).ReadBack;
        Check(collisions.Fruits[0].TimeMs == 1001 && collisions.BananaShowers[0].TimeMs == 999,
            "Circle and spinner corrections cannot merge distinct heads");
        controls.Fruits.Clear(); controls.BananaShowers[0].EndTimeMs = 1000;
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.BananaShowers[0].TimeMs == 999,
            "One-ms correction cannot collapse a positive spinner duration");

        controls.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 1000 }, new() { TimeMs = 387, BeatLengthMs = 500 }]);
        controls.BananaShowers[0].TimeMs = 386; controls.BananaShowers[0].EndTimeMs = 2387;
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.BananaShowers[0].TimeMs == 386, "Spinner head cannot cross a later red-line grid");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
}
