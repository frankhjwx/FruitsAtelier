using System.Globalization;
using FruitsAtelier.Core;

internal static class TimingBatchEditingTests
{
    public static void Run()
    {
        DurationIncludesImportedTails();
        ResnapMatchesIndependentLoop();
    }

    private static MapDocument ImportedMap(int version = 14) => OsuBeatmapReader.Read($"""
        osu file format v{version}
        [General]
        Mode:2
        [Difficulty]
        SliderMultiplier:1.4
        SliderTickRate:2
        [TimingPoints]
        100,500,4,1,0,100,1,0
        800,-50,4,1,0,80,0,0
        2000,400,3,1,0,100,1,0
        2000,-80,3,1,0,70,0,0
        2400,-125,3,1,0,80,0,0
        [HitObjects]
        100,192,101,2,0,L|250:192,1,153
        100,192,799,2,0,B|160:100|250:192,2,219
        100,192,800,2,0,L|250:192,3,171
        100,192,1999,2,0,L|250:192,2,133
        100,192,2000,2,0,L|250:192,1,203
        100,192,2400,2,0,L|250:192,2,1
        """);

    private static void DurationIncludesImportedTails()
    {
        foreach (int version in new[] { 7, 14 })
        foreach (string operation in new[] { "move", "scale", "resnap" })
        {
            var map = ImportedMap(version);
            map.DurationMs = 0;
            var history = new EditorHistory(map);
            history.Begin(operation);
            if (operation == "move") TimingEditing.TransformObjects(history.Document, t => (t + 10000, 1));
            else if (operation == "resnap") TimingEditing.Resnap(history.Document, 1);
            else
            {
                var old = TimingEditing.Copy(history.Document.TimingPoints[0]);
                var next = TimingEditing.Copy(old); next.TimeMs += 10000; next.BeatLengthMs *= 2;
                var points = history.Document.TimingPoints.Select((p, i) => i == 0 ? next : p).ToArray();
                TimingEditing.Apply(history.Document, points, [(old, next)], new(Scale: true));
            }
            history.Commit();
            var result = history.Document.DeepClone();
            double tail = result.ImportedSliders.Max(s => ImportedSliderConverter.EndTimeMs(result, s));
            Check(result.DurationMs >= tail, $"{operation} v{version} omitted an imported tail");
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(result)).ContentEquals(result), "Retimed duration did not persist");
            history.Undo(); Check(history.Document.ContentEquals(map), "Retiming undo lost the original content");
            history.Redo(); Check(history.Document.ContentEquals(result), "Retiming redo lost the complete duration");
        }
        var ranged = ImportedMap();
        double first = ranged.ImportedSliders[0].TimeMs;
        TimingEditing.TransformObjects(ranged, t => (t + 10000, 1), 800, 801);
        Check(ranged.ImportedSliders[0].TimeMs == first && ranged.ImportedSliders[2].TimeMs == 10800,
            "Range retiming changed an unselected slider");
        Check(ranged.DurationMs >= ranged.ImportedSliders.Max(s => ImportedSliderConverter.EndTimeMs(ranged, s)), "Range retiming omitted a tail");
        ranged.DurationMs = 100000;
        TimingEditing.TransformObjects(ranged, t => (t, 1));
        Check(ranged.DurationMs == 100000, "Retiming shrank the existing duration");
        var shiftedGreen = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n1000,500,4,1,0,100,1,0\n1800,-200,4,1,0,100,0,0\n[HitObjects]\n100,192,2000,2,0,L|250:192,2,150\n");
        shiftedGreen.ImportedSliders[0].SliderVelocityMultiplier = null;
        shiftedGreen.DurationMs = 0;
        var beforeRed = TimingEditing.Copy(shiftedGreen.TimingPoints[0]);
        var afterRed = TimingEditing.Copy(beforeRed); afterRed.TimeMs = 0;
        TimingEditing.Apply(shiftedGreen, [afterRed, shiftedGreen.TimingPoints[1]], [(beforeRed, afterRed)],
            new(Scale: true, OffsetMarkers: true));
        Check(shiftedGreen.TimingPoints[1].TimeMs == 800 && shiftedGreen.ImportedSliders[0].TimeMs == 1000,
            "Offset-marker timing fixture did not move the green before the retimed head");
        Check(shiftedGreen.DurationMs >= ImportedSliderConverter.EndTimeMs(shiftedGreen, shiftedGreen.ImportedSliders[0]),
            "Final moved green timing extended a tail beyond the retimed duration");
    }

    private static void ResnapMatchesIndependentLoop()
    {
        foreach (int version in new[] { 7, 14 })
        foreach (int divisor in new[] { 1, 3, 4, 12, 48 })
        foreach (bool independent in new[] { false, true })
        {
            var map = ImportedMap(version);
            if (!independent) foreach (var slider in map.ImportedSliders) slider.SliderVelocityMultiplier = null;
            else { map.SliderMultiplierOverride = 1.6; map.DistancePerBeatOverride = 160; }
            var track = new CurveTrack { Kind = CurveKind.Bezier, SpanCount = 2 };
            track.Nodes.AddRange([
                new Anchor { TimeMs = 799, X = 100, HandleOut = new(120, 30) },
                new Anchor { TimeMs = 1371, X = 250, HandleIn = new(-90, -20) }
            ]);
            map.Tracks.Add(track);
            var expected = map.DeepClone();
            ReferenceResnapLengths(expected, divisor);
            TimingEditing.ResnapLengths(map, divisor);
            Check(map.ContentEquals(expected), $"Batched length resnap differs from reference: v{version}, 1/{divisor}, independent={independent}");
            var once = map.DeepClone();
            TimingEditing.ResnapLengths(map, divisor);
            ReferenceResnapLengths(expected, divisor);
            Check(map.ContentEquals(expected), "Repeated length resnap differs from reference");
            Check(ProjectSerializer.Read(ProjectSerializer.Serialize(once)).ContentEquals(once), $"Resnapped source lines or handles did not persist: v{version}, 1/{divisor}, independent={independent}");
        }
    }

    // Retain the unbatched calculation as an oracle for lookup ownership changes.
    private static void ReferenceResnapLengths(MapDocument map, int divisor)
    {
        foreach (var slider in map.ImportedSliders)
        {
            var state = TimingMap.At(map, slider.TimeMs);
            double oldEnd = ImportedSliderConverter.EndTimeMs(map, slider);
            double duration = Math.Max(state.BeatLengthMs / divisor, TimingMap.Snap(map, oldEnd, divisor) - slider.TimeMs);
            slider.PixelLength = duration / slider.SpanCount * 100 * map.EffectiveSliderMultiplier
                * ImportedSliderConverter.SliderVelocityAt(map, slider) / state.BeatLengthMs;
            var fields = slider.OriginalLine!.Split(',');
            if (double.Parse(fields[7], CultureInfo.InvariantCulture) != slider.PixelLength)
            {
                fields[7] = slider.PixelLength.ToString("R", CultureInfo.InvariantCulture);
                slider.OriginalLine = string.Join(',', fields);
            }
        }
        foreach (var track in map.Tracks.Where(t => t.Nodes.Count >= 2))
        {
            double start = track.Nodes[0].TimeMs, end = CurveMath.EndTimeMs(track);
            double duration = Math.Max(TimingMap.At(map, start).BeatLengthMs / divisor, TimingMap.Snap(map, end, divisor) - start);
            TimingEditing.TransformTrack(track, start, start, duration / (end - start));
        }
        TimingEditing.Validate(map);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
