using FruitsAtelier.Core;

internal static class SliderImportPolicyTests
{
    public static void Run()
    {
        SliderImportDetectionTests.Run();
        foreach (bool derandomize in new[] { false, true })
        {
            var map = Native();
            var original = OsuBeatmapWriter.Serialize(map);
            map.SliderImportState = SliderImportState.Pending;
            map.DerandomizeFSliderDroplets = derandomize;
            Check(!SliderImportEditing.LooksCompensated(map), "Ordinary line detected as compensation.");
            var initialized = SliderImportEditing.Initialize(map, false);
            Check(initialized.Success, string.Join("; ", initialized.Diagnostics));
            var imported = OsuBeatmapWriter.Serialize(map);
            if (!derandomize) Equivalent(original.PlayableObjects, imported.PlayableObjects, 0);
            else Check(imported.PlayableObjects.Where(o => o.Kind == CatchObjectKind.TinyDroplet)
                .All(o => Math.Abs(o.X - 256) <= 1), "Native derandomized import did not follow the source path.");
            var before = map.DeepClone();
            var result = SliderImportEditing.ConvertFollowingMap(map, map.ImportedSliders.Select(s => s.Id).ToArray());
            Check(result.Tracks.Count == 1 && result.Failures.Count == 0, "Native conversion failed.");
            Check(map.Tracks[0].RandomizesDroplets(map) == !derandomize, "Converted mode disagrees with the map.");
            Equivalent(imported.PlayableObjects, OsuBeatmapWriter.Serialize(map).PlayableObjects, 1);
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(before)).ContentEquals(before), "Import state/baseline lost in persistence.");
        }

        foreach (int spans in new[] { 1, 2, 3 })
        foreach (bool curved in new[] { false, true })
        {
            var authored = new MapDocument { DurationMs = 12000, DerandomizeFSliderDroplets = true };
            var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = spans, DropletMode = SliderDropletMode.Derandomize };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = curved ? 340 : 200 },
                new() { TimeMs = 2000, X = 300 }]);
            authored.Tracks.Add(track);
            var map = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(authored).Text);
            var imported = OsuBeatmapWriter.Serialize(map);
            Check(SliderImportEditing.LooksCompensated(map), "Generated compensation was not detected.");
            map.SliderImportState = SliderImportState.Pending;
            map.DerandomizeFSliderDroplets = false;
            var initialized = SliderImportEditing.Initialize(map, true);
            Check(initialized.Success, $"Compensated import {spans}/{curved}: {string.Join("; ", initialized.Diagnostics)}");
            Check(map.DerandomizeFSliderDroplets && map.SliderImportState == SliderImportState.Compensated,
                "Confirmed source did not override the randomized import default.");
            Equivalent(imported.PlayableObjects, OsuBeatmapWriter.Serialize(map).PlayableObjects, 0);
            var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(map));
            Check(saved.ContentEquals(map), "Confirmed source lost its baseline on reload.");
            var refreshed = SliderRepresentationEditing.RefreshLegacyDroplets(map);
            Check(refreshed.Success, string.Join("; ", refreshed.Diagnostics));
            Equivalent(imported.PlayableObjects, OsuBeatmapWriter.Serialize(map).PlayableObjects, 1);
            var once = OsuBeatmapWriter.Serialize(map);
            Check(SliderRepresentationEditing.RefreshLegacyDroplets(map).Success, "Repeated refresh failed.");
            Equivalent(once.PlayableObjects, OsuBeatmapWriter.Serialize(map).PlayableObjects, .001);
            var history = new EditorHistory(map);
            history.Begin("Convert");
            var converted = SliderImportEditing.ConvertFollowingMap(history.Document, [map.ImportedSliders[0].Id]);
            Check(converted.Failures.Count == 0 && converted.Tracks.Count == 1, "Compensated conversion failed.");
            history.Commit();
            Equivalent(imported.PlayableObjects, OsuBeatmapWriter.Serialize(history.Document).PlayableObjects, 1);
            history.Undo(); Check(history.Document.ContentEquals(map), "Conversion undo lost the source baseline.");
            history.Redo();
            history.Document.Tracks[0].DropletMode = SliderDropletMode.Randomize;
            var randomized = OsuBeatmapWriter.Serialize(history.Document);
            Check(randomized.PlayableObjects.Zip(imported.PlayableObjects).Any(p => Math.Abs(p.First.X - p.Second.X) > 2),
                "Randomize did not add offsets around the recovered baseline.");
            history.Document.Tracks[0].DropletMode = SliderDropletMode.Derandomize;
            Equivalent(imported.PlayableObjects, OsuBeatmapWriter.Serialize(history.Document).PlayableObjects, 1);
            history.Document.DerandomizeFSliderDroplets = false;
            var legacy = SliderRepresentationEditing.ConvertToLegacy(history.Document, history.Document.Tracks.Single().Id);
            Check(legacy.Success, string.Join("; ", legacy.Diagnostics));
            Check(SliderRepresentationEditing.RefreshLegacyDroplets(history.Document).Success,
                "Explicit slider mode failed after converting back to Legacy.");
            Check(history.Document.ImportedSliders.Single().RandomizationTrack!.DropletMode == SliderDropletMode.Derandomize,
                "Legacy refresh replaced the slider's explicit mode with the map mode.");
            Equivalent(imported.PlayableObjects, OsuBeatmapWriter.Serialize(history.Document).PlayableObjects, 1);
            Console.WriteLine($"PASS compensated import, refresh and modes: spans={spans}, curved={curved}");
        }
    }

    internal static MapDocument Native() => OsuBeatmapReader.Read(
        "osu file format v14\n[General]\nMode:0\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n"
        + "[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n256,64,1000,2,0,L|256:344,1,280\n");

    private static void Equivalent(IReadOnlyList<ConvertedCatchObject> a, IReadOnlyList<ConvertedCatchObject> b, double tolerance)
        => Check(a.Count == b.Count && a.Zip(b).All(p => p.First.Kind == p.Second.Kind
            && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= 1e-6 && Math.Abs(p.First.X - p.Second.X) <= tolerance),
            $"Playback changed (tolerance {tolerance}; max X error {(a.Count == b.Count ? a.Zip(b).Max(p => Math.Abs(p.First.X - p.Second.X)) : -1)}).");
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
