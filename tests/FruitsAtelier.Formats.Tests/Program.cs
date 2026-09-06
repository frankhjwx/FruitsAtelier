using FruitsAtelier.Core;

var tests = new (string Name, Action Run)[]
{
    ("Workspace isolation, recovery, indexing and explicit export", WorkspaceTests.Run),
    ("Multi-difficulty projects preserve content, history and compatibility", MultiProjectTests.Run),
    ("Pre-rename schema 1 projects preserve user names and editable curves", RenameCompatibilityTests.Run),
    ("Reader preserves original timing order, duplicates, samples and unknown sections", ReadOriginal),
    ("Unedited export retains all object and timing text", OriginalRoundTrip),
    ("Edited fruit preserves flags, geometry Y and sample suffix", EditFruit),
    ("Legacy fractional coordinates truncate but original spelling survives", LegacyCoordinates),
    ("Sixth beat time quantization is reported without mutating source", SixthBeat),
    ("Collapsing fractional times preserves current parent order rather than stale source order", QuantizedOrder),
    ("Generated Bezier exports as slider and reports actual reconversion", CurveExport),
    ("Authored repeats retain span count and every repeat fruit and tick after export", AuthoredRepeats),
    ("Mixed segment kinds and repeat counts survive project files and old defaults", MixedProject),
    ("Converted Legacy repeat retains identity, object count and samples as a FSlider", PromotedRepeat),
    ("Changing promoted repeat count resizes edge samples without losing head or tail", ChangedRepeatEdges),
    ("Temporary SV restores following imported slider speed", RestoreSv),
    ("Independent duration formula survives the head lookup window", SliderTimingTests.RestorationStaysOutsideHeadWindow),
    ("An isolated edited slider does not restore unused original SV", SliderTimingTests.IsolatedEditedSliderDoesNotRestoreUnusedSv),
    ("Same-time SV replaces greens while preserving red timing and effective samples", SliderTimingTests.SameTimeOverrideReplacesGreensAndPreservesRed),
    ("Natural red and green boundary is not overwritten by an SV restore", SliderTimingTests.NaturalBoundaryIsNotOverwrittenByRestore),
    ("Adjacent generated heads use one SV each and independent durations", SliderTimingTests.AdjacentGeneratedHeadsHaveOneSvEach),
    ("Shared generated SV restores only for the following imported slider", SliderTimingTests.SharedGeneratedSvRestoresOnlyForFollowingImportedSlider),
    ("A following generated head reestablishes original SV without an unused restore", SliderTimingTests.FollowingGeneratedHeadReestablishesOriginalSv),
    ("FSliders above stable's SV=10 limit are not exported", SliderTimingTests.HighSvExportIsRejected),
    ("Unsafe fractional or next-millisecond restoration is rejected atomically", SliderTimingTests.UnsafeRestorationWindowIsRejected),
    ("Fractional head restores original NaN state after the lookup window", SliderTimingTests.NaNAndFractionalHeadRestoreOriginalState),
    ("User project export durations pass an independent raw-field calculation", SliderTimingTests.UserProjectExportHasIndependentCorrectDurations),
    ("NaN inherited metadata does not cause a spurious same-time Catch SV conflict", InheritedNaN),
    ("Conflicting same-time imported slider blocks export", ConflictingSv),
    ("Quantized curve head cannot cross a BPM boundary", QuantizedBoundary),
    ("Imported sliders require promotion while banana times preserve metadata", ReadOnlyObjects),
    ("Project JSON retains curves, IDs, paths, original sections and NaN timing", ProjectRoundTrip),
    ("Project resources are relative on disk and resolve on reload", ProjectResourcePaths),
    ("Project rejects missing or unknown schema, duplicate IDs and invalid numbers", InvalidProjects),
    ("Reader rejects non-Catch, unsupported format and unknown objects", InvalidBeatmaps),
    ("Saving refuses original osu and failed serialization leaves target untouched", SafeWrites),
    ("Beatmap audio paths stay local and changed audio exports its new name", AudioReferences),
    ("Supplied real maps preserve original objects and timing on export", RealMaps),
    ("Real B/P/L and repeat sliders remain exportable after per-segment editing", RealEditableWrites)
};
int failed = 0, skipped = 0;
foreach (var (name, run) in tests)
{
    if (args.Contains("--skip-external-fixtures") && (run == (Action)RealMaps || run == (Action)RealEditableWrites))
    {
        skipped++; Console.WriteLine("SKIP " + name + " (external beatmap fixtures explicitly excluded)"); continue;
    }
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
}
Console.WriteLine($"{tests.Length - failed - skipped}/{tests.Length - skipped} format tests passed; {skipped} external-fixture tests skipped.");
return failed == 0 ? 0 : 1;

