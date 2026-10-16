using FruitsAtelier.Core;

internal static class XnorTimingTests
{
    public static void Run()
    {
        const string invertedSpinner = "256,192,1200,8,0,1199,0:0:0:0:";
        var spinnerMap = OsuBeatmapReader.Read(Source("0,500,4,2,1,47,1,0") + "\n" + invertedSpinner);
        Check(spinnerMap.BananaShowers.Single().EndTimeMs == 1200, "Legacy spinners must clamp negative duration to zero.");
        var spinnerOutput = OsuBeatmapWriter.Serialize(ProjectSerializer.Read(ProjectSerializer.Serialize(spinnerMap)));
        Check(spinnerOutput.ObjectSequenceMatches && spinnerOutput.Text.Contains(invertedSpinner), "Clamped spinners must retain their original source line.");
        const string timing = "1000,-50,4,2,1,47,1,0";
        string source = Source(timing);
        var map = OsuBeatmapReader.Read(source);
        var state = TimingMap.At(map, 1000);
        Check(state == new TimingState(1000, 6, 2, true), "Negative red playback must clamp BPM while retaining its independent SV.");
        Check(map.BeatLengthMs == 6 && map.TimingPoints.Single().BeatLengthMs == -50
            && map.TimingPoints.Single().OriginalLine == timing, "Import must retain raw red timing and use a finite default.");
        Check(ImportedSliderConverter.EndTimeMs(map, map.ImportedSliders.Single()) == 1006,
            "Negative red playback must produce the expected finite slider duration.");
        Check(TimingMap.Grid(map, 1000, 1024, 1).Select(l => l.TimeMs).SequenceEqual(new[] { 1000d, 1006, 1012, 1018, 1024 }),
            "Beat grids must use effective timing.");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(map));
        Check(map.ContentEquals(restored), "Project round trip changed retained negative timing.");
        var output = OsuBeatmapWriter.Serialize(restored);
        Check(output.Text.Split('\n').Any(l => l.TrimEnd('\r') == timing)
            && output.ReadBack.TimingPoints.Single().BeatLengthMs == -50
            && output.ObjectSequenceMatches, "Export must preserve raw negative red timing and playback.");

        var before = TimingEditing.Copy(restored.TimingPoints.Single());
        var after = TimingEditing.Copy(before);
        after.BeatLengthMs = 12;
        TimingEditing.Apply(restored, [after], [(before, after)], new(Scale: true));
        Check(restored.BeatLengthMs == 12 && restored.Fruits.Single().TimeMs == 1006,
            "Timing edits must scale with effective beat lengths.");
        foreach (string raw in new[] { "NaN", "Infinity", "-Infinity" })
        {
            try { OsuBeatmapReader.Read(Source($"1000,{raw},4,2,1,47,1,0")); }
            catch (InvalidDataException) { continue; }
            throw new Exception($"Nonfinite red timing {raw} must be rejected.");
        }
        foreach (var (raw, expected) in new[] { (0d, 6d), (3d, 6d), (120000d, 60000d) })
        {
            var point = new TimingPoint { TimeMs = 0, BeatLengthMs = raw };
            var boundary = new MapDocument(); boundary.TimingPoints.Add(point);
            Check(TimingMap.At(boundary, 0).BeatLengthMs == expected, "Red playback bounds differ from legacy decoding.");
        }
        ImportedPathBoundaries();
    }

    private static void ImportedPathBoundaries()
    {
        var zero = OsuBeatmapReader.Read(Source("1000,500,4,2,1,47,1,0")
            .Replace("L|200:192,1,200", "L|0:192,3,200"));
        var zeroConverted = CatchStreamConverter.Convert(zero, false);
        var zeroSlider = zeroConverted.Sliders.Single();
        var zeroEvents = zeroConverted.Objects.Where(o => o.SourceId == zeroSlider.SourceId).ToArray();
        Check(zeroConverted.Success && zeroSlider.Length == 0 && zeroSlider.DurationMs == 0
            && zeroEvents.Length == 4 && zeroEvents.All(o => o.Kind == CatchObjectKind.Fruit && o.TimeMs == 1000 && o.X == 0),
            "A collapsed imported path must retain all coincident repeat fruits without droplets.");
        var zeroExport = OsuBeatmapWriter.Serialize(ProjectSerializer.Read(ProjectSerializer.Serialize(zero)));
        Check(zeroExport.ObjectSequenceMatches && zeroExport.ReadBack.ImportedSliders.Single().PixelLength == 200,
            "A collapsed path must preserve its positive raw expected length through project and export.");

        var longMap = OsuBeatmapReader.Read(Source("1000,-50,4,2,1,47,1,0")
            .Replace("L|200:192,1,200", "L|200:192,1,200000"));
        var longConverted = CatchStreamConverter.Convert(longMap, false);
        Check(longConverted.Success, "Long imported paths must produce complete Catch conversion.");
        var longSlider = longConverted.Sliders.Single();
        var longEvents = longConverted.Objects.Where(o => o.SourceId == longSlider.SourceId).ToArray();
        Check(longSlider.Length == 200000 && Math.Abs(longSlider.DurationMs - 6000) < .000001
            && Math.Abs(longEvents.Last().TimeMs - 7000) < .000001,
            "Tick bounds must retain the full imported path and tail duration.");
        var droplets = longEvents.Where(o => o.Kind == CatchObjectKind.Droplet).ToArray();
        Check(droplets.Length == 498 && Math.Abs(droplets[0].TimeMs - 1012) < .000001,
            "Legacy ticks must use capped-distance progress with the full span duration.");
        Check(longEvents.All(o => double.IsFinite(o.TimeMs) && double.IsFinite(o.X)),
            "Full imported conversion must contain finite playable events.");
        var longExport = OsuBeatmapWriter.Serialize(ProjectSerializer.Read(ProjectSerializer.Serialize(longMap)));
        Check(longExport.ObjectSequenceMatches && longExport.ReadBack.ImportedSliders.Single().PixelLength == 200000,
            "Long imported paths must preserve raw length and playback through project and export.");
    }

    private static string Source(string timing) => $"""
        osu file format v14

        [General]
        Mode:2

        [Difficulty]
        SliderMultiplier:1
        SliderTickRate:1

        [TimingPoints]
        {timing}

        [HitObjects]
        0,192,1000,2,0,L|200:192,1,200
        200,192,1003,1,0,0:0:0:0:
        """;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
