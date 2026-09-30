using FruitsAtelier.Core;

internal static class BananaQuantizationTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 100_000, BeatLengthMs = 500 };
        var shower = new BananaShower { TimeMs = 51443.03448275864, EndTimeMs = 54649.931034482775 };
        map.BananaShowers.Add(shower);
        var track = new CurveTrack { CompensateTinyDroplets = true };
        track.Nodes.Add(new Anchor { TimeMs = 56000, X = 256 });
        track.Nodes.Add(new Anchor { TimeMs = 57000, X = 256 });
        map.Tracks.Add(track);
        var cache = new OsuWriteCache();
        Check(65);

        var saved = map.DeepClone();
        shower.TimeMs = 51443.5; shower.EndTimeMs = 54650.5;
        Check(65);
        map = saved.DeepClone(); shower = map.BananaShowers.Single();
        Check(65);

        // Untouched fractional source fields are retained verbatim by export.
        shower.OriginalLine = "256,192,51443.03448275864,8,0,54649.931034482775,0:0:0:0:";
        Check(64);
        shower.EndTimeMs = 54650.5;
        Check(65);
        shower.EndTimeMs = 54649.931034482775;
        shower.TimeMs = 51443.5;
        Check(64);

        void Check(int expectedCount)
        {
            var before = map.DeepClone();
            var source = cache.Convert(map);
            var output = OsuBeatmapWriter.Serialize(map, cache: cache);
            var full = OsuBeatmapWriter.Serialize(map);
            var readBack = CatchStreamConverter.Convert(output.ReadBack);
            if (!output.ObjectSequenceMatches || !full.ObjectSequenceMatches
                || output.Text != full.Text || !output.PlayableObjects.SequenceEqual(full.PlayableObjects))
                throw new Exception("Banana quantization changed exported event identities or cached output.");
            var bananas = source.Objects.Where(o => o.SourceId == shower.Id).ToArray();
            var exported = readBack.Objects.Where(o => o.SourceId == shower.Id).ToArray();
            if (bananas.Length != expectedCount || !bananas.SequenceEqual(exported))
                throw new Exception("Banana counts, times or RNG positions differ from export.");
            var originalTiny = source.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).ToArray();
            var exportedTiny = readBack.Objects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).ToArray();
            if (originalTiny.Length == 0 || originalTiny.Length != exportedTiny.Length
                || originalTiny.Zip(exportedTiny).Any(p => p.First.RandomOffset != p.Second.RandomOffset)
                || output.MaxConvertedXError > 1 || exportedTiny.Any(o => Math.Abs(o.X - 256) > 1))
                throw new Exception("Export shifted downstream RNG or tiny compensation.");
            if (!map.ContentEquals(before)) throw new Exception("Conversion changed authored banana endpoints.");
        }
    }
}
