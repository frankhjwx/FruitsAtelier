using System.Text.Json;
using FruitsAtelier.Core;

internal static class AimodReportTests
{
    public static void Run()
    {
        var map = new MapDocument();
        map.TimingPoints.Add(new() { BeatLengthMs = 480 });
        map.Fruits.AddRange([new() { TimeMs = 0, X = 100 }, new() { TimeMs = 96, X = 200 }, new() { TimeMs = 40000, X = 300 }]);
        var before = map.DeepClone();
        var report = Aimod.Analyze(map);
        Check(report.Any(i => i.Code == "rare" && i.TimeMs == 96 && (int)i.Arguments[0] == 5), "Fifth-beat information");
        Check(report.Any(i => i.Code == "noBackground" && i.Severity == AimodSeverity.Error), "Missing background");
        Check(report.Any(i => i.Code == "previewUnset"), "Missing preview");
        Check(before.ContentEquals(map), "Analysis is read-only");
        Check(report.Select(Key).SequenceEqual(Aimod.Analyze(map, checkDistanceSnap: true).Select(Key)), "Stable Catch ignores distance checking");
        var state = new TimingState(2512, 60000d / 145, 1, true);
        Check(Aimod.SnapDivisor(state, 29374) == 12 && Aimod.SnapDivisor(state, 129215) == 5, "Rare divisor precedence");
        Check(Aimod.SnapDivisor(new(0, 480, 1, true), 13) == 0, "Unsupported snap emits warning");
        map.TimingPoints.AddRange([new() { TimeMs = 20000, BeatLengthMs = -100, Uninherited = false, Effects = 1 },
            new() { TimeMs = 21000, BeatLengthMs = -100, Uninherited = false },
            new() { TimeMs = 22000, BeatLengthMs = -100, Uninherited = false, Effects = 1 }]);
        report = Aimod.Analyze(map);
        Check(!report.Any(i => i.Code == "shortKiai" && i.TimeMs == 20000)
            && report.Any(i => i.Code == "shortKiai" && i.TimeMs == 22000), "Stable short-Kiai uses the preceding off gap");
        Check(report.Any(i => i.Code == "openKiai"), "Unclosed Kiai");
        var other = map.DeepClone(); SongSetup.Set(other, "Metadata", "Title", "Different");
        Check(Aimod.Analyze(map, otherDifficulties: [other]).Any(i => i.Code == "conflictTitle"), "Mapset metadata comparison");
        foreach (var point in map.TimingPoints) point.Volume = 0;
        Check(Aimod.Analyze(map).Any(i => i.Code == "allQuiet" && i.Severity == AimodSeverity.Error), "Silent timing sections");
        Resources();
    }

    public static void TwinBloom(string path)
    {
        var map = OsuBeatmapReader.ReadFile(path);
        var before = map.DeepClone();
        var report = Aimod.Analyze(map).Concat(Aimod.AnalyzeResources(map)).ToArray();
        Check(report.Length == 399, $"Expected 399 results, got {report.Length}");
        Check(report.Count(i => i.Severity == AimodSeverity.Warning) == 42 && report.All(i => i.Severity != AimodSeverity.Error), "42 warnings and zero errors");
        Check(report.Count(i => i.Category == AimodCategory.Compose) == 357 && report.Count(i => i.Category == AimodCategory.Timing) == 42, "Category counts");
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "aimod-twin-bloom.json")));
        var expectedTiming = reference.RootElement.GetProperty("timing").EnumerateArray().Select(i =>
            (i.GetProperty("kind").GetString(), i.GetProperty("timeMs").ValueKind == JsonValueKind.Null ? (int?)null : i.GetProperty("timeMs").GetInt32())).ToArray();
        var actualTiming = report.Where(i => i.Category == AimodCategory.Timing).Select(i => ((string?)i.Code, i.TimeMs is double t ? (int?)t : null)).ToArray();
        Check(expectedTiming.SequenceEqual(actualTiming), "Every Timing message and timestamp must match in order");
        foreach (var row in reference.RootElement.GetProperty("composeScreenshots").EnumerateArray())
        {
            int time = row.GetProperty("timeMs").GetInt32(), divisor = row.GetProperty("divisor").GetInt32();
            string code = row.GetProperty("kind").GetString() == "start" ? "rare" : "rareEnd";
            Check(report.Any(i => i.Code == code && (int?)i.TimeMs == time && (int)i.Arguments[0] == divisor), $"Compose {code} at {time}, 1/{divisor}");
        }
        Check(before.ContentEquals(map), "Twin Bloom remains unchanged");
        Console.WriteLine("PASS Twin Bloom: 399 results, 357 Compose, 42 Timing, 42 warnings, 0 errors; all 42 Timing and 90 visible Compose rows match.");
    }

    private static void Resources()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "aimod-resources", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var map = new MapDocument { SourcePath = Path.Combine(root, "test.osu") };
        OsuTimeline.SetBackgroundFilename(map, "missing.png");
        Check(Aimod.AnalyzeResources(map).Any(i => i.Code == "missingFile"), "Missing referenced background");
        byte[] png = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 3000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 1600);
        File.WriteAllBytes(Path.Combine(root, "missing.png"), png);
        Check(Aimod.AnalyzeResources(map).Single().Code == "largeBackground", "Oversized background");
    }
    private static string Key(AimodIssue issue) => $"{issue.Category}/{issue.Severity}/{issue.Code}/{issue.TimeMs}";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
