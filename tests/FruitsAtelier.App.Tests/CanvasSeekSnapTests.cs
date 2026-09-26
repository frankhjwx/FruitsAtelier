using FruitsAtelier.Core;

internal static class CanvasSeekSnapTests
{
    public static void BeatGrid()
    {
        var ui = Create();
        double lastSeek = -1;
        ui.View.RequestSeek = time => lastSeek = time;
        foreach (var (divisor, raw, expected) in new[]
        {
            (4, 1128d, 1125d), (4, 1102d, 1125d), (4, 1148d, 1125d),
            (4, 1730d, 1750d), (4, 1118d, 1125d),
            (6, 1178d, 1166.6666666666667),
            (4, 2090d, 2100d), (4, 2217d, 2200d), (8, 2217d, 2200d)
        })
        {
            ui.SetSnapDivisor(divisor);
            ui.ClickMap(raw, 480);
            Near(0, ui.View.PlayheadMs);
            Near(-1, lastSeek);
        }
        if (ui.View.IsDirty) throw new Exception("Snapped navigation edited the beatmap.");
        ui.View.UpdateTransport(2000, 10000, true, true, false, null, "fixture.wav"); ui.Paint();
        lastSeek = -1;
        ui.ClickMap(2500, 480);
        Near(2000, ui.View.PlayheadMs); Near(-1, lastSeek);
        if (!ui.View.AudioPlaying || ui.View.IsDirty) throw new Exception("Blank canvas click interrupted playback or edited content.");
        ui.View.UpdateTransport(2100, 10000, true, false, false, null, "fixture.wav"); ui.Paint();
        ui.ClickMap(2217, 480);
        Near(-1, lastSeek);
    }

    public static void FreeMode()
    {
        var ui = Create();
        ui.ClickMap(1137.25, 480);
        Near(0, ui.View.PlayheadMs);
        var timeline = ui.View.ObjectTimelineBounds;
        ui.Click(timeline.X + timeline.Width * .75f, timeline.Bottom - 3);
        if (ui.View.PlayheadMs != 0 || ui.View.IsDirty) throw new Exception("Empty object timeline clicks must preserve time and content.");
    }

    private static Ui Create()
    {
        var map = new MapDocument { DurationMs = 10000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 2100, BeatLengthMs = 400, Uninherited = true });
        var ui = new Ui();
        ui.LoadDocument(map);
        return ui;
    }

    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.001)
            throw new Exception($"Expected {expected:R} ms, got {actual:R} ms.");
    }
}
