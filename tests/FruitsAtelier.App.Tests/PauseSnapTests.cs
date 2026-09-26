using FruitsAtelier.Core;

internal static class PauseSnapTests
{
    public static void UserPause()
    {
        foreach (bool timing in new[] { false, true })
        foreach (int gesture in new[] { 32, 67, 0 })
        foreach (var (divisor, position, expected) in new[]
        {
            (4, 562.4, 500d), (4, 562.5, 625d), (4, 562.6, 625d),
            (8, 550d, 562.5), (3, 560d, 500d),
            (4, 2070d, 2100d), (4, 2160d, 2200d), (4, 5060d, 5070d)
        })
        {
            var ui = Create();
            if (timing) ui.Key(114);
            ui.Key('0' + divisor, shift: true);
            var before = ui.View.Document.DeepClone();
            var seeks = new List<double>();
            ui.View.RequestSeek = seeks.Add;
            ui.View.RequestTogglePlayback = ui.View.RequestPausePlayback = () =>
                ui.View.UpdateTransport(position, 5070, true, false, false, null, null);
            ui.View.UpdateTransport(500, 5070, true, true, false, null, null);
            ui.Paint();
            if (gesture != 0) ui.Key(gesture);
            else PauseButton(ui);
            Check(seeks.Count == 1 && Math.Abs(seeks[0] - expected) < 1e-7
                && Math.Abs(ui.View.PlayheadMs - expected) < 1e-7 && !ui.View.AudioPlaying,
                "Explicit pause snaps the confirmed position and seeks audio once.");
            ui.View.UpdateTransport(expected, 5070, true, false, false, null, null);
            Check(seeks.Count == 1, "Subsequent paused snapshots do not snap again.");
            Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty,
                "Pause snapping does not edit beatmap content.");
            if (!timing && expected < 5070)
            {
                ui.Key('F'); ui.MoveMap(expected, 256);
                var p = ui.ScreenAt(expected, 256);
                Check(ui.Canvas.Circles.Any(c => Math.Abs(c.X - p.X) < .01 && Math.Abs(c.Y - p.Y) < .01),
                    "The placement preview appears at the aligned time.");
                ui.ClickMap(expected, 256);
                Check(Math.Abs(ui.View.Document.Fruits.Single().TimeMs - expected) < 1e-7,
                    "Placement uses the aligned preview time.");
            }
        }
    }

    public static void PauseIsolation()
    {
        var ui = Create();
        var seeks = new List<double>();
        ui.View.RequestSeek = seeks.Add;
        ui.View.RequestTogglePlayback = ui.View.RequestPausePlayback = () => { };
        void State(double time, bool playing, bool loading = false) =>
            ui.View.UpdateTransport(time, 5020, true, playing, loading, null, null);

        State(550, true); ui.Key(32);
        State(570, true);
        Check(seeks.Count == 0, "Wait for the pause confirmation before snapping.");
        State(580, false);
        Check(seeks.SequenceEqual(new[] { 625d }), "Delayed confirmation uses the final pause time.");
        seeks.Clear();
        State(625, false); ui.Key(32); State(650, true); State(660, false);
        Check(seeks.Count == 0 && ui.View.PlayheadMs == 660, "Resume and external pauses do not snap.");

        State(550, true); ui.Key(32); ui.Key(36); seeks.Clear(); State(0, false);
        Check(seeks.Count == 0, "An explicit seek cancels pending pause snapping.");
        State(550, true); ui.Key(32); State(560, true, loading: true); State(570, false);
        Check(seeks.Count == 0, "Loading cancels pending pause snapping.");
        State(550, true); ui.Key(32); ui.LoadDocument(Map()); State(570, false);
        Check(seeks.Count == 0, "Replacing the document cancels pending pause snapping.");

        State(550, true); ui.Paint();
        ui.View.RequestPausePlayback = () => State(570, false);
        var test = ui.View.TestplayButtonBounds;
        ui.Click(test.X - 49 + 21, test.Y + test.Height / 2);
        Check(seeks.SequenceEqual(new[] { 0d }), "Stop seeks only to the beginning.");
        seeks.Clear();
        State(550, true); ui.Key(117);
        Check(seeks.Count == 0, "Opening timing setup does not snap its automatic pause.");
        ui.Key(27);

        typeof(FruitsAtelier.App.Editor.EditorView)
            .GetField("snap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(ui.View, false);
        State(550, true); PauseButton(ui); State(570, false);
        Check(seeks.Count == 0 && ui.View.PlayheadMs == 570, "Disabled snapping preserves the exact pause time.");
    }

    private static void PauseButton(Ui ui)
    {
        ui.Paint();
        var test = ui.View.TestplayButtonBounds;
        ui.Click(test.X - 98 + 21, test.Y + test.Height / 2);
    }

    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 6000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Uninherited = true });
        map.TimingPoints.Add(new() { TimeMs = 2100, BeatLengthMs = 400, Uninherited = true });
        return map;
    }

    private static Ui Create() { var ui = new Ui(); ui.LoadDocument(Map()); return ui; }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
