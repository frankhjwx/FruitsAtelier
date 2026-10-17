using FruitsAtelier.Core;

internal static class OsuCanvasRenderTests
{
    internal static MapDocument Fixture()
    {
        var map = new MapDocument { DurationMs = 10000, BeatLengthMs = 500, SliderMultiplier = 1.4, ApproachRate = 5, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 1000, X = 80, Y = 64 });
        map.Fruits.Add(new() { TimeMs = 1100, X = 80, Y = 64 });
        foreach (var (kind, x, y) in new[] { ('L', 220d, 80d), ('B', 360d, 100d), ('P', 80d, 260d), ('C', 300d, 260d) })
        {
            var slider = new ImportedSlider { X = x, Y = y, TimeMs = 2000, PathType = kind, PixelLength = 160, SpanCount = 3 };
            slider.ControlPoints.AddRange([new(x, y), new(x + 60, y - 55), new(x + 120, y + 10)]);
            map.ImportedSliders.Add(slider);
        }
        map.BananaShowers.Add(new() { TimeMs = 5000, EndTimeMs = 6500 });
        return map;
    }
    public static void Run()
    {
        OsuMappingPresentationTests.Run();
        StableGridAndControlMarkers();
        QuantizedTrackGeometry();
        var collapsed = new MapDocument { DurationMs = 10000, IsDemo = false };
        var collapsedSlider = new ImportedSlider { X = 100, Y = 96, TimeMs = 1000, PixelLength = 140 };
        collapsedSlider.ControlPoints.AddRange([new(100, 96), new(100, 96)]);
        collapsed.ImportedSliders.Add(collapsedSlider);
        var zeroUi = new Ui(); zeroUi.LoadDocument(collapsed); zeroUi.View.ToggleOsuCanvas();
        zeroUi.View.UpdateTransport(1800, 10000, true, false, false, null, null); zeroUi.Paint();
        var zeroHead = zeroUi.View.OsuCanvasScreen(100, 96);
        if (zeroUi.View.OsuCanvasHit(zeroHead.X, zeroHead.Y) == collapsedSlider.Id)
            throw new Exception("Zero-distance slider used its declared length as its visible duration.");
        var map = Fixture(); var ui = new Ui(); ui.LoadDocument(map);
        var original = ui.View.Document.DeepClone();
        ui.Key(9); ui.View.KeyUp(9);
        if (!ui.View.IsOsuCanvas) throw new Exception("Tab did not enter the standard canvas.");
        At(900);
        var at = ui.View.OsuCanvasScreen(80, 64);
        if (ui.View.OsuCanvasHit(at.X, at.Y) != map.Fruits[0].Id) throw new Exception("Standard hit testing did not retain source identity and chronological order.");
        At(1400);
        if (!ui.Canvas.Circles.Any(c => c.Color == 0xFFFFFF && c.Opacity > 0 && c.Opacity < 1 && Math.Abs(c.X - at.X) < 5))
            throw new Exception("Past standard circles must fade to white.");
        At(1900);
        if (ui.View.OsuCanvasHit(at.X, at.Y) == map.Fruits[0].Id) throw new Exception("Circle remained pickable after its 700ms disappearance.");
        At(2500);
        if (!ui.Canvas.Lines.Any(l => l.Width > 10)) throw new Exception("Legacy slider bodies were not drawn.");
        At(5700);
        if (!ui.Canvas.Circles.Any(c => c.Radius > 70)) throw new Exception("Spinner visual was not drawn.");
        if (!original.ContentEquals(ui.View.Document) || ui.View.IsDirty) throw new Exception("Rendering or seeking changed beatmap content.");
        var stacked = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, ApproachRate = 5, IsDemo = false };
        stacked.Fruits.Add(new() { TimeMs = 500, X = 80, Y = 80 });
        var first = new ImportedSlider { X = 80, Y = 80, TimeMs = 600, PathType = 'L', PixelLength = 120 };
        first.ControlPoints.AddRange([new(80, 80), new(200, 80)]); stacked.ImportedSliders.Add(first);
        var second = new ImportedSlider { X = 200, Y = 80, TimeMs = 700, PathType = 'L', PixelLength = 120 };
        second.ControlPoints.AddRange([new(200, 80), new(320, 80)]); stacked.ImportedSliders.Add(second);
        ui.LoadDocument(stacked); At(400);
        double offset = (54.4 - 4.48 * stacked.CircleSize) / 10;
        CheckCircle(80 - 2 * offset, 80 - 2 * offset, "Slider-chain stacking did not propagate through preceding circle.");
        stacked.ImportedSliders.Remove(second); stacked.Fruits.Clear();
        stacked.Fruits.Add(new() { TimeMs = 1100, X = 200, Y = 80 });
        ui.LoadDocument(stacked); At(900);
        CheckCircle(200 + offset, 80 + offset, "Circle under slider tail did not receive negative stacking.");
        var lifecycle = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, BeatLengthMs = 500, IsDemo = false };
        var lifecycleSlider = new ImportedSlider { X = 100, Y = 96, TimeMs = 1000, PathType = 'L', PixelLength = 140, SpanCount = 2 };
        lifecycleSlider.ControlPoints.AddRange([new(100, 96), new(240, 96)]); lifecycle.ImportedSliders.Add(lifecycleSlider);
        ui.LoadDocument(lifecycle); At(1100);
        CheckCircle(100, 96, "Slider head disappeared immediately after its start.");
        var lifecycleHead = ui.View.OsuCanvasScreen(100, 96);
        if (!ui.Canvas.Circles.Any(c => c.Filled && c.Color == 0xFFFFFF && c.Opacity is > 0 and < 1 && Math.Abs(c.X - lifecycleHead.X) < .05))
            throw new Exception("Slider head did not use its own white 700ms fade.");
        At(2260);
        if (ui.Canvas.Lines.Any(l => l.Width > 10 && l.Opacity > 0)) throw new Exception("Slider body remained beyond its 240ms fade.");
        CheckCircle(100, 96, "Even-repeat slider tail did not remain at its final head-side endpoint during its own 700ms fade.");
        At(2701);
        if (ui.Canvas.Circles.Any(c => c.Filled && c.Opacity > 0 && Math.Abs(c.X - lifecycleHead.X) < .05 && Math.Abs(c.Y - lifecycleHead.Y) < .05))
            throw new Exception("Slider tail remained beyond its independent 700ms fade.");
        foreach (double length in new[] { 142d, 144d })
        {
            var tickMap = new MapDocument { DurationMs = 10000, SliderMultiplier = 1.4, BeatLengthMs = 500, IsDemo = false };
            var tickSlider = new ImportedSlider { X = 100, Y = 96, TimeMs = 1000, PathType = 'L', PixelLength = length };
            tickSlider.ControlPoints.AddRange([new(100, 96), new(300, 96)]); tickMap.ImportedSliders.Add(tickSlider);
            ui.LoadDocument(tickMap); At(900);
            var tickAt = ui.View.OsuCanvasScreen(240, 96);
            float tickRadius = 3 * ui.View.OsuCanvasBounds.Width / 512;
            bool visibleTick = ui.Canvas.Circles.Any(c => c.Filled && c.Opacity > 0 && Math.Abs(c.Radius - tickRadius) < .01
                && Math.Abs(c.X - tickAt.X) < .05 && Math.Abs(c.Y - tickAt.Y) < .05);
            if (visibleTick != (length == 144)) throw new Exception("Slider tick visibility did not retain the legacy 10ms tail exclusion.");
        }
        var dense = new MapDocument { DurationMs = 120000, IsDemo = false };
        for (int i = 0; i < 10000; i++) dense.Fruits.Add(new() { TimeMs = i * 10, X = 32 + i % 8 * 64, Y = 32 + i % 6 * 64 });
        ui.LoadDocument(dense); At(50000);
        for (int i = 0; i < 10; i++) ui.Paint();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 120; i++) ui.Paint();
        clock.Stop();
        Console.WriteLine($"Standard canvas 10000 circles warm CPU: {clock.Elapsed.TotalMilliseconds / 120:F3}ms/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 120} bytes/frame (recording canvas; native GPU excluded).");
        var pointer = ui.View.OsuCanvasScreen(256, 192);
        allocated = GC.GetAllocatedBytesForCurrentThread(); clock.Restart();
        for (int i = 0; i < 1000; i++) ui.View.PointerMove(pointer.X + i % 16, pointer.Y, false, false);
        clock.Stop();
        Console.WriteLine($"Standard canvas 10000 circles pointer CPU: {clock.Elapsed.TotalMilliseconds / 1000:F4}ms/dispatch, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1000} bytes/dispatch.");
        allocated = GC.GetAllocatedBytesForCurrentThread(); clock.Restart();
        for (int i = 0; i < 120; i++) { ui.View.PointerMove(pointer.X + i % 16, pointer.Y, false, false); ui.Paint(); }
        clock.Stop();
        Console.WriteLine($"Standard canvas 10000 circles pointer+paint CPU: {clock.Elapsed.TotalMilliseconds / 120:F3}ms/frame, {(GC.GetAllocatedBytesForCurrentThread() - allocated) / 120} bytes/frame (recording canvas; native GPU excluded).");
        void At(double time)
        {
            ui.View.UpdateTransport(time, 10000, true, false, false, null, null);
            ui.View.UpdateTransport(time, 10000, true, false, false, null, null);
            ui.Paint();
        }
        void CheckCircle(double x, double y, string message)
        {
            var p = ui.View.OsuCanvasScreen(x, y);
            if (!ui.Canvas.Circles.Any(c => c.Filled && Math.Abs(c.X - p.X) < .05 && Math.Abs(c.Y - p.Y) < .05)) throw new Exception(message);
        }
    }

    private static void QuantizedTrackGeometry()
    {
        var map = new MapDocument { DurationMs = 10000, IsDemo = false };
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 1000, X = 100.1 }, new() { TimeMs = 2000, X = 300.9 }]);
        map.Tracks.Add(track);
        var ui = new Ui(); ui.LoadDocument(map); ui.View.ToggleOsuCanvas(); ui.Paint();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var output = (OsuWriteResult)typeof(FruitsAtelier.App.Editor.EditorView).GetField("playableExport", flags)!.GetValue(ui.View)!;
        var expected = ImportedSliderConverter.Path(output.ReadBack.ImportedSliders.Single());
        var items = (Array)typeof(FruitsAtelier.App.Editor.EditorView).GetField("osuCanvasObjects", flags)!.GetValue(ui.View)!;
        var item = items.GetValue(0)!;
        var actual = (SliderPathPoint[])item.GetType().GetField("Path")!.GetValue(item)!;
        if (!actual.SequenceEqual(expected)) throw new Exception("Readonly FSlider canvas did not use exported integer geometry.");
    }

    private static void StableGridAndControlMarkers()
    {
        var map = new MapDocument { DurationMs = 10000, IsDemo = false };
        var slider = new ImportedSlider { X = 64, Y = 64, TimeMs = 1000, PathType = 'B', PixelLength = 180 };
        slider.ControlPoints.AddRange([new(64, 64), new(128, 64), new(128, 64), new(192, 160)]); map.ImportedSliders.Add(slider);
        var ui = new Ui(); ui.LoadDocument(map); ui.View.ToggleOsuCanvas();
        ui.View.UpdateTransport(900, 10000, true, false, false, null, null); ui.Paint();
        ui.Key('1', ctrl: true); ui.Key('T');
        var head = ui.View.OsuCanvasScreen(64, 64); ui.Click(head.X, head.Y);
        var before = ui.View.Document.DeepClone();
        CheckMarker(64, 64, 0xB8B8B8); CheckMarker(128, 64, 0xE00000);
        if (!ui.Canvas.Lines.Any(l => l.Color == 0xFFFFFF && l.Width == .5f && l.Opacity == .9f)) throw new Exception("Stable control polygon must use thin white lines.");
        CheckGrid();
        ui.View.SetModifiers(false, true); ui.Paint();
        CheckGrid();
        ui.View.SetModifiers(false, false);
        var center = ui.View.OsuCanvasScreen(256, 192); ui.View.ZoomOsuCanvasAt(center.X, center.Y, 2); ui.Paint();
        CheckGrid(); CheckMarker(128, 64, 0xE00000);
        if (!before.ContentEquals(ui.View.Document) || ui.View.IsDirty) throw new Exception("Grid or control presentation edited map content.");
        void CheckMarker(double x, double y, uint colour)
        {
            var at = ui.View.OsuCanvasScreen(x, y);
            if (!ui.Canvas.Fills.Any(f => f.Color == colour && f.Bounds.Width == 4 && f.Bounds.Height == 4
                && Math.Abs(f.Bounds.X - at.X + 2) < .05 && Math.Abs(f.Bounds.Y - at.Y + 2) < .05))
                throw new Exception("Standard control markers must retain small gray squares and red doubled-point separators.");
        }
        IEnumerable<RecordingCanvas.Segment> GridLines() => ui.Canvas.Lines.Where(l => l.Color == 0xFFFFFF && l.Width == .5f && l.Opacity == .15f);
        void CheckGrid()
        {
            var lines = GridLines().Where(l => l.X1 == l.X2).OrderBy(l => l.X1).ToArray();
            float expected = 4 * ui.View.OsuCanvasBounds.Width / 512;
            if (lines.Length < 2 || Math.Abs(lines[1].X1 - lines[0].X1 - expected) > .05)
                throw new Exception("Standard grid did not follow the selected four-pixel logical lattice and native zoom.");
        }
    }
}