static string Fixture() => """
osu file format v14

[General]
AudioFilename: song.mp3
Mode: 2
SampleSet: Soft

[Metadata]
Title:Fixture
TitleUnicode:测试曲
Artist:Test
Version:Catch

[Difficulty]
CircleSize:5
ApproachRate:8
OverallDifficulty:7
SliderMultiplier:1.4
SliderTickRate:1

[Events]
0,0,"cover.jpg",0,0
Video,0,"unloaded.mp4"
// Original event comment

[TimingPoints]
-100,500,4,2,1,70,1,0
1000,-50,4,2,2,65,0,1
1000,400,3,1,1,80,1,1
1000,-80,3,1,3,55,0,1
5000,600,4,1,1,75,1,0

[PrivateData]
custom:value
keep,this,line

[HitObjects]
123,176,250,21,8,2:3:4:80:custom.wav
100,192,1800,6,2,B|150:100|150:100|250:192,2,280,2|4|8,2:3|3:2|1:0,2:3:4:65:edge.wav
256,192,4500,12,0,4800,1:2:3:60:spinner.wav
400,120,6000,1,4,1:2:3:50:tail.wav
""";

static string ConvertibleRepeatFixture() => Fixture().Replace(",2,280,2|4|8,", ",2,1,2|4|8,", StringComparison.Ordinal);

static void ReadOriginal()
{
    var d = OsuBeatmapReader.Read(Fixture());
    Equal("测试曲", d.Name); Check(!d.IsDemo, "Imported map must not be demo");
    Equal(5, d.TimingPoints.Count); Equal(-100d, d.TimingPoints[0].TimeMs);
    Equal(400d, TimingMap.At(d, 1000).BeatLengthMs); Near(1.25, TimingMap.At(d, 1000).SliderVelocityMultiplier);
    Equal(2, d.Fruits.Count); Equal(1, d.ImportedSliders.Count); Equal(1, d.BananaShowers.Count);
    Equal(2, d.ImportedSliders[0].SpanCount); Equal(4, d.ImportedSliders[0].ControlPoints.Count);
    Equal(d.ImportedSliders[0].ControlPoints[1], d.ImportedSliders[0].ControlPoints[2]);
    Check(d.OriginalSections.Any(s => s.Name == "PrivateData" && s.Lines.Contains("keep,this,line")), "Unknown section missing");
}

static void OriginalRoundTrip()
{
    var d = OsuBeatmapReader.Read(Fixture());
    var r = OsuBeatmapWriter.Serialize(d);
    Preserved(d, r.ReadBack);
    Check(r.Text.Contains("Video,0,\"unloaded.mp4\""), "Video reference dropped");
    Check(r.Text.Contains("[PrivateData]\r\ncustom:value\r\nkeep,this,line"), "Unknown section altered");
    Check(r.ObjectSequenceMatches, "Unedited sequence changed"); Near(0, r.MaxConvertedXError); Near(0, r.MaxConvertedTimeErrorMs);
}

static void EditFruit()
{
    var d = OsuBeatmapReader.Read(Fixture());
    d.Fruits[0].X = 222.5; d.Fruits[0].TimeMs = 333.5;
    var r = OsuBeatmapWriter.Serialize(d);
    Check(r.Text.Contains("223,176,334,21,8,2:3:4:80:custom.wav"), "Fruit sample or flags changed");
    Equal(222.5, d.Fruits[0].X); Equal(333.5, d.Fruits[0].TimeMs);
    Near(0.5, r.MaxTimeQuantizationMs); Near(0.5, r.MaxCoordinateQuantization);
}

static void LegacyCoordinates()
{
    string text = Fixture().Replace("123,176,250", "123.9,176.8,250").Replace("150:100", "150.9:100.9");
    var d = OsuBeatmapReader.Read(text);
    Equal(123d, d.Fruits[0].X); Equal(new SliderPathPoint(150, 100), d.ImportedSliders[0].ControlPoints[1]);
    var r = OsuBeatmapWriter.Serialize(d);
    Check(r.Text.Contains("123.9,176.8,250"), "Original fractional head was rewritten");
    Check(r.Text.Contains("150.9:100.9"), "Original fractional path was rewritten");
}

