using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class DifficultyCurveSidebarTests
{
    public static void LayoutAndSeeking()
    {
        var ui = new Ui(overview: false);
        var map = new MapDocument { DurationMs = 10000, CircleSize = 5 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 80 }, new Fruit { TimeMs = 1500, X = 430 }]);
        ui.LoadDocument(map);
        ui.Resize(1440, 800);
        var baseline = ui.View.Document.DeepClone();
        var toggle = ui.View.DifficultyCurveToggleBounds;
        ui.View.PointerMove(toggle.X + 10, toggle.Y + 10, false, false); ui.Paint();
        Check(ui.Canvas.Fills.Any(fill => fill.Bounds == toggle && fill.Color == 0x3D495A)
            && ui.Canvas.Outlines.Any(line => line.Bounds == toggle && line.Color == 0x71849A),
            "Curve toggle uses the same hover highlight as the preview toggle");
        ui.Click(toggle.X + 10, toggle.Y + 10);
        Check(ui.View.DifficultyCurveVisible, "Curve panel opens at the far left");
        toggle = ui.View.DifficultyCurveToggleBounds;
        ui.View.PointerMove(toggle.X + 10, toggle.Y + 10, false, false); ui.Paint();
        Check(ui.Canvas.Fills.Any(fill => fill.Bounds == toggle && fill.Color == 0x3D495A),
            "Open curve toggle retains the hover highlight");
        ui.OpenPreview();
        var panel = ui.View.DifficultyCurvePanelBounds;
        Check(panel.X == 0 && panel.Y == 84 && panel.Height == ui.View.PreviewResizeBounds.Bottom - panel.Y
            && panel.Width == 280 && ui.View.DifficultyCurveToggleBounds.Right < ui.View.ToolButtonBounds[0].X,
            "Fixed-width curve panel matches preview height and sits left of the four tool buttons");
        Check(ui.View.CanvasPlotBounds.Width >= EditorView.MinimumPlayfieldWidth,
            "Both panels leave the canvas playfield usable");
        var graph = ui.View.DifficultyCurveGraphBounds;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (ui.View.CurrentStarRatingRefreshing && DateTime.UtcNow < deadline)
        { Thread.Sleep(10); ui.Paint(); }
        ui.Paint();
        Check(ui.Canvas.PaintCalls.Any(call => call.Color == 0x59D3C3 && call.Opacity < 1
            && call.FillBounds is { } fill && fill.X >= graph.X && fill.Right <= graph.Right),
            "Curve renders a translucent area from calculated movement strain");
        var seeks = new List<double>();
        ui.View.RequestSeek = seeks.Add;
        ui.Click(graph.X + graph.Width / 2, graph.Y + 1);
        Check(seeks[^1] > 9900, "Top of the curve represents the end of the map");
        ui.Click(graph.X + graph.Width / 2, graph.Bottom - 1);
        Check(seeks[^1] < 100, "Bottom of the curve represents the start of the map");
        ui.View.PointerDown(graph.X + graph.Width / 2, graph.Y + graph.Height / 4, 0, false, false);
        Check(ui.View.WantsCapture, "Curve seek captures the pointer");
        ui.View.PointerMove(graph.Right + 40, graph.Y + graph.Height / 2, false, false);
        ui.View.PointerUp(graph.Right + 40, graph.Y + graph.Height / 2, 0);
        ui.Paint();
        Check(seeks.Count >= 2 && Math.Abs(seeks[^1] - 5000) < 1 && !ui.View.WantsCapture,
            "Dragging outside the graph seeks continuously and releases capture");
        float originalWidth = ui.View.DifficultyCurvePanelBounds.Width;
        ui.View.PointerDown(panel.Right - 1, panel.Y + 60, 0, false, false);
        Check(!ui.View.WantsCapture, "Curve border does not capture a resize drag");
        ui.View.PointerMove(panel.Right + 44, panel.Y + 60, false, false);
        ui.View.PointerUp(panel.Right + 44, panel.Y + 60, 0);
        ui.Paint();
        Check(ui.View.DifficultyCurvePanelBounds.Width == originalWidth,
            "Curve width remains fixed after dragging its border");
        ui.Resize(1100, 800);
        Check(ui.View.DifficultyCurvePanelBounds.Width == originalWidth,
            "Curve width remains fixed when the preview and window are resized");
        Check(ui.View.CanvasPlotBounds.Width >= EditorView.MinimumPlayfieldWidth,
            "Narrow windows retain the minimum playfield width");
        Check(ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty,
            "Panel controls and seeking leave beatmap content unchanged");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
