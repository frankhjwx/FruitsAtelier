internal static class TimelineDragTests
{
    public static void ReturnToStart()
    {
        foreach (float grabOffset in new[] { -2f, -0.125f, 0f, 0.125f, 2f })
        {
            var ui = new Ui();
            const double original = 12345.678, duration = 180000;
            ui.View.UpdateTransport(original, duration, true, false, false, null, "song.mp3");
            ui.Paint();
            var head = ui.Canvas.Lines.Single(l => l.Color == 0xF2C66D && l.X1 == l.X2 && l.Y1 > ui.Plot.Bottom);
            float start = head.X1 + grabOffset, y = grabOffset == 0 ? head.Y1 + 1 : head.Y1 + 12;
            int seeks = 0;
            ui.View.RequestSeek = _ => seeks++;
            var snapshot = ui.View.Document.DeepClone();
            ui.View.PointerDown(start, y, 0, false, false);
            Equal(original, ui.View.PlayheadMs, "Grabbing the head changed its time");
            if (seeks != 0) throw new Exception("Grabbing the head unnecessarily repositioned audio.");
            foreach (float delta in new[] { 120f, -60f, 200f, 0f })
            {
                ui.View.PointerMove(start + delta, y, false, false); ui.Paint();
                if (delta == 0) Equal(original, ui.View.PlayheadMs, "Returning to the grab position drifted");
            }
            ui.View.PointerUp(start, y, 0); ui.Paint();
            Equal(original, ui.View.PlayheadMs, "Releasing changed the restored time");
            if (ui.View.WantsCapture || ui.View.IsDirty || !snapshot.ContentEquals(ui.View.Document))
                throw new Exception("Timeline drag affected capture or document content.");
        }
    }

    public static void ClickAndLimits()
    {
        var ui = new Ui();
        const double duration = 180000;
        ui.View.UpdateTransport(12000, duration, true, false, false, null, "song.mp3"); ui.Paint();
        const float left = 220, width = 1192, y = 840;
        ui.View.PointerDown(left + width / 2, y, 0, false, false);
        Equal(duration / 2, ui.View.PlayheadMs, "Clicking empty timeline did not seek");
        ui.View.PointerMove(left - 100, y, false, false);
        Equal(0, ui.View.PlayheadMs, "Left edge did not clamp");
        ui.View.PointerMove(left + width + 100, y, false, false);
        Equal(duration, ui.View.PlayheadMs, "Right edge did not clamp");
        ui.View.PointerMove(left + width / 2, y, false, false);
        Equal(duration / 2, ui.View.PlayheadMs, "Clamping changed the drag origin");
        ui.View.PointerUp(left + width / 2, y, 0);
        ui.View.PointerDown(left + width / 2, y, 0, false, false);
        ui.Resize(980, 620);
        double before = ui.View.PlayheadMs;
        ui.View.PointerMove(left + 40, y, false, false);
        Equal(before, ui.View.PlayheadMs, "Resizing kept a stale drag coordinate system");
        if (ui.View.WantsCapture) throw new Exception("Resize did not end timeline dragging.");
    }

    private static void Equal(double expected, double actual, string message)
    {
        if (expected != actual) throw new Exception($"{message}: expected {expected:R} ms, got {actual:R} ms.");
    }
}
