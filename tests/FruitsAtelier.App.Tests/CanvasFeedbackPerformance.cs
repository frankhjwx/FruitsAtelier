using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class CanvasFeedbackPerformance
{
    internal static void Run()
    {
        var map = new MapDocument { DurationMs = 90000, BeatLengthMs = 500 };
        map.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 20000, BeatLengthMs = 400 },
            new() { TimeMs = 40000, BeatLengthMs = -50, Uninherited = false }]);
        for (int i = 0; i < 3000; i++) map.Fruits.Add(new() { TimeMs = 200 + i * 20, X = 40 + i * 73 % 430, Y = 30 + i * 53 % 320 });
        for (int i = 0; i < 100; i++)
        {
            var slider = new ImportedSlider { TimeMs = 205 + i * 600, X = 100, Y = 100, PixelLength = 100, SpanCount = 2 };
            slider.ControlPoints.AddRange([new(100, 100), new(140, 160), new(200, 100)]); map.ImportedSliders.Add(slider);
        }
        map.BananaShowers.AddRange([new() { TimeMs = 70000, EndTimeMs = 71000 }, new() { TimeMs = 80000, EndTimeMs = 81000 }]);
        foreach (bool osu in new[] { false, true })
        {
            var ui = new Ui(false); ui.LoadDocument(map);
            if (osu) ui.View.ToggleOsuCanvas();
            typeof(EditorView).GetProperty("movementAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ui.View, true);
            ui.View.UpdateTransport(30500, 90000, true, false, false, null, null); ui.Key('2');
            var pointer = new List<double>(); var paint = new List<double>(); var allocated = new List<long>();
            for (int i = 0; i < 100; i++)
            {
                var p = osu ? ui.View.OsuCanvasScreen(180 + i % 140, 120 + i % 100) : (X: ui.View.PlayfieldBounds.X + (180 + i % 140) / 512f * ui.View.PlayfieldBounds.Width, Y: ui.View.CanvasPlotBounds.Bottom - (float)((30500 + i % 8 * 125 - ui.View.ViewStartMs) * ui.View.PixelsPerMs));
                long before = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                ui.View.PointerMove(p.X, p.Y, false, false);
                double dispatch = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started = Stopwatch.GetTimestamp(); ui.Paint();
                double render = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (i >= 20) { pointer.Add(dispatch); paint.Add(render); allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before); }
            }
            pointer.Sort(); paint.Sort(); allocated.Sort();
            Console.WriteLine($"{(osu ? "osu" : "Catch")} movement placement CPU: pointer p50={pointer[40]:F4}ms p95={pointer[76]:F4}ms; "
                + $"paint p50={paint[40]:F3}ms p95={paint[76]:F3}ms; allocation p50={allocated[40]}B (recording canvas, native GPU excluded).");
            if (!ui.View.Document.ContentEquals(map)) throw new Exception("Movement placement benchmark edited its fixture.");
        }
    }
}
