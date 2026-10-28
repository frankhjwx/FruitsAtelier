using FruitsAtelier.Core;

internal static class ExportSnapTests
{
    public static void Run()
    {
        Spinners();
        Kiai();
        NativeSliderHeads();
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:2.02\nSliderTickRate:2\n[TimingPoints]\n670,254.237288135593,3,2,1,85,1,0\n12873,254.237288135593,4,2,1,85,1,0\n[HitObjects]\n215,192,33212,5,0,2:0:0:0:\n100,192,38297,5,8,2:0:0:0:\n442,192,55585,5,4,1:2:0:0:\n");
        var before = map.DeepClone();
        var cache = new OsuWriteCache();
        var source = cache.Convert(map);
        var output = OsuBeatmapWriter.Serialize(map, cache: cache);
        var again = OsuBeatmapWriter.Serialize(map, cache: cache);
        var full = OsuBeatmapWriter.Serialize(map);
        var expected = new[] { 33212d, 38297, 55585 };
        Check(source.Objects.Select(o => o.TimeMs).SequenceEqual(expected), "Internal conversion preserves one-ms offsets");
        Check(output.ReadBack.Fruits.Select(f => f.TimeMs).SequenceEqual(expected), "Export preserves retained integer source lines");
        Check(output.ObjectSequenceMatches && output.MaxConvertedTimeErrorMs == 0
            && output.MaxTimeQuantizationMs == 0
            && output.Text == again.Text && output.Text == full.Text, "Cached and full export agree with conversion");
        Check(OsuBeatmapWriter.Serialize(output.ReadBack).Text == output.Text, "Export is idempotent");
        Check(map.ContentEquals(before), "Export and preview retain source and undo content");

        var controls = new MapDocument { BeatLengthMs = 500, DurationMs = 5000 };
        controls.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1001, X = 200 },
            new() { TimeMs = 1007, X = 300 }, new() { TimeMs = 1125, X = 400 }]);
        var untouched = OsuBeatmapWriter.Serialize(controls);
        Check(untouched.ReadBack.Fruits.Select(f => f.TimeMs).SequenceEqual(new[] { 1000d, 1001, 1007, 1125 }),
            "Export preserves close distinct heads, off-grid notes and already snapped notes");
        controls.Fruits.Clear();
        controls.Fruits.AddRange([new() { TimeMs = 999, X = 100 }, new() { TimeMs = 1001, X = 200 }]);
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.Fruits.Select(f => f.TimeMs).SequenceEqual(new[] { 999d, 1001 }),
            "Two distinct heads cannot converge onto one grid point");
        controls.Fruits.Clear(); controls.Fruits.Add(new() { TimeMs = 2168, X = 100 });
        controls.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 1000 }, new() { TimeMs = 2169, BeatLengthMs = 500 }]);
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.Fruits[0].TimeMs == 2168,
            "A nearby red line cannot pull a note across its active timing grid");
        controls.TimingPoints.Clear(); controls.Fruits[0].TimeMs = Math.BitDecrement(1000d);
        Check(CatchStreamConverter.Convert(controls).Objects[0].TimeMs == Math.BitDecrement(1000d)
            && OsuBeatmapWriter.Serialize(controls).ReadBack.Fruits[0].TimeMs == 999,
            "Fractional authoring times retain ordinary integer truncation");

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

    private static void NativeSliderHeads()
    {
        foreach (double sv in new[] { 1d, 2d })
        {
            var map = OsuBeatmapReader.Read("osu file format v14\n[Difficulty]\nSliderMultiplier:2\nSliderTickRate:1\n[TimingPoints]\n0,324.324324324324,4,2,1,50,1,0\n32439.432432432397,-100,4,2,1,50,0,0\n[HitObjects]\n230,187,32439.432432432397,6,0,P|194:171|189:38,1,200.00000000000003,0|0,0:0|\n");
            map.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -50, Uninherited = false, SampleSet = 2, SampleIndex = 1, Volume = 50 });
            var slider = map.ImportedSliders.Single();
            slider.PreserveNativeGeometry = true;
            slider.SliderVelocityMultiplier = sv;
            var before = map.DeepClone();
            var cache = new OsuWriteCache();
            var output = OsuBeatmapWriter.Serialize(map, cache: cache);
            var emitted = output.ReadBack.ImportedSliders.Single();
            Check(emitted.TimeMs == 32439 && output.ObjectSequenceMatches, $"Canvas slider heads must export integer milliseconds (SV {sv}): {string.Join("; ", output.Diagnostics)}\n{output.Text}");
            Check(Math.Abs(ImportedSliderConverter.SliderVelocityAt(output.ReadBack, emitted) - sv) < 1e-9,
                "The exported integer slider head must use its authored SV");
            Check(Math.Abs(ImportedSliderConverter.DurationMs(map, slider) - ImportedSliderConverter.DurationMs(output.ReadBack, emitted)) < 1e-6,
                "Quantizing a canvas slider head must preserve its SV and duration");
            Check(emitted.PathType == slider.PathType && emitted.ControlPoints.SequenceEqual(slider.ControlPoints)
                && emitted.PixelLength == slider.PixelLength, "Head quantization changed native geometry");
            Check(output.MaxTimeQuantizationMs > .43 && output.MaxTimeQuantizationMs < .44, "Native head quantization must be reported");
            Check(output.Text == OsuBeatmapWriter.Serialize(map, cache: cache).Text
                && output.Text == OsuBeatmapWriter.Serialize(map).Text, "Cached native export differs");
            Check(map.ContentEquals(before), "Export must retain exact authoring times");
        }
    }

    private static void Kiai()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n1001,-50,4,2,0,40,0,1\n2001,-100,4,3,0,80,0,0\n3003,-100,4,1,0,60,0,1\n[HitObjects]\n100,192,1100,2,0,L|300:192,1,140\n128,192,2200,1,0,0:0:0:0:\n");
        var before = map.DeepClone();
        var result = KiaiTimingEditing.Prepare(map) ?? throw new Exception("Expected Kiai offsets");
        Check(map.ContentEquals(before), "Kiai preparation is read-only");
        bool At(double time) => (TimingEditing.Current(result, time)!.Effects & 1) != 0;
        Check(!At(999) && At(1000) && At(1999) && !At(2000) && !At(3002) && At(3003),
            "Kiai start/end fix only one-ms offsets");
        Check(result.TimingPoints.Any(p => p.TimeMs == 1001 && p.BeatLengthMs == -50 && p.SampleSet == 2 && p.Volume == 40)
            && result.TimingPoints.Any(p => p.TimeMs == 2001 && p.SampleSet == 3 && p.Volume == 80),
            "Combined SV and sample changes retain their original timing");
        Check(KiaiTimingEditing.Prepare(result) is null, "Kiai fixes are idempotent");
        var occupied = new MapDocument { BeatLengthMs = 500 };
        occupied.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, Volume = 40 },
            new() { TimeMs = 1001, BeatLengthMs = -100, Uninherited = false, Effects = 1 }]);
        Check(KiaiTimingEditing.Prepare(occupied) is null, "Kiai fixes preserve occupied timing boundaries");
        occupied.TimingPoints[1].TimeMs = 1001;
        Check(KiaiTimingEditing.Prepare(occupied) is null, "Kiai fixes preserve coincident timing lines");
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
            Check(spinner.TimeMs == 386 && spinner.EndTimeMs == end, "Spinner head and tail retain one-ms offsets");
            Check(output.ObjectSequenceMatches && output.MaxConvertedTimeErrorMs == 0
                && conversion.Objects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(output.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X))),
                "Spinner preview, exported bananas and downstream slider RNG agree");
            Check(output.Text == OsuBeatmapWriter.Serialize(map, cache: cache).Text
                && output.Text == OsuBeatmapWriter.Serialize(map).Text
                && output.Text == OsuBeatmapWriter.Serialize(output.ReadBack).Text, "Spinner export is cached and idempotent");
            Check(map.ContentEquals(before), "Spinner export preserves authoring data and source lines");
            Check(output.Text.Contains($"256,192,386,8,4,{end},1:2:0:50:"), "Exported spinner retains position, sounds and samples");
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
            "Export retains distinct circle and spinner heads");
        controls.Fruits.Clear(); controls.BananaShowers[0].EndTimeMs = 1000;
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.BananaShowers[0].TimeMs == 999,
            "Export preserves a positive one-ms spinner duration");

        controls.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 1000 }, new() { TimeMs = 387, BeatLengthMs = 500 }]);
        controls.BananaShowers[0].TimeMs = 386; controls.BananaShowers[0].EndTimeMs = 2387;
        Check(OsuBeatmapWriter.Serialize(controls).ReadBack.BananaShowers[0].TimeMs == 386, "Spinner head cannot cross a later red-line grid");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
}
