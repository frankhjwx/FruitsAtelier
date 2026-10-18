using FruitsAtelier.Core;
using System.Text.Json.Nodes;

internal static class OsuCanvasModelTests
{
    public static void Run()
    {
        var imported = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:0\n[Difficulty]\nSliderMultiplier:1.92\n[TimingPoints]\n0,500\n[HitObjects]\n100,70,1000,5,2,1:2:3:60:hit.wav\n100,80,2000,2,0,L|200:80,1,192,2|4,1:2|2:3,1:2:3:60:slide.wav");
        Check(imported.Fruits[0].Y == 70, "Standard circle Y was lost");
        var exported = OsuBeatmapWriter.Serialize(imported);
        Check(OsuBeatmapReader.Setting(exported.ReadBack, "General", "Mode") == "2", "Export mode");
        Check(exported.ReadBack.Fruits[0].Y == 70, "Export circle Y");
        imported.Fruits[0].Y = 111;
        imported.OsuDistanceSpacing = 3.2;
        var clone = imported.DeepClone();
        Check(clone.ContentEquals(imported), "Clone loses canvas data");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(imported));
        Check(restored.ContentEquals(imported), "Project loses canvas data");
        Check(OsuBeatmapWriter.Serialize(imported).ReadBack.Fruits[0].Y == 111, "Edited circle Y ignored");
        var old = JsonNode.Parse(ProjectSerializer.Serialize(imported))!;
        old["SchemaVersion"] = 1;
        old["Document"]!["Fruits"]![0]!.AsObject().Remove("Y");
        old["Document"]!.AsObject().Remove("OsuDistanceSpacing");
        Check(ProjectSerializer.Read(old.ToJsonString()).Fruits[0].Y == 70, "Old source Y migration");
        Check(new Fruit().Y == 192, "New fruit Y default");
        var defaultProject = ProjectSerializer.Serialize(new MapDocument());
        Check(!defaultProject.Contains("\"OsuDistanceSpacing\"") && !defaultProject.Contains("\"Y\""), "Default fields leak into older schemas");
        imported.Fruits[0].Y = 192;
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(imported)).Fruits[0].Y == 192, "Explicit default Y lost to source migration");
        foreach (int spans in new[] { 1, 2, 3 })
        {
            var map = new MapDocument { SliderTickRate = .1 };
            var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = spans };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
            map.Tracks.Add(track);
            var before = OsuBeatmapWriter.Serialize(map);
            var conversion = SliderRepresentationEditing.ConvertToLegacy(map, track.Id);
            Check(conversion.Success, string.Join("; ", conversion.Diagnostics));
            Check(map.ImportedSliders.Single().Id == track.Id && map.Tracks.Count == 0, "Legacy identity");
            var after = OsuBeatmapWriter.Serialize(map);
            Check(before.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).SequenceEqual(after.PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X))), "Materialized playback");
            conversion = SliderRepresentationEditing.ConvertToTrack(map, track.Id);
            Check(conversion.Success && map.Tracks.Single().Id == track.Id, "FSlider identity");
        }
        var randomMap = imported.DeepClone();
        randomMap.DerandomizeFSliderDroplets = true;
        var random = SliderRepresentationEditing.RefreshLegacyDroplets(randomMap);
        Check(random.Success, string.Join("; ", random.Diagnostics));
        Check(randomMap.ImportedSliders[0].RandomizationSource is not null, "Missing durable Legacy baseline");
        var randomized = ProjectSerializer.Read(ProjectSerializer.Serialize(randomMap));
        Check(randomized.ContentEquals(randomMap), "Legacy baseline persistence");
        var native = new ImportedSlider { X = 64, Y = 64, TimeMs = 3000, PixelLength = 128, PathType = 'L', PreserveNativeGeometry = true };
        native.ControlPoints.AddRange([new(64, 64), new(192, 64)]);
        var mixed = randomized.DeepClone();
        mixed.ImportedSliders.Add(native);
        random = SliderRepresentationEditing.RefreshLegacyDroplets(mixed);
        Check(random.Success, string.Join("; ", random.Diagnostics));
        var retainedNative = mixed.ImportedSliders.Single(s => s.Id == native.Id);
        Check(retainedNative.PreserveNativeGeometry && retainedNative.RandomizationTrack is not null
            && retainedNative.RandomizationSource!.ControlPoints.SequenceEqual(native.ControlPoints) && retainedNative.RandomizationSource.PixelLength == native.PixelLength,
            "Mixed Legacy refresh lost original native controls");
        random = SliderRepresentationEditing.RefreshLegacyDroplets(randomized);
        Check(random.Success, string.Join("; ", random.Diagnostics));
        var stable = OsuBeatmapWriter.Serialize(randomized).PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).ToArray();
        randomized.RandomizeDropletStrength = 8;
        randomized.RandomizeDropletSeed = 99;
        randomized.DerandomizeFSliderDroplets = false;
        random = SliderRepresentationEditing.RefreshLegacyDroplets(randomized);
        Check(random.Success, string.Join("; ", random.Diagnostics));
        Check(randomized.ImportedSliders[0].OriginalLine!.Contains("1:2:3:60:slide.wav"), "Legacy samples lost");
        var firstEffect = OsuBeatmapWriter.Serialize(randomized).PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).ToArray();
        random = SliderRepresentationEditing.RefreshLegacyDroplets(randomized);
        Check(random.Success && firstEffect.SequenceEqual(OsuBeatmapWriter.Serialize(randomized).PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X))), "Repeated effects accumulate");
        randomized.DerandomizeFSliderDroplets = true;
        random = SliderRepresentationEditing.RefreshLegacyDroplets(randomized);
        var returned = OsuBeatmapWriter.Serialize(randomized).PlayableObjects.Select(o => (o.Kind, o.TimeMs, o.X)).ToArray();
        Check(random.Success && stable.SequenceEqual(returned), "Returning to derandomized baseline changed fruits: " + string.Join("; ", stable.Zip(returned).Where(p => p.First != p.Second).Select(p => $"{p.First} -> {p.Second}")));
        var lowSv = randomized.DeepClone();
        lowSv.DurationMs = 100000;
        var lowSvBefore = lowSv.DeepClone();
        var managed = lowSv.ImportedSliders[0];
        Check(TimingMap.At(lowSv, managed.TimeMs).SliderVelocityMultiplier > managed.RandomizationBaseVelocity.GetValueOrDefault(),
            "Low-SV fixture must require generated SV above its fitting seed");
        foreach (var point in lowSv.TimingPoints.Where(p => p.TimeMs == managed.TimeMs && !p.Uninherited))
            point.BeatLengthMs = -1000;
        double targetDuration = ImportedSliderConverter.DurationMs(lowSv, managed);
        random = SliderRepresentationEditing.RefreshLegacyDroplets(lowSv, previous: lowSvBefore);
        Check(random.Success, string.Join("; ", random.Diagnostics));
        Check(lowSv.ImportedSliders[0].RandomizationBaseVelocity == .1
            && Math.Abs(ImportedSliderConverter.DurationMs(lowSv, lowSv.ImportedSliders[0]) - targetDuration) < 1e-6,
            $"Clamping the fitting seed changed the exact timing scale: seed={lowSv.ImportedSliders[0].RandomizationBaseVelocity:R}, duration={ImportedSliderConverter.DurationMs(lowSv, lowSv.ImportedSliders[0]):R}, expected={targetDuration:R}");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(lowSv)).ContentEquals(lowSv), "Minimum-SV baseline cannot be persisted");
        var unchanged = randomized.DeepClone();
        var failure = SliderRepresentationEditing.ConvertToLegacy(randomized, Guid.NewGuid());
        Check(!failure.Success && randomized.ContentEquals(unchanged), "Failed conversion mutated source");
        var repeated = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:0\n[TimingPoints]\n0,500\n[HitObjects]\n100,80,2000,2,0,L|200:80,2,192");
        repeated.RandomizeDropletStrength = 8;
        repeated.RandomizeDropletSeed = 99;
        unchanged = repeated.DeepClone();
        failure = SliderRepresentationEditing.RefreshLegacyDroplets(repeated);
        Check(failure.Success && repeated.ImportedSliders[0].SpanCount == 2 && OsuBeatmapWriter.Serialize(repeated).ObjectSequenceMatches,
            "Repeated Legacy effects lost traversals or export compatibility");
        repeated.ImportedSliders[0].RandomizationSource = null;
        repeated.ImportedSliders[0].RandomizationTrack = null;
        repeated.ImportedSliders[0].PathType = 'Z';
        unchanged = repeated.DeepClone();
        failure = SliderRepresentationEditing.RefreshLegacyDroplets(repeated);
        Check(!failure.Success && failure.Diagnostics.Count > 0 && repeated.ContentEquals(unchanged), "Invalid Legacy effects must report failure atomically");
        var history = new EditorHistory(randomized);
        var original = history.Document.DeepClone();
        double originalDuration = ImportedSliderConverter.DurationMs(history.Document, history.Document.ImportedSliders[0]);
        history.Begin("Timing");
        history.Document.TimingPoints.First(p => p.Uninherited).BeatLengthMs *= 2;
        random = history.RefreshLegacyDroplets();
        Check(random.Success, string.Join("; ", random.Diagnostics));
        history.Commit();
        Check(Math.Abs(ImportedSliderConverter.DurationMs(history.Document, history.Document.ImportedSliders[0]) - originalDuration * 2) < 1e-6,
            "Managed Legacy duration ignored timing edit");
        var timed = history.Document.DeepClone();
        history.Undo(); Check(history.Document.ContentEquals(original), "Legacy timing undo");
        history.Redo(); Check(history.Document.ContentEquals(timed), "Legacy timing redo");
        history.Begin("Move and flags");
        var moved = history.Document.ImportedSliders[0];
        moved.TimeMs += 1000;
        var fields = moved.OriginalLine!.Split(','); fields[2] = moved.TimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields[3] = "6"; fields[4] = "8"; moved.OriginalLine = string.Join(',', fields);
        random = history.RefreshLegacyDroplets();
        Check(random.Success, string.Join("; ", random.Diagnostics));
        Check(history.Document.ImportedSliders[0].TimeMs == moved.TimeMs
            && history.Document.ImportedSliders[0].OriginalLine!.Split(',')[3] == "6"
            && history.Document.ImportedSliders[0].OriginalLine!.Split(',')[4] == "8", "Legacy move or flags reverted");
        history.Commit();
        var preference = history.Document.DeepClone(); preference.OsuDistanceSpacing = 2;
        Check(!SliderRepresentationEditing.NeedsLegacyDropletRefresh(history.Document, preference), "DS preference triggers Legacy fitting");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
