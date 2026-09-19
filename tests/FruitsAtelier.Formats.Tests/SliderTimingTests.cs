using System.Globalization;
using FruitsAtelier.Core;

internal static class SliderTimingTests
{
    public static void CompatibleCloseTiming()
    {
        var document = Map();
        AddTrack(document, 1000, 2000, 0, 400);
        double sv = CatchStreamConverter.Convert(document, false).Sliders.Single().SliderVelocityMultiplier;
        document.TimingPoints.Add(new() { TimeMs = 1001, BeatLengthMs = -100 / sv, Uninherited = false, Volume = 35 });
        AddTrack(document, 1001, 2001, 0, 400);
        AddImported(document, 1100, 100, 1);
        var before = document.DeepClone();
        var result = OsuBeatmapWriter.Serialize(document, false);
        var raw = RawMap.Parse(result.Text);
        foreach (int offset in new[] { 0, 1 })
        {
            Near(1000, raw.Duration(1000, offset)); Near(1000, raw.Duration(1001, offset));
            Near(500 / sv, raw.Duration(1100, offset));
        }
        Check(document.ContentEquals(before) && raw.Timing.Single(p => p.Time == 1001).Fields[5] == "35",
            "Compatible close timing must preserve metadata and source content");
        var conflict = Map(); AddTrack(conflict, 1000, 2000, 0, 400); AddImported(conflict, 1001, 100, 1);
        try { OsuBeatmapWriter.Serialize(conflict, false); throw new Exception("Conflicting heads should fail"); }
        catch (InvalidDataException error)
        { Check(error.Message.Contains("1000") && error.Message.Contains("1001"), "Conflict must identify both timestamps"); }
    }
    public static void HighSvExportIsRejected()
    {
        var document = Map();
        AddTrack(document, 2000, 2020, 0, 512);
        Throws(() => OsuBeatmapWriter.Serialize(document, false));
    }

    public static void RestorationStaysOutsideHeadWindow()
    {
        var document = Map();
        AddTrack(document, 1000, 2000, 0, 400);
        AddImported(document, 1100, 100, 1);
        var result = OsuBeatmapWriter.Serialize(document, false);
        var raw = RawMap.Parse(result.Text);
        Near(1000, raw.Duration(1000, 0));
        Near(1000, raw.Duration(1000, 1));
        Check(raw.Timing.Where(p => p.Time > 1000 && p.Time <= 1001).Count() == 0,
            "SV restoration entered the slider head's next-millisecond window.");
        Near(1, raw.State(1002).Sv);
        Near(500, raw.Duration(1100, 0)); Near(500, raw.Duration(1100, 1));
        Check(raw.Timing.All(p => p.Time == Math.Truncate(p.Time)), "Writer introduced a fractional timing offset.");
    }

    public static void IsolatedEditedSliderDoesNotRestoreUnusedSv()
    {
        var document = Map();
        AddTrack(document, 1000, 2000, 0, 400);
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Near(1000, raw.Duration(1000, 0)); Near(1000, raw.Duration(1000, 1));
        Check(raw.Timing.Count == 2 && raw.Timing[^1].Time == 1000,
            "An isolated edited slider introduced an unused SV restoration.");
        Near(raw.State(1000).Sv, raw.State(9999).Sv);
    }

