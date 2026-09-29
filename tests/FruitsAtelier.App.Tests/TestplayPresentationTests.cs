using System.Buffers.Binary;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TestplayPresentationTests
{
    public static void ControlsAndBackground()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
                string folder = Path.GetFullPath("artifacts/tests/testplay-presentation");
                Directory.CreateDirectory(folder);
                var map = new MapDocument { SourcePath = Path.Combine(folder, "map.osu") };
                map.Fruits.AddRange([new Fruit { TimeMs = 10000, X = 256 }, new Fruit { TimeMs = 20000, X = 256 }]);
                map.OriginalSections.Add(new OsuSection { Name = "Events", Lines = { "0,0,\"background,photo.jpg\",0,0", "2,12000,16000" } });
                ui.Canvas.AcceptBackgrounds = true;
                ui.LoadDocument(map);
                var before = ui.View.Document.DeepClone();
                var sounds = new List<string>(); var loops = new List<bool>();
                var prepared = new List<Hitsound>(); ui.View.RequestPrepareHitsound = prepared.Add;
                ui.View.RequestAuditionHitsound = sound => sounds.Add(sound.Name ?? "");
                ui.View.RequestTestplayMenuLoop = sound => loops.Add(sound is not null);
                Check(ui.View.LibrarySettings.BackgroundDim == 90, "Default dim is 90%");
                Check(!ui.Canvas.Images.Any(i => i.Path == Path.Combine(folder, "background,photo.jpg")), "Editing canvas has an opaque backing");
                ui.View.StartTestplay(); ui.Paint();
                Check(ui.Canvas.Images.Any(i => i.Path == Path.Combine(folder, "background,photo.jpg")), "Testplay resolves the quoted background path");
                Check(ui.View.TestplaySkipVisible, "Long intro offers Skip");
                Check(prepared.Where(h => HitsoundDefaults.IsInterface(h.Name)).All(h => h.FilePath == HitsoundDefaults.FindInterface(h.Name)), "Disabled skin sounds resolve to packaged osu resources");
                ui.Key(32); Near(7000, ui.View.PlayheadMs);
                Check(!ui.View.TestplaySkipVisible && ui.View.TestplayCombo == 0, "Skip preserves a three-second lead and judges no objects");
                ui.View.KeyUp(32); clock.Advance(1000); ui.Paint(); clock.Advance(800); ui.Paint();
                Near(.9, Dim(ui));
                ui.Key(27); ui.Key(27);
                Check(ui.View.TestplayPauseMenuVisible, "Held Escape opens the menu once");
                Check(loops.Last() && sounds.Contains("menuhit"), "Pause starts its loop and opening sample");
                double paused = ui.View.PlayheadMs;
                clock.Advance(1000); ui.Key(39); Near(paused, ui.View.PlayheadMs);
                ui.Key(13);
                Check(ui.View.TestplayPaused && !ui.View.TestplayPauseMenuVisible, "Continue in gameplay stays frozen while the menu fades out");
                clock.Advance(599); ui.Paint(); Near(paused, ui.View.PlayheadMs);
                clock.Advance(1); ui.Paint(); Check(!ui.View.TestplayPaused, "Fade completion resumes gameplay");
                clock.Advance(12000 - paused); ui.Paint(); clock.Advance(800); ui.Paint();
                Near(.6, Dim(ui));
                clock.Advance(1200); ui.Paint();
                Check(!sounds.Any(n => n is "sectionpass" or "sectionfail"), "Break never plays section result samples");
                ui.View.KeyUp(27); ui.Key(27); ui.Key(13);
                Check(ui.View.TestplayPaused, "Break Continue also waits for the fade");
                clock.Advance(600); ui.Paint();
                Check(!ui.View.TestplayPaused, "Break Continue resumes after the fade");
                clock.Advance(1676); ui.Paint(); clock.Advance(800); ui.Paint(); Near(.9, Dim(ui));
                ui.View.KeyUp(27); ui.Key(27); ui.Key(40); ui.Key(13);
                Near(0, ui.View.PlayheadMs); Check(ui.View.TestplayCombo == 0, "Retry resets score and returns to the session start");
                var skip = ui.View.TestplaySkipBounds;
                ui.Click(skip.X + skip.Width / 2, skip.Y + skip.Height / 2); Near(7000, ui.View.PlayheadMs);
                ui.View.KeyUp(27); ui.Key(27); ui.Key(38); ui.Key(13);
                Check(!ui.View.IsTestplaying && ui.View.Document.ContentEquals(before), "Up wraps to Back; menus preserve content");

                Header(folder, "pause-continue@2x.png", 800, 160);
                Header(folder, "pause-overlay.png", 1366, 768);
                Header(folder, "play-skip-0.png", 200, 100); Header(folder, "play-skip-1@2x.png", 400, 200);
                File.WriteAllText(Path.Combine(folder, "skin.ini"), "[General]\nAnimationFramerate: 10");
                File.WriteAllBytes(Path.Combine(folder, "pause-continue-hover.wav"), HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
                ui.View.LibrarySettings.UseSkinSounds = true;
                prepared.Clear();
                ui.View.LoadSkin(folder); ui.View.StartTestplay(); ui.Paint();
                Check(prepared.Single(h => h.Name == "pause-continue-hover").FilePath == Path.Combine(folder, "pause-continue-hover.wav"), "Enabled skin sounds prefer the custom interface sample");
                clock.Advance(100); ui.Paint();
                Check(ui.Canvas.Images.Any(i => i.Path.EndsWith("play-skip-1@2x.png")), "Skip renders animated custom @2x frames");
                ui.View.KeyUp(27); ui.Key(27);
                var button = ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue@2x.png")).Bounds;
                Near(400 * ui.Height / 768, button.Width);
                Check(ui.Canvas.Images.Any(i => i.Path.EndsWith("pause-overlay.png")), "Custom pause overlay renders");
                ui.Click(button.X + button.Width / 2, button.Y + button.Height / 2);
                Check(ui.View.TestplayPaused && !ui.View.TestplayPauseMenuVisible, "Custom button begins menu fade");
                clock.Advance(600); ui.Paint();
                Check(!ui.View.TestplayPaused, "Custom button resumes after fade");
                ui.View.StopTestplay();
                ui.View.LibrarySettings.BackgroundDim = 37;
                string settingsPath = Path.Combine(folder, "settings.json");
                ui.View.LibrarySettings.Save(settingsPath);
                Check(LibrarySettings.Load(settingsPath).BackgroundDim == 37, "Dim persists independently of map content");
            }
            AudioSkip();
            PointerAndArrows();
        }
        finally { L.SetLanguage(language); }
    }


    private static void PointerAndArrows()
    {
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        string folder = Path.GetFullPath("artifacts/tests/testplay-pointer"); Directory.CreateDirectory(folder);
        foreach (string name in new[] { "cursor", "cursormiddle", "cursortrail", "arrow-pause", "arrow-warning", "pause-continue" })
            Header(folder, name + ".png", name == "pause-continue" ? 320 : 32, name == "pause-continue" ? 64 : 32);
        File.WriteAllText(Path.Combine(folder, "skin.ini"), "[General]\nCursorRotate: 0\nCursorCentre: 0\nCursorExpand: 0");
        var map = new MapDocument(); map.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
        ui.LoadDocument(map); ui.View.LoadSkin(folder); ui.View.StartTestplay(); ui.Paint();
        Check(ui.View.TestplayUsesCursor, "Testplay replaces the system cursor");
        Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("cursor.png")), "Running gameplay hides the skin cursor");
        ui.Key(27); ui.View.KeyUp(27);
        ui.View.PointerMove(100, 100, false, false); clock.Advance(20);
        ui.View.PointerMove(200, 100, false, false); ui.Paint();
        Check(ui.Canvas.Images.Any(i => i.Path.EndsWith("cursortrail.png")), "Moving the skin cursor leaves a trail");
        Check(ui.Canvas.Images.Any(i => i.Path.EndsWith("cursormiddle.png")), "Cursor middle renders above its trail");
        var cursor = ui.Canvas.Images.Single(i => i.Path.EndsWith("cursor.png"));
        Near(200, cursor.Bounds.X); Near(100, cursor.Bounds.Y);
        clock.Advance(501); ui.Paint();
        Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("cursortrail.png")), "Trail expires on real time");
        ui.Key(13); clock.Advance(600); ui.Paint();
        ui.Key(32); ui.View.KeyUp(32);
        Check(ui.Canvas.Images.Count(i => i.Path.EndsWith("arrow-warning.png")) == 4, "Intro lead displays four warning arrows");
        ui.Key(27); ui.View.KeyUp(27); ui.Key(40);
        Check(ui.Canvas.Images.Count(i => i.Path.EndsWith("arrow-pause.png")) == 2, "Keyboard selection displays two skin arrows");
        var button = ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Bounds;
        ui.View.PointerMove(button.X + button.Width / 2, button.Y + button.Height / 2, false, false); ui.Paint();
        clock.Advance(600); ui.Paint();
        Check(ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Bounds.Width > button.Width, "Hover scales the button");
        Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("arrow-pause.png")), "Mouse movement clears keyboard arrows");
        ui.Key(13); clock.Advance(100); ui.Paint();
        Check(ui.View.TestplayPaused && ui.Canvas.Images.Any(i => i.Path.EndsWith("pause-continue.png")), "Fade retains the menu while gameplay is frozen");
        clock.Advance(500); ui.Paint();
        Check(!ui.View.TestplayPaused && !ui.Canvas.Images.Any(i => i.Path.EndsWith("pause-continue.png")), "Menu disappears before play resumes");
        ui.View.StopTestplay(); Check(!ui.View.TestplayUsesCursor, "Leaving testplay restores the system cursor");
    }

    private static void AudioSkip()
    {
        var time = new ManualTime();
        var map = new MapDocument(); map.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
        var objects = CatchStreamConverter.Convert(map).Objects;
        var session = new CatchTestplaySession(new CatchTestplay(objects, 5, 0), new CatchTestplayClock(0, 1, 0, true),
            0, true, false, 37, 39, 16, time, 5, []);
        Check(!session.SkipIntro(10000), "Skip cannot cross a note");
        Check(session.SkipIntro(7000), "Pending audio can seek forward safely");
        time.Advance(2000); session.Tick(); Near(7000, session.Capture().TimeMs);
        session.UpdateAudio(1, 2000, 12000, true, true, false, false);
        Near(7000, session.Capture().TimeMs);
        session.UpdateAudio(7001, 2000, 12000, true, true, false, false);
        Near(7001, session.Capture().TimeMs);
        Check(!session.Ended && session.Combo == 0, "Stale device samples do not end or judge the skipped intro");
    }

    private static float Dim(Ui ui) => ui.Canvas.PaintCalls.Last(p => p.Color == 0 && p.FillBounds?.Width == ui.Width).Opacity;
    private static void Header(string folder, string name, int width, int height)
    {
        var bytes = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12)); BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height); File.WriteAllBytes(Path.Combine(folder, name), bytes);
    }
    private sealed class ManualTime : TimeProvider
    {
        private double time;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => (long)time;
        public void Advance(double value) => time += value;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}");
}