static void SixthBeat()
{
    var d = new MapDocument();
    double time = BeatGrid.Snap(83, 0, 500, 6);
    d.Fruits.Add(new Fruit { TimeMs = time, X = 127.5 });
    var r = OsuBeatmapWriter.Serialize(d);
    Equal(83d, r.ReadBack.Fruits.Single().TimeMs); Equal(128d, r.ReadBack.Fruits.Single().X);
    Near(1d / 3, r.MaxTimeQuantizationMs, 1e-10); Equal(time, d.Fruits.Single().TimeMs);
}

static void CurveExport()
{
    var d = new MapDocument();
    AddCurve(d, 1000, 3000);
    var before = d.DeepClone();
    var r = OsuBeatmapWriter.Serialize(d);
    Check(d.ContentEquals(before), "Export mutated authoring model");
    Equal(1, r.ReadBack.ImportedSliders.Count); Equal(0, r.ReadBack.Tracks.Count);
    Equal('L', r.ReadBack.ImportedSliders[0].PathType); Equal(1, r.ReadBack.ImportedSliders[0].SpanCount);
    Check(r.MaxCoordinateQuantization <= 0.5, "Unexpected integer error");
    Check(r.Diagnostics.Any(x => x.Contains("回读") || x.Contains("量化")), "No reconversion report");
}

static void QuantizedOrder()
{
    var d = new MapDocument();
    d.Fruits.Add(new Fruit { TimeMs = 774.1428571428571, X = 370, SourceOrder = 0 });
    d.Fruits.Add(new Fruit { TimeMs = 774, X = 378, SourceOrder = 1 });
    var r = OsuBeatmapWriter.Serialize(d);
    Check(r.ObjectSequenceMatches, "Rounding reordered parent identity");
    Equal(378d, r.ReadBack.Fruits[0].X); Equal(370d, r.ReadBack.Fruits[1].X);
    Near(0, r.MaxConvertedXError); Near(0.1428571428571, r.MaxConvertedTimeErrorMs);
}

static void AuthoredRepeats()
{
    var d = new MapDocument();
    AddCurve(d, 1000, 2000, 256, 256);
    d.Tracks[0].SpanCount = 3;
    var before = d.DeepClone();
    var result = OsuBeatmapWriter.Serialize(d, false);
    Check(d.ContentEquals(before), "Repeat export changed authoring data");
    var slider = result.ReadBack.ImportedSliders.Single();
    Equal(3, slider.SpanCount); Near(3000, ImportedSliderConverter.DurationMs(result.ReadBack, slider));
    Check(result.ObjectSequenceMatches, "Repeated event identities were lost");
    var converted = CatchStreamConverter.Convert(result.ReadBack, false);
    var fruit = converted.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
    Equal(4, fruit.Length);
    for (int i = 0; i < fruit.Length; i++) Near(1000 + i * 1000, fruit[i].TimeMs);
    Equal(3, converted.Objects.Count(o => o.Kind == CatchObjectKind.Droplet));
    Check(converted.Objects.Any(o => o.Kind == CatchObjectKind.TinyDroplet), "Repeated stream lost tiny droplets");
    Near(0, result.MaxConvertedXError); Near(0, result.MaxConvertedTimeErrorMs);
    string[] fields = slider.OriginalLine!.Split(',');
    Equal(4, fields[8].Split('|').Length); Equal(4, fields[9].Split('|').Length);
}

