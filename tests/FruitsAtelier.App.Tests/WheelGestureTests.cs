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
        ui.View.Wheel(ox, oy, -120, true);
        Check(ui.View.SnapDivisor != afterTimeline, "Ctrl+wheel on overview must adjust Snap.");

        ui.Key(114);
        var waveform = ui.View.WaveformBounds;
        foreach (var family in new[] { new[] { 1, 2, 4, 8, 16 }, new[] { 3, 6, 12 }, new[] { 5 }, new[] { 7 }, new[] { 9 } })
        {
            ui.SetSnapDivisor(family[0]);
            foreach (int expected in family.Skip(1))
            {
                int previous = ui.View.SnapDivisor;
                ui.View.Wheel(waveform.X + 100, waveform.Y + 80, 60, true);
                Check(ui.View.SnapDivisor == previous, "Partial wheel notch accumulates.");
                ui.View.Wheel(waveform.X + 100, waveform.Y + 80, 60, true);
                Check(ui.View.SnapDivisor == expected, "Timing wheel doubles within its Snap family.");
            }
            ui.View.Wheel(waveform.X + 100, waveform.Y + 80, 240, true);
            Check(ui.View.SnapDivisor == family[^1], "Timing Snap stops at upper family limit.");
            ui.View.Wheel(waveform.X + 100, waveform.Y + 80, -120 * family.Length, true);
            Check(ui.View.SnapDivisor == family[0], "Timing Snap stops at lower family limit.");
        }
        ui.Key(112);

        double zoom = ui.View.CanvasZoom;
        ui.View.Wheel(cx, cy, 120, false, false, true);
        Check(ui.View.CanvasZoom > zoom, "Alt+wheel must zoom the canvas.");
        zoom = ui.View.CanvasZoom;
        ui.View.Wheel(cx, cy, 120, true, true);
        Check(ui.View.CanvasZoom == zoom, "Ctrl+Shift+wheel must not zoom the canvas.");
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
