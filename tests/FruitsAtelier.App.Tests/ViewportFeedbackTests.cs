using FruitsAtelier.Core;

internal static class ViewportFeedbackTests
{
    public static void WheelSnapSteps()
    {
        foreach (int surface in new[] { 0, 1, 2 })
        foreach (bool playing in new[] { false, true })
        {
            var ui = new Ui();
            var map = new MapDocument { DurationMs = 6000 };
            map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
            map.TimingPoints.Add(new() { TimeMs = 1120, BeatLengthMs = 400, SourceOrder = 1 });
            ui.LoadDocument(map);
            ui.View.UpdateTransport(1061, 6000, true, playing, false, null, "song.mp3"); ui.Paint();
            float x = surface == 0 ? ui.Plot.X + 20 : surface == 1 ? ui.View.ObjectTimelineBounds.X + 20 : 700;
            float y = surface == 0 ? ui.Plot.Y + 30 : surface == 1 ? ui.View.ObjectTimelineBounds.Y + 20
                : ui.Canvas.Outlines.Single(s => s.Color == 0x71849A).Bounds.Y + 20;
            foreach (var (delta, target) in new[] { (120f, 1000.0), (-120f, 1120.0), (-120f, 1220.0), (120f, 1120.0), (120f, 1000.0) })
            {
                ui.View.Wheel(x, y, delta, false); ui.Paint();
                if (Math.Abs(ui.View.PlayheadMs - target) > 1e-7) throw new Exception($"Surface {surface}: expected {target}, got {ui.View.PlayheadMs}.");
            }
            ui.SetSnapDivisor(6);
            ui.View.UpdateTransport(1320, 6000, true, playing, false, null, "song.mp3"); ui.Paint();
            ui.View.Wheel(x, y, -360, false); ui.Paint();
            if (Math.Abs(ui.View.PlayheadMs - 1520) > 1e-7) throw new Exception("Three notches must advance three sixth-beat steps.");
            double before = ui.View.PlayheadMs;
            ui.View.Wheel(ui.Plot.X + 20, ui.Plot.Y + 30, 120, true); ui.Paint();
            ui.View.Wheel(x, y, -120, false); ui.Paint();
            if (Math.Abs(ui.View.PlayheadMs - before - 400.0 / 6) > 1e-7) throw new Exception("Canvas zoom changed wheel step size.");
            if (ui.View.IsDirty || !ui.View.Document.ContentEquals(map) || ui.View.AudioPlaying != playing)
                throw new Exception("Wheel snapping changed content or playback intent.");
        }
    }

    public static void Run()
    {
        var ui = new Ui();
        ui.View.UpdateTransport(0, 60000, true, false, false, null, "song.mp3");
        ui.Paint();
        AssertPinned(ui);
        foreach (double time in new[] { 0.0, 12000, 11900, 1, 59999, 60000 })
        {
            ui.View.UpdateTransport(time, 60000, true, true, false, null, "song.mp3");
            ui.Paint();
            AssertPinned(ui);
            foreach (var size in new[] { (980f, 620f), (1440f, 900f) })
            {
                ui.Resize(size.Item1, size.Item2);
                AssertPinned(ui);
                ui.View.Wheel(ui.Plot.X + 80, ui.Plot.Y + 80, 120, true);
                ui.Paint();
                AssertPinned(ui);
            }
            ui.ClickText(FruitsAtelier.Localization.Strings.Get("ui.resetView"));
            AssertPinned(ui);
        }
        ui.View.UpdateTransport(15000, 60000, true, false, false, null, "song.mp3");
        ui.Paint();
        ui.Key(36);
        AssertPinned(ui);
        if (ui.View.PlayheadMs != 0 || ui.View.ViewStartMs >= 0)
            throw new Exception("The start needs blank past time below its fixed playhead.");
        if (ui.View.IsDirty) throw new Exception("Viewport following edited the map.");
    }

    private static void AssertPinned(Ui ui)
    {
        var plot = ui.Plot;
        var canvasPlot = ui.View.CanvasPlotBounds;
        var head = ui.Canvas.Lines.Single(l => l.Color == 0xF2C66D && l.X1 == canvasPlot.X && l.X2 == canvasPlot.Right && l.Y1 == l.Y2);
        if (Math.Abs(head.Y1 - (plot.Bottom - plot.Height * 0.25)) > 0.01)
            throw new Exception("Playback line moved away from the lower-quarter anchor.");
        var viewport = ui.Canvas.Outlines.Single(o => o.Color == 0x71849A).Bounds;
        if (viewport.X < 219.99 || viewport.Right > ui.Width - 27.99 || viewport.Width < 0)
            throw new Exception("Overview viewport extended beyond the song range.");
    }
}
