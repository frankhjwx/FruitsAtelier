using FruitsAtelier.Core;
#if WINDOWS
using FruitsAtelier.App.Platform;
#endif

internal static class TestplayHeldKeyTests
{
    public static void Run()
    {
        foreach (int right in new[] { 39, 68 })
        foreach (int dash in new[] { 16, 32, 90 })
        foreach (int pressAt in new[] { 0, 1, 2, 3 })
        {
            if (dash == 32 && pressAt > 0) continue; // Space activates Skip during the intro.
            var time = new ManualTime();
            var ui = new Ui(timeProvider: time);
            var map = new MapDocument(); map.Fruits.Add(new() { TimeMs = 15000, X = 256 });
            ui.LoadDocument(map);
            ui.View.LibrarySettings.TestplayRightKey = right;
            ui.View.LibrarySettings.TestplayDashKey = dash;
            void Press() { ui.Key(right); ui.Key(dash, shift: dash == 16); }
            if (pressAt == 0) Press();
            ui.View.StartTestplay();
            if (pressAt == 1) Press();
            if (pressAt > 0)
            {
                ui.Key(27); ui.View.KeyUp(27);
                if (pressAt == 2) Press();
                ui.Key(27); ui.View.KeyUp(27);
                if (pressAt == 3) Press();
                time.Advance(600); ui.Paint();
            }
            double before = ui.View.TestplayCatcherX;
            time.Advance(100); ui.Paint();
            Check(Math.Abs(ui.View.TestplayCatcherX - before - 100) < .001,
                $"Held movement and dash must work at entry/pause/countdown {pressAt} ({right}/{dash})");
            ui.View.KeyUp(right); ui.View.KeyUp(dash);
            before = ui.View.TestplayCatcherX;
            time.Advance(100); ui.Paint();
            Check(ui.View.TestplayCatcherX == before, "Release must stop movement");
            ui.View.StopTestplay();
        }

        foreach (bool audio in new[] { false, true })
        foreach (bool release in new[] { false, true })
        {
            var time = new ManualTime();
            var note = new ConvertedCatchObject(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 15000, 256, 256, 256, 0);
            var session = new CatchTestplaySession(new CatchTestplay([note], 5, 0),
                new CatchTestplayClock(0, 1, 0, false), 0, audio, audio, 37, 39, 16, time, 5, []);
            session.SetKey(39, true); session.SetKey(16, true);
            session.TogglePause();
            if (release && !audio) { session.SetKey(39, false); session.SetKey(16, false); }
            time.Advance(100); session.Tick();
            Check(session.X == 256, "Paused keys must not move the catcher");
            session.TogglePause();
            if (audio)
            {
                // Releases during the audio restart must also reach the held state.
                session.SetKey(39, !release); session.SetKey(16, !release);
                time.Advance(1); session.UpdateAudio(1, 101, 20000, true, true, false, false);
            }
            time.Advance(50); session.Tick();
            Check(release ? session.X == 256 && !session.Capture().Dashing
                : session.X > 256 && session.Capture().Dashing, "Resume must use the current held state");
            session.ReleaseKeys(); double before = session.X;
            time.Advance(10); session.Tick();
            Check(session.X == before, "Focus loss must release held keys");
        }

#if WINDOWS
        foreach (bool ctrl in new[] { false, true })
        foreach (bool alt in new[] { false, true })
        {
            var time = new ManualTime();
            var note = new ConvertedCatchObject(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 15000, 256, 256, 256, 0);
            var session = new CatchTestplaySession(new CatchTestplay([note], 5, 0),
                new CatchTestplayClock(0, 1, 0, false), 0, false, false, 37, 38, 16, time, 5, []);
            TestplayInputThread.InitializeHeldKeys(session, key => key is 38 or 16 || key == 17 && ctrl || key == 18 && alt);
            time.Advance(50); session.Tick();
            Check(ctrl || alt ? session.X == 256 : session.X > 256,
                "Native startup must read held movement and exclude modifier shortcuts");
            Check(session.Capture().Dashing, "Native startup must read held Shift without a new key event");
            session.SetKey(38, false); session.SetKey(16, false);
            double before = session.X; time.Advance(50); session.Tick();
            Check(session.X == before && !session.Capture().Dashing, "Native startup keys must release normally");
        }
#endif
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class ManualTime : TimeProvider
    {
        private long time;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => time;
        public void Advance(long milliseconds) => time += milliseconds;
    }
}
