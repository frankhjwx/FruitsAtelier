using FruitsAtelier.Core;

static class WheelGestureTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
        var ui = new Ui();
        ui.LoadDocument(map);
        var canvas = ui.View.CanvasPlotBounds;
        var timeline = ui.View.ObjectTimelineBounds;
        float cx = canvas.X + canvas.Width / 2, cy = canvas.Y + canvas.Height / 2;
        float tx = timeline.X + 100, ty = timeline.Y + 20;
        float ox = 320, oy = ui.Height - 57;

        int initial = ui.View.SnapDivisor;
        ui.View.Wheel(cx, cy, 120, true);
        Check(ui.View.SnapDivisor != initial, "Ctrl+wheel on canvas must adjust Snap.");
        int afterCanvas = ui.View.SnapDivisor;
        ui.View.Wheel(tx, ty, 120, true);
        Check(ui.View.SnapDivisor != afterCanvas, "Ctrl+wheel on object timeline must adjust Snap.");
        int afterTimeline = ui.View.SnapDivisor;
        ui.View.Wheel(ox, oy, 120, true);
        Check(ui.View.SnapDivisor != afterTimeline, "Ctrl+wheel on overview must adjust Snap.");

        double zoom = ui.View.CanvasZoom;
        ui.View.Wheel(cx, cy, 120, true, true);
        Check(ui.View.CanvasZoom > zoom, "Ctrl+Shift+wheel must zoom the canvas.");
        double timelineZoom = ui.View.ObjectTimelinePixelsPerMs;
        ui.View.Wheel(tx, ty, 120, false, false, true);
        Check(ui.View.ObjectTimelinePixelsPerMs > timelineZoom, "Alt+wheel must zoom the object timeline.");

        string tool = ui.View.ActiveTool;
        ui.View.Wheel(cx, cy, 120, true, false, true);
        Check(ui.View.ActiveTool != tool, "Ctrl+Alt+wheel must cycle placement tools.");
        double before = ui.View.PlayheadMs;
        ui.View.Wheel(ox, oy, -120, false, false, true);
        Check(ui.View.PlayheadMs == before, "Alt+wheel on overview must not seek.");
        ui.View.Wheel(cx, cy, -120, false, true);
        Check(ui.View.PlayheadMs > before, "Shift+wheel must seek on the canvas.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
