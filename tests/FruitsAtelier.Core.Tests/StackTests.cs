using FruitsAtelier.Core;

internal static class StackTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000, BeatLengthMs = 500 };
        var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4,
            Stack = new StackEnvelope { Points = [new(0, 0), new(.25, 40), new(.75, 40), new(1, 0)] } };
        track.Nodes.AddRange([new Anchor { TimeMs = 1000, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
        map.Tracks.Add(track);
        var fruits = SliderFruitStream.Convert(map, track);
        Check(fruits.Count == 9 && fruits[0].X == 256 && fruits[^1].X == 256, "closed endpoints");
        Check(fruits[2].X == 216 && fruits[3].X == 296 && fruits[4].X == 216, "alternating envelope plateau");
        Check(track.Stack.DistanceAt(.125) == 20, "smooth envelope midpoint");
        track.Stack.StartLeft = false;
        Check(SliderFruitStream.Convert(map, track)[2].X == 296, "first side flips");
        track.Stack.SetAdjustment(.25, -20);
        var adjusted = SliderFruitStream.Convert(map, track);
        var unadjusted = track.DeepClone(); unadjusted.Stack!.FruitAdjustments.Clear();
        Check(adjusted[2].X == 276 && adjusted.Where((f, i) => i != 2).Select(f => f.X)
            .SequenceEqual(SliderFruitStream.Convert(map, unadjusted).Where((f, i) => i != 2).Select(f => f.X)), "one fruit moves without changing neighbours");
        track.StreamSnapDivisor = 8;
        Check(SliderFruitStream.Convert(map, track)[4].X == 276 && track.Stack.AdjustmentAt(.3125) == 0, "snap changes retain adjustments only at matching progress");
        track.StreamSnapDivisor = 4;
        Check(map.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(map))), "single persistence");
        Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(BeatmapProject.FromDocuments([map]))).Difficulties[0].Document.ContentEquals(map), "multi persistence");
        var clone = map.DeepClone(); clone.Tracks[0].Stack!.Points[1] = new(.25, 80);
        Check(track.Stack.Points[1].Distance == 40 && !clone.ContentEquals(map), "independent clone and equality");
        var cache = new CatchConversionCache(); CatchStreamConverter.Convert(map, cache: cache);
        track.Stack.Points[1] = new(.25, 60);
        Check(CatchStreamConverter.Convert(map, cache: cache).Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects), "cache invalidation");
        var output = OsuBeatmapWriter.Serialize(map);
        Check(output.ObjectSequenceMatches && output.ReadBack.Fruits.Count == 9 && output.ReadBack.ImportedSliders.Count == 0, "circle export");
        track.Stack.FruitAdjustments.Clear();
        track.Stack.Points = [new(0, 512), new(1, 512)];
        var clamped = SliderFruitStream.Convert(map, track);
        Check(clamped[0].X == 512 && clamped[1].X == 0, "playfield clamp");
        track.Stack.Points[1] = new(0, 20);
        Check(!CatchStreamConverter.Convert(map).Success, "invalid envelope rejected");
        bool rejected = false;
        try { ProjectSerializer.Serialize(map); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "invalid envelope cannot persist");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
