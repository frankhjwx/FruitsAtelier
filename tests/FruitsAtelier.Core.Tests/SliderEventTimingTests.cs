using FruitsAtelier.Core;

internal static class SliderEventTimingTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000 };
        var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 120 }, new() { TimeMs = 3000, X = 360 }]);
        map.Tracks.Add(track);
        var cache = new CatchConversionCache();
        var baseline = CatchStreamConverter.Convert(map, cache: cache);
        var target = baseline.Objects.Single(o => o.Kind == CatchObjectKind.Droplet && o.TimeMs == 1500);
        foreach (double x in new[] { 200.10650634765625, 220d, 240d })
        {
            var current = CatchStreamConverter.Convert(map, cache: cache).Objects.Single(o => o.EventIndex == target.EventIndex);
            DistanceSpacingEditing.ApplyIsolatedX(map, current, x, true, cache);
            var after = CatchStreamConverter.Convert(map, cache: cache);
            Check(after.Success && after.Objects.Count == baseline.Objects.Count, "Isolated edit changed the event sequence.");
            foreach (var old in baseline.Objects)
            {
                var moved = after.Objects.Single(o => o.EventIndex == old.EventIndex);
                Check(Math.Abs(old.TimeMs - moved.TimeMs) < .001 && old.Kind == moved.Kind,
                    "Automatic SV changed a rounded event time.");
                Check(Math.Abs(moved.X - (old.EventIndex == target.EventIndex ? x : old.X)) < .001,
                    "Isolated edit moved an unrelated event.");
            }
            Check(track.MinimumSliderVelocityMultiplier is > 1 && track.SliderVelocityMultiplier is null,
                "Timing stabilization must keep automatic SV enabled.");
            var text = ProjectSerializer.Serialize(map);
            Check(text.Contains("\"SchemaVersion\": 45"), "Automatic SV floors need their own schema.");
            var restored = ProjectSerializer.Read(text);
            Check(map.ContentEquals(restored), "Automatic SV floor was lost on reload.");
            Check(after.Objects.SequenceEqual(CatchStreamConverter.Convert(restored).Objects), "Reload changed events.");
            Check(OsuBeatmapWriter.Serialize(map).Text == OsuBeatmapWriter.Serialize(restored).Text, "Reload changed export.");
        }
        var project = new BeatmapProject();
        project.Difficulties.Add(new ProjectDifficulty { Name = "Timing", Document = map });
        string multi = ProjectSerializer.Serialize(project);
        Check(multi.Contains("\"SchemaVersion\": 46")
            && ProjectSerializer.ReadProject(multi).Difficulties.Single().Document.ContentEquals(map), "Multi-project lost the floor.");
        var legacy = map.DeepClone();
        Check(SliderRepresentationEditing.ConvertToLegacy(legacy, track.Id).Success, "Edited track did not convert to Legacy.");
        string legacyText = ProjectSerializer.Serialize(legacy);
        Check(legacyText.Contains("\"SchemaVersion\": 45")
            && ProjectSerializer.Read(legacyText).ContentEquals(legacy), "Legacy authoring baseline lost its floor schema.");
        var explicitSv = map.DeepClone();
        explicitSv.Tracks[0].Nodes.Clear();
        explicitSv.Tracks[0].Nodes.AddRange([new() { TimeMs = 1000, X = 120 }, new() { TimeMs = 3000, X = 360 }]);
        explicitSv.Tracks[0].MinimumSliderVelocityMultiplier = 10;
        explicitSv.Tracks[0].SliderVelocityMultiplier = 1;
        Check(CatchStreamConverter.Convert(explicitSv).Sliders.Single().SliderVelocityMultiplier == 1,
            "Automatic floor overrode an explicit SV.");
        foreach (double invalid in new[] { .01, 11d, double.NaN })
        {
            var bad = map.DeepClone(); bad.Tracks[0].MinimumSliderVelocityMultiplier = invalid;
            bool rejected = false;
            try { OsuBeatmapReader.Validate(bad); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid automatic SV floor was accepted.");
            Check(!CatchStreamConverter.Convert(bad).Success, "Conversion accepted an invalid floor.");
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
