internal static class TimelineDragTests
{
    public static void ReturnToStart()
    {
        PlaybackHold();
        foreach (float grabOffset in new[] { -2f, -0.125f, 0f, 0.125f, 2f })
        {
            var ui = new Ui();
            const double original = 12345.678, duration = 180000;
            ui.View.UpdateTransport(original, duration, true, false, false, null, "song.mp3");
            ui.Paint();
            var head = ui.Canvas.Lines.Single(l => l.Color == 0xFFFFFF && l.X1 == l.X2 && l.Y1 > ui.Plot.Bottom);
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

    private static void PlaybackHold()
    {
        foreach (bool initiallyPlaying in new[] { true, false })
        foreach (bool grabHead in new[] { true, false })
        foreach (bool cancel in new[] { true, false })
        foreach (bool publishPause in new[] { true, false })
        {
            var ui = new Ui();
            const double duration = 180000;
            double position = 12345.678;
            bool playing = initiallyPlaying;
            int pauses = 0, resumes = 0;
            void Poll() => ui.View.UpdateTransport(position, duration, true, playing, false, null, "song.mp3");
            ui.View.RequestPausePlayback = () => { pauses++; playing = false; if (publishPause) Poll(); };
            ui.View.RequestTogglePlayback = () => { resumes++; playing = !playing; Poll(); };
            ui.View.RequestSeek = time => { position = time; Poll(); };
            Poll(); ui.Paint();
            var snapshot = ui.View.Document.DeepClone();
            var head = ui.Canvas.Lines.Single(l => l.Color == 0xFFFFFF && l.X1 == l.X2 && l.Y1 > ui.Plot.Bottom);
            float x = grabHead ? head.X1 : 816, y = head.Y1 + 12;
            ui.View.PointerDown(x, y, 0, false, false);
            if (playing || pauses != (initiallyPlaying ? 1 : 0))
                throw new Exception("Timeline grab must pause active audio without starting paused audio.");
            if (grabHead) Equal(12345.678, ui.View.PlayheadMs, "Grabbing a playing head changed its time");
            if (publishPause) ui.View.PointerMove(x - 30, y, false, false);
            double held = ui.View.PlayheadMs;
            for (int tick = 0; tick < (publishPause ? 10 : 0); tick++)
            {
                if (playing) position += 100;
                Poll(); ui.Paint();
                Equal(held, position, "Audio advanced while the pointer held a timeline position");
                Equal(held, ui.View.PlayheadMs, "Held timeline position drifted");
            }
            if (cancel) ui.View.CancelInteraction();
            else ui.View.PointerUp(publishPause ? x - 30 : x, y, 0);
            if (playing != initiallyPlaying || resumes != (initiallyPlaying ? 1 : 0))
                throw new Exception("Ending a timeline drag must restore its original playback state once.");
            Equal(held, position, "Playback resumed from a different time");
            if (playing) { position += 100; Poll(); Equal(position, ui.View.PlayheadMs, "Released playback no longer follows audio"); }
            ui.View.CancelInteraction();
            if (resumes != (initiallyPlaying ? 1 : 0) || ui.View.WantsCapture
                || !snapshot.ContentEquals(ui.View.Document) || ui.View.IsDirty)
                throw new Exception("Timeline scrub repeated playback restoration or changed content.");
        }
    }

    public static void ClickAndLimits()
    {
        var ui = new Ui();
        const double duration = 180000;
        ui.View.UpdateTransport(12000, duration, true, false, false, null, "song.mp3"); ui.Paint();
        var overview = ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
        float left = overview.X, width = overview.Width;
        float y = overview.Y + overview.Height / 2;
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