static void MixedProject()
{
    var d = new MapDocument();
    var track = new CurveTrack { Kind = CurveKind.Bezier, SpanCount = 4 };
    track.Nodes.Add(new Anchor { TimeMs = 1000, X = 100, OutgoingKind = CurveKind.Linear, HandleOut = new(200, 100) });
    track.Nodes.Add(new Anchor { TimeMs = 2000, X = 300, OutgoingKind = CurveKind.Bezier, HandleIn = new(-200, -100), HandleOut = new(200, -120) });
    track.Nodes.Add(new Anchor { TimeMs = 3000, X = 200, HandleIn = new(-200, 100) });
    d.Tracks.Add(track);
    InTemp(folder =>
    {
        string path = Path.Combine(folder, "mixed.catchproj");
        ProjectSerializer.WriteFile(d, path);
        var restored = ProjectSerializer.ReadFile(path);
        Check(d.ContentEquals(restored), "Mixed project lost curve fields");
        Equal(4, restored.Tracks[0].SpanCount);
        Equal(CurveKind.Linear, restored.Tracks[0].Nodes[0].OutgoingKind!.Value);
        Equal(CurveKind.Bezier, restored.Tracks[0].Nodes[1].OutgoingKind!.Value);
        Check(restored.Tracks[0].Nodes[2].OutgoingKind is null, "Inherited segment kind changed");
        Near(CurveMath.Evaluate(track, 0, 0.37).X, CurveMath.Evaluate(restored.Tracks[0], 0, 0.37).X);
        Near(CurveMath.Evaluate(track, 1, 0.37).X, CurveMath.Evaluate(restored.Tracks[0], 1, 0.37).X);
    });
    string json = ProjectSerializer.Serialize(d);
    Throws(() => ProjectSerializer.Read(json.Replace("\"SpanCount\": 4", "\"SpanCount\": 0")), "invalid authored span count");
    Throws(() => ProjectSerializer.Read(json.Replace("\"OutgoingKind\": 1", "\"OutgoingKind\": 99")), "undefined segment kind");
    var legacy = System.Text.Json.Nodes.JsonNode.Parse(json)!;
    var legacyTrack = legacy["Document"]!["Tracks"]![0]!.AsObject();
    legacyTrack.Remove("SpanCount");
    legacyTrack.Remove("OriginalLine");
    legacyTrack.Remove("CompensateTinyDroplets");
    foreach (var node in legacyTrack["Nodes"]!.AsArray()) node!.AsObject().Remove("OutgoingKind");
    var restoredLegacy = ProjectSerializer.Read(legacy.ToJsonString());
    Equal(1, restoredLegacy.Tracks.Single().SpanCount);
    Check(restoredLegacy.Tracks[0].Nodes.All(n => n.OutgoingKind is null), "Old project must inherit whole-track kind");
}

static void PromotedRepeat()
{
    var d = OsuBeatmapReader.Read(ConvertibleRepeatFixture());
    var original = d.ImportedSliders.Single();
    Guid originalId = original.Id;
    int originalOrder = original.SourceOrder;
    string[] originalFields = original.OriginalLine!.Split(',');
    var edit = ImportedSliderEditing.ConvertToTrack(d, original.Id);
    Equal(originalId, edit.Track.Id); Equal(originalOrder, edit.Track.SourceOrder); Equal(2, edit.Track.SpanCount);
    Equal(0, d.ImportedSliders.Count); Equal(1, d.Tracks.Count);
    edit.Track.Name = "Edited repeat";
    foreach (var node in edit.Track.Nodes) node.X += 3;
    var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(d));
    Check(d.ContentEquals(restored), "Editable imported repeat did not persist");
    Equal(true, restored.Tracks.Single().CompensateTinyDroplets!.Value);
    var result = OsuBeatmapWriter.Serialize(restored, false);
    Equal(1, result.ReadBack.ImportedSliders.Count); Equal(2, result.ReadBack.ImportedSliders[0].SpanCount);
    Equal(original.X + 3, result.ReadBack.ImportedSliders[0].X);
    Equal(d.Fruits.Count, result.ReadBack.Fruits.Count); Equal(d.BananaShowers.Count, result.ReadBack.BananaShowers.Count);
    Check(result.ObjectSequenceMatches, "Promotion/export dropped or reordered repeat objects");
    string[] fields = result.ReadBack.ImportedSliders[0].OriginalLine!.Split(',');
    foreach (int column in new[] { 3, 4, 8, 9, 10 }) Equal(originalFields[column], fields[column]);
}

static void ChangedRepeatEdges()
{
    var d = OsuBeatmapReader.Read(ConvertibleRepeatFixture());
    var edit = ImportedSliderEditing.ConvertToTrack(d, d.ImportedSliders.Single().Id);
    edit.Track.SpanCount = 3;
    var result = OsuBeatmapWriter.Serialize(d, false);
    var slider = result.ReadBack.ImportedSliders.Single();
    Equal(3, slider.SpanCount);
    string[] fields = slider.OriginalLine!.Split(',');
    Equal("2|4|0|8", fields[8]); Equal("2:3|3:2|0:0|1:0", fields[9]); Equal("2:3:4:65:edge.wav", fields[10]);
    Check(result.ObjectSequenceMatches, "Repeat-count change lost generated events on export");
    Check(result.Diagnostics.Any(message => message.Contains("边缘样本")), "Changed repeat samples lack a diagnostic");
}

