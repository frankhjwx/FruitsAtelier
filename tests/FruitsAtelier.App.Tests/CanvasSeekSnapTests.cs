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
            Near(expected, ui.View.PlayheadMs);
            Near(expected, lastSeek);
        }
        if (ui.View.IsDirty) throw new Exception("Snapped navigation edited the beatmap.");
    }

    public static void FreeMode()
    {
        var ui = Create();
        ui.ClickText("自由");
        ui.ClickMap(1137.25, 480);
        Near(1137.25, ui.View.PlayheadMs);
        ui.ClickText("自由");
        ui.ClickMap(1137.25, 480);
        Near(1125, ui.View.PlayheadMs);
        if (ui.View.IsDirty) throw new Exception("Changing seek modes edited the beatmap.");
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
