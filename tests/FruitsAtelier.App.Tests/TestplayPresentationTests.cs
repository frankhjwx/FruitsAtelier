using System.Buffers.Binary;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TestplayPresentationTests
{
    public static void DrainTime()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(timeProvider: new ManualTime());
                var map = new MapDocument { DurationMs = 240000, IsDemo = false };
                map.Fruits.AddRange([new() { TimeMs = 10000 }, new() { TimeMs = 200000 }]);
                map.BananaShowers.Add(new() { TimeMs = 180000, EndTimeMs = 220000 });
                OsuTimeline.AddBreak(map, 20000, 40000);
                OsuTimeline.AddBreak(map, 30000, 45000);
                OsuTimeline.AddBreak(map, 0, 5000);
                OsuTimeline.AddBreak(map, 225000, 230000);
                Show(map, "3:05 (185s)", 100000);
                var history = (EditorHistory)typeof(FruitsAtelier.App.Editor.EditorView).GetProperty("history",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ui.View)!;
                history.Begin("Add break"); OsuTimeline.AddBreak(history.Document, 60000, 70000); history.Commit();
                ui.View.StartTestplay(); ui.Paint();
                Check(ui.Canvas.Texts.Any(text => text.Value == "Total Drain Time: 2:55 (175s)"), "Break edits did not refresh cached drain time.");
                ui.View.StopTestplay(); ui.Key('Z', ctrl: true);
                ui.View.StartTestplay(); ui.Paint();
                Check(ui.Canvas.Texts.Any(text => text.Value == "Total Drain Time: 3:05 (185s)"), "Undo did not restore drain time.");
                ui.View.StopTestplay();

                map = new MapDocument { DurationMs = 30000, IsDemo = false };
                map.Fruits.AddRange([new() { TimeMs = 10000 }, new() { TimeMs = 14000 }]);
                var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 2 };
                track.Nodes.AddRange([new() { TimeMs = 12000, X = 100 }, new() { TimeMs = 17000, X = 200 }]);
                map.Tracks.Add(track);
                Show(map, "0:12 (12s)");
                map.Tracks.Clear();
                var slider = new ImportedSlider { TimeMs = 12000, X = 100, Y = 192, PathType = 'L', PixelLength = 1400, SpanCount = 2 };
                slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
                map.ImportedSliders.Add(slider);
                map.SliderMultiplier = 1.4;
                map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
                Show(map, "0:12 (12s)");

                map = new MapDocument { DurationMs = 20000, IsDemo = false };
                map.BananaShowers.Add(new() { TimeMs = 10000, EndTimeMs = 11000 });
                Show(map, "0:01 (1s)");
                map.BananaShowers.Clear(); map.Fruits.Add(new() { TimeMs = 10000 });
                Show(map, "0:00 (0s)");

                map = new MapDocument { DurationMs = 3700000, IsDemo = false };
                map.Fruits.AddRange([new() { TimeMs = 10000 }, new() { TimeMs = 3670000 }]);
                OsuTimeline.AddBreak(map, 100000, 101000);
                Show(map, "60:59 (3659s)");

                void Show(MapDocument document, string expected, double position = 0)
                {
                    ui.LoadDocument(document);
                    ui.View.UpdateTransport(position, document.DurationMs, true, false, false, null, null);
                    var before = ui.View.Document.DeepClone();
                    ui.View.StartTestplay(); ui.Paint();
                    Check(ui.Canvas.Texts.Any(text => text.Value == "Total Drain Time: " + expected), "Drain time did not match object duration minus bounded break coverage.");
                    Check(ui.View.Document.ContentEquals(before), "Displaying drain time changed content.");
                    ui.View.StopTestplay();
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

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
                Check(ui.View.TestplayPaused && !ui.View.TestplayPauseMenuVisible, "Menu Retry begins the Continue reaction transition");
                clock.Advance(599); ui.Paint(); Near(0, ui.View.PlayheadMs);
                Check(ui.View.TestplayPaused, "Menu Retry keeps judgement frozen before the deadline");
                clock.Advance(1); ui.Paint();
                Check(!ui.View.TestplayPaused, "Menu Retry resumes after 600 ms");
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
            WarningTiming();
            DimSlider();
            SettingsDimSlider();
            FullyDimBackground();
        }
        finally { L.SetLanguage(language); }
    }


    private static void FullyDimBackground()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
                var map = new MapDocument { SourcePath = Path.GetFullPath("artifacts/tests/full-dim/map.osu") };
                map.Fruits.AddRange([new Fruit { TimeMs = 10000, X = 256 }, new Fruit { TimeMs = 20000, X = 256 }]);
                map.OriginalSections.Add(new OsuSection { Name = "Events", Lines = { "0,0,\"background.jpg\",0,0", "2,12000,16000" } });
                ui.Canvas.AcceptBackgrounds = true; ui.LoadDocument(map);
                var before = ui.View.Document.DeepClone();
                ui.View.LibrarySettings.BackgroundDim = 37;
                int saves = 0; ui.View.RequestViewPreference = () => saves++;
                ui.ClickText(L.Get("ui.view")); ui.ClickText(L.Get("settings.forceBackgroundDim"));
                Check(ui.View.LibrarySettings.ForceBackgroundDim && saves == 1, "View enables and saves full dim immediately");
                ui.View.StartTestplay(); ui.Paint(); Near(1, Dim(ui));
                clock.Advance(12800); ui.Paint(); Near(1, Dim(ui));
                ui.View.LibrarySettings.BackgroundDim = 0; ui.Paint(); Near(1, Dim(ui));
                Check(ui.View.Document.ContentEquals(before), "Full dim preserves beatmap content");
                ui.View.StopTestplay();
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
                ui.ClickText("✓ " + L.Get("settings.forceBackgroundDim"));
                Check(ui.View.LibrarySettings.ForceBackgroundDim, "Settings full dim remains a draft before Apply");
                string path = Path.GetFullPath("artifacts/tests/full-dim-settings.json");
                ui.View.ApplySettings(path); ui.Paint();
                var saved = LibrarySettings.Load(path);
                Check(!saved.ForceBackgroundDim && saved.BackgroundDim == 0, "Apply saves full dim separately from its percentage");
                ui.Key(27); ui.View.KeyUp(27);
                ui.View.StartTestplay(); ui.Paint(); Near(0, Dim(ui)); ui.View.StopTestplay();
                Check(!new LibrarySettings().ForceBackgroundDim, "Full dim defaults off for existing settings");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void SettingsDimSlider()
    {
        var ui = new Ui();
        ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.testplay"));
        var bounds = ui.View.SettingsBounds;
        float left = bounds.X + 266, width = Math.Min(280, bounds.Width - 262) - 72, y = bounds.Y + 400;
        int saved = ui.View.LibrarySettings.BackgroundDim;
        ui.View.PointerDown(left + width * .25f, y, 0, false, false);
        ui.View.PointerMove(left + width * .5f, y, false, false);
        ui.View.PointerUp(left + width * .5f, y, 0); ui.Paint();
        Check(ui.View.LibrarySettings.BackgroundDim == saved && !ui.View.WantsCapture, "Settings dim drag only changes the draft and releases capture");
        string path = Path.GetFullPath("artifacts/tests/settings-dim-slider.json");
        ui.View.ApplySettings(path); ui.Paint();
        Check(ui.View.LibrarySettings.BackgroundDim == 50 && LibrarySettings.Load(path).BackgroundDim == 50, "Apply persists the dim slider draft");
        ui.Click(left, y);
        ui.Key(27); ui.View.KeyUp(27);
        Check(ui.View.LibrarySettings.BackgroundDim == 50 && !ui.View.WantsCapture, "Closing Settings discards unapplied dim changes");
    }

    private static void DimSlider()
    {
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        var map = new MapDocument(); map.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
        ui.LoadDocument(map); var before = ui.View.Document.DeepClone();
        int saves = 0; ui.View.RequestViewPreference = () => saves++;
        ui.View.StartTestplay(); ui.Paint(); ui.Key(27); ui.View.KeyUp(27);
        clock.Advance(300); ui.Paint();
        float left = ui.Width / 2f - 124, y = ui.Height - 39, width = 248;
        ui.View.PointerDown(left + width / 2, y, 0, false, false);
        Check(ui.View.LibrarySettings.BackgroundDim == 50 && ui.View.WantsCapture, "Dim slider clicks set the percentage and capture the pointer");
        ui.View.PointerMove(left - 100, y, false, false);
        Check(ui.View.LibrarySettings.BackgroundDim == 0, "Dim dragging clamps at zero outside the control");
        ui.View.PointerMove(left + width + 100, y, false, false);
        Check(ui.View.LibrarySettings.BackgroundDim == 100 && saves == 0, "Dim dragging clamps at 100 without saving every movement");
        ui.View.PointerUp(left + width * .75f, y, 0); ui.Paint();
        Check(ui.View.LibrarySettings.BackgroundDim == 75 && !ui.View.WantsCapture && saves == 1, "Release applies the final value and persists once");
        ui.View.PointerDown(left, y, 0, false, false);
        ui.View.CancelInteraction(preserveTestplay: true);
        Check(!ui.View.WantsCapture && saves == 2, "Focus cancellation ends and saves the dim drag");
        ui.View.PointerMove(left + width, y, false, false);
        Check(ui.View.LibrarySettings.BackgroundDim == 0, "Pointer movement after cancellation does not change dim");
        Check(ui.View.TestplayPaused && ui.View.PlayheadMs == 0 && ui.View.Document.ContentEquals(before), "Dim controls preserve paused gameplay and beatmap content");
        ui.View.StopTestplay();
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
        Near(0, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
        clock.Advance(150); ui.Paint();
        Near(.5, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
        Check(ui.View.PlayheadMs == 0, "Pause freezes gameplay during its fade-in");
        clock.Advance(150); ui.Paint();
        Near(1, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
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
        Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("arrow-warning.png")), "Skip does not start the warning three seconds early");
        ui.Key(27); ui.View.KeyUp(27); ui.Key(40);
        Check(ui.Canvas.Images.Count(i => i.Path.EndsWith("arrow-pause.png")) == 2, "Keyboard selection displays two skin arrows");
        var button = ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Bounds;
        ui.View.PointerMove(button.X + button.Width / 2, button.Y + button.Height / 2, false, false); ui.Paint();
        clock.Advance(600); ui.Paint();
        Check(ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Bounds.Width > button.Width, "Hover scales the button");
        Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("arrow-pause.png")), "Mouse movement clears keyboard arrows");
        ui.Key(13); clock.Advance(100); ui.Paint();
        Check(ui.View.TestplayPaused && ui.Canvas.Images.Any(i => i.Path.EndsWith("pause-continue.png")), "Fade retains the menu while gameplay is frozen");
        Near(5d / 6, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
        ui.Key(27); ui.View.KeyUp(27);
        Near(5d / 6, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
        clock.Advance(150); ui.Paint();
        Near(11d / 12, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
        clock.Advance(150); ui.Paint();
        Near(1, ui.Canvas.Images.Single(i => i.Path.EndsWith("pause-continue.png")).Opacity);
        Check(ui.View.TestplayPauseMenuVisible, "Cancelling resume fades back in without an opacity jump");
        ui.Key(13); clock.Advance(600); ui.Paint();
        Check(!ui.View.TestplayPaused && !ui.Canvas.Images.Any(i => i.Path.EndsWith("pause-continue.png")), "Menu disappears before play resumes");
        ui.View.StopTestplay(); Check(!ui.View.TestplayUsesCursor, "Leaving testplay restores the system cursor");
    }

    private static void WarningTiming()
    {
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        string folder = Path.GetFullPath("artifacts/tests/testplay-warning"); Directory.CreateDirectory(folder);
        Header(folder, "arrow-warning.png", 32, 32);
        var map = new MapDocument();
        map.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
        map.Fruits.Add(new Fruit { TimeMs = 18000, X = 256 });
        map.OriginalSections.Add(new OsuSection { Name = "Events", Lines = { "2,12000,16000" } });
        ui.LoadDocument(map); ui.View.LoadSkin(folder); ui.View.StartTestplay(); ui.Paint();
        foreach (int end in new[] { 10000, 16000 })
        {
            At(end - 1451, 0);
            for (int flash = 0; flash < 7; flash++)
            {
                int start = end - 1450 + flash * 200;
                At(start, 4); At(start + 99, 4);
                At(start + 100, 0); At(start + 199, 0);
            }
            At(end, 0);
        }
        ui.View.StopTestplay();
        void At(int time, int count)
        {
            clock.Advance(time - ui.View.PlayheadMs); ui.Paint();
            var arrows = ui.Canvas.Images.Where(i => i.Path.EndsWith("arrow-warning.png")).ToArray();
            Check(arrows.Length == count, $"Warning at {time} ms: expected {count} arrows, got {arrows.Length}");
            Check(arrows.All(i => i.Opacity == 1), "Visible warnings are fully opaque");
        }
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
