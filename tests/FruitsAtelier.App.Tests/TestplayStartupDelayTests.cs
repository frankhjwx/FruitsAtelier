using FruitsAtelier.Core;

internal static class TestplayStartupDelayTests
{
    public static void IndependentSpeed()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        var map = new MapDocument { DurationMs = 20000 };
        map.Fruits.Add(new() { TimeMs = 10000, X = 256 });
        ui.LoadDocument(map);
        ui.View.SetPlaybackSpeed(.5);
        var requests = new List<double>(); ui.View.RequestPlaybackSpeed = requests.Add;
        ui.View.StartTestplay();
        Check(ui.View.PlaybackSpeed == 1 && requests.SequenceEqual(new[] { 1d }), "Testplay inherited the editor speed.");
        clock.Advance(100); ui.Paint();
        Check(ui.View.PlayheadMs == 100, "Silent testplay clock ignored its independent speed.");
        ui.View.SetPlaybackSpeed(1.25);
        ui.Key('R', ctrl: true); ui.View.KeyUp('R');
        Check(ui.View.PlaybackSpeed == 1.25, "Retry lost the current testplay speed.");
        ui.View.StopTestplay();
        Check(ui.View.PlaybackSpeed == .5 && requests[^1] == .5 && ui.View.LibrarySettings.TestplaySpeed == 1,
            "Testplay speed changes leaked into editor speed or saved settings.");
        ui.View.OpenSettings(); ui.Paint();
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("settings.testplay"));
        var bounds = ui.View.TestplaySpeedBounds;
        for (int i = 0; i < 5; i++) ui.Click(bounds.X + 12, bounds.Y + 16);
        Check(ui.View.LibrarySettings.TestplaySpeed == 1, "Speed setting escaped its draft.");
        string path = Path.GetFullPath("artifacts/tests/testplay-speed-settings.json");
        ui.View.ApplySettings(path);
        Check(ui.View.LibrarySettings.TestplaySpeed == .75 && LibrarySettings.Load(path).TestplaySpeed == .75,
            "Independent testplay speed did not persist.");
        ui.Key(27); ui.View.StartTestplay();
        Check(ui.View.IsTestplaying && ui.View.PlaybackSpeed == .75, "Configured testplay speed was not applied.");
        ui.View.StopTestplay(true);
        Check(ui.View.PlaybackSpeed == .5 && ui.View.Document.ContentEquals(map), "Exit failed to restore editor speed and content.");
        var audioCalls = new List<string>();
        ui.View.UpdateTransport(2000, 20000, true, true, false, null, null);
        ui.View.RequestPausePlayback = () => audioCalls.Add("pause");
        ui.View.RequestPlaybackSpeed = _ => audioCalls.Add("speed");
        ui.View.RequestSeek = _ => audioCalls.Add("seek");
        ui.View.RequestTogglePlayback = () => audioCalls.Add("play");
        ui.View.StartTestplay();
        Check(audioCalls.SequenceEqual(new[] { "pause", "speed", "seek", "play" }),
            "Playing audio must restart at the independent speed before testplay resumes.");
        ui.View.StopTestplay();
        Check(ui.View.PlaybackSpeed == .5 && audioCalls.TakeLast(3).SequenceEqual(new[] { "pause", "speed", "seek" }),
            "Audio exit failed to restore editor speed before seeking.");
        Check(new LibrarySettings().TestplaySpeed == 1
            && new LibrarySettings { TestplaySpeed = double.NaN }.TestplaySpeed == 1
            && new LibrarySettings { TestplaySpeed = 0 }.TestplaySpeed == .1
            && new LibrarySettings { TestplaySpeed = 2 }.TestplaySpeed == 1.5, "Testplay speed defaults or bounds are invalid.");
    }
    public static void LeadIn()
    {
        var settingsUi = new Ui();
        settingsUi.View.OpenSettings(); settingsUi.Paint();
        settingsUi.ClickText(FruitsAtelier.Localization.Strings.Get("settings.testplay"));
        var bounds = settingsUi.View.TestplayStartupDelayBounds;
        float rightX = bounds.Right - 12, leftX = bounds.X + 12;
        float arrowY = bounds.Y + 16;
        settingsUi.Click(rightX, arrowY);
        settingsUi.View.PointerDoubleClick(rightX, arrowY, false, false); settingsUi.Paint();
        settingsUi.View.ApplySettings(Path.GetFullPath("artifacts/tests/testplay-delay-clicks.json"));
        Check(settingsUi.View.LibrarySettings.TestplayStartupDelaySeconds == 1,
            "Rapid consecutive lead-in arrow clicks did not both apply.");
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
        Check(ui.View.IsTestplaying && ui.View.PlayheadMs == -1000, "Testplay did not provide two seconds before the first note.");
        clock.Advance(750); ui.Paint();
        Check(ui.View.PlayheadMs == -250 && sounds == 0, "Preparation did not advance gameplay before audio zero.");
        ui.Key(112);
        Check(!ui.View.IsTestplaying && ui.View.PlayheadMs == 500, "F1 did not return to the selected position.");
        ui.View.KeyUp(112);

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