static void RestoreSv()
{
    var d = OsuBeatmapReader.Read(Fixture());
    AddCurve(d, 1200, 1500, 20, 480);
    double oldDuration = ImportedSliderConverter.DurationMs(d, d.ImportedSliders.Single());
    var r = OsuBeatmapWriter.Serialize(d);
    var old = r.ReadBack.ImportedSliders.Single(s => s.TimeMs == 1800);
    Near(oldDuration, ImportedSliderConverter.DurationMs(r.ReadBack, old));
    Near(TimingMap.At(d, 1800).SliderVelocityMultiplier, TimingMap.At(r.ReadBack, 1800).SliderVelocityMultiplier);
    var originals = d.TimingPoints.Select(t => t.OriginalLine).ToArray();
    var retained = r.ReadBack.TimingPoints.Select(t => t.OriginalLine).Where(l => originals.Contains(l)).ToArray();
    Check(originals.SequenceEqual(retained), "Original timing order changed");
}

static void ConflictingSv()
{
    var d = OsuBeatmapReader.Read(Fixture());
    AddCurve(d, 1800, 1950, 10, 500);
    Throws(() => OsuBeatmapWriter.Serialize(d), "same-time SV conflict");
}

static void InheritedNaN()
{
    var d = OsuBeatmapReader.Read(Fixture().Replace("1000,-80,", "1000,NaN,"));
    AddCurve(d, 1800, 2800, 200, 200);
    var r = OsuBeatmapWriter.Serialize(d, false);
    Check(r.ReadBack.TimingPoints.Select(t => t.OriginalLine).SequenceEqual(d.TimingPoints.Select(t => t.OriginalLine)), "Unnecessary SV override replaced NaN metadata");
    Check(r.ObjectSequenceMatches, "NaN Catch event sequence changed");
}

static void QuantizedBoundary()
{
    var d = new MapDocument();
    d.TimingPoints.Add(new TimingPoint { TimeMs = 0, BeatLengthMs = 500 });
    d.TimingPoints.Add(new TimingPoint { TimeMs = 1000, BeatLengthMs = 400 });
    AddCurve(d, 999.8, 1800);
    Throws(() => OsuBeatmapWriter.Serialize(d), "rounded head crossing timing");
}

static void ReadOnlyObjects()
{
    var d = OsuBeatmapReader.Read(Fixture()); d.ImportedSliders[0].PixelLength++;
    Throws(() => OsuBeatmapWriter.Serialize(d), "modified raw slider");
    d = OsuBeatmapReader.Read(Fixture());
    d.BananaShowers[0].TimeMs += 100; d.BananaShowers[0].EndTimeMs += 250;
    var written = OsuBeatmapWriter.Serialize(d);
    var shower = written.ReadBack.BananaShowers.Single();
    Near(4600, shower.TimeMs); Near(5050, shower.EndTimeMs);
    Check(written.Text.Contains("256,192,4600,12,0,5050,1:2:3:60:spinner.wav"),
        "Editing banana times discarded original flags or sample metadata");
}

static void ProjectRoundTrip()
{
    var d = OsuBeatmapReader.Read(Fixture()); AddCurve(d, 7000, 9000);
    d.TimingPoints.Add(new TimingPoint { TimeMs = 10000, BeatLengthMs = double.NaN, Uninherited = false });
    var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(d));
    Check(d.ContentEquals(restored), "Project did not round-trip all authoring fields");
    Check(!ReferenceEquals(d.Tracks[0].Nodes, restored.Tracks[0].Nodes), "Lists alias original model");
    Equal(d.Tracks[0].Nodes[0].HandleOut, restored.Tracks[0].Nodes[0].HandleOut);
    Check(double.IsNaN(restored.TimingPoints[^1].BeatLengthMs), "Inherited NaN metadata lost");
}

static void ProjectResourcePaths()
{
    InTemp(folder =>
    {
        string map = Path.Combine(folder, "input.osu"), project = Path.Combine(folder, "work.catchproj");
        var d = OsuBeatmapReader.Read(Fixture(), map);
        ProjectSerializer.WriteFile(d, project);
        var restored = ProjectSerializer.ReadFile(project);
        Check(d.ContentEquals(restored), "Relative resources changed effective paths");
        Check(!File.ReadAllText(project).Contains(folder.Replace("\\", "\\\\")), "Absolute workspace leaked into portable project");
    });
}

