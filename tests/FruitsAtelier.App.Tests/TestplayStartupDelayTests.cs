using FruitsAtelier.Core;

internal static class TestplayStartupDelayTests
{
    public static void LeadIn()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 256 });
        map.Fruits.Add(new Fruit { TimeMs = 3000, X = 256 });
        ui.LoadDocument(map);
        ui.View.LibrarySettings.TestplayStartupDelaySeconds = .5;
        ui.View.UpdateTransport(500, 5000, true, false, false, null, null);
        ui.View.UpdateTransport(500, 5000, false, false, false, null, null);
        int sounds = 0;
        ui.View.RequestHitsound = _ => sounds++;
        ui.View.StartTestplay();
        Check(ui.View.IsTestplaying && ui.View.PlayheadMs == 0, "Testplay did not start immediately from the lead-in position.");
        clock.Advance(750); ui.Paint();
        Check(ui.View.PlayheadMs == 750 && sounds == 0, "Lead-in did not advance gameplay before the selected position.");
        ui.Key(27);
        Check(!ui.View.IsTestplaying && ui.View.PlayheadMs == 500, "Escape did not return to the selected position.");
        ui.View.KeyUp(27);

        ui.View.UpdateTransport(2500, 5000, true, false, false, null, null);
        ui.View.UpdateTransport(2500, 5000, false, false, false, null, null);
        ui.View.StartTestplay();
        Check(ui.View.IsTestplaying && ui.View.PlayheadMs == 2000,
            "Testplay did not subtract the configured lead-in from the selected position.");
        clock.Advance(250); ui.Paint();
        Check(ui.View.PlayheadMs == 2250, "Gameplay did not advance immediately.");
        ui.View.StopTestplay();
        Check(ui.View.PlayheadMs == 2500, "Leaving testplay did not restore the selected position.");

        var audioClock = new ManualTime();
        var audioUi = new Ui(timeProvider: audioClock);
        audioUi.LoadDocument(map);
        audioUi.View.LibrarySettings.TestplayStartupDelaySeconds = 1;
        audioUi.View.UpdateTransport(2500, 5000, true, true, false, null, null);
        double seek = -1;
        int starts = 0, pauses = 0;
        audioUi.View.RequestSeek = position => seek = position;
        audioUi.View.RequestTogglePlayback = () => starts++;
        audioUi.View.RequestPausePlayback = () => pauses++;
        audioUi.View.StartTestplay();
        Check(pauses == 1 && starts == 1 && seek == 1500 && audioUi.View.PlayheadMs == 1500,
            "Playing audio was not restarted immediately at the lead-in position.");
        audioUi.View.StopTestplay();
        Check(audioUi.View.PlayheadMs == 2500, "Audio testplay did not restore the selected position.");

        audioUi.View.LibrarySettings.TestplayStartupDelaySeconds = 0;
        audioUi.View.UpdateTransport(2500, 5000, true, false, false, null, null);
        audioUi.View.StartTestplay();
        Check(audioUi.View.PlayheadMs == 2500, "Zero lead-in did not start at the selected position.");
        audioUi.View.StopTestplay();

        var settings = new LibrarySettings { Workspace = Path.GetFullPath("artifacts/tests/testplay-delay-workspace"), TestplayStartupDelaySeconds = 2.5 };
        string path = Path.GetFullPath("artifacts/tests/testplay-delay-settings.json");
        settings.Save(path);
        Check(LibrarySettings.Load(path).TestplayStartupDelaySeconds == 2.5, "Startup delay did not persist.");
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
