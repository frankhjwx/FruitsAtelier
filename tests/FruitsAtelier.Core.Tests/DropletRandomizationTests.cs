using FruitsAtelier.Core;

internal static class DropletRandomizationTests
{
    public static void Run()
    {
        var map = Fixture();
        var track = map.Tracks[0];
        var baseline = map.DeepClone();
        var original = CatchStreamConverter.Convert(map);
        track.DropletRandomization = new() { Enabled = true };
        var random = CatchStreamConverter.Convert(map);
        Check(random.Success && random.Objects.Count == original.Objects.Count, "randomization keeps event structure");
        Check(random.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).Any(o => Math.Abs(o.X - 256) > 1), "randomized tiny positions");
        Check(random.Objects.Where(o => o.Kind != CatchObjectKind.TinyDroplet).All(o => o.X == 256), "fruits and droplets retain targets");
        Check(random.Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects), "deterministic generation");
        var cache = new CatchConversionCache();
        CatchStreamConverter.Convert(map, cache: cache);
        map.RandomizeDropletSeed = -42;
        var seeded = CatchStreamConverter.Convert(map, cache: cache);
        Check(seeded.Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects)
            && !seeded.Objects.Select(o => o.TargetX).SequenceEqual(random.Objects.Select(o => o.TargetX)), "seed invalidates cache and changes targets");
        var targets = seeded.Objects.Select(o => o.TargetX).ToArray();
        map.BananaShowers.Add(new() { TimeMs = 100, EndTimeMs = 800 });
        var reordered = CatchStreamConverter.Convert(map, cache: cache);
        Check(reordered.Success && !reordered.Objects.Where(o => o.SourceId == track.Id).Select(o => o.TargetX).SequenceEqual(targets)
            && reordered.Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects), "preceding bananas advance the sequence and invalidate cached FX");
        map.BananaShowers.Clear();
        map.RandomizeDropletStrength = 0;
        Check(CatchStreamConverter.Convert(map, cache: cache).Objects.All(o => Math.Abs(o.X - 256) < .001), "zero strength follows base curve");
        map.RandomizeDropletStrength = 10;
        var beforeAdjustment = map.DeepClone();
        var eventBefore = CatchStreamConverter.Convert(map).Objects.First(o => o.Kind == CatchObjectKind.TinyDroplet);
        var history = new EditorHistory(map);
        history.Begin("adjust");
        DistanceSpacingEditing.ApplyIsolatedX(history.Document, eventBefore, eventBefore.X + 3, true);
        history.Commit();
        var adjusted = CatchStreamConverter.Convert(history.Document);
        Check(Math.Abs(adjusted.Objects.Single(o => o.EventIndex == eventBefore.EventIndex).X - eventBefore.X - 3) < .001, "manual target reached");
        Check(history.Document.Tracks[0].Nodes.Count == 2 && history.Document.Tracks[0].Nodes.All(n => n.X == 256), "manual FX edit leaves base curve intact");
        Check(history.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "manual correction stored separately");
        Check(history.Document.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document))), "single persistence");
        Check(history.Document.ContentEquals(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(BeatmapProject.FromDocuments([history.Document]))).Difficulties[0].Document), "multi persistence");
        var clone = history.Document.DeepClone(); clone.Tracks[0].DropletRandomization!.Adjustments.Clear();
        Check(!clone.ContentEquals(history.Document) && history.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "clone independence");
        var write = OsuBeatmapWriter.Serialize(history.Document);
        Check(write.ObjectSequenceMatches && write.PlayableObjects.Count == adjusted.Objects.Count, "export read-back preserves sequence");
        history.Document.Tracks[0].DropletRandomization!.Enabled = false;
        Check(CatchStreamConverter.Convert(history.Document).Objects.All(o => Math.Abs(o.X - 256) < .001), "disabled effect restores base targets");
        history.Document.Tracks[0].DropletRandomization!.Enabled = true;
        Check(CatchStreamConverter.Convert(history.Document).Objects.SequenceEqual(adjusted.Objects), "reenabling restores manual correction");
        history.Undo(); Check(history.Document.ContentEquals(beforeAdjustment), "manual edit undoes in one step");
        history.Redo(); Check(history.Document.Tracks[0].DropletRandomization!.Adjustments.Count == 1, "manual edit redo");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(baseline)).RandomizeDropletStrength == 20
            && !ProjectSerializer.Serialize(baseline).Contains("RandomizeDroplet"), "older schema retains defaults without new fields");
        map = Fixture(); map.Tracks[0].SpanCount = 3; map.Tracks[0].DropletRandomization = new() { Enabled = true };
        Check(CatchStreamConverter.Convert(map).Success && OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches, "repeats use shared geometry and export");
        map.RandomizeDropletStrength = double.NaN;
        bool rejected = false;
        try { ProjectSerializer.Serialize(map); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && !CatchStreamConverter.Convert(map).Success, "invalid strength rejected");
        map.RandomizeDropletStrength = 20;
        cache = new(); CatchStreamConverter.Convert(map, cache: cache);
        map.Tracks[0].DropletRandomization!.Adjustments = null!;
        Check(!CatchStreamConverter.Convert(map, cache: cache).Success, "invalid corrections reject a cached conversion safely");
        DiffWideSequence();
        NativeSequence();
        StrengthRange();
        var defaults = Fixture(); defaults.RandomizeNewSliders = true;
        Check(defaults.ContentEquals(defaults.DeepClone()) && defaults.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(defaults)))
            && ProjectSerializer.ReadProject(ProjectSerializer.Serialize(BeatmapProject.FromDocuments([defaults]))).Difficulties[0].Document.RandomizeNewSliders,
            "new-slider default survives cloning and single/multi project persistence");
        var changedDefault = defaults.DeepClone(); changedDefault.RandomizeNewSliders = false;
        Check(!defaults.ContentEquals(changedDefault) && !ProjectSerializer.Serialize(changedDefault).Contains("RandomizeNewSliders"),
            "default flag participates in content equality and old projects omit it");
    }
    private static void StrengthRange()
    {
        var map = Fixture(); map.RandomizeDropletStrength = 100;
        Check(CatchStreamConverter.Convert(map).Success, "maximum strength is valid");
        string single = ProjectSerializer.Serialize(map);
        string multi = ProjectSerializer.Serialize(BeatmapProject.FromDocuments([map]));
        Check(ProjectSerializer.Read(single.Replace("\"RandomizeDropletStrength\": 100", "\"RandomizeDropletStrength\": 512")).RandomizeDropletStrength == 100,
            "earlier single-difficulty strength migrates to the new maximum");
        Check(ProjectSerializer.ReadProject(multi.Replace("\"RandomizeDropletStrength\": 100", "\"RandomizeDropletStrength\": 512")).Difficulties[0].Document.RandomizeDropletStrength == 100,
            "earlier multi-difficulty strength migrates to the new maximum");
        map.RandomizeDropletStrength = 101;
        bool rejected = false;
        try { ProjectSerializer.Serialize(map); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && !CatchStreamConverter.Convert(map).Success, "new content rejects strength above 100");
    }

    private static void DiffWideSequence()
    {
        var map = Fixture();
        var first = map.Tracks[0]; first.DropletRandomization = new() { Enabled = true };
        var second = first.DeepClone(); second.Id = Guid.NewGuid();
        foreach (var node in second.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs += 4000; }
        map.Tracks.Add(second);
        var cache = new CatchConversionCache();
        var original = CatchStreamConverter.Convert(map, cache: cache);
        double[] Targets(CatchConversionResult result, Guid id) => result.Objects
            .Where(o => o.SourceId == id && o.Kind == CatchObjectKind.TinyDroplet).Select(o => o.TargetX).ToArray();
        var wanted = Targets(original, second.Id);
        Check(!Targets(original, first.Id).SequenceEqual(wanted), "identical sliders continue different portions of the sequence");
        var history = new EditorHistory(map);
        history.Begin("insert fruit"); history.Document.Fruits.Add(new() { TimeMs = 100, X = 256 }); history.Commit();
        var shifted = CatchStreamConverter.Convert(history.Document, cache: cache);
        Check(wanted.SequenceEqual(Targets(shifted, second.Id))
            && shifted.Objects.SequenceEqual(CatchStreamConverter.Convert(history.Document).Objects), "NM standalone fruit preserves downstream random offsets");
        history.Undo(); Check(CatchStreamConverter.Convert(history.Document, cache: cache).Objects.SequenceEqual(original.Objects), "undo restores global sequence");
        history.Redo(); Check(CatchStreamConverter.Convert(history.Document, cache: cache).Objects.SequenceEqual(shifted.Objects), "redo restores shifted sequence");
        first.DropletRandomization.Enabled = false;
        Check(Targets(CatchStreamConverter.Convert(map, cache: cache), second.Id).SequenceEqual(wanted), "disabled sliders still consume native random draws");
        map.Tracks.Remove(first);
        var removed = CatchStreamConverter.Convert(map, cache: cache);
        Check(!Targets(removed, second.Id).SequenceEqual(wanted)
            && removed.Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects), "parent removal shifts native random draws");
        Check(OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches, "global sequence exports through read-back");
    }

    private static void NativeSequence()
    {
        var rng = new CatchLegacyRandom(1337);
        Check(Enumerable.Range(0, 12).Select(_ => rng.NextTinyOffset()).SequenceEqual(new[] { -14, -10, -2, 15, 17, 1, 0, -14, -7, -11, -3, 14 }),
            "legacy seed 1337 offsets retain ranged-draw truncation");
        foreach (int spans in new[] { 1, 3 })
        foreach (double x in new[] { 0d, 256d, 512d })
        foreach (bool curved in new[] { false, true })
        {
            var map = Fixture();
            var track = map.Tracks[0]; track.SpanCount = spans;
            track.Nodes[0].X = x; track.Nodes[1].X = x;
            if (curved)
            {
                track.Kind = CurveKind.Bezier;
                track.Nodes[0].HandleOut = new(100, 500);
                track.Nodes[1].HandleIn = new(-100, -500);
                foreach (var node in track.Nodes) node.TimeMs += .25;
            }
            var preceding = track.DeepClone(); preceding.Id = Guid.NewGuid(); preceding.SpanCount = 1;
            foreach (var node in preceding.Nodes) { node.Id = Guid.NewGuid(); node.TimeMs -= 1000; node.X = 256; }
            map.Tracks.Insert(0, preceding);
            map.BananaShowers.Add(new() { TimeMs = 0, EndTimeMs = 500 });
            map.Fruits.AddRange([new() { TimeMs = 10, X = 256 }, new() { TimeMs = 100, X = 256 }]);
            var nativeMap = map.DeepClone();
            foreach (var item in nativeMap.Tracks) item.CompensateTinyDroplets = false;
            var native = CatchStreamConverter.Convert(nativeMap, compensateTinyDroplets: false);
            track.DropletRandomization = new() { Enabled = true };
            var cache = new CatchConversionCache();
            var actual = CatchStreamConverter.Convert(map, cache: cache);
            Check(native.Success && actual.Success && actual.Objects.Where(o => o.SourceId == track.Id)
                .Select(o => (o.Kind, o.TimeMs, o.X, o.PathX, o.RandomOffset))
                .SequenceEqual(native.Objects.Where(o => o.SourceId == track.Id).Select(o => (o.Kind, o.TimeMs, o.X, o.PathX, o.RandomOffset))),
                "default FX matches native positions, repeats and edge clamping after preceding parents");
            Check(actual.Objects.SequenceEqual(CatchStreamConverter.Convert(map, cache: cache).Objects), "native FX cache restores both sequences");
            var write = OsuBeatmapWriter.Serialize(map);
            var nativeWrite = OsuBeatmapWriter.Serialize(nativeMap, compensateTinyDroplets: false);
            Check(write.ObjectSequenceMatches && write.PlayableObjects.Where(o => o.SourceId == track.Id).Select(o => (o.EventIndex, o.X))
                .SequenceEqual(nativeWrite.PlayableObjects.Where(o => o.SourceId == track.Id).Select(o => (o.EventIndex, o.X))), "native FX survives export read-back");
            Check(write.PlayableHardRockObjects.Where(o => o.SourceId == track.Id).Select(o => (o.EventIndex, o.X))
                .SequenceEqual(nativeWrite.PlayableHardRockObjects.Where(o => o.SourceId == track.Id).Select(o => (o.EventIndex, o.X))), "native FX preserves HR random consumption and positions");
        }
    }

    public static MapDocument Fixture()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, IsDemo = false };
        var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 3000, X = 256 }]);
        map.Tracks.Add(track); return map;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
