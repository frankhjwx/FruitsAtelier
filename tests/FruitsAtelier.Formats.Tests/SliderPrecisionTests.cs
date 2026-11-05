using System.Globalization;
using FruitsAtelier.Core;

internal static class SliderPrecisionTests
{
    public static void Run()
    {
        foreach (var (length, saved) in new[] { (129.68676013495843, "129.686760134958"),
            (209.99999999999994, "210"), (78.75000300407498, "78.750003004075"),
            (83.99999743652351, "83.9999974365235") })
        foreach (int spans in new[] { 1, 2, 3 })
        foreach (bool native in new[] { false, true })
        {
            var map = Map(length, spans);
            var slider = map.ImportedSliders.Single();
            if (native) slider.PreserveNativeGeometry = true;
            else slider.OriginalLine = null;
            var before = map.DeepClone();
            var cache = new OsuWriteCache();
            var output = OsuBeatmapWriter.Serialize(map, cache: cache);
            Check(Length(output) == saved, "New and canvas sliders save stable length precision");
            Check(output.Text == OsuBeatmapWriter.Serialize(map, cache: cache).Text
                && output.Text == OsuBeatmapWriter.Serialize(map).Text, "Cached and cold publication agree");
            Check(map.ContentEquals(before), "Publication retains authored geometry, length and source text");
            Repeat(output);
        }

        var untouched = Map(209.99999999999994, 2);
        Check(Length(OsuBeatmapWriter.Serialize(untouched)) == "209.99999999999994",
            "Unedited imported source text retains its original length");

        var boundary = Map(83.99999743652351, 2);
        boundary.ImportedSliders[0].OriginalLine = null;
        var boundaryOutput = OsuBeatmapWriter.Serialize(boundary);
        var fruits = boundaryOutput.PlayableObjects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
        Check(fruits.Length == 3 && (int)fruits[1].TimeMs == 1314 && (int)fruits[2].TimeMs == 2314,
            "Repeat and tail calculations use the persisted length at integer boundaries");
        Check(boundaryOutput.PlayableObjects.Any(o => o.Kind == CatchObjectKind.TinyDroplet && o.TimeMs == 439)
            && !boundaryOutput.PlayableObjects.Any(o => o.TimeMs == 440),
            "Tiny intervals use the saved repeat boundary instead of the authored 440 ms result");
        Check(ImportedSliderConverter.EndTimeMs(boundaryOutput.ReadBack, boundaryOutput.ReadBack.ImportedSliders[0])
            == fruits[^1].TimeMs, "Read-back duration and playable tail agree");

        foreach (int spans in new[] { 1, 2, 3 })
        {
            var map = Map(83.99999743652351, spans);
            map.ImportedSliders.Clear();
            var track = new CurveTrack { SpanCount = spans, SliderVelocityMultiplier = .6, CompensateTinyDroplets = false };
            track.Nodes.AddRange([new() { TimeMs = 315, X = 100 }, new() { TimeMs = 1315, X = 100 }]);
            map.Tracks.Add(track);
            var before = map.DeepClone();
            var output = OsuBeatmapWriter.Serialize(map, false);
            double length = CatchStreamConverter.Convert(map, false).Sliders.Single().Length;
            Check(Length(output) == length.ToString("G15", CultureInfo.InvariantCulture), "Generated FSliders save stable length precision");
            Check(map.ContentEquals(before), "Generated publication preserves anchors and calculation precision");
            Repeat(output);
        }
    }

    private static MapDocument Map(double length, int spans) => OsuBeatmapReader.Read(
        "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n"
        + "[TimingPoints]\n0,1000,4,2,1,50,1,0\n0,-166.666666666667,4,2,1,50,0,0\n[HitObjects]\n"
        + $"100,192,315,2,0,L|400:192,{spans},{length.ToString("R", CultureInfo.InvariantCulture)},0|0,0:0|0:0,0:0:0:0:\n");

    private static string Length(OsuWriteResult output) => output.ReadBack.ImportedSliders.Single().OriginalLine!.Split(',')[7];

    private static void Repeat(OsuWriteResult output)
    {
        var expected = Events(output);
        var hardRock = output.PlayableHardRockObjects.Select(o => (o.Kind, o.TimeMs, o.X)).ToArray();
        string length = Length(output);
        for (int round = 0; round < 10; round++)
        {
            var read = OsuBeatmapReader.Read(output.Text);
            var converted = CatchStreamConverter.Convert(read);
            Check(expected.SequenceEqual(converted.Objects.Select(o => (o.Kind, o.TimeMs, o.X))),
                "Playable output matches independently parsed tails, repeats, ticks and tiny droplets");
            read.ImportedSliders[0].PreserveNativeGeometry = true;
            output = OsuBeatmapWriter.Serialize(read);
            Check(Length(output) == length && expected.SequenceEqual(Events(output)),
                "Repeated rebuilt publication preserves length and every playable event");
            Check(hardRock.SequenceEqual(output.PlayableHardRockObjects.Select(o => (o.Kind, o.TimeMs, o.X))),
                "Repeated publication retains HR droplets and random sequence");
        }
    }

    private static (CatchObjectKind, double, double)[] Events(OsuWriteResult output)
        => output.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).ToArray();
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
