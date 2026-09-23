using FruitsAtelier.Core;

internal static class SliderStreamTests
{
    public static void ConversionAndPersistence()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500 };
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2, StreamSnapDivisor = 4,
            OriginalLine = "100,192,1000,6,2,L|400:192,2,300,0|0|0,0:0|0:0|0:0,2:3:4:65:" };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 100 }, new Anchor { TimeMs = 1500, X = 400 }]);
        map.Tracks.Add(track);
        map.Fruits.Add(new Fruit { TimeMs = 1375, X = 256 });
        map.BananaShowers.Add(new BananaShower { TimeMs = 2500, EndTimeMs = 3000 });
        var cache = new CatchConversionCache();
        var converted = CatchStreamConverter.Convert(map, cache: cache);
        Check(converted.Success && converted.Sliders.Count == 0, "stream does not generate a slider path");
        var fruits = converted.Objects.Where(o => o.SourceId == track.Id).ToArray();
        Check(fruits.Length == 9 && fruits.All(o => o.IsStandalone && o.Kind == CatchObjectKind.Fruit), "snap emits independent fruits over repeats");
        Check(fruits[4].X == 400 && fruits[8].X == 100, "repeat sampling reverses the curve");
        var saved = ProjectSerializer.Serialize(map);
        Check(map.ContentEquals(ProjectSerializer.Read(saved)), "single document persistence");
        Check(ProjectSerializer.ReadProject(saved).Difficulties[0].Document.ContentEquals(map), "single document opens as a project");
        var project = BeatmapProject.FromDocuments([map]);
        Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Difficulties[0].Document.ContentEquals(map), "multi difficulty persistence");
        var output = OsuBeatmapWriter.Serialize(map);
        Check(output.ObjectSequenceMatches && output.ReadBack.ImportedSliders.Count == 0 && output.ReadBack.Fruits.Count == 10, "export emits circles and preserves interleaved order");
        Check(output.PlayableEndTimes[track.Id] == output.PlayableObjects.Where(o => o.SourceId == track.Id).Max(o => o.TimeMs),
            "stream end follows the last emitted osu fruit");
        var readObjects = CatchStreamConverter.Convert(output.ReadBack);
        var hr = CatchPreviewMods.HardRock(map, converted);
        var readHr = CatchPreviewMods.HardRock(output.ReadBack, readObjects);
        Check(hr.Select(o => (o.TimeMs, o.X, o.Kind)).SequenceEqual(readHr.Select(o => (o.TimeMs, o.X, o.Kind))), "HR matches exported fruits and downstream RNG");
        var beforeSounds = new HitsoundResolver(map, converted.Objects);
        var afterSounds = new HitsoundResolver(output.ReadBack, readObjects.Objects);
        Check(converted.Objects.Zip(readObjects.Objects).All(p => beforeSounds.Resolve(p.First).SequenceEqual(afterSounds.Resolve(p.Second))), "stream sample banks and volume match export");
        var baseline = map.DeepClone();
        track.StreamSnapDivisor = 8;
        Check(!map.ContentEquals(baseline), "snap changes participate in dirty tracking");
        Check(CatchStreamConverter.Convert(map, cache: cache).Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects), "cache invalidates on snap change");
        track.StreamSnapDivisor = 0;
        Check(!CatchStreamConverter.Convert(map).Success, "invalid snap is rejected");
        bool rejected = false;
        try { ProjectSerializer.Serialize(map); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "invalid snap cannot be saved");
        track.StreamSnapDivisor = 3; track.SpanCount = 1; track.Nodes[^1].TimeMs = 1420;
        var offGrid = CatchStreamConverter.Convert(map).Objects.Where(o => o.SourceId == track.Id).ToArray();
        Check(offGrid.Length == 3 && offGrid[^1].TimeMs < 1420, "off-grid tails do not introduce unsnapped fruits");
        track.StreamSnapDivisor = 4; track.SourceOrder = 5; track.Nodes[^1].TimeMs = 1500;
        map.Fruits[0].SourceOrder = 0;
        Check(OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches, "same-time independent fruit order matches export");
    }

    public static void ExportedMillisecondsDriveHyperdash()
    {
        var map = new MapDocument { CircleSize = 4, DurationMs = 12000 };
        var first = new Fruit { TimeMs = 11013.936, X = 459.766 };
        var second = new Fruit { TimeMs = 11099.251, X = 317.958, SourceOrder = 1 };
        map.Fruits.AddRange([first, second]);
        var precise = CatchStreamConverter.Convert(map);
        var exported = OsuBeatmapWriter.Serialize(map);
        Check(exported.ObjectSequenceMatches, "export preserves gameplay object identity");
        Check(exported.PlayableObjects.Select(o => o.TimeMs).SequenceEqual([11014d, 11099d]), "playable times match osu file milliseconds");
        Check(exported.PlayableObjects.Select(o => o.X).SequenceEqual([460d, 318d]), "playable coordinates match osu file integers");
        Check(!HyperDashCalculator.Calculate(precise.Objects, map.CircleSize)[0].IsHyperDash,
            "precise editing coordinates stay below this hyperdash threshold");
        Check(HyperDashCalculator.Calculate(exported.PlayableObjects, map.CircleSize)[0].IsHyperDash,
            "the exported osu event crosses the hyperdash threshold");
        Check(exported.PlayableObjects[0].SourceId == first.Id && exported.PlayableObjects[1].SourceId == second.Id,
            "playable events retain editor identities");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
