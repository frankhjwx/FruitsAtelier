using FruitsAtelier.Core;

internal static class DropletDerandomizationTests
{
    public static void Run()
    {
        var map = DropletRandomizationTests.Fixture();
        var track = map.Tracks.Single();
        track.DropletRandomization = new() { Enabled = true };
        var randomized = CatchStreamConverter.Convert(map);
        map.DerandomizeFSliderDroplets = true;
        var cache = new CatchConversionCache();
        var normal = CatchStreamConverter.Convert(map, cache: cache);
        Check(normal.Success && normal.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).All(o => Math.Abs(o.X - 256) < .001), "map derandomization suspends enabled FX");
        Check(track.DropletRandomization.Enabled, "derandomization retains slider FX state");
        map.DerandomizeFSliderDroplets = false;
        Check(CatchStreamConverter.Convert(map, cache: cache).Objects.SequenceEqual(randomized.Objects), "turning the gate off restores FX and invalidates its cache");
        map.DerandomizeFSliderDroplets = map.DerandomizeDropletsForHardRock = true;
        map.Fruits.AddRange([new Fruit { TimeMs = 0, X = 256 }, new Fruit { TimeMs = 100, X = 256 }, new Fruit { TimeMs = 200, X = 256 }]);
        CheckHr(map, cache);
        var later = new CurveTrack { Kind = CurveKind.Linear, SourceOrder = 3 };
        later.Nodes.AddRange([new Anchor { TimeMs = 4000, X = 256 }, new Anchor { TimeMs = 6000, X = 256 }]);
        map.Tracks.Add(later);
        CheckHr(map, cache);
        var fractional = DropletRandomizationTests.Fixture();
        fractional.DerandomizeFSliderDroplets = fractional.DerandomizeDropletsForHardRock = true;
        fractional.Fruits.AddRange([new Fruit { TimeMs = 0.9, X = 256.1 }, new Fruit { TimeMs = 100.1, X = 256.2 }]);
        CheckHr(fractional, new(), checkUnquantizedPreview: false);
        var stream = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4, SourceOrder = 2 };
        stream.Nodes.AddRange([new Anchor { TimeMs = 300, X = 256 }, new Anchor { TimeMs = 900, X = 256 }]);
        map.Tracks.Add(stream);
        map.BananaShowers.Add(new BananaShower { TimeMs = 400, EndTimeMs = 600, SourceOrder = 1 });
        map.ImportedSliders.AddRange(OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n256,192,700,2,0,L|300:192,1,70\n").ImportedSliders);
        CheckHr(map, cache);
        map.Fruits[1].X = 260;
        CheckHr(map, cache);
        map.Fruits[1].X = 256;
        map.BananaShowers[0].EndTimeMs = 800;
        CheckHr(map, cache);
        Check(map.ContentEquals(map.DeepClone()) && map.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(map))), "single schema preserves gate and HR mode");
        Check(map.ContentEquals(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(BeatmapProject.FromDocuments([map]))).Difficulties[0].Document), "multi schema preserves HR mode");
        var old = DropletRandomizationTests.Fixture();
        Check(!ProjectSerializer.Read(ProjectSerializer.Serialize(old)).DerandomizeFSliderDroplets, "old projects preserve their FX behavior");
        var history = new EditorHistory(map); history.Begin("HR mode"); history.Document.DerandomizeDropletsForHardRock = false; history.Commit();
        history.Undo(); Check(history.Document.DerandomizeDropletsForHardRock, "HR mode is undoable");
        history.Redo(); Check(!history.Document.DerandomizeDropletsForHardRock, "HR mode supports redo");
        track.SpanCount = 2;
        var repeated = CatchStreamConverter.Convert(map, cache: cache);
        Check(repeated.Success && OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches, "HR compensation supports repeated slider export");
    }

    private static void CheckHr(MapDocument map, CatchConversionCache cache, bool checkUnquantizedPreview = true)
    {
        var conversion = CatchStreamConverter.Convert(map, cache: cache);
        Check(conversion.Success, string.Join("; ", conversion.Diagnostics));
        Check(conversion.Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects), "cached HR conversion matches a fresh conversion");
        var hardRock = CatchPreviewMods.HardRock(map, conversion);
        var authored = map.Tracks.Where(t => t.StreamSnapDivisor is null).ToDictionary(t => t.Id);
        foreach (var item in hardRock.Where(o => checkUnquantizedPreview && o.Kind == CatchObjectKind.TinyDroplet && authored.ContainsKey(o.SourceId)))
            Check(Math.Abs(item.X - CurveMath.PositionAtTime(authored[item.SourceId], item.TimeMs)) < .001, "HR tiny droplet lands on its authored path");
        var write = OsuBeatmapWriter.Serialize(map);
        Check(write.ObjectSequenceMatches, "HR compensation preserves normal-mode export read-back");
        foreach (var item in write.PlayableHardRockObjects.Where(o => o.Kind == CatchObjectKind.TinyDroplet && authored.ContainsKey(o.SourceId)))
            Check(Math.Abs(item.X - CurveMath.PositionAtTime(authored[item.SourceId], item.TimeMs)) <= write.MaxConvertedXError + .001,
                $"exported HR tiny droplet at {item.TimeMs}: X={item.X}, target={CurveMath.PositionAtTime(authored[item.SourceId], item.TimeMs)}, path={item.PathX}, offset={item.RandomOffset}");
        Check(write.PlayableHardRockObjects.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet)
            .Select(o => (o.Kind, o.TimeMs)).SequenceEqual(write.PlayableObjects.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet).Select(o => (o.Kind, o.TimeMs))), "HR compensation retains palpable object kinds and times");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
