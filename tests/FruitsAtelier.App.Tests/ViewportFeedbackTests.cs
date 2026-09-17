internal static class ViewportFeedbackTests
{
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
