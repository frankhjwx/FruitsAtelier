using FruitsAtelier.Core;

internal static class SliderReimportTests
{
    public static void Run()
    {
        foreach (bool refresh in new[] { false, true })
        foreach (int spans in new[] { 1, 2 })
        foreach (var (startX, endX) in new[] { (64d, 448d), (448d, 64d), (256d, 256d) })
        {
            var map = new MapDocument { DurationMs = 10000, DerandomizeFSliderDroplets = true };
            var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = spans, DropletMode = SliderDropletMode.Derandomize };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = startX }, new() { TimeMs = 2000, X = endX }]);
            map.Tracks.Add(track);
            // Only the exported file crosses to the other installation, with no FA baseline.
            var imported = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(map).Text);
            imported.DerandomizeFSliderDroplets = true;
            imported.DerandomizeDroplets = true;
            Check(imported.ImportedSliders.All(s => s.RandomizationTrack is null && s.RandomizationSource is null),
                "Reimport fixture retained FA authoring data.");
            var before = OsuBeatmapWriter.Serialize(imported);
            if (refresh)
            {
                var refreshed = SliderRepresentationEditing.RefreshLegacyDroplets(imported);
                Check(refreshed.Success, string.Join("; ", refreshed.Diagnostics));
            }
            var history = new EditorHistory(imported);
            history.Begin("Convert");
            var result = ImportedSliderEditing.ConvertAll(history.Document);
            Check(result.Failures.Count == 0 && result.Tracks.Count == 1, "Reimport conversion failed.");
            var converted = result.Tracks.Single();
            Check(converted.Nodes.Count == 2 && converted.Nodes[0].OutgoingKind == CurveKind.Linear,
                $"Reimported straight playback kept compensation bends ({startX}/{endX}/{spans}).");
            Check(!converted.RandomizesDroplets(history.Document), "Reimport conversion enabled randomization.");
            history.Commit();
            var after = OsuBeatmapWriter.Serialize(history.Document);
            Check(before.PlayableObjects.Count == after.PlayableObjects.Count && before.PlayableObjects.Zip(after.PlayableObjects).All(p =>
                p.First.Kind == p.Second.Kind && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= 1e-6
                && Math.Abs(p.First.X - p.Second.X) <= 1), "Conversion shifted exported playback.");
            var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
            Check(saved.ContentEquals(history.Document) && saved.DerandomizeFSliderDroplets,
                "Project persistence lost the imported difficulty mode or fitted path.");
            history.Undo(); Check(history.Document.ContentEquals(imported), "Conversion undo lost the Legacy source or mode.");
            history.Redo(); Check(history.Document.ContentEquals(saved), "Conversion redo lost the fitted curve.");
        }
        var explicitRandom = new MapDocument { DerandomizeFSliderDroplets = false, DerandomizeDroplets = false };
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(explicitRandom));
        Check(!restored.DerandomizeFSliderDroplets && restored.DerandomizeDroplets == false,
            "Opening an existing project replaced its saved Randomize mode.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
