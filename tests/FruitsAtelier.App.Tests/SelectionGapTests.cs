using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SelectionGapTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (string kind in new[] { "fruit", "slider", "track", "banana" })
            foreach (double gap in new[] { -125.25, 0, 250.125 })
            {
                L.SetLanguage(locale);
                var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,2,0,L|200:192,3,100\n");
                map.DurationMs = 10000;
                double end = 2500;
                if (kind != "slider") map.ImportedSliders.Clear();
                if (kind == "fruit") { map.Fruits.Add(new() { TimeMs = 1000, X = 100 }); end = 1000; }
                if (kind == "track")
                {
                    var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 3 };
                    track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
                    map.Tracks.Add(track);
                }
                if (kind == "banana") map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = end });
                // Two instantaneous fruits cannot overlap when ordered by their start times.
                double expected = kind == "fruit" ? Math.Abs(gap) : gap;
                map.Fruits.Add(new() { TimeMs = end + gap, X = 400 });
                var ui = new Ui(); ui.LoadDocument(map); ui.Key('A', ctrl: true);
                AssertGap();
                if (kind != "banana")
                {
                    foreach (bool reverse in new[] { false, true })
                    {
                        ui.LoadDocument(map); ui.Key('1');
                        ui.ClickMap(reverse ? end + gap : 1000, reverse ? 400 : 100);
                        Check(ui.View.SelectedObjectGapMs is null, "Single-object selection showed a gap.");
                        ui.ClickMap(reverse ? 1000 : end + gap, reverse ? 100 : 400, ctrl: true);
                        Check(ui.View.SelectedObjectIds.Count == 2, $"Ctrl-click did not select both {kind} fixture objects: {ui.View.SelectedObjectIds.Count}, reverse={reverse}, gap={gap}.");
                        AssertGap();
                    }
                }
                var conversion = ui.View.Conversion;
                var baseline = ui.View.Document.DeepClone();
                for (int i = 0; i < 5; i++) ui.Paint();
                Check(ReferenceEquals(conversion, ui.View.Conversion) && baseline.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
                    "Measurement changed content or rebuilt conversion.");
                ui.Key(46); Check(ui.View.SelectedObjectGapMs is null, "Deleted selection retained its measurement.");
                ui.Key('Z', ctrl: true); ui.Key('A', ctrl: true); AssertGap();
                ui.View.ToggleOsuCanvas(); ui.Paint();
                Check(ui.View.SelectedObjectGapMs is null && !ui.Canvas.Texts.Any(t => t.Value == L.Get("distance.selectionGap", expected)),
                    "osu canvas displayed the Catch-only gap.");
                ui.View.ToggleOsuCanvas(); ui.Key('A', ctrl: true); AssertGap();
                ui.Key(27);
                Check(ui.View.SelectedObjectGapMs is null, "Cleared selection retained its measurement.");

                void AssertGap()
                {
                    Check(ui.View.SelectedObjectGapMs is { } actual && Math.Abs(actual - expected) < .001,
                        $"Wrong {kind} interval: expected {expected}, got {ui.View.SelectedObjectGapMs}.");
                    Check(ui.View.MovementOverlayBounds is { } panel && ui.Canvas.Texts.Any(t =>
                        t.Value == L.Get("distance.selectionGap", expected) && panel.Contains(t.X, t.Y)), "Gap missing from DS Indicator.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    public static void Performance()
    {
        var map = new MapDocument { IsDemo = false, DurationMs = 120000 };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true },
            new() { TimeMs = 60000, BeatLengthMs = 400, Uninherited = true }]);
        for (int i = 0; i < 2000; i++) map.Fruits.Add(new() { TimeMs = i * 50, X = 50 + i % 400 });
        for (int i = 0; i < 100; i++)
        {
            var slider = new ImportedSlider { TimeMs = i * 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 2 };
            slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]); map.ImportedSliders.Add(slider);
        }
        map.BananaShowers.Add(new() { TimeMs = 110000, EndTimeMs = 112000 });
        var ui = new Ui(false); ui.LoadDocument(map);
        typeof(FruitsAtelier.App.Editor.EditorView).GetMethod("SelectObjects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ui.View, [new[] { map.ImportedSliders[0].Id, map.Fruits[20].Id }, Guid.Empty]);
        Measure("cold render", 1, ui.Paint);
        for (int i = 0; i < 30; i++) ui.Paint();
        var plot = ui.View.CanvasPlotBounds;
        Measure("warm pointer", 500, () => ui.View.PointerMove(plot.X + 40, plot.Y + 40, false, false));
        Measure("warm render", 500, ui.Paint);
        var conversion = ui.View.Conversion;
        Measure("warm gap lookup", 10000, () => { _ = ui.View.SelectedObjectGapMs; });
        Check(ReferenceEquals(conversion, ui.View.Conversion) && !ui.View.IsDirty, "Warm measurement invalidated content.");
        static void Measure(string name, int iterations, Action action)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) action();
            double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Console.WriteLine($"{name}: {elapsed / iterations:F4} ms/op; {allocated / iterations} bytes/op");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