static void InvalidProjects()
{
    Throws(() => ProjectSerializer.Read("{}"), "missing schema");
    Throws(() => ProjectSerializer.Read("{\"SchemaVersion\":2,\"Document\":{}}"), "unknown schema");
    var d = new MapDocument(); d.Fruits.Add(new Fruit { X = 100, TimeMs = 100 });
    d.Fruits.Add(new Fruit { Id = d.Fruits[0].Id, X = 200, TimeMs = 200 });
    Throws(() => ProjectSerializer.Serialize(d), "duplicate IDs");
    d.Fruits.RemoveAt(1); d.Fruits[0].X = double.NaN;
    Throws(() => ProjectSerializer.Serialize(d), "non-finite fruit");
    Throws(() => ProjectSerializer.Read(ProjectSerializer.Serialize(new MapDocument()).Replace("\"CircleSize\": 5", "\"CircleSize\": 11")), "invalid CS on input");
}

static void InvalidBeatmaps()
{
    Throws(() => OsuBeatmapReader.Read(Fixture().Replace("Mode: 2", "Mode: 0")), "non-Catch input");
    Throws(() => OsuBeatmapReader.Read(Fixture().Replace("format v14", "format v13")), "unsupported version");
    Throws(() => OsuBeatmapReader.Read(Fixture().Replace("250,21,8", "250,128,8")), "unknown object");
    Throws(() => OsuBeatmapReader.Read(Fixture().Replace("-100,500,4", "-100,NaN,4")), "NaN red timing");
}

static void SafeWrites()
{
    InTemp(folder =>
    {
        string source = Path.Combine(folder, "source.osu"), target = Path.Combine(folder, "target.catchproj");
        File.WriteAllText(source, Fixture()); File.WriteAllText(target, "sentinel");
        var d = OsuBeatmapReader.ReadFile(source);
        Throws(() => OsuBeatmapWriter.WriteFile(d, source), "original file protection");
        Throws(() => ProjectSerializer.WriteFile(d, source), "project cannot replace original osu");
        Equal(Fixture(), File.ReadAllText(source));
        d.Fruits[0].TimeMs = double.NaN;
        Throws(() => ProjectSerializer.WriteFile(d, target), "invalid document atomic save");
        Equal("sentinel", File.ReadAllText(target));
    });
}

static void RealMaps()
{
    string root = FindRoot();
    string folder = Path.Combine(root, "artifacts", "beatmaps");
    string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.osu", SearchOption.AllDirectories)
        .Where(f => Path.GetFileName(f).Contains("Vidro Moyou") || Path.GetFileName(f).Contains("Oriental Blossom")).ToArray() : [];
    if (files.Length < 2) throw new Exception("Both supplied real-map fixtures must be extracted under artifacts/beatmaps.");
    foreach (string file in files)
    {
        var d = OsuBeatmapReader.ReadFile(file);
        var r = OsuBeatmapWriter.Serialize(d);
        Preserved(d, r.ReadBack); Check(r.ObjectSequenceMatches, "Real-map output sequence changed");
        Near(0, r.MaxConvertedXError); Near(0, r.MaxConvertedTimeErrorMs);
        Check(d.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(d))), "Real-map project lost fields");
        Console.WriteLine($"  REAL {Path.GetFileName(file)}: {d.Fruits.Count} fruit, {d.ImportedSliders.Count} sliders, {d.BananaShowers.Count} showers, {d.TimingPoints.Count} timing");
    }
}

static void AudioReferences()
{
    foreach (string name in new[] { "../outside.mp3", "C:\\outside.mp3", "\\\\server\\audio.mp3", "/audio.mp3" })
        Throws(() => OsuBeatmapReader.Read(Fixture().Replace("song.mp3", name)), "unsafe audio path");
    InTemp(folder =>
    {
        var d = OsuBeatmapReader.Read(Fixture().Replace("TitleUnicode:测试曲", "TitleUnicode:"), Path.Combine(folder, "source.osu"));
        Equal("Fixture", d.Name);
        d.AudioPath = Path.Combine(folder, "replacement.mp3");
        Check(OsuBeatmapWriter.Serialize(d).Text.Contains("AudioFilename:replacement.mp3"), "Changed audio path did not export");
        d.AudioPath = "\\\\server\\audio.mp3";
        Throws(() => ProjectSerializer.Serialize(d), "UNC project resource");
    });
}

