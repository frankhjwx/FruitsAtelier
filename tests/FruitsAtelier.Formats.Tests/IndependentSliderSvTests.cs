using FruitsAtelier.Core;

internal static class IndependentSliderSvTests
{
    public static void Run()
    {
        foreach (int version in new[] { 7, 14 })
        {
            var map = OsuBeatmapReader.Read($"osu file format v{version}\n[General]\nMode:2\n[TimingPoints]\n0,500,4,1,0,80,1,0\n500,-50,4,2,3,60,0,1\n1500,-200,4,1,0,50,0,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n100,192,2000,2,0,L|300:192,1,200\n");
            var first = map.ImportedSliders[0]; var second = map.ImportedSliders[1];
            Check(first.SliderVelocityMultiplier == 2 && second.SliderVelocityMultiplier == .5, "Import did not capture each slider's SV.");
            double duration = ImportedSliderConverter.DurationMs(map, first);
            first.TimeMs = 2000; first.OriginalLine = first.OriginalLine!.Replace(",1000,", ",2000,");
            second.TimeMs = 3000; second.OriginalLine = second.OriginalLine!.Replace(",2000,", ",3000,");
            Check(ImportedSliderConverter.DurationMs(map, first) == duration, "Moving a slider inherited another green's SV.");
            map.TimingPoints[2].BeatLengthMs = -100;
            Check(ImportedSliderConverter.DurationMs(map, second) == duration * 4, "A green edit changed an independent SV.");
            var baseline = map.DeepClone();
            var export = OsuBeatmapWriter.Serialize(map);
            Check(export.ObjectSequenceMatches && map.ContentEquals(baseline), "Export changed content or playback.");
            Check(export.ReadBack.ImportedSliders[0].SliderVelocityMultiplier == 2
                && export.ReadBack.ImportedSliders[1].SliderVelocityMultiplier == .5, "Export did not encode independent head velocities.");
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).ContentEquals(map), "Independent SV did not persist.");
            var project = BeatmapProject.FromDocuments([map]);
            Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Difficulties[0].Document.ContentEquals(map), "Multi-difficulty SV did not persist.");
            LegacySliderVelocityEditing.Apply(map, new HashSet<Guid> { first.Id }, 1);
            Check(first.SliderVelocityMultiplier == 1 && ImportedSliderConverter.DurationMs(map, first) == duration * 2,
                "Manual SV did not change duration while retaining length.");
            Check(first.PixelLength == 200 && map.TimingPoints.Count == baseline.TimingPoints.Count, "Manual SV changed geometry or added green points.");
            map.TimingPoints[2].BeatLengthMs = -1000;
            if (version == 14)
            {
                var compensated = SliderMultiplierEditing.ValidateChange(map, map.SliderMultiplier * 2);
                Check(compensated.ObjectSequenceMatches, "Unused green SV constrained independent-slider base compensation.");
            }
        }
        SharedHead();
        ProcessedSv();
        ProcessedSvOverride();
    }

    private static void ProcessedSvOverride()
    {
        foreach (double multiplier in new[] { 1.92, 2.4, 1.75 })
        {
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,337.078651685393\n[HitObjects]\n240,192,1322,2,0,P|272:192|320:128,1,110.25\n");
            map.SliderMultiplierOverride = multiplier; map.OverrideSliderMultiplier = true;
            map.DerandomizeFSliderDroplets = true;
            var refreshed = SliderRepresentationEditing.RefreshLegacyDroplets(map);
            Check(refreshed.Success && refreshed.ValidatedOutput is { ObjectSequenceMatches: true },
                "Base-SV override rejected processed Legacy materialization: " + string.Join("; ", refreshed.Diagnostics));
            var first = OsuBeatmapWriter.Serialize(map);
            refreshed = SliderRepresentationEditing.RefreshLegacyDroplets(map, previous: map.DeepClone());
            Check(refreshed.Success, "Repeated base-SV refresh failed: " + string.Join("; ", refreshed.Diagnostics));
            var next = OsuBeatmapWriter.Serialize(map);
            Check(first.PlayableObjects.Count == next.PlayableObjects.Count && first.PlayableHardRockObjects.Count == next.PlayableHardRockObjects.Count
                && first.PlayableObjects.Zip(next.PlayableObjects).All(p => p.First == p.Second)
                && first.PlayableHardRockObjects.Zip(next.PlayableHardRockObjects).All(p => p.First == p.Second),
                "Repeated refresh changed NM or HR events with a base-SV override");
        }
    }

    private static void ProcessedSv()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        map.DerandomizeFSliderDroplets = true;
        var original = map.DeepClone();
        double duration = ImportedSliderConverter.DurationMs(map, map.ImportedSliders.Single());
        var result = SliderRepresentationEditing.RefreshLegacyDroplets(map);
        Check(result.Success, string.Join("; ", result.Diagnostics));
        var slider = map.ImportedSliders.Single();
        Check(slider.SliderVelocityMultiplier == 1 && slider.ProcessedSliderVelocityMultiplier > 1,
            "Postprocessing did not keep visible SV separate from actual SV.");
        Check(Math.Abs(ImportedSliderConverter.DurationMs(map, slider) - duration) < 1e-6
            && original.TimingPoints.Count == map.TimingPoints.Count
            && original.TimingPoints.Select(p => (p.TimeMs, p.BeatLengthMs)).SequenceEqual(map.TimingPoints.Select(p => (p.TimeMs, p.BeatLengthMs))),
            "Postprocessing changed duration or authored green timing.");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).ContentEquals(map), "Processed SV did not persist.");
        var overlap = map.DeepClone();
        var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.Randomize };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 100 }]); overlap.Tracks.Add(track);
        result = SliderRepresentationEditing.RefreshLegacyDroplets(overlap, previous: map);
        Check(result.Success && overlap.ImportedSliders.Single().SliderVelocityMultiplier == 1
            && OsuBeatmapWriter.Serialize(overlap).ObjectSequenceMatches,
            "A compatible FSlider did not share the Legacy postprocessed SV: " + string.Join("; ", result.Diagnostics));
        var migrated = map.DeepClone();
        var legacy = migrated.ImportedSliders.Single();
        double actual = legacy.ProcessedSliderVelocityMultiplier!.Value;
        legacy.SliderVelocityMultiplier = legacy.ProcessedSliderVelocityMultiplier = null;
        migrated.TimingPoints.Add(new() { TimeMs = legacy.TimeMs, Uninherited = false, BeatLengthMs = -100 / actual });
        LegacySliderVelocityEditing.Capture(migrated);
        Check(legacy.SliderVelocityMultiplier == 1 && legacy.ProcessedSliderVelocityMultiplier == actual,
            "Older processed projects did not recover both SV values.");
        var conflict = overlap.DeepClone();
        conflict.Tracks.Single().Nodes[1].TimeMs = 1001; conflict.Tracks.Single().Nodes[1].X = 500;
        var baseline = conflict.DeepClone();
        result = SliderRepresentationEditing.RefreshLegacyDroplets(conflict);
        Check(!result.Success && conflict.ContentEquals(baseline), "Failed overlap processing overwrote SV or authoring data.");
    }

    private static void SharedHead()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n");
        var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.Randomize };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 2000, X = 100 }]);
        map.Tracks.Add(track);
        var cache = new CatchConversionCache();
        var result = CatchStreamConverter.Convert(map, cache: cache);
        Check(result.Success && result.Sliders.All(s => s.SliderVelocityMultiplier == 1), "A simultaneous FSlider did not share Legacy SV.");
        Check(OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches, "A compatible overlap failed export.");
        track.Nodes[1].TimeMs = 1010; track.Nodes[1].X = 500;
        Check(!CatchStreamConverter.Convert(map, cache: cache).Success, "An impossible overlap silently increased Legacy SV.");
        track.Nodes[1].TimeMs = 2000; track.Nodes[1].X = 100;
        map.ImportedSliders[0].SliderVelocityMultiplier = 2;
        result = CatchStreamConverter.Convert(map, cache: cache);
        Check(result.Success && result.Sliders.All(s => s.SliderVelocityMultiplier == 2), "Cache retained the old shared SV.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
