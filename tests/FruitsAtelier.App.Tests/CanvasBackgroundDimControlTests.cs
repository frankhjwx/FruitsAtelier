using FruitsAtelier.Core;

static class CanvasBackgroundDimControlTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int milliseconds) => ticks += milliseconds;
    }

    public static void Run()
    {
        var clock = new Clock();
        var ui = new Ui(false, clock);
        var original = ui.View.Document.DeepClone();
        bool dirty = ui.View.IsDirty;
        int saves = 0;
        ui.View.RequestViewPreference = () => saves++;
        ui.View.LibrarySettings.BackgroundDim = 35;
        var bounds = ui.View.CanvasBackgroundDimBounds;
        var slider = ui.View.CanvasBackgroundDimSliderBounds;
        Check(ui.View.CanvasBackgroundDimOpacity == 0 && !HasLabel(), "Control was not hidden initially.");
        ui.View.PointerMove(bounds.X - 5, bounds.Y + 10, false, false); ui.Paint();
        Check(ui.View.CanvasBackgroundDimNeedsRedraw, "Proximity did not start the fade animation.");
        clock.Advance(75); ui.Paint();
        Near(.5f, ui.View.CanvasBackgroundDimOpacity);
        Check(HasLabel(), "Control omitted the Background Dim label.");
        clock.Advance(75); ui.Paint();
        Near(1, ui.View.CanvasBackgroundDimOpacity);
        Check(!ui.View.CanvasBackgroundDimNeedsRedraw, "A stationary visible control continued requesting frames.");
        ui.View.PointerMove(ui.Plot.X + 400, ui.Plot.Y + 10, false, false);
        clock.Advance(999); ui.Paint(); Near(1, ui.View.CanvasBackgroundDimOpacity);
        clock.Advance(76); ui.Paint(); Near(.5f, ui.View.CanvasBackgroundDimOpacity);
        ui.View.PointerMove(bounds.X + 10, bounds.Y + 10, false, false); ui.Paint();
        Near(.5f, ui.View.CanvasBackgroundDimOpacity);
        clock.Advance(150); ui.Paint(); Near(1, ui.View.CanvasBackgroundDimOpacity);
        ui.View.PointerLeave(); clock.Advance(1150);
        Check(ui.View.CanvasBackgroundDimNeedsRedraw, "Fade completion did not request the final clearing frame.");
        ui.Paint();
        Check(!HasLabel() && !ui.View.CanvasBackgroundDimNeedsRedraw, "Pointer leave did not finish fading out.");

        ui.View.PointerDown(slider.X + slider.Width / 2, slider.Y + 12, 0, false, false);
        Check(ui.View.WantsCapture && ui.View.LibrarySettings.MappingCanvasBackgroundDim == 50, "Slider did not capture and apply its live value.");
        clock.Advance(2000); ui.Paint(); Near(1, ui.View.CanvasBackgroundDimOpacity);
        ui.View.PointerMove(slider.Right + 100, slider.Y + 12, false, false);
        Check(ui.View.LibrarySettings.MappingCanvasBackgroundDim == 100, "Drag did not clamp beyond the slider.");
        ui.View.PointerUp(slider.X + slider.Width * .75f, slider.Y + 12, 0); ui.Paint();
        Check(!ui.View.WantsCapture && saves == 1 && ui.View.LibrarySettings.MappingCanvasBackgroundDim == 75,
            "Release did not retain and persist the drag once.");
        Check(ui.View.LibrarySettings.BackgroundDim == 35, "Canvas slider changed testplay dim.");
        ui.View.Wheel(ui.Plot.X + 400, ui.Plot.Y + 10, -240, false, false, true); ui.Paint();
        Check(bounds == ui.View.CanvasBackgroundDimBounds && slider == ui.View.CanvasBackgroundDimSliderBounds, "Zoom moved or resized the control.");

        int beforeInterruptedDrag = saves;
        ui.View.PointerDown(slider.X, slider.Y + 12, 0, false, false);
        ui.View.CancelInteraction(); ui.Paint();
        Check(!ui.View.WantsCapture && saves == beforeInterruptedDrag + 1, "Interrupted drag did not release capture and persist its value.");
        ui.View.OpenSettings(); ui.Paint();
        Check(ui.View.CanvasBackgroundDimOpacity == 0 && !ui.View.CanvasBackgroundDimNeedsRedraw,
            "Settings did not hide and isolate the canvas control.");
        ui.Key(27);
        ui.View.PointerMove(ui.Plot.X + 400, ui.Plot.Y + 10, false, false);
        clock.Advance(1150); ui.Paint();
        Check(original.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Canvas dim changed map content or undo state.");

        bool HasLabel() => ui.Canvas.Texts.Any(t => t.Value == "Background Dim");
    }

    private static void Near(float expected, float actual)
    { Check(Math.Abs(expected - actual) < .001f, $"Expected opacity {expected}, got {actual}."); }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
