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
        var panel = ui.View.CanvasControlsPanelBounds;
        ui.View.PointerDown(panel.X + 8, panel.Y + panel.Height / 2, 0, false, false);
        Check(ui.View.WantsCapture, "Panel handle did not capture the pointer.");
        ui.View.PointerMove(panel.X + 48, panel.Y + panel.Height / 2 - 80, false, false);
        ui.View.PointerUp(panel.X + 48, panel.Y + panel.Height / 2 - 80, 0); ui.Paint();
        var moved = ui.View.CanvasControlsPanelBounds;
        Check(moved.X == panel.X + 40 && moved.Y == panel.Y - 80, "Panel drag did not move both rows.");
        Check(ui.View.CanvasMediaToggleBounds.Y == ui.View.CanvasBackgroundDimBounds.Bottom, "Compact rows have a gap.");
        for (int corner = 1; corner <= 4; corner++)
        {
            ui.View.LibrarySettings.CanvasControlsScale = 1;
            ui.Paint();
            var r = ui.View.CanvasControlsPanelBounds;
            bool left = corner is 1 or 3, top = corner is 1 or 2;
            float x = left ? r.X : r.Right, y = top ? r.Y : r.Bottom;
            float endX = x + (left ? 1 : -1) * r.Width, endY = y + (top ? 1 : -1) * r.Height;
            ui.View.PointerDown(x, y, 0, false, false);
            ui.View.PointerMove(endX, endY, false, false);
            ui.View.PointerUp(endX, endY, 0); ui.Paint();
            var small = ui.View.CanvasControlsPanelBounds;
            Check(ui.View.LibrarySettings.CanvasControlsScale == .5f && small.Width == r.Width / 2 && small.Height == r.Height / 2,
                "Corner resizing did not clamp proportionally at 50%.");
            Check(Math.Abs((left ? small.Right : small.X) - (left ? r.Right : r.X)) < .01f
                && Math.Abs((top ? small.Bottom : small.Y) - (top ? r.Bottom : r.Y)) < .01f, "Resize moved the opposite corner.");
            var scaledSlider = ui.View.CanvasBackgroundDimSliderBounds;
            ui.View.PointerDown(scaledSlider.X + scaledSlider.Width / 2, scaledSlider.Y + scaledSlider.Height / 2, 0, false, false);
            ui.View.PointerUp(scaledSlider.X + scaledSlider.Width / 2, scaledSlider.Y + scaledSlider.Height / 2, 0);
            Check(ui.View.LibrarySettings.MappingCanvasBackgroundDim == 50, "Half-size slider hit region was not scaled.");
        }
        ui.View.LibrarySettings.CanvasControlsOffsetX = 100000;
        ui.View.LibrarySettings.CanvasControlsOffsetY = -100000;
        ui.Paint();
        var clamped = ui.View.CanvasControlsPanelBounds;
        ui.View.PointerDown(clamped.X + 4, clamped.Y + clamped.Height / 2, 0, false, false);
        ui.View.PointerMove(clamped.X - 26, clamped.Y + clamped.Height / 2 + 40, false, false);
        ui.View.PointerUp(clamped.X - 26, clamped.Y + clamped.Height / 2 + 40, 0); ui.Paint();
        Check(ui.View.CanvasControlsPanelBounds.X == clamped.X - 30 && ui.View.CanvasControlsPanelBounds.Y == clamped.Y + 40,
            "Clamped panel could not be dragged after a viewport change.");
        ui.View.ShowDiscussionPanel(); ui.Paint();
        var grouped = ui.View.CanvasControlsPanelBounds;
        var annotations = ui.View.CanvasDiscussionToggleBounds;
        Check(annotations.Y == grouped.Y && ui.View.CanvasBackgroundDimBounds.Y == annotations.Bottom
            && ui.View.CanvasMediaToggleBounds.Bottom == grouped.Bottom, "Discussion toggle is outside the compact panel.");
        bool visible = ui.View.LibrarySettings.ShowCanvasDiscussionAnnotations;
        ui.Click(annotations.X + annotations.Width / 2, annotations.Y + annotations.Height / 2);
        Check(ui.View.LibrarySettings.ShowCanvasDiscussionAnnotations != visible, "Half-size annotation toggle did not respond.");
        string preferences = Path.GetFullPath("artifacts/tests/overlay-preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        ui.View.LibrarySettings.Save(preferences);
        var restored = LibrarySettings.Load(preferences);
        Check(restored.CanvasControlsScale == .5f && restored.CanvasControlsOffsetX == ui.View.LibrarySettings.CanvasControlsOffsetX
            && restored.CanvasControlsOffsetY == ui.View.LibrarySettings.CanvasControlsOffsetY, "Panel preferences did not roundtrip.");
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