    public static void SameTimeOverrideReplacesGreensAndPreservesRed()
    {
        var document = Map();
        document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -50, Uninherited = false, SourceOrder = 1, SampleSet = 2, Volume = 35 });
        document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, SourceOrder = 2, SampleSet = 3, SampleIndex = 7, Volume = 65, Effects = 1 });
        document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = 500, SourceOrder = 3, SampleSet = 1, Volume = 10, Meter = 3 });
        AddTrack(document, 1000, 2000, 0, 400);
        var before = document.DeepClone();
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Check(document.ContentEquals(before), "Timing export mutated the original document.");
        var sameTime = raw.Timing.Where(p => p.Time == 1000).ToArray();
        Check(sameTime.Length == 2 && sameTime[0].Red && !sameTime[1].Red,
            "Edited head must have its original red point followed by one effective green point.");
        Near(500, sameTime[0].BeatLength);
        Check(sameTime[0].Fields[2] == "3", "The red point's meter changed.");
        Check(sameTime[1].Fields[3] == "3" && sameTime[1].Fields[4] == "7"
            && sameTime[1].Fields[5] == "65" && sameTime[1].Fields[7] == "1",
            "Override did not inherit the effective green point's sample/effect state.");
        Near(1000, raw.Duration(1000, 0)); Near(1000, raw.Duration(1000, 1));
        Check(raw.Timing.All(p => p.Time <= 1000), "Unused original SV was restored after replacing the head.");
    }

    public static void NaturalBoundaryIsNotOverwrittenByRestore()
    {
        var document = Map();
        document.TimingPoints.Add(new() { TimeMs = 1100, BeatLengthMs = 250, SourceOrder = 1, Meter = 3 });
        document.TimingPoints.Add(new() { TimeMs = 1100, BeatLengthMs = -40, Uninherited = false, SourceOrder = 2, SampleIndex = 9, Volume = 20 });
        AddTrack(document, 1000, 2000, 0, 400);
        AddImported(document, 1200, 50, 2);
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Near(1000, raw.Duration(1000, 1));
        Check(raw.Timing.All(p => p.Time <= 1000 || p.Time >= 1100), "Unused restore was inserted before a natural boundary.");
        Near(250, raw.State(1100).BeatLength); Near(2.5, raw.State(1100).Sv);
        Near(100, raw.Duration(1200, 0)); Near(100, raw.Duration(1200, 1));
        var points = raw.Timing.Where(p => p.Time == 1100).ToArray();
        Check(points.Length == 2 && points[1].Fields[4] == "9" && points[1].Fields[5] == "20",
            "Restoration overwrote the next red/green boundary or its samples.");
    }

    public static void AdjacentGeneratedHeadsHaveOneSvEach()
    {
        var document = Map();
        AddTrack(document, 1000, 2000, 0, 400);
        AddTrack(document, 1002, 1502, 400, 0);
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Near(1000, raw.Duration(1000, 1)); Near(500, raw.Duration(1002, 1));
        Check(raw.Timing.Count(p => p.Time == 1002 && !p.Red) == 1, "Restore and the next generated override created conflicting greens.");
        Check(raw.Timing.Count == 3 && raw.Timing[^1].Time == 1002, "Consecutive generated sliders introduced an unused restore.");
    }

    public static void SharedGeneratedSvRestoresOnlyForFollowingImportedSlider()
    {
        var document = Map();
        AddTrack(document, 1000, 2000, 0, 400);
        AddTrack(document, 1010, 2010, 400, 0);
        AddImported(document, 1100, 100, 1);
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Near(1000, raw.Duration(1000, 1)); Near(1000, raw.Duration(1010, 1));
        Near(500, raw.Duration(1100, 0)); Near(500, raw.Duration(1100, 1));
        Check(raw.Timing.Count == 3 && raw.Timing[^1].Time == 1012,
            "Shared SV must continue through generated heads and restore only for the following imported slider.");
        Near(1, raw.State(1012).Sv);
    }

    public static void FollowingGeneratedHeadReestablishesOriginalSv()
    {
        var document = Map();
        AddTrack(document, 1000, 2000, 0, 400);
        AddTrack(document, 1100, 2100, 100, 100);
        AddImported(document, 1100, 100, 1);
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Near(1000, raw.Duration(1000, 1));
        Check(raw.Timing.Count == 3 && raw.Timing[^1].Time == 1100,
            "The next generated head must establish its own original SV without an intervening restore.");
        Near(1, raw.State(1100).Sv);
        var importedLine = "100,100,1100,2,0,L|200:100,1,100";
        Near(500, raw.DurationOfLine(importedLine, 0)); Near(500, raw.DurationOfLine(importedLine, 1));
    }

    public static void UnsafeRestorationWindowIsRejected()
    {
        foreach (double boundary in new[] { 1000.5, 1001.0, 1001.75 })
        {
            var document = Map();
            document.TimingPoints.Add(new() { TimeMs = boundary, BeatLengthMs = -80, Uninherited = false, SourceOrder = 1 });
            AddTrack(document, 1000, 2000, 0, 400);
            var before = document.DeepClone();
            Throws(() => OsuBeatmapWriter.Serialize(document, false));
            Check(document.ContentEquals(before), "Rejected timing insertion mutated the source.");
        }
        var closeSlider = Map();
        AddTrack(closeSlider, 1000, 2000, 0, 400);
        AddImported(closeSlider, 1001, 100, 1);
        Throws(() => OsuBeatmapWriter.Serialize(closeSlider, false));
        var precedingSlider = Map();
        AddImported(precedingSlider, 999, 100, 1);
        AddTrack(precedingSlider, 1000, 2000, 0, 400);
        Throws(() => OsuBeatmapWriter.Serialize(precedingSlider, false));
    }

    public static void NaNAndFractionalHeadRestoreOriginalState()
    {
        var document = Map();
        document.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = double.NaN, Uninherited = false, SourceOrder = 1 });
        AddTrack(document, 1000.25, 2000.25, 0, 400);
        AddImported(document, 1200, 100, 1);
        var raw = RawMap.Parse(OsuBeatmapWriter.Serialize(document, false).Text);
        Near(1000, raw.Duration(1000, 0)); Near(1000, raw.Duration(1000, 1));
        var restored = raw.Timing.Single(p => p.Time == 1002);
        Check(double.IsNaN(restored.BeatLength) && !restored.Red, "Original NaN state was not restored.");
        Near(1, raw.State(1002).Sv);
        Near(500, raw.Duration(1200, 0)); Near(500, raw.Duration(1200, 1));
    }

    public static void UserProjectExportHasIndependentCorrectDurations()
    {
        string root = FindRoot();
        string projectPath = Path.Combine(root, "artifacts", "projects", "Oriental Blossom -栄華秀英-.catchproj");
        if (!File.Exists(projectPath)) { Console.WriteLine("  SKIP optional user project: " + projectPath); return; }
        var document = ProjectSerializer.ReadFile(projectPath);
        var before = document.DeepClone();
        var result = OsuBeatmapWriter.Serialize(document, false);
        var raw = RawMap.Parse(result.Text);
        var baseline = RawMap.FromTiming(document);
        string previousPath = Path.Combine(root, "artifacts", "exports", "Oriental Blossom -栄華秀英-", "T1.osu");
        var previous = File.Exists(previousPath) ? RawMap.Parse(File.ReadAllText(previousPath)) : null;
        var sliderReports = new List<object>();
        foreach (var track in document.Tracks)
        {
            double head = Math.Round(track.Nodes[0].TimeMs, MidpointRounding.AwayFromZero);
            double duration = (track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs) * track.SpanCount;
            Near(duration, raw.Duration(head, 0)); Near(duration, raw.Duration(head, 1));
            Check(raw.Timing.Count(p => p.Time == head && !p.Red) <= 1, "User export has ambiguous same-time SV points.");
            double originalSv = baseline.State(head).Sv;
            double requiredSv = raw.State(head).Sv;
            double durationWithOriginalSv = raw.Duration(head, 0) * requiredSv / originalSv;
            sliderReports.Add(new
            {
                track.Name, StartMs = head, track.SpanCount, ExpectedDurationMs = duration,
                ExpectedEndMs = head + duration, OriginalSv = originalSv, RequiredSv = requiredSv,
                IndependentDurationMs = raw.Duration(head, 0), IndependentEndMs = head + raw.Duration(head, 0),
                NextMillisecondLookupDurationMs = raw.Duration(head, 1),
                DurationIfOriginalSvSelectedMs = durationWithOriginalSv, OriginalSvDurationRatio = requiredSv / originalSv,
                PreviousExportExactLookupMs = previous?.Duration(head, 0),
                PreviousExportNextMillisecondLookupMs = previous?.Duration(head, 1)
            });
            Console.WriteLine($"  USER slider {head:R}: target {duration:R} ms, independent {raw.Duration(head, 1):R} ms");
        }
        var outputLines = result.Text.Split('\n').Select(line => line.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        int retainedSliders = 0, retainedFruits = 0, retainedShowers = 0;
        double maximumUneditedDurationChange = 0;
        foreach (var slider in document.ImportedSliders)
        {
            Check(slider.OriginalLine is not null && outputLines.Contains(slider.OriginalLine), "An unedited imported slider line changed.");
            string originalLine = slider.OriginalLine!;
            double difference = Math.Abs(raw.DurationOfLine(originalLine, 0) - baseline.DurationOfLine(originalLine, 0));
            maximumUneditedDurationChange = Math.Max(maximumUneditedDurationChange, difference);
            Near(0, difference);
            Near(0, raw.DurationOfLine(originalLine, 1) - baseline.DurationOfLine(originalLine, 1));
            retainedSliders++;
        }
        foreach (var fruit in document.Fruits)
        {
            Check(fruit.OriginalLine is not null && outputLines.Contains(fruit.OriginalLine), "An unedited fruit line changed.");
            retainedFruits++;
        }
        foreach (var shower in document.BananaShowers)
        {
            Check(shower.OriginalLine is not null && outputLines.Contains(shower.OriginalLine), "An unedited banana-shower line changed.");
            retainedShowers++;
        }
        Check(document.ContentEquals(before), "User project changed during export validation.");
        var originalTimes = document.TimingPoints.Select(p => p.TimeMs).ToHashSet();
        var generatedHeads = document.Tracks.Select(t => Math.Round(t.Nodes[0].TimeMs, MidpointRounding.AwayFromZero)).ToHashSet();
        double[] currentRestoreTimes = raw.Timing.Where(p => !p.Red && !originalTimes.Contains(p.Time) && !generatedHeads.Contains(p.Time))
            .Select(p => p.Time).ToArray();
        double[] previousRestoreTimes = previous?.Timing.Where(p => !p.Red && !originalTimes.Contains(p.Time) && !generatedHeads.Contains(p.Time))
            .Select(p => p.Time).ToArray() ?? [];
        string output = Path.Combine(root, "artifacts", "audio-export-validation");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "T1-fixed.osu"), result.Text);
        File.WriteAllText(Path.Combine(output, "slider-timing-report.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            SourceProject = projectPath, PreviousExport = previousPath, Output = Path.Combine(output, "T1-fixed.osu"),
            Verification = "Independent emitted-field duration formula; exact and +1 ms lookup scenarios. The +1 ms scenario is a conservative compatibility check, not a verified stable-client lookup rule.",
            StableClientVerified = false, Sliders = sliderReports,
            Timing = new
            {
                OriginalPointCount = document.TimingPoints.Count, PreviousPointCount = previous?.Timing.Count, OutputPointCount = raw.Timing.Count,
                PreviousRestoreTimes = previousRestoreTimes, OutputRestoreTimes = currentRestoreTimes,
                RemovedUnusedRestoreCount = previousRestoreTimes.Except(currentRestoreTimes).Count()
            },
            Unedited = new { Fruits = retainedFruits, Sliders = retainedSliders, BananaShowers = retainedShowers, MaximumDurationChangeMs = maximumUneditedDurationChange },
            SourceDocumentUnchanged = document.ContentEquals(before)
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static MapDocument Map()
    {
        var document = new MapDocument { SliderMultiplier = 1, DurationMs = 10000 };
        document.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SourceOrder = 0 });
        return document;
    }

    private static void AddTrack(MapDocument document, double start, double end, double from, double to)
    {
        var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = false };
        track.Nodes.Add(new() { TimeMs = start, X = from });
        track.Nodes.Add(new() { TimeMs = end, X = to });
        document.Tracks.Add(track);
    }

    private static void AddImported(MapDocument document, double start, double length, int spans)
    {
        var slider = new ImportedSlider { TimeMs = start, X = 100, Y = 100, PathType = 'L', PixelLength = length, SpanCount = spans };
        slider.ControlPoints.Add(new(100, 100)); slider.ControlPoints.Add(new(100 + length, 100));
        document.ImportedSliders.Add(slider);
    }

    // This oracle parses emitted fields directly and uses length * spans * beatLength / (100 * multiplier * SV).
    // It does not call the production reader, timing lookup, geometry converter or slider-duration helper.
    private sealed class RawMap
    {
        public List<RawTiming> Timing { get; } = [];
        private readonly List<string[]> sliders = [];
        private double multiplier;

        public static RawMap FromTiming(MapDocument document)
        {
            var map = new RawMap { multiplier = document.SliderMultiplier };
            map.Timing.AddRange(document.TimingPoints.Select(p => new RawTiming(p.TimeMs, p.BeatLengthMs, p.Uninherited, [])));
            return map;
        }

        public static RawMap Parse(string text)
        {
            var map = new RawMap();
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith('[')) { section = line; continue; }
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (section == "[Difficulty]" && line.StartsWith("SliderMultiplier:", StringComparison.Ordinal))
                    map.multiplier = Number(line.Split(':', 2)[1]);
                else if (section == "[TimingPoints]")
                {
                    var fields = line.Split(',');
                    map.Timing.Add(new(Number(fields[0]), Number(fields[1]), fields[6] == "1", fields));
                }
                else if (section == "[HitObjects]")
                {
                    var fields = line.Split(',');
                    if ((int.Parse(fields[3], CultureInfo.InvariantCulture) & 2) != 0) map.sliders.Add(fields);
                }
            }
            Check(map.multiplier > 0, "Export has no slider multiplier.");
            return map;
        }

        public (double BeatLength, double Sv) State(double query)
        {
            double beatLength = Timing.First(p => p.Red).BeatLength, sv = 1;
            foreach (var point in Timing)
            {
                if (Math.Floor(point.Time) > query) break;
                if (point.Red) { beatLength = point.BeatLength; sv = 1; }
                else sv = point.BeatLength < 0 ? Math.Clamp(-100 / point.BeatLength, 0.1, 10) : 1;
            }
            return (beatLength, sv);
        }

        public double Duration(double head, double lookupOffset)
        {
            var fields = sliders.Single(p => Number(p[2]) == head);
            return DurationOfLine(string.Join(',', fields), lookupOffset);
        }

        public double DurationOfLine(string line, double lookupOffset)
        {
            var fields = line.Split(',');
            var timing = State(Number(fields[2]) + lookupOffset);
            return Number(fields[7]) * Number(fields[6]) * timing.BeatLength / (100 * multiplier * timing.Sv);
        }
    }

    private sealed record RawTiming(double Time, double BeatLength, bool Red, string[] Fields);
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static void Near(double expected, double actual)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > 0.001) throw new Exception($"Expected {expected:R}, got {actual:R}."); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Unsafe export was not rejected.");
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "global.json"))) return directory.FullName;
        throw new Exception("Workspace root not found.");
    }
}
