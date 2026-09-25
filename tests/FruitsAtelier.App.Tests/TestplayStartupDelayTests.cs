using FruitsAtelier.Core;

internal static class TestplayStartupDelayTests
{
    public static void Countdown()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
        map.Fruits.Add(new Fruit { TimeMs = 3000, X = 256 });
        ui.LoadDocument(map);
        ui.View.LibrarySettings.TestplayStartupDelaySeconds = 1;
        ui.View.UpdateTransport(500, 5000, true, false, false, null, null);
        ui.View.UpdateTransport(500, 5000, false, false, false, null, null);
        int sounds = 0;
        ui.View.RequestHitsound = _ => sounds++;
        ui.View.StartTestplay();
        Check(ui.View.IsTestplaying && ui.View.TestplayCountingDown, "Countdown did not start.");
        clock.Advance(999); ui.Paint();
        Check(ui.View.TestplayCountingDown && ui.View.PlayheadMs == 500 && sounds == 0,
            "Countdown advanced gameplay before its deadline.");
        ui.Key(27);
        Check(!ui.View.IsTestplaying && ui.View.PlayheadMs == 500, "Escape did not cancel at the original position.");
        ui.View.KeyUp(27);

        ui.View.StartTestplay();
        clock.Advance(1000); ui.Paint();
        Check(ui.View.IsTestplaying && !ui.View.TestplayCountingDown && ui.View.PlayheadMs == 500,
            "Gameplay did not begin at the original position when the countdown ended.");
        clock.Advance(250); ui.Paint();
        Check(ui.View.PlayheadMs == 750 && sounds == 0, "Gameplay clock did not start at countdown completion.");
        ui.View.StopTestplay();

        var audioClock = new ManualTime();
        var audioUi = new Ui(timeProvider: audioClock);
        audioUi.LoadDocument(map);
        audioUi.View.LibrarySettings.TestplayStartupDelaySeconds = 1;
        audioUi.View.UpdateTransport(500, 5000, true, false, false, null, null);
        double seek = -1;
        int starts = 0, pauses = 0;
        audioUi.View.RequestSeek = position => seek = position;
        audioUi.View.RequestTogglePlayback = () => starts++;
        audioUi.View.RequestPausePlayback = () => pauses++;
        audioUi.View.StartTestplay();
        audioUi.View.UpdateTransport(510, 5000, true, true, false, null, null);
        Check(pauses == 1 && audioUi.View.PlayheadMs == 500,
            "Playback that started during countdown was not paused at the saved position.");
        audioUi.View.UpdateTransport(510, 5000, true, false, false, null, null);
        audioClock.Advance(999); audioUi.Paint();
        Check(starts == 0 && seek < 0 && audioUi.View.PlayheadMs == 500,
            "Audio or gameplay started during the countdown.");
        audioClock.Advance(1); audioUi.Paint();
        Check(starts == 1 && seek == 500 && !audioUi.View.TestplayCountingDown && audioUi.View.PlayheadMs == 500,
            "Audio and gameplay did not start together at the saved position.");
        audioUi.View.StopTestplay();

        var settings = new LibrarySettings { Workspace = Path.GetFullPath("artifacts/tests/testplay-delay-workspace"), TestplayStartupDelaySeconds = 5 };
        string path = Path.GetFullPath("artifacts/tests/testplay-delay-settings.json");
        settings.Save(path);
        Check(LibrarySettings.Load(path).TestplayStartupDelaySeconds == 5, "Startup delay did not persist.");
        Check(new LibrarySettings().TestplayStartupDelaySeconds == 1, "Startup delay default changed.");
        Check(new LibrarySettings { TestplayStartupDelaySeconds = -1 }.TestplayStartupDelaySeconds == 0
            && new LibrarySettings { TestplayStartupDelaySeconds = 9 }.TestplayStartupDelaySeconds == 5,
            "Startup delay range was not enforced.");
    }

    private sealed class ManualTime : TimeProvider
    {
        private long microseconds;
        public override long TimestampFrequency => 1_000_000;
        public override long GetTimestamp() => microseconds;
        public void Advance(int milliseconds) => microseconds += milliseconds * 1000L;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
