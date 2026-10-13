using FruitsAtelier.Core;

internal static class BananaPatternTests
{
    internal static void Run()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        var shower = new BananaShower { TimeMs = 1000, EndTimeMs = 1510, Pattern = new() };
        map.BananaShowers.Add(shower);
        var segments = BananaShowerPattern.Segments(shower, new(map));
        Check(segments.Count == 5 && segments[0].EndTimeMs == 1031.25 && segments[1].TimeMs == 1125
            && segments[^1].EndTimeMs == 1510, "Length, empty gap and clipped tail");
        Check(!BananaShowerPattern.Layout(shower, new(map)).IsExactFit, "Clipped final shower is not a perfect fit");
        shower.EndTimeMs = 1531.25;
        Check(BananaShowerPattern.Layout(shower, new(map)) is { IsExactFit: true, Segments.Count: 5 }, "N lengths plus N-1 gaps fits exactly");
        shower.EndTimeMs = 1500;
        Check(!BananaShowerPattern.Layout(shower, new(map)).IsExactFit, "Trailing gap is not a perfect fit");
        shower.EndTimeMs = 1031.25;
        Check(BananaShowerPattern.Layout(shower, new(map)) is { IsExactFit: true, Segments.Count: 1 }, "One shower has no gap");
        shower.EndTimeMs = 1510;
        shower.EndTimeMs = 1500.00000001;
        Check(BananaShowerPattern.Segments(shower, new(map)).Count == 4, "Floating-point tail cannot create a zero-length shower");
        shower.EndTimeMs = 1001.999;
        Check(BananaShowerPattern.Segments(shower, new(map)).Count == 0, "Sub-2ms tail is omitted");
        shower.EndTimeMs = 1002;
        Check(BananaShowerPattern.Segments(shower, new(map)).Count == 1, "Exactly 2ms is allowed");
        shower.EndTimeMs = 1510;
        var old = map.DeepClone();
        shower.Pattern = new(2, 3, 0, 7);
        Check(!old.ContentEquals(map) && old.BananaShowers[0].Pattern == new BananaShowerPattern(), "Detached pattern snapshots");
        shower.Pattern = new();
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 1010, BeatLengthMs = 250 }]);
        segments = BananaShowerPattern.Segments(shower, new(map));
        Check(Math.Abs(segments[0].EndTimeMs - 1020.625) < .0001 && Math.Abs(segments[1].TimeMs - 1067.5) < .0001, "Beat fractions cross red lines");
        map.TimingPoints.Clear();
        map.BananaShowers.Insert(0, new() { TimeMs = 100, EndTimeMs = 650 });
        map.BananaShowers.Add(new() { TimeMs = 1180, EndTimeMs = 1400, Pattern = new(1, 8, 1, 16) });
        map.ImportedSliders.Add(new() { TimeMs = 1150, X = 128, Y = 192, PathType = 'L', PixelLength = 100, ControlPoints = { new(128, 192), new(228, 192) } });
        map.Fruits.Add(new() { TimeMs = 1100, X = 256 });
        var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 2000, X = 256 }, new() { TimeMs = 3000, X = 256 }]); map.Tracks.Add(track);
        var cache = new OsuWriteCache();
        Verify();
        foreach (bool hr in new[] { false, true })
        {
            map.DerandomizeFSliderDroplets = true; map.DerandomizeDropletsForHardRock = hr;
            Verify();
            shower.EndTimeMs = 1780; Verify();
            shower.TimeMs = 999.7; Verify();
            map.TimingPoints.Add(new() { TimeMs = 1200, BeatLengthMs = 333.33 }); Verify();
            shower.Pattern = new(2, 7, 1, 3); Verify();
            map.LegacySliderTicks = true; Verify(); map.LegacySliderTicks = false;
        }
        var saved = ProjectSerializer.Serialize(map);
        Check(saved.Contains("\"SchemaVersion\": 21") && ProjectSerializer.Read(saved).ContentEquals(map), "Single project round trip");
        var project = BeatmapProject.FromDocuments([map]);
        var multi = ProjectSerializer.Serialize(project);
        Check(multi.Contains("\"SchemaVersion\": 22") && ProjectSerializer.ReadProject(multi).Difficulties[0].Document.ContentEquals(map), "Multi project round trip");
        var history = new EditorHistory(map); history.Begin("pattern"); history.Document.BananaShowers[1].Pattern = new(3, 8, 0, 16); history.Commit();
        history.Undo(); Check(history.Document.ContentEquals(map), "Undo pattern"); history.Redo();
        Check(history.Document.BananaShowers[1].Pattern == new BananaShowerPattern(3, 8, 0, 16), "Redo pattern");
        map.DerandomizeDropletsForHardRock = false;
        var output = OsuBeatmapWriter.Serialize(map);
        SliderMultiplierEditing.Rebase(output, 1.8);
        Check(SliderMultiplierEditing.TryRebase(output, 1.8, out var rebased, double.PositiveInfinity), "SV rebasing grouped identities");
        Check(rebased!.PlayableEndTimes.ContainsKey(shower.Id), "SV group end identity");
        var split = map.DeepClone();
        var childIds = ObjectStructureEditing.BreakBananaShowers(split, [shower.Id]);
        Check(childIds.Length > 1 && !split.BananaShowers.Any(s => s.Id == shower.Id)
            && split.BananaShowers.Where(s => childIds.Contains(s.Id)).All(s => s.Pattern is null), "Split creates independent short showers");
        var splitOutput = OsuBeatmapWriter.Serialize(split);
        Check(splitOutput.Text == output.Text && splitOutput.PlayableObjects.Select(o => (o.TimeMs, o.X)).SequenceEqual(output.PlayableObjects.Select(o => (o.TimeMs, o.X))), "Split preserves actual playback and exported lines");
        var invalid = new BananaShowerPattern(0, 16, 3, 16);
        try { invalid.Validate(); throw new Exception("Accepted zero length"); } catch (InvalidDataException) { }

        void Verify()
        {
            var snapshot = map.DeepClone();
            var source = cache.Convert(map); var fullSource = CatchStreamConverter.Convert(map);
            Check(source.Success && source.Objects.SequenceEqual(fullSource.Objects), "Cached source equivalence: " + string.Join("; ", source.Diagnostics));
            var cached = OsuBeatmapWriter.Serialize(map, cache: cache); var full = OsuBeatmapWriter.Serialize(map);
            Check(cached.ObjectSequenceMatches && cached.Text == full.Text && cached.PlayableObjects.SequenceEqual(full.PlayableObjects)
                && cached.PlayableHardRockObjects.SequenceEqual(full.PlayableHardRockObjects), "Cached export NM/HR equivalence");
            var physical = OsuBeatmapReader.Read(cached.Text); var physicalConversion = CatchStreamConverter.Convert(physical);
            Check(cached.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(physicalConversion.Objects.Select(o => (o.Kind, o.TimeMs, o.X))), "Independent exported NM playback");
            Check(cached.PlayableHardRockObjects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(CatchPreviewMods.HardRock(physical, physicalConversion).Select(o => (o.Kind, o.TimeMs, o.X))), "Independent exported HR playback");
            Check(source.Objects.Where(o => o.Kind == CatchObjectKind.Banana).SequenceEqual(cached.PlayableObjects.Where(o => o.Kind == CatchObjectKind.Banana)), "Authored banana times and RNG match playable export");
            Check(CatchPreviewMods.HardRock(map, source).Where(o => o.Kind == CatchObjectKind.Banana)
                .Select(o => (o.SourceId, o.EventIndex, o.TimeMs, o.X)).SequenceEqual(cached.PlayableHardRockObjects.Where(o => o.Kind == CatchObjectKind.Banana).Select(o => (o.SourceId, o.EventIndex, o.TimeMs, o.X))), "Grouped source HR playback");
            Check(cached.PlayableObjects.Select(o => (o.SourceId, o.EventIndex)).Distinct().Count() == cached.PlayableObjects.Count, "Unique grouped events");
            Check(map.ContentEquals(snapshot), "Conversion leaves content untouched");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
