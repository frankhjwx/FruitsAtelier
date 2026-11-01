using FruitsAtelier.Core;

internal static class SliderBpmTests
{
    public static void Run()
    {
        var map = new MapDocument { EnableSliderBpmMultiplier = true, DurationMs = 20000, SliderMultiplier = 1.4 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Meter = 3, SampleSet = 2, SampleIndex = 4, Volume = 67, Effects = 1 });
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = -50, Uninherited = false, Meter = 3, SampleSet = 2, SampleIndex = 4, Volume = 67, Effects = 1 });
        var first = Track(1000, 2000, 4); var next = Track(2100, 3100, 4); var normal = Track(3200, 4200, 1);
        map.Tracks.AddRange([first, next, normal]);
        var conversionCache = new CatchConversionCache(); var writeCache = new OsuWriteCache();
        foreach (double multiplier in SliderBpm.Presets.Concat(new[] { 3.7, 16d, 32d }))
        {
            first.BpmMultiplier = multiplier;
            var converted = CatchStreamConverter.Convert(map, cache: conversionCache);
            Check(converted.Success, string.Join(";", converted.Diagnostics));
            Near(converted.Sliders.First().BeatLengthMs, 500 / multiplier, "Per-parent beat length");
            Check(converted.Objects.Where(o => o.SourceId == first.Id).All(o => Math.Abs(o.X - CurveMath.PositionAtTime(first, o.TimeMs)) < .001
                || o.Kind == CatchObjectKind.TinyDroplet), "BPM edit changed the authored trajectory");
            var output = OsuBeatmapWriter.Serialize(map, cache: writeCache);
            Check(output.ObjectSequenceMatches, string.Join(";", output.Diagnostics));
            var timing = new TimingMap.Lookup(output.ReadBack);
            Near(timing.At(1000).BeatLengthMs, 500 / multiplier, "First exported BPM");
            Near(timing.At(2100).BeatLengthMs, 125, "Next slider's independent BPM");
            Near(timing.At(3200).BeatLengthMs, 500, "Normal slider's restored BPM");
            Near(timing.At(5000).BeatLengthMs, 500, "Final restored BPM");
            AssertPairs(output.ReadBack);
            Check(output.Text == OsuBeatmapWriter.Serialize(map).Text, "Timing cache returned stale BPM output");
        }
        first.BpmMultiplier = 4;
        // Adjacent equal-BPM sliders share the effective red line when their lifetimes meet.
        next.Nodes[0].TimeMs = 2000;
        Check(!OsuBeatmapWriter.Serialize(map).ReadBack.TimingPoints.Any(p => p.Uninherited && p.TimeMs == 2000), "Equal BPM emitted a redundant red");
        map.HitsoundOverrides.Add(new(first.Id, 0, new HitSampleSettings(3, 3, 5, 41)));
        AssertPairs(OsuBeatmapWriter.Serialize(map).ReadBack);
        map.HitsoundOverrides.Clear();
        var representation = map.DeepClone();
        Check(SliderRepresentationEditing.ConvertToLegacy(representation, first.Id).Success, "BPM track did not convert to Legacy");
        Check(representation.ImportedSliders.Single().BpmMultiplier == 4, "Representation lost BPM");
        var priorLegacy = representation.DeepClone();
        representation.ImportedSliders.Single().BpmMultiplier = 8;
        Check(SliderRepresentationEditing.RefreshLegacyDroplets(representation, previous: priorLegacy).Success, "Legacy BPM refresh failed");
        Check(representation.ImportedSliders.Single().BpmMultiplier == 8, "Legacy refresh lost BPM");
        Check(SliderRepresentationEditing.ConvertToTrack(representation, first.Id).Success, "BPM Legacy did not convert to track");
        var boundary = map.DeepClone();
        boundary.TimingPoints.Add(new() { TimeMs = 1500, BeatLengthMs = 400, Meter = 5, SampleSet = 3, SampleIndex = 2, Volume = 52, Effects = 0 });
        boundary.TimingPoints.Add(new() { TimeMs = 2000, BeatLengthMs = -50, Uninherited = false, Meter = 5, SampleSet = 2, SampleIndex = 7, Volume = 36, Effects = 1 });
        var boundaryOutput = OsuBeatmapWriter.Serialize(boundary);
        Check(boundaryOutput.ObjectSequenceMatches, "Authored timing boundary changed slider playback");
        Near(new TimingMap.Lookup(boundaryOutput.ReadBack).At(2000).BeatLengthMs, 100, "New base BPM was not multiplied independently");
        AssertPairs(boundaryOutput.ReadBack);
        var conflicting = map.DeepClone();
        conflicting.Tracks[1].Nodes[0].TimeMs = conflicting.Tracks[0].Nodes[0].TimeMs;
        conflicting.Tracks[1].Nodes[1].TimeMs = conflicting.Tracks[0].Nodes[1].TimeMs;
        conflicting.Tracks[1].BpmMultiplier = 2;
        bool collision = false;
        try { OsuBeatmapWriter.Serialize(conflicting); } catch (InvalidDataException) { collision = true; }
        Check(collision, "Conflicting BPMs at the same exported head were accepted");
        var serialized = ProjectSerializer.Serialize(map);
        Check(serialized.Contains("\"SchemaVersion\": 47"), "Missing BPM schema");
        Check(ProjectSerializer.Read(serialized).ContentEquals(map), "Single difficulty lost BPM settings");
        var project = BeatmapProject.FromDocuments([map]);
        var multi = ProjectSerializer.Serialize(project);
        Check(multi.Contains("\"SchemaVersion\": 48") && ProjectSerializer.ReadProject(multi).Difficulties[0].Document.ContentEquals(map), "Project lost BPM settings");
        var history = new EditorHistory(map); history.Begin("BPM"); history.Document.Tracks[0].BpmMultiplier = 8; history.Commit();
        history.Undo(); Check(history.Document.ContentEquals(map), "BPM undo lost content");
        history.Redo(); Check(history.Document.Tracks[0].BpmMultiplier == 8, "BPM redo failed");
        map.EnableSliderBpmMultiplier = false;
        Near(CatchStreamConverter.Convert(map, cache: conversionCache).Sliders[0].BeatLengthMs, 500, "Disabled multiplier still applied");
        var native = OsuBeatmapReader.Read("osu file format v14\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,2,3,62,1,1\n[HitObjects]\n100,192,1000,2,0,L|300:192,1,200\n100,192,2000,2,0,L|300:192,1,200\n");
        native.EnableSliderBpmMultiplier = true;
        native.ImportedSliders[0].BpmMultiplier = 8;
        var geometry = ImportedSliderConverter.Path(native.ImportedSliders[0]).ToArray();
        var nativeOutput = OsuBeatmapWriter.Serialize(native);
        Check(nativeOutput.ObjectSequenceMatches, "Native BPM export failed");
        AssertPairs(nativeOutput.ReadBack);
        Check(ImportedSliderConverter.Path(native.ImportedSliders[0]).SequenceEqual(geometry), "Native geometry changed");
        Near(new TimingMap.Lookup(nativeOutput.ReadBack).At(2000).BeatLengthMs, 500, "BPM leaked into native neighbour");
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            map.DefaultSliderBpmMultiplier = invalid;
            bool rejected = false;
            try { ProjectSerializer.Serialize(map); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid custom BPM accepted");
        }
        var fast = new MapDocument { EnableSliderBpmMultiplier = true, SliderMultiplier = 1, DurationMs = 2000 };
        var fastTrack = Track(1000, 1010, 32); fastTrack.Nodes[1].X = 400; fast.Tracks.Add(fastTrack);
        Check(CatchStreamConverter.Convert(fast).Success, "BPM did not permit speed above 10x base SV");
        fastTrack.BpmMultiplier = .125;
        Check(!CatchStreamConverter.Convert(fast).Success, "Low BPM silently changed an impossible curve");
    }

    private static CurveTrack Track(double start, double end, double multiplier)
    {
        var track = new CurveTrack { BpmMultiplier = multiplier, CompensateTinyDroplets = false };
        track.Nodes.AddRange([new() { TimeMs = start, X = 100 }, new() { TimeMs = end, X = 200 }]);
        return track;
    }

    private static void AssertPairs(MapDocument map)
    {
        foreach (var group in map.TimingPoints.GroupBy(p => p.TimeMs))
        {
            var red = group.LastOrDefault(p => p.Uninherited); var green = group.LastOrDefault(p => !p.Uninherited);
            if (red is null || green is null) continue;
            Check(red.Meter == green.Meter && red.SampleSet == green.SampleSet && red.SampleIndex == green.SampleIndex
                && red.Volume == green.Volume && red.Effects == green.Effects, "Red/green public properties differ");
        }
    }
    private static void Near(double a, double b, string message) => Check(Math.Abs(a - b) < 1e-6, message);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
