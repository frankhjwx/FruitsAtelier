using System.Text.Json;
using FruitsAtelier.Core;

internal static class AimodReportTests
{
    public static void Run()
    {
        OverlapBoundaries();
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
        VideoResources();
        Check(Aimod.VideoDimensionIssue(1280, 720) is null && Aimod.VideoDimensionIssue(1920, 720) is null
            && Aimod.VideoDimensionIssue(1024, 1000) is null, "Stable video dimensions require both axes to exceed the limit");
        Check(Aimod.VideoDimensionIssue(1280, 960) == "largeVideoStandard"
            && Aimod.VideoDimensionIssue(1920, 1080) == "largeVideoWide", "Video threshold priority");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", " F,0,0,99,1"]) == 1, "Short sprite lifetime");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", " F,0,0,100,1",
            "Sample,0,0,a.wav,100"]) == 0, "100 ms boundary and samples excluded");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", " L,1000,3", "  F,0,0,40,1,0"]) == 0,
            "Loop lifetime includes every repetition");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", " F,0,0,40,0,1,0,1"]) == 0,
            "Compound commands extend the lifetime");
        Check(Aimod.ShortStoryboardEvents(["Animation,Foreground,Centre,a.png,0,0,20,100,LoopForever", " F,0,0,99,0"]) == 1,
            "Animation frame duration and alpha do not replace command lifetime");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", " T,HitSound,1000,1100", "  F,0,0,10,1"]) == 0,
            "Trigger activation window participates in storyboard lifetime");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", " L,0,2", "  L,0,2", "   F,0,0,25,1"]) == 0,
            "Nested loops extend command lifetime to the exact 100 ms boundary");
        Check(Aimod.ShortStoryboardEvents(["Sprite,Foreground,Centre,a.png,0,0", "Sample,0,0,a.wav,100"]) == 0,
            "Sprites without commands and sample events do not count as short objects");
        var medium = new MapDocument();
        medium.Fruits.AddRange([new() { TimeMs = 0 }, new() { TimeMs = 100000 }]);
        medium.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 500, BeatLengthMs = -100, Uninherited = false, Effects = 1 },
            new() { TimeMs = 99000, BeatLengthMs = -100, Uninherited = false }]);
        Check(!Aimod.Analyze(medium).Any(i => i.Code is "muchKiai" or "muchKiaiTv"), "Stable does not apply Kiai ratios between 90 and 180 seconds");
        medium.TimingPoints.Add(new() { TimeMs = -500, BeatLengthMs = -100, Uninherited = false });
        Check(Aimod.Analyze(medium).Any(i => i.Code == "inheritedBeforeTiming"), "Inherited timing requires a preceding red point");
    }

    private static void OverlapBoundaries()
    {
        var map = new MapDocument { SliderMultiplier = 1 };
        // Keep overlap fixtures outside the automatic one-millisecond grid correction.
        map.TimingPoints.Add(new() { TimeMs = 3, BeatLengthMs = 500 });
        var slider = new ImportedSlider { TimeMs = 1000, PixelLength = 100.3, SpanCount = 2 };
        slider.ControlPoints.AddRange([new(0, 192), new(200, 192)]);
        map.ImportedSliders.Add(slider);
        var fruit = new Fruit { TimeMs = 2001 };
        map.Fruits.Add(fruit);
        // Two 501.5 ms spans end at 2003; the head alone is nowhere near the next fruit.
        var issue = Aimod.Analyze(map).Single(i => i.Code == "overlap");
        Check(issue.TimeMs == 2002 && issue.ObjectIds!.ToHashSet().SetEquals([slider.Id, fruit.Id]),
            "Repeated slider tail overlap locates the integer midpoint and selects both source objects");
        slider.SpanCount = 1; slider.PixelLength = 100.19;
        fruit.TimeMs = 1510;
        Check(!Aimod.Analyze(map).Any(i => i.Code == "overlap"), "A fractional tail at 1500.95 truncates before the strict 10 ms test");
        fruit.TimeMs = 1509;
        Check(Aimod.Analyze(map).Single(i => i.Code == "overlap").TimeMs == 1505,
            "Nine milliseconds reports a midpoint rounded toward the next start");
        fruit.TimeMs = 1499;
        Check(Aimod.Analyze(map).Single(i => i.Code == "overlap").TimeMs == 1499,
            "One millisecond of tail overlap remains an error");
    }

    public static void Ethanol(string path)
    {
        var map = OsuBeatmapReader.ReadFile(path);
        var before = map.DeepClone();
        var report = Aimod.Analyze(map).Concat(Aimod.AnalyzeResources(map)).ToArray();
        (string Code, int Time)[] expected = [("overlap", 34472), ("overlap", 118661),
            ("unsnappedEnd", 119410), ("unsnappedEnd", 160522), ("unsnappedEnd", 160902), ("shortKiai", 195351)];
        Check(report.Select(i => (i.Code, (int)i.TimeMs!)).SequenceEqual(expected), "All six Ethanol messages and timestamps match stable");
        Check(report.Count(i => i.Severity == AimodSeverity.Warning) == 4
            && report.Count(i => i.Severity == AimodSeverity.Error) == 2, "Ethanol has four warnings and two errors");
        Check(report.Count(i => i.Category == AimodCategory.Compose) == 5
            && report.Count(i => i.Category == AimodCategory.Timing) == 1, "Ethanol category totals");
        foreach (var (first, second) in new[] { (34086d, 34471d), (117350d, 117913d) })
            Check(report.Any(i => i.Code == "overlap" && i.ObjectIds!.ToHashSet().SetEquals(
                map.ImportedSliders.Where(s => s.TimeMs == first || s.TimeMs == second).Select(s => s.Id))),
                "Ethanol overlap selects the two original sliders");
        Check(before.ContentEquals(map), "Ethanol remains unchanged");
        Console.WriteLine("PASS Ethanol: 6 results, 5 Compose, 1 Timing, 4 warnings, 2 errors; all messages and timestamps match.");
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
        File.WriteAllBytes(Path.Combine(root, "hitcircle@2x.png"), png);
        Check(Aimod.AnalyzeResources(map).Any(i => i.Code == "skinDimensions" && i.Severity == AimodSeverity.Error
            && (int)i.Arguments[1] == 256), "Retina skin image dimensions");
        File.Delete(Path.Combine(root, "hitcircle@2x.png"));
        string storyboard = "[Variables]\n$img=missing.png\n[Events]\n" + string.Join('\n', Enumerable.Range(0, 6)
            .Select(i => $"Sprite,Foreground,Centre,\"$img\",320,240\n F,0,{i * 100},{i * 100 + 99},1"));
        File.WriteAllText(Path.Combine(root, "shared.osb"), storyboard);
        Check(Aimod.AnalyzeResources(map).Count(i => i.Code == "epilepsy") == 1, "Shared storyboard and variables");
        SongSetup.Set(map, "General", "EpilepsyWarning", "1");
        Check(!Aimod.AnalyzeResources(map).Any(i => i.Code == "epilepsy"), "Explicit warning suppresses diagnostic");
        File.WriteAllText(Path.Combine(root, "broken.mp4"), "invalid video");
        map.OriginalSections.Single(s => s.Name == "Events").Lines.Add("Video,0,broken.mp4");
        Check(Aimod.AnalyzeResources(map).Any(i => i.Code == "unreadableMedia"), "Unreadable video does not suppress other findings");
        File.WriteAllBytes(Path.Combine(root, "Video.flv"), [70, 76, 86, 1, 1, 0, 0, 0, 9, 0, 0, 0, 0]);
        map.OriginalSections.Single(s => s.Name == "Events").Lines.Add("Video,0,Video.flv");
        Check(Aimod.AnalyzeResources(map).Any(i => i.Code == "unreadableMedia" && (string)i.Arguments[0] == "Video.flv"),
            "Unsupported video formats report unreadable metadata");
        SongSetup.Set(map, "General", "AudioFilename", "Video.flv");
        Check(Aimod.AnalyzeResources(map).Any(i => i.Code == "unreadableMedia" && (string)i.Arguments[0] == "Video.flv"),
            "Unsupported audio still reports unreadable metadata");
        File.WriteAllBytes(Path.Combine(root, "broken.mp4"), []);
        File.WriteAllBytes(Path.Combine(root, "video.avi"), Video());
        map.OriginalSections.Single(s => s.Name == "Events").Lines.Add("Video,0,video.avi");
        Check(Aimod.AnalyzeResources(map).Any(i => i.Code == "largeVideoWide"), "Video dimensions are read from the actual media header");
        SongSetup.Set(map, "General", "AudioFilename", "audio.wav");
        using (var writer = new BinaryWriter(File.Create(Path.Combine(root, "audio.wav"))))
        {
            int data = 8000 * 2 * 60;
            writer.Write("RIFF"u8); writer.Write(36 + data); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(data); writer.Write(new byte[data]);
        }
        map.Fruits.AddRange([new() { TimeMs = 10000 }, new() { TimeMs = 49000 }]);
        Check(!Aimod.AnalyzeResources(map).Any(i => i.Code == "longAudio"), "Resource metadata does not estimate audio length");
        Check(!Aimod.Analyze(map).Any(i => i.Code == "longAudio"), "Audio length waits for a supplied playback duration");
        Check(Aimod.Analyze(map, audioDurationMs: 60000).Any(i => i.Code == "longAudio"), "Audio mapped ratio below 80 percent");
        map.Fruits[1].TimeMs = 50000;
        Check(!Aimod.Analyze(map, audioDurationMs: 60000).Any(i => i.Code == "longAudio"), "Exactly 80 percent is permitted");
    }
    private static void VideoResources()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "aimod-video-resources", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var map = new MapDocument { SourcePath = Path.Combine(root, "test.osu") };
            var events = new OsuSection { Name = "Events" };
            events.Lines.AddRange(["Video,0,$video", "1,10,clip.flv"]);
            map.OriginalSections.Add(events);
            var variables = new OsuSection { Name = "Variables" };
            variables.Lines.Add("$video=clip.flv");
            map.OriginalSections.Add(variables);
            File.WriteAllBytes(Path.Combine(root, "clip.flv"), [1]);
            File.WriteAllBytes(Path.Combine(root, "shared.wmv"), [2]);
            File.WriteAllText(Path.Combine(root, "shared.osb"), "[Variables]\n$video=shared.wmv\n[Events]\nVideo,0,\"$video\"\n");
            var before = map.DeepClone();
            var probed = new List<string>();
            var issues = Aimod.AnalyzeResources(map, path =>
            {
                probed.Add(Path.GetFileName(path));
                return new(1920, 1080, true);
            });
            Check(probed.Order().SequenceEqual(new[] { "clip.flv", "shared.wmv" }), "Video probing scopes variables to their owning file and deduplicates references");
            Check(issues.Count(i => i.Code == "largeVideoWide") == 2, "Supplied decoder metadata checks formats outside TagLib coverage");
            Check(before.ContentEquals(map), "Video resource analysis preserves authoring content");
            Check(Aimod.VideoResources(map).Order().SequenceEqual(probed.Order()), "Video discovery and resource analysis use the same expanded references");
            issues = Aimod.AnalyzeResources(map, _ => throw new IOException("unreadable fixture"));
            Check(issues.Count(i => i.Code == "unreadableMedia") == 2, "Video probe failures remain diagnostics for every referenced file");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool cancelled = false;
            try { Aimod.AnalyzeResources(map, _ => throw new OperationCanceledException(cancellation.Token)); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Video inspection cancellation is not converted into an unreadable warning");
        }
        finally { Directory.Delete(root, true); }
    }

    private static byte[] Video()
    {
        static byte[] Chunk(string name, byte[] data)
        {
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(System.Text.Encoding.ASCII.GetBytes(name)); writer.Write(data.Length); writer.Write(data);
            if ((data.Length & 1) != 0) writer.Write((byte)0);
            return output.ToArray();
        }
        static void Int(byte[] data, int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
        byte[] main = new byte[56]; Int(main, 0, 1000000); Int(main, 16, 1); Int(main, 24, 1); Int(main, 32, 1920); Int(main, 36, 1080);
        byte[] stream = new byte[56]; "vidsDIB "u8.CopyTo(stream); Int(stream, 20, 1); Int(stream, 24, 1); Int(stream, 32, 1);
        byte[] bitmap = new byte[40]; Int(bitmap, 0, 40); Int(bitmap, 4, 1920); Int(bitmap, 8, 1080); bitmap[12] = 1; bitmap[14] = 24;
        return Chunk("RIFF", [.. "AVI "u8, .. Chunk("LIST", [.. "hdrl"u8, .. Chunk("avih", main),
            .. Chunk("LIST", [.. "strl"u8, .. Chunk("strh", stream), .. Chunk("strf", bitmap)])]), .. Chunk("LIST", [.. "movi"u8])]);
    }
    private static string Key(AimodIssue issue) => $"{issue.Category}/{issue.Severity}/{issue.Code}/{issue.TimeMs}";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
