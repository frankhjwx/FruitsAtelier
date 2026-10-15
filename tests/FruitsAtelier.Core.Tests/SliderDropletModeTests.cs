using FruitsAtelier.Core;

internal static class SliderDropletModeTests
{
    public static void Run()
    {
        foreach (bool legacyTicks in new[] { false, true })
        foreach (bool hr in new[] { false, true })
        foreach (bool derandomize in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 10000, DerandomizeFSliderDroplets = derandomize,
                DerandomizeDropletsForHardRock = hr, LegacySliderTicks = legacyTicks };
            foreach (var mode in Enum.GetValues<SliderDropletMode>())
            {
                int index = map.Tracks.Count;
                var track = new CurveTrack { DropletMode = mode, Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 1000 + index * 2500, X = 256 },
                    new Anchor { TimeMs = 3000 + index * 2500, X = 256 }]);
                map.Tracks.Add(track);
            }
            var cache = new CatchConversionCache();
            Verify();
            map.DerandomizeFSliderDroplets = !derandomize;
            Verify();
            map.RandomizeDropletStrength = 12; map.RandomizeDropletSeed = -123;
            Verify();
            Check(map.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(map))), "Single project preserves all three modes");
            var project = BeatmapProject.FromDocuments([map]); project.Notes = "Droplet modes";
            var restored = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project));
            Check(restored.Notes == project.Notes && map.ContentEquals(restored.Difficulties[0].Document),
                "Multi project preserves modes and project notes");

            var history = new EditorHistory(map);
            history.Begin("mode"); history.Document.Tracks[0].DropletMode = SliderDropletMode.Randomize; history.Commit();
            Check(!history.Document.ContentEquals(map), "Mode participates in content equality");
            history.Undo(); Check(history.Document.ContentEquals(map), "Undo restores mode");
            history.Redo(); Check(history.Document.Tracks[0].DropletMode == SliderDropletMode.Randomize, "Redo restores override");

            void Verify()
            {
                var converted = CatchStreamConverter.Convert(map, cache: cache);
                Check(converted.Success && converted.Objects.SequenceEqual(CatchStreamConverter.Convert(map).Objects),
                    "Cached and uncached mixed-mode output match after default and parameter edits");
                foreach (var track in map.Tracks)
                {
                    bool random = track.DropletMode == SliderDropletMode.Randomize
                        || track.DropletMode == SliderDropletMode.FollowMap && !map.DerandomizeFSliderDroplets;
                    Check(track.RandomizesDroplets(map) == random, "Explicit mode precedes map default");
                    var tinies = converted.Objects.Where(o => o.SourceId == track.Id && o.Kind == CatchObjectKind.TinyDroplet).ToArray();
                    Check(tinies.Length > 0 && (random ? tinies.Any(o => Math.Abs(o.TargetX - 256) > .1)
                        : tinies.All(o => o.TargetX == 256)), "Resolved modes generate their own targets");
                }
                var exported = OsuBeatmapWriter.Serialize(map);
                Check(exported.ObjectSequenceMatches, "Mixed NM/HR compensation and random overrides export in v7 and v14");
                var playable = hr ? exported.PlayableHardRockObjects : exported.PlayableObjects;
                var derandomized = map.Tracks.Where(t => !t.RandomizesDroplets(map)).Select(t => t.Id).ToHashSet();
                Check(playable.Where(o => o.Kind == CatchObjectKind.TinyDroplet && derandomized.Contains(o.SourceId))
                    .All(o => Math.Abs(o.X - 256) <= exported.MaxConvertedXError + .001),
                    "Exported compensated droplets follow their paths in the selected NM/HR target");
            }
        }
        var legacy = DropletRandomizationTests.Fixture();
        legacy.DerandomizeDropletsForHardRock = true;
        var previous = CatchStreamConverter.Convert(legacy).Objects;
        var restoredLegacy = ProjectSerializer.Read(ProjectSerializer.Serialize(legacy));
        restoredLegacy.DerandomizeDropletsForHardRock = false;
        Check(previous.SequenceEqual(CatchStreamConverter.Convert(restoredLegacy).Objects),
            "Older projects with derandomization off retain NM output even if their dormant HR option is on");
        var invalid = new MapDocument();
        var badTrack = new CurveTrack { DropletMode = (SliderDropletMode)99 };
        badTrack.Nodes.AddRange([new Anchor { TimeMs = 0, X = 256 }, new Anchor { TimeMs = 2000, X = 256 }]);
        invalid.Tracks.Add(badTrack);
        Check(!CatchStreamConverter.Convert(invalid).Success, "Conversion rejects undefined modes");
        try { ProjectSerializer.Serialize(invalid); throw new Exception("Undefined mode was saved"); }
        catch (InvalidDataException) { }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