static void RealEditableWrites()
{
    string fixtureRoot = Path.Combine(FindRoot(), "artifacts", "beatmaps");
    foreach (string name in new[] { "Vidro Moyou", "Oriental Blossom" })
    {
        string path = Directory.EnumerateFiles(fixtureRoot, "*.osu", SearchOption.AllDirectories).Single(p => p.Contains(name));
        var original = OsuBeatmapReader.ReadFile(path);
        var candidates = original.ImportedSliders.GroupBy(s => (s.PathType, Repeats: s.SpanCount > 1))
            .Select(group => group.OrderByDescending(s => s.ControlPoints.Count).First()).ToArray();
        Check(candidates.Any(s => s.PathType is 'B' or 'P'), "Non-linear real fixture missing");
        if (name == "Oriental Blossom") Check(candidates.Any(s => s.SpanCount > 1), "Real repeat fixture missing");
        foreach (var source in candidates)
        {
            var document = original.DeepClone();
            var promotion = ImportedSliderEditing.ConvertToTrack(document, source.Id);
            var first = promotion.Track.Nodes[0];
            first.OutgoingKind = CurveKind.Bezier;
            Check(CurveMath.SegmentKind(promotion.Track, 0) == CurveKind.Bezier, "Segment edit did not take effect");
            InTemp(folder =>
            {
                string projectPath = Path.Combine(folder, "edited.catchproj");
                ProjectSerializer.WriteFile(document, projectPath);
                var restored = ProjectSerializer.ReadFile(projectPath);
                Check(document.ContentEquals(restored), "Real editable project lost curve, span or metadata");
                var result = OsuBeatmapWriter.Serialize(restored);
                Check(result.ObjectSequenceMatches, $"{name} {source.PathType}/{source.SpanCount}: edited real slider lost object sequence: {string.Join("; ", result.Diagnostics)}");
                Equal(original.ImportedSliders.Count, result.ReadBack.ImportedSliders.Count);
                Check(double.IsFinite(result.MaxConvertedXError) && double.IsFinite(result.MaxConvertedTimeErrorMs), "Real export lacks measured errors");
                Console.WriteLine($"  EDIT-EXPORT {name} {source.PathType} spans={source.SpanCount} sourceControls={source.ControlPoints.Count} anchors={promotion.Track.Nodes.Count} edit=segment0Bezier X={result.MaxConvertedXError:R} timeMs={result.MaxConvertedTimeErrorMs:R}");
            });
        }
    }
}

static void AddCurve(MapDocument d, double start, double end, double x1 = 100, double x2 = 350)
{
    var t = new CurveTrack { Name = "Fixture Bezier", Kind = CurveKind.Bezier };
    double duration = end - start;
    t.Nodes.Add(new Anchor { TimeMs = start, X = x1, HandleOut = new(duration / 3, (x2 - x1) / 3) });
    t.Nodes.Add(new Anchor { TimeMs = end, X = x2, HandleIn = new(-duration / 3, -(x2 - x1) / 3) });
    d.Tracks.Add(t); d.DurationMs = Math.Max(d.DurationMs, end + 1000);
}

static void Preserved(MapDocument a, MapDocument b)
{
    Check(a.TimingPoints.Select(t => t.OriginalLine).SequenceEqual(b.TimingPoints.Select(t => t.OriginalLine)), "Timing lines changed");
    Check(a.Fruits.Select(t => t.OriginalLine).SequenceEqual(b.Fruits.Select(t => t.OriginalLine)), "Fruit lines changed");
    Check(a.ImportedSliders.Select(t => t.OriginalLine).SequenceEqual(b.ImportedSliders.Select(t => t.OriginalLine)), "Slider lines changed");
    Check(a.BananaShowers.Select(t => t.OriginalLine).SequenceEqual(b.BananaShowers.Select(t => t.OriginalLine)), "Spinner lines changed");
}

static void InTemp(Action<string> action)
{
    string folder = Path.Combine(FindRoot(), "artifacts", "format-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder); action(folder);
}
static string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "global.json"))) return d.FullName;
    throw new Exception("Workspace root not found");
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
static void Near(double expected, double actual, double tolerance = 1e-8) { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected}; got {actual}"); }
static void Throws(Action action, string label)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { return; }
    throw new Exception("Expected rejection: " + label);
}
