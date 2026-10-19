using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class CanvasFeedbackPerformance
{
    internal static void Run()
    {
        var map = Fixture();
        foreach (bool osu in new[] { false, true }) RunPlacement(map, osu);
    }

    private static MapDocument Fixture()
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
        return map;
    }

    private static void RunPlacement(MapDocument map, bool osu)
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


    internal static void RunRotation()
    {
        foreach (bool osu in new[] { false, true })
        {
            var map = Fixture();
            var track = new CurveTrack { Kind = CurveKind.Bezier };
            track.Nodes.AddRange([new() { TimeMs = 65000, X = 180, HandleOut = new(150, 30) },
                new() { TimeMs = 65500, X = 260, HandleIn = new(-150, -20) }]); map.Tracks.Add(track);
            var ui = new Ui(false); ui.LoadDocument(map); if (osu) ui.View.ToggleOsuCanvas();
            ui.View.UpdateTransport(osu ? 30500 : 65000, 90000, true, false, false, null, null); ui.Key('1');
            Guid id = osu ? map.ImportedSliders[50].Id : track.Id;
            typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, [new[] { id }, id]);
            ui.Key('R', ctrl: true, shift: true); ui.ClickText(FruitsAtelier.Localization.Strings.Get("rotation.selection"));
            var bounds = ui.View.RotationBounds;
            float left = bounds.X + 24, length = bounds.Width - 162, y = bounds.Y + 97;
            long coldStart = Stopwatch.GetTimestamp();
            ui.View.PointerDown(left + length / 360, y, 0, false, false); ui.Paint();
            double cold = Stopwatch.GetElapsedTime(coldStart).TotalMilliseconds;
            var dispatch = new List<double>(); var paint = new List<double>(); var allocations = new List<long>();
            for (int i = 0; i < 100; i++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                ui.View.PointerMove(left + length * (1 + i % 8) / 360, y, false, false);
                double input = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started = Stopwatch.GetTimestamp(); ui.Paint();
                double render = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (i >= 20) { dispatch.Add(input); paint.Add(render); allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated); }
            }
            dispatch.Sort(); paint.Sort(); allocations.Sort();
            Console.WriteLine($"{(osu ? "osu" : "Catch")} rotation preview CPU: cold input+paint={cold:F2}ms; pointer p50={dispatch[40]:F3}ms p95={dispatch[76]:F3}ms; "
                + $"paint p50={paint[40]:F3}ms p95={paint[76]:F3}ms; allocation p50={allocations[40]}B (recording canvas, native GPU excluded).");
            ui.Key(27);
            if (!ui.View.Document.ContentEquals(map)) throw new Exception("Rotation benchmark cancellation changed its fixture.");
        }
    }
}
