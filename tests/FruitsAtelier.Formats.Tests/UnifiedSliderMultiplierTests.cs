using FruitsAtelier.Core;

internal static class UnifiedSliderMultiplierTests
{
    public static void Run()
    {
        InheritedTiming();
        foreach (bool compensate in new[] { false, true })
        {
            var map = new MapDocument
            {
                BeatLengthMs = 588.235294117647, TimingOffsetMs = 848,
                SliderMultiplier = 1.4, SliderMultiplierOverride = 2.94,
                SliderTickRate = 2, DurationMs = 80000
            };
            map.TimingPoints.Add(new() { TimeMs = 848, BeatLengthMs = map.BeatLengthMs });
            var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = compensate };
            track.Nodes.AddRange([new() { TimeMs = 73593.09803921568, X = 21.571262649948387 },
                new() { TimeMs = 73789.17647058822, X = 488.1619151932774 }]);
            map.Tracks.Add(track);
            var history = new EditorHistory(map);
            var cache = new OsuWriteCache();
            CheckCurrent();
            history.Begin("Extend slider");
            history.Document.Tracks[0].Nodes[^1].X = 512;
            history.Commit();
            CheckCurrent();
            history.Undo(); CheckCurrent();
            history.Redo(); CheckCurrent();

            var noOverride = history.Document.DeepClone(); noOverride.SliderMultiplierOverride = null;
            Check(!cache.Convert(noOverride).Success, "Original base cannot represent the extended slider.");
            CheckCurrent();
            var tooFast = history.Document.DeepClone();
            tooFast.Tracks[0].Nodes[^1].TimeMs = tooFast.Tracks[0].Nodes[0].TimeMs + 90;
            Check(!cache.Convert(tooFast).Success, "Final SV=10 remains a real speed limit.");
            CheckCurrent();

            void CheckCurrent()
            {
                var document = history.Document;
                var before = document.DeepClone();
                var converted = cache.Convert(document);
                Check(converted.Success, string.Join("; ", converted.Diagnostics));
                var slider = converted.Sliders.Single();
                Check(slider.SliderVelocityMultiplier is > 0 and <= 10, "Conversion uses final inherited SV.");
                double expectedVelocity = 100 * document.EffectiveSliderMultiplier
                    / (document.BeatLengthMs * ((float)(100 / slider.SliderVelocityMultiplier) / 100d));
                Check(Math.Abs(slider.Velocity - expectedVelocity) < 1e-10, "Conversion uses the current base.");
                var output = OsuBeatmapWriter.Serialize(document, cache: cache);
                var full = OsuBeatmapWriter.Serialize(document);
                var restored = OsuBeatmapWriter.Serialize(ProjectSerializer.Read(ProjectSerializer.Serialize(document)));
                Check(output.ObjectSequenceMatches && output.ReadBack.SliderMultiplier == 2.94, "Export uses the same base.");
                Check(output.Text == full.Text && output.Text == restored.Text
                    && output.PlayableObjects.SequenceEqual(full.PlayableObjects)
                    && output.PlayableHardRockObjects.SequenceEqual(full.PlayableHardRockObjects), "Cache and reload preserve playback.");
                var exportedSlider = output.ReadBack.ImportedSliders.Single();
                double exportedSv = TimingMap.At(output.ReadBack, exportedSlider.TimeMs).SliderVelocityMultiplier;
                Check(Math.Abs(exportedSv - slider.SliderVelocityMultiplier) < 1e-10, "Export does not rebase generated SV a second time.");
                Check(document.ContentEquals(before), "Conversion and export must not mutate authoring.");
            }
        }
    }

    private static void InheritedTiming()
    {
        var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n100,-50,4,1,0,100,0,0\n[HitObjects]\n100,192,1000,2,0,L|300:192,2,200\n");
        var original = CatchStreamConverter.Convert(map);
        map.SliderMultiplierOverride = 2.8;
        var lookup = new TimingMap.Lookup(map);
        Check(Math.Abs(lookup.At(1000).SliderVelocityMultiplier - 1) < 1e-12, "Inherited SV uses the current base.");
        var created = TimingEditing.Create(map, 500, true);
        map.TimingPoints.Add(created);
        var converted = CatchStreamConverter.Convert(map);
        Check(converted.Success && converted.Objects.SequenceEqual(original.Objects), "Inserting inherited timing preserves legacy playback under an override.");
        var slider = converted.Sliders.Single();
        Check(Math.Abs(ImportedSliderConverter.EndTimeMs(map, map.ImportedSliders.Single())
            - slider.StartTimeMs - slider.DurationMs) < 1e-8, "Imported duration agrees with conversion.");
        var output = OsuBeatmapWriter.Serialize(map);
        Check(output.ObjectSequenceMatches, "Inherited timing exports with an override.");
        map.SliderMultiplierOverride = 2.94;
        Check(!lookup.MatchesTiming(map), "Base changes invalidate timing snapshots.");
        var cache = new OsuWriteCache();
        var before = OsuBeatmapWriter.Serialize(map, cache: cache);
        map.DistancePerBeatOverride = 350;
        var after = OsuBeatmapWriter.Serialize(map, cache: cache);
        Check(before.Text == after.Text, "Distance-snap DPB does not change the map's base SV.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
