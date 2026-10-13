using FruitsAtelier.Core;

internal static class LegacyV7Tests
{
    public static void Run()
    {
        foreach (double sv in new[] { .5, 1, 2, 3.3 })
        {
            string text = Fixture(sv);
            var document = OsuBeatmapReader.Read(text);
            Check(document.LegacySliderTicks, "v7 tick rules missing");
            var converted = CatchStreamConverter.Convert(document);
            Check(converted.Success, "v7 conversion failed");
            var droplets = converted.Objects.Where(o => o.SourceId == document.ImportedSliders[0].Id && o.Kind == CatchObjectKind.Droplet).ToArray();
            Check(droplets.Length == 2, "v7 has one tick in each 200px span, independently of SV");
            Near(200, droplets[0].X); Near(200, droplets[1].X);
            double velocity = 100 / (500 * ((float)(100 / sv) / 100d));
            double tickDistance = velocity * 500 / sv;
            Near(1000 + tickDistance / velocity, droplets[0].TimeMs);
            Near(1000 + (400 - tickDistance) / velocity, droplets[1].TimeMs);
            var result = RoundTrip(document);
            Check(result.PlayableHardRockObjects.Select(o => (o.Kind, o.TimeMs, o.X))
                .SequenceEqual(CatchPreviewMods.HardRock(document, converted).Select(o => (o.Kind, o.TimeMs, o.X))), "v7 HR playback changed");

            var modern = OsuBeatmapReader.Read(text.Replace("format v7", "format v14"));
            Check(WorkspaceSynchronization.HasFieldDifferences(document, modern), "format-only changes must be reviewable");
            var cache = new CatchConversionCache();
            CatchStreamConverter.Convert(document, false, cache);
            document.LegacySliderTicks = false;
            var changed = CatchStreamConverter.Convert(document, false, cache);
            Check(changed.Objects.SequenceEqual(CatchStreamConverter.Convert(document, false).Objects), "version change reused stale tick/RNG cache");
            document.LegacySliderTicks = true;
            Check(CatchStreamConverter.Convert(document, false, cache).Objects.SequenceEqual(converted.Objects), "restoring v7 left stale tick/RNG cache");
            var external = new WorkspaceSyncCandidate(Path.Combine(AppContext.BaseDirectory, "format-v7.osu"),
                WorkspaceSynchronization.Digest(text), text.Replace("format v7", "format v14"), modern);
            var merge = WorkspaceSynchronization.CompareWithoutBaseline(document, external, AppContext.BaseDirectory, false);
            Check(merge.Conflicts.Any(c => c.Key == "Format/Version"), "format-only change needs an explicit choice");
            foreach (bool useExternal in new[] { false, true })
            {
                var resolved = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => useExternal));
                Check(resolved.LegacySliderTicks != useExternal, "format choice changed or lost the tick rules");
                Check(!OsuBeatmapWriter.Serialize(resolved).Text.Contains("[Format]"), "internal format comparison leaked into osu sections");
            }
        }

        foreach (bool compensation in new[] { false, true })
        foreach (bool hardRock in new[] { false, true })
        {
            var document = OsuBeatmapReader.Read(Fixture(2));
            document.DerandomizeFSliderDroplets = hardRock;
            document.DerandomizeDropletsForHardRock = hardRock;
            var track = new CurveTrack { SpanCount = 2, CompensateTinyDroplets = compensation };
            track.Nodes.Add(new() { TimeMs = 3000, X = 100 });
            track.Nodes.Add(new() { TimeMs = 4000, X = 300 });
            document.Tracks.Add(track);
            var later = new CurveTrack();
            later.Nodes.Add(new() { TimeMs = 5000, X = 100 });
            later.Nodes.Add(new() { TimeMs = 5500, X = 450 });
            document.Tracks.Add(later);
            var cache = new OsuWriteCache();
            var output = OsuBeatmapWriter.Serialize(document, compensation, cache);
            Check(output.ObjectSequenceMatches && output.ReadBack.LegacySliderTicks, "v7 FSlider changed its nested sequence on export");
            Check(output.PlayableObjects.SequenceEqual(OsuBeatmapWriter.Serialize(document, compensation).PlayableObjects), "v7 cached export differs");
            var warm = OsuBeatmapWriter.Serialize(document, compensation, cache);
            Check(output.PlayableObjects.SequenceEqual(warm.PlayableObjects) && output.PlayableHardRockObjects.SequenceEqual(warm.PlayableHardRockObjects), "v7 warm export changed RNG or geometry");
            track.Nodes[1].X = 350;
            output = OsuBeatmapWriter.Serialize(document, compensation, cache);
            Check(output.ObjectSequenceMatches && output.PlayableObjects.SequenceEqual(OsuBeatmapWriter.Serialize(document, compensation).PlayableObjects), "v7 edited FSlider differs");
        }

        var legacy = OsuBeatmapReader.Read(Fixture(2));
        var history = new EditorHistory(legacy);
        history.Begin("Change tick rules"); history.Document.LegacySliderTicks = false; history.Commit();
        history.Undo(); Check(history.Document.LegacySliderTicks, "undo lost v7 rules");
        history.Redo(); Check(!history.Document.LegacySliderTicks, "redo lost modern rules");
        var project = BeatmapProject.FromDocuments([legacy, OsuBeatmapReader.Read(Fixture(1).Replace("format v7", "format v14"))]);
        string multi = ProjectSerializer.Serialize(project);
        Check(multi.Contains("\"SchemaVersion\": 20"), "v7 multi-project must reject older readers");
        var restored = ProjectSerializer.ReadProject(multi);
        Check(restored.Difficulties[0].Document.LegacySliderTicks && !restored.Difficulties[1].Document.LegacySliderTicks, "mixed-format project lost independent rules");
        string ordinary = ProjectSerializer.Serialize(new MapDocument());
        Check(!ordinary.Contains("LegacySliderTicks") && !ProjectSerializer.Read(ordinary).LegacySliderTicks, "ordinary/older projects changed defaults");
        Check(OsuBeatmapWriter.Serialize(new MapDocument()).Text.StartsWith("osu file format v14"), "new maps must still use v14");
        Check(!SliderMultiplierEditing.TryRebase(OsuBeatmapWriter.Serialize(legacy), 2, out _, double.PositiveInfinity),
            "SV compensation must reject changed v7 ticks even when slider velocity stays equal");
    }

    private static OsuWriteResult RoundTrip(MapDocument document)
    {
        string project = ProjectSerializer.Serialize(document);
        Check(project.Contains("\"SchemaVersion\": 19"), "v7 project must reject older readers");
        var restored = ProjectSerializer.Read(project);
        Check(document.ContentEquals(restored), "project lost v7 content");
        var result = OsuBeatmapWriter.Serialize(restored);
        Check(result.Text.StartsWith("osu file format v7\r\n") && result.ReadBack.LegacySliderTicks, "v7 export changed tick rules");
        Check(result.ObjectSequenceMatches, "v7 export changed the object sequence");
        Near(0, result.MaxConvertedXError); Near(0, result.MaxConvertedTimeErrorMs);
        Check(document.TimingPoints.Select(p => p.OriginalLine).SequenceEqual(result.ReadBack.TimingPoints.Select(p => p.OriginalLine)), "v7 timing changed");
        return result;
    }

    private static string Fixture(double sv) => FormattableString.Invariant($"""
        osu file format v7
        [General]
        Mode:2
        [Difficulty]
        OverallDifficulty:7
        SliderMultiplier:1
        SliderTickRate:1
        [TimingPoints]
        0,500
        0,{-100 / sv},4,1,0,100,0,0
        [HitObjects]
        100,192,1000,2,0,L|300:192,2,200
        256,192,6000,8,0,6500
        100,192,7000,2,0,L|300:192,1,200
        """);

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > 1e-7) throw new Exception($"Expected {expected}; got {actual}"); }
}
