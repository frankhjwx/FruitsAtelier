using FruitsAtelier.Core;

internal static class FractionalSliderQuantizationTests
{
    public static void Run()
    {
        foreach (var (duration, tinyCount) in new[] { (116.25, 0), (136.25, 0), (236.25, 1), (436.25, 3) })
        foreach (bool compensation in new[] { false, true })
        foreach (bool hardRock in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 2000,
                DerandomizeFSliderDroplets = hardRock, DerandomizeDropletsForHardRock = hardRock };
            var track = new CurveTrack { CompensateTinyDroplets = compensation };
            track.Nodes.AddRange([new() { TimeMs = 1000.9, X = 192 }, new() { TimeMs = 1000.9 + duration, X = 208 }]);
            map.Tracks.Add(track);
            var later = new CurveTrack { CompensateTinyDroplets = true };
            later.Nodes.AddRange([new() { TimeMs = 6000, X = 256 }, new() { TimeMs = 7000, X = 256 }]);
            map.Tracks.Add(later);
            map.Fruits.AddRange([new() { TimeMs = 100, X = 256 }, new() { TimeMs = 200, X = 256 }]);
            map.BananaShowers.Add(new() { TimeMs = 4000, EndTimeMs = 4100 });
            var history = new EditorHistory(map);
            map = history.Document;
            var cache = new OsuWriteCache();
            Check(); Check();
            if (cache.Convert(map).Objects.Count(o => o.SourceId == track.Id && o.Kind == CatchObjectKind.TinyDroplet) != tinyCount)
                throw new Exception($"Fractional slider duration {duration} used the wrong tiny interval threshold.");

            history.Begin("Move fractional slider");
            foreach (var node in map.Tracks[0].Nodes) node.TimeMs += .5;
            history.Commit(); Check();
            history.Begin("Repeat fractional slider");
            map.Tracks[0].SpanCount = 2;
            history.Commit(); Check();
            history.Undo(); map = history.Document; Check();
            history.Undo(); map = history.Document; Check();
            history.Redo(); map = history.Document; Check();

            if (!compensation && !hardRock && tinyCount == 1)
            {
                var selected = cache.Convert(map).Objects.Single(o => o.SourceId == track.Id && o.Kind == CatchObjectKind.TinyDroplet);
                DistanceSpacingEditing.ApplyIsolatedX(map, selected, selected.X + 2, false);
                var moved = cache.Convert(map).Objects.Single(o => o.SourceId == track.Id && o.EventIndex == selected.EventIndex);
                if (Math.Abs(moved.X - selected.X - 2) > .001 || moved.TimeMs != selected.TimeMs)
                    throw new Exception("Fractional tiny editing changed its event identity or missed the requested position.");
                Check();
            }

            void Check()
            {
                var before = map.DeepClone();
                var source = cache.Convert(map);
                var output = OsuBeatmapWriter.Serialize(map, cache: cache);
                var full = OsuBeatmapWriter.Serialize(map);
                if (!source.Success || !output.ObjectSequenceMatches || !full.ObjectSequenceMatches
                    || output.Text != full.Text || !output.PlayableObjects.SequenceEqual(full.PlayableObjects)
                    || !output.PlayableHardRockObjects.SequenceEqual(full.PlayableHardRockObjects))
                    throw new Exception("Fractional slider export changed event identities or cached NM/HR output.");
                if (!map.ContentEquals(before)) throw new Exception("Export changed authored fractional times or geometry.");
                foreach (var (authored, exported) in source.Objects.Zip(output.PlayableObjects))
                {
                    if (authored.SourceId != exported.SourceId || authored.EventIndex != exported.EventIndex
                        || authored.Kind != exported.Kind || authored.RandomOffset != exported.RandomOffset
                        || Math.Abs(authored.TimeMs - exported.TimeMs) >= 1.001)
                        throw new Exception("Fractional slider export changed nested timing, identity or downstream RNG.");
                }
                if (hardRock && output.PlayableHardRockObjects.Where(o => o.SourceId == later.Id
                    && o.Kind == CatchObjectKind.TinyDroplet).Any(o => Math.Abs(o.X - 256) > 1))
                    throw new Exception("Fractional slider RNG changed downstream HR compensation.");
            }
        }
    }
}
