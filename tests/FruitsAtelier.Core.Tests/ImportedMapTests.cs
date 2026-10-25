using FruitsAtelier.Core;

internal static class ImportedMapTests
{
    public static void TimingGrid()
    {
        var doc = new MapDocument();
        doc.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, SourceOrder = 0 });
        doc.TimingPoints.Add(new() { TimeMs = 187.5, BeatLengthMs = -50, Uninherited = false, SourceOrder = 1 });
        doc.TimingPoints.Add(new() { TimeMs = 1120, BeatLengthMs = 400, Meter = 3, SourceOrder = 2 });
        var afterGreen = TimingMap.At(doc, 1000);
        Near(0, afterGreen.OffsetMs); Near(500, afterGreen.BeatLengthMs); Near(2, afterGreen.SliderVelocityMultiplier);
        Near(1120, TimingMap.Snap(doc, 1110, 4));
        Near(1120, TimingMap.Snap(doc, 1130, 4));
        Near(1320, TimingMap.Snap(doc, 1290, 6));
        Near(125, TimingMap.Snap(doc, 62.5, 4));
        var lines = TimingMap.Grid(doc, 1000, 1420, 4).ToArray();
        True(lines.Any(l => l.TimeMs == 1120 && l.IsTimingBoundary && l.IsBeat), "Red timing boundary missing.");
        True(lines.All(l => l.TimeMs != 1125), "Old tempo grid crossed the new red point.");
        True(TimingMap.Grid(doc, 0, 300, 4).All(l => l.TimeMs != 187.5), "Green timing reset beat-grid phase.");
        Near(1, TimingMap.At(doc, 1120).SliderVelocityMultiplier);
        True(TimingMap.At(doc, 1119).Meter == 4 && TimingMap.At(doc, 1120).Meter == 3, "Red point meter did not follow the active timing section.");
        doc.TimingPoints.Insert(0, new() { TimeMs = -125, BeatLengthMs = 500, SourceOrder = -1 });
        Near(-125, TimingMap.Snap(doc, -120, 6));
    }

    public static void TimingBoundaries()
    {
        var doc = new MapDocument();
        doc.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -50, Uninherited = false, SourceOrder = 0 });
        doc.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = 500, SourceOrder = 1 });
        doc.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = 250, SourceOrder = 2 });
        Near(500, TimingMap.At(doc, 1000).BeatLengthMs);
        Near(2, TimingMap.At(doc, 1000).SliderVelocityMultiplier);
        Near(500, TimingMap.At(doc, 0).BeatLengthMs);
        Near(1, TimingMap.At(doc, 0).SliderVelocityMultiplier);
        doc.TimingPoints.Add(new() { TimeMs = 1100, BeatLengthMs = double.NaN, Uninherited = false });
        True(!TimingMap.At(doc, 1100).GenerateTicks, "Inherited NaN marker was lost.");
        Near(1, TimingMap.At(doc, 1100).SliderVelocityMultiplier);
        doc.TimingPoints.Clear();
        doc.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 1e-12 });
        var dense = TimingMap.Grid(doc, 0, 1000, 6).ToArray();
        True(dense.Length is > 0 and <= 10000, "Dense timing escaped the bounded grid budget.");
        True(dense.Any(l => l.IsTimingBoundary), "Density limiting dropped the red timing boundary.");
    }

    public static void LockedStartTiming()
    {
        var doc = new MapDocument { SliderMultiplier = 5, SliderTickRate = 2 };
        doc.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        doc.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = -50, Uninherited = false });
        doc.TimingPoints.Add(new() { TimeMs = 100, BeatLengthMs = 250 });
        doc.TimingPoints.Add(new() { TimeMs = 200, BeatLengthMs = -25, Uninherited = false });
        var slider = Slider('L', 1000, 2, new(100, 100), new(100, 1100));
        doc.ImportedSliders.Add(slider);
        Near(1000, ImportedSliderConverter.DurationMs(doc, slider));
        var result = CatchStreamConverter.Convert(doc);
        Valid(result);
        Near(2, result.Sliders[0].Velocity);
        Near(1000, result.Sliders[0].DurationMs);
        var ticks = result.Objects.Where(o => o.Kind == CatchObjectKind.Droplet).ToArray();
        True(ticks.Length == 2, "Later timing points changed an already-started slider's ticks.");
        Near(250, ticks[0].TimeMs); Near(750, ticks[1].TimeMs);
        var authoring = new CurveTrack { Kind = CurveKind.Linear };
        authoring.Nodes.Add(new() { TimeMs = 0, X = 200 });
        authoring.Nodes.Add(new() { TimeMs = 1000, X = 200 });
        doc.Tracks.Add(authoring);
        var generated = CatchStreamConverter.Convert(doc, false);
        Valid(generated);
        var generatedTicks = generated.Objects.Where(o => o.SourceId == authoring.Id && o.Kind == CatchObjectKind.Droplet).ToArray();
        True(generatedTicks.Length == 3, "Authored slider changed velocity or tick timing during the span.");
        Near(250, generatedTicks[0].TimeMs); Near(750, generatedTicks[^1].TimeMs);
    }

    public static void ImportedPaths()
    {
        var doc = new MapDocument { SliderMultiplier = 5 };
        var trim = Slider('L', 100, 1, new(100, 100), new(300, 100));
        Near(100, ImportedSliderConverter.DurationMs(doc, trim));
        Near(200, ImportedSliderConverter.PositionAtTime(doc, trim, 100));
        var extend = Slider('L', 300, 1, new(100, 100), new(300, 100));
        Near(400, ImportedSliderConverter.PositionAtTime(doc, extend, 300));
        var repeatedEnd = Slider('L', 300, 1, new(100, 100), new(200, 100), new(200, 100));
        Near(100, ImportedSliderConverter.DurationMs(doc, repeatedEnd));
        Near(200, ImportedSliderConverter.PositionAtTime(doc, repeatedEnd, 300));
        var splitBezier = Slider('B', 200, 1, new(100, 100), new(200, 100), new(200, 100), new(200, 200));
        Near(200, ImportedSliderConverter.PositionAtTime(doc, splitBezier, 100));
        var collinearPerfect = Slider('P', 250, 1, new(100, 100), new(200, 100), new(50, 100));
        Near(200, ImportedSliderConverter.PositionAtTime(doc, collinearPerfect, 100));
        var semicircle = Slider('P', 0, 1, new(100, 100), new(150, 150), new(200, 100));
        Near(150, ImportedSliderConverter.PositionAtTime(doc, semicircle, ImportedSliderConverter.DurationMs(doc, semicircle) / 2), 0.001);
        var bezier = Slider('B', 0, 1, new(100, 100), new(200, 200), new(300, 100));
        Near(200, ImportedSliderConverter.PositionAtTime(doc, bezier, ImportedSliderConverter.DurationMs(doc, bezier) / 2), 0.001);
        var catmull = Slider('C', 200, 1, new(100, 100), new(200, 100), new(300, 100));
        Near(150, ImportedSliderConverter.PositionAtTime(doc, catmull, 50), 0.001);
        foreach (var item in new[] { trim, extend, repeatedEnd, splitBezier, collinearPerfect, semicircle, bezier, catmull })
            doc.ImportedSliders.Add(item);
        Valid(CatchStreamConverter.Convert(doc));
    }

    public static void RepeatEvents()
    {
        var doc = new MapDocument { SliderMultiplier = 5, SliderTickRate = 10 };
        var slider = Slider('L', 100, 3, new(100, 100), new(200, 100));
        doc.ImportedSliders.Add(slider);
        var result = CatchStreamConverter.Convert(doc);
        Valid(result);
        var fruit = result.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
        True(fruit.Length == 4, "Three spans must contain head, two repeats and tail.");
        double[] expectedX = [100, 200, 100, 200];
        for (int i = 0; i < fruit.Length; i++) { Near(i * 100, fruit[i].TimeMs); Near(expectedX[i], fruit[i].X); }
        var ticks = result.Objects.Where(o => o.Kind == CatchObjectKind.Droplet).ToArray();
        True(ticks.Length == 3, "Repeat spans lost ticks.");
        for (int i = 0; i < ticks.Length; i++) { Near(50 + i * 100, ticks[i].TimeMs); Near(150, ticks[i].X); }
        Near(125, ImportedSliderConverter.PositionAtTime(doc, slider, 175));
    }

    public static void BananaRandom()
    {
        var doc = new MapDocument();
        var shower = new BananaShower { TimeMs = 0, EndTimeMs = 200, SourceOrder = 0 };
        doc.BananaShowers.Add(shower);
        var track = new CurveTrack { Kind = CurveKind.Linear, SourceOrder = 1 };
        track.Nodes.Add(new() { TimeMs = 300, X = 256 }); track.Nodes.Add(new() { TimeMs = 1300, X = 256 });
        doc.Tracks.Add(track);
        var result = CatchStreamConverter.Convert(doc, false);
        Valid(result);
        var bananas = result.Objects.Where(o => o.Kind == CatchObjectKind.Banana).ToArray();
        True(bananas.Length == 3, "Banana endpoint generation changed.");
        double[] positions = [65.55122375488281, 482.8815612792969, 164.77008056640625];
        for (int i = 0; i < bananas.Length; i++) { Near(i * 100, bananas[i].TimeMs); Near(positions[i], bananas[i].X); }
        Near(4, result.Objects.First(o => o.Kind == CatchObjectKind.TinyDroplet).RandomOffset);
        // Snapped endpoints keep this fixture focused on float accumulation rather than one-ms alignment.
        var tailShower = new MapDocument { TimingOffsetMs = 274929, BeatLengthMs = 12595 };
        tailShower.BananaShowers.Add(new() { TimeMs = 274929, EndTimeMs = 287524 });
        True(CatchStreamConverter.Convert(tailShower).Objects.Count == 128, "Float banana accumulation was replaced with idealised interval multiplication.");
        var hyperInputs = new ConvertedCatchObject[]
        {
            new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 0, 100, 100, 100, 0),
            new(Guid.NewGuid(), 0, CatchObjectKind.Banana, 1, 500, 500, 0, 500),
            new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 500, 100, 100, 100, 0)
        };
        True(HyperDashCalculator.Calculate(hyperInputs, 5).All(s => !s.IsHyperDash), "Bananas participated in hyperdash detection.");
    }

    public static void ImportedHistory()
    {
        var doc = new MapDocument();
        doc.Fruits.Add(new() { X = 100, TimeMs = 0, SourceOrder = 3, OriginalLine = "fruit source" });
        doc.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = double.NaN, Uninherited = false, OriginalLine = "NaN source" });
        var slider = Slider('B', 100, 2, new(100, 100), new(200, 100));
        slider.OriginalLine = "slider source"; slider.SourceOrder = 4; doc.ImportedSliders.Add(slider);
        doc.BananaShowers.Add(new() { TimeMs = 500, EndTimeMs = 1000, SourceOrder = 5, OriginalLine = "spinner source" });
        var clone = doc.DeepClone();
        True(doc.ContentEquals(clone), "NaN or raw metadata broke clone equality.");
        clone.ImportedSliders[0].ControlPoints[1] = new(300, 100);
        True(!doc.ContentEquals(clone) && doc.ImportedSliders[0].ControlPoints[1].X == 200, "Imported path clone shares mutable storage.");
        var history = new EditorHistory(doc);
        history.Begin("Timing change"); history.Document.TimingPoints[0].BeatLengthMs = -50; history.Commit(); history.Undo();
        True(double.IsNaN(history.Document.TimingPoints[0].BeatLengthMs) && !history.IsDirty, "Timing undo lost NaN semantics or dirty state.");
        True(history.Document.ImportedSliders[0].Id == slider.Id && history.Document.ImportedSliders[0].OriginalLine == "slider source", "Undo changed imported identity.");
    }

    public static void RealFixtures()
    {
        string fixtureRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "beatmaps");
        foreach (var expected in new[] { (Name: "Vidro Moyou", Fruit: 1430, Bananas: 257, Sliders: 229), (Name: "Oriental Blossom", Fruit: 2181, Bananas: 162, Sliders: 613) })
        {
            string path = Directory.EnumerateFiles(fixtureRoot, "*.osu", SearchOption.AllDirectories).Single(p => p.Contains(expected.Name));
            var doc = OsuBeatmapReader.ReadFile(path);
            var result = CatchStreamConverter.Convert(doc);
            Valid(result);
            True(result.Sliders.Count == expected.Sliders, "An imported slider failed conversion.");
            True(result.Objects.Count(o => o.Kind == CatchObjectKind.Fruit) == expected.Fruit, "Head/repeat/tail fruit total differs from the source map.");
            True(result.Objects.Count(o => o.Kind == CatchObjectKind.Banana) == expected.Bananas, "Banana float timing total differs from the pinned reference rules.");
            True(result.Objects.All(o => double.IsFinite(o.X) && o.X is >= 0 and <= 512 && double.IsFinite(o.TimeMs)), "Real map generated invalid objects.");
            Console.WriteLine($"FIXTURE {expected.Name}: fruits={expected.Fruit}, droplets={result.Objects.Count(o => o.Kind == CatchObjectKind.Droplet)}, tiny={result.Objects.Count(o => o.Kind == CatchObjectKind.TinyDroplet)}, bananas={expected.Bananas}");
        }
    }

    private static ImportedSlider Slider(char type, double length, int spans, params SliderPathPoint[] points)
    {
        var slider = new ImportedSlider { X = points[0].X, Y = points[0].GeometryY, PathType = type, PixelLength = length, SpanCount = spans };
        slider.ControlPoints.AddRange(points);
        return slider;
    }
    private static void Valid(CatchConversionResult result) => True(result.Success, string.Join("; ", result.Diagnostics));
    private static void Near(double expected, double actual, double tolerance = 1e-7)
    { if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected {expected:R}, got {actual:R}."); }
    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
}
