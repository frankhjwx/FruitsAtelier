using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class TestplayTests
{
    public static void PauseAndExitShortcuts()
    {
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        var map = new MapDocument(); map.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
        ui.LoadDocument(map); ui.Key(36); var before = ui.View.Document.DeepClone();
        ui.View.StartTestplay(); clock.Advance(500); ui.Paint();
        ui.Key('P', ctrl: true); ui.Key('P', ctrl: true);
        Check(ui.View.TestplayPaused, "held pause toggles only once");
        clock.Advance(2000); ui.Key(39); ui.Paint();
        Near(500, ui.View.PlayheadMs); Near(256, ui.View.TestplayCatcherX);
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("testplay.paused")), "pause state appears with shortcuts");
        ui.View.KeyUp('P'); ui.Key('P', ctrl: true); ui.View.KeyUp('P');
        clock.Advance(100); ui.Paint(); Near(600, ui.View.PlayheadMs);
        ui.Key(113); Check(!ui.View.IsTestplaying, "F2 exits"); Near(600, ui.View.PlayheadMs);
        ui.View.StartTestplay(); clock.Advance(250); ui.Paint(); ui.Key(112);
        Near(600, ui.View.PlayheadMs); Check(!ui.View.IsTestplaying && ui.View.Document.ContentEquals(before), "F1 returns to testplay start without edits");

        double now = clock.GetTimestamp() * 1000d / clock.TimestampFrequency;
        var objects = CatchStreamConverter.Convert(map).Objects;
        var session = new CatchTestplaySession(new CatchTestplay(objects, 5, 0), new CatchTestplayClock(0, 1, now, false),
            0, true, true, 37, 39, 16, clock, 5, []);
        session.TogglePause(); clock.Advance(1000);
        session.UpdateAudio(40, now, 20000, true, false, false, false);
        Check(!session.Ended && session.Paused, "audio pause does not end testplay");
        session.TogglePause(); session.UpdateAudio(40, now, 20000, true, false, false, false);
        Check(!session.Ended, "resume waits for asynchronous audio start");
        now = clock.GetTimestamp() * 1000d / clock.TimestampFrequency;
        session.UpdateAudio(50, now, 20000, true, true, false, false);
        clock.Advance(20); session.Tick();
        Check(!session.Ended && session.Capture().TimeMs >= 50 && session.Capture().TimeMs < 100, "audio resume does not include paused wall time");
    }

    public static void AutoplaySwitching()
    {
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        var map = new MapDocument();
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 0 }, new Fruit { TimeMs = 1100, X = 512 },
            new Fruit { TimeMs = 2000, X = 200 }, new Fruit { TimeMs = 10000, X = 256 }]);
        ui.LoadDocument(map); var snapshot = map.DeepClone(); int sounds = 0;
        ui.View.RequestHitsound = _ => sounds++;
        ui.View.StartTestplay();
        clock.Advance(500); ui.Paint();
        ui.Key(9); ui.Key(9);
        Check(ui.View.TestplayAutoplay && ui.View.PlayheadMs == 500, "Tab enables autoplay once without seeking");
        ui.View.KeyDown(39, false, false); ui.View.KeyUp(39);
        clock.Advance(600); ui.Paint();
        Check(ui.View.TestplayCombo == 2 && sounds > 0, "autoplay catches distant notes and dispatches live sounds");
        Near(512, ui.View.TestplayCatcherX);
        ui.View.KeyUp(9); ui.Key(9); ui.View.KeyUp(9);
        Check(!ui.View.TestplayAutoplay, "a fresh Tab returns to manual control");
        double before = ui.View.TestplayCatcherX;
        clock.Advance(10); ui.Paint(); Near(before, ui.View.TestplayCatcherX);
        ui.Key(37); clock.Advance(20); ui.View.KeyUp(37);
        Near(before - 10, ui.View.TestplayCatcherX);
        Check(ui.View.Document.ContentEquals(snapshot), "autoplay never edits the map");
        ui.View.StopTestplay(); ui.View.StartTestplay();
        Check(!ui.View.TestplayAutoplay, "each testplay begins in manual mode");
    }

    public static void VisualTransformsAndColours()
    {
        ConvertedCatchObject Note(CatchObjectKind kind) => new(Guid.NewGuid(), 0, kind, 1234, 256, 256, 256, 0);
        var fruit = Note(CatchObjectKind.Fruit); var banana = Note(CatchObjectKind.Banana); var drop = Note(CatchObjectKind.Droplet);
        Near(.711679995059967, CatchObjectVisual.RandomSingle(1234, 1));
        Near(8.4672, CatchObjectVisual.At(fruit, 0, 5).Rotation);
        Check(CatchObjectVisual.At(fruit, 0, 5) == CatchObjectVisual.At(fruit, 1234, 5), "fruit rotation remains deterministic through fall and seek");
        var start = CatchObjectVisual.At(banana, 34, 5); var end = CatchObjectVisual.At(banana, 1234, 5);
        Check(start.Scale > end.Scale && start.Rotation != end.Rotation, "banana rotates and shrinks during approach");
        Near(1, end.Scale);
        Check(end == CatchObjectVisual.At(banana, 1600, 5, true), "caught bananas retain arrival rotation and size");
        Check(CatchObjectVisual.At(banana, 1400, 5).Scale < end.Scale, "missed bananas continue their transforms");
        Check(CatchObjectVisual.At(drop, 1234, 5).Rotation > CatchObjectVisual.At(drop, 34, 5).Rotation, "droplets spin during approach");
        var clock = new ManualTime(); var ui = new Ui(timeProvider: clock);
        var map = new MapDocument();
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 2000, X = 256 },
            new Fruit { TimeMs = 3000, X = 256, OriginalLine = "256,192,3000,21,0" }, new Fruit { TimeMs = 10000, X = 256 }]);
        var colours = new OsuSection { Name = "Colours" };
        colours.Lines.Add("Combo1 : 255,0,0"); colours.Lines.Add("Combo2 : 0,255,0"); colours.Lines.Add("Combo3 : 0,0,255");
        map.OriginalSections.Add(colours);
        ui.LoadDocument(map); ui.View.StartTestplay();
        clock.Advance(1000); ui.Paint();
        Check(ui.Canvas.Circles.Any(c => c.Color == 0x00FF00 && c.Filled), "first combo uses the beatmap palette");
        clock.Advance(1000); ui.Paint();
        Check(ui.Canvas.Circles.Count(c => c.Color == 0x00FF00 && c.Filled) >= 2, "objects in one combo retain their colour on the plate");
        clock.Advance(1000); ui.Paint();
        Check(ui.Canvas.Circles.Any(c => c.Color == 0xFF0000 && c.Filled), "new combo applies the stable colour skip offset");
        ui.View.StopTestplay();
        string folder = Path.GetFullPath("artifacts/tests/visual-rotation-skin"); Directory.CreateDirectory(folder);
        byte[] header = new byte[24]; new byte[] {137, 80, 78, 71, 13, 10, 26, 10}.CopyTo(header, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), 128);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), 128);
        foreach (string name in new[] { "pear", "grapes", "apple", "orange" }) File.WriteAllBytes(Path.Combine(folder, "fruit-" + name + ".png"), header);
        ui.View.LoadSkin(folder); ui.Paint();
        Check(ui.Canvas.Sprites.Count > 0 && ui.Canvas.Sprites.All(s => s.Rotation == 0), "main editing canvas keeps fruit static");
        ui.View.UpdateTransport(900, 20000, true, false, false, null, null);
        ui.OpenPreview(); ui.Paint();
        Check(ui.Canvas.Sprites.Any(s => s.Rotation != 0), "right-hand preview applies fruit rotation");
        ui.View.StartTestplay(); clock.Advance(100); ui.Paint();
        Check(ui.Canvas.Sprites.Any(s => s.Rotation != 0), "F5 playtest applies the same fruit rotation");
    }
    public static void EscapeReturnsToEditor()
    {
        var ui = new Ui(timeProvider: new ManualTime());
        var map = new MapDocument();
        map.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
        ui.LoadDocument(map);
        ui.Resize(1440, 900);
        ui.View.StartTestplay();
        ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("testplay.hintQuickExit")), "testplay shows legacy shortcuts");
        var stage = ui.Canvas.Clips.Last();
        Check(stage.Y == 0 && stage.Height == 900, "landscape testplay uses full available height");
        ui.View.PointerDown(1340, 24, 0, false, false); ui.View.PointerUp(1340, 24, 0);
        Check(ui.View.IsTestplaying, "the upper-right canvas has no exit hit target");
        ui.Resize(600, 900);
        stage = ui.Canvas.Clips.Last();
        Check(stage.X == 0 && stage.Width == 600 && stage.Y == 225 && stage.Height == 450,
            "tall windows use full width and center the playfield without stretching");
        ui.Key(27); ui.Paint();
        Check(!ui.View.IsTestplaying && !ui.View.LibraryVisible && ui.View.HasEditorProject,
            "Escape returns from testplay to the editor");
        ui.Key(27); ui.Paint(); ui.Key(27);
        Check(!ui.View.LibraryVisible && ui.View.HasEditorProject,
            "held Escape cannot continue through the editor into Library");
        ui.View.KeyUp(27); ui.Key(27);
        Check(ui.View.LibraryVisible, "a fresh Escape press retains editor navigation");
        ui.View.KeyUp(27); ui.LoadDocument(map); ui.View.StartTestplay(); ui.Key(27);
        ui.View.CancelInteraction(); ui.Key(27);
        Check(ui.View.LibraryVisible, "focus loss clears Escape suppression when key-up is lost");
    }
    public static void LivePlate()
    {
        ConvertedCatchObject Note(double time, double x, CatchObjectKind kind = CatchObjectKind.Fruit)
            => new(Guid.NewGuid(), 0, kind, time, x, x, x, 0);
        var notes = new[] { Note(100, 256), Note(200, 258), Note(300, 256, CatchObjectKind.Droplet),
            Note(400, 256, CatchObjectKind.TinyDroplet), Note(500, 256) };
        var ends = new HashSet<(Guid, int)> { (notes[^1].SourceId, 0) };
        var auto = new CatchAutoPreview(notes, 5);
        var preview = new CatchPlatePreview(notes, auto, 5, ends);
        var live = new CatchPlate(5);
        foreach (var note in notes) live.Judge(note, auto.At(note.TimeMs).X, true, ends.Contains((note.SourceId, 0)));
        foreach (double time in new[] { 100d, 250, 400, 500, 625, 1000, 1250, 200 })
            Check(preview.At(time, 300).SequenceEqual(live.At(time, 300)), "live and preview share stack, explosion and rewind trajectories");
        var clock = new ManualTime();
        var game = new CatchTestplay([notes[0], Note(200, 0), notes[^1]], 5, 0);
        int sounds = 0;
        var session = new CatchTestplaySession(game, new(0, 1, 0, false), 0, false, false,
            37, 39, 16, clock, 5, ends, _ => sounds++);
        clock.Advance(100); session.Tick();
        var frozen = session.Capture();
        clock.Advance(100); session.Tick();
        var missed = session.Capture();
        Check(missed.Plate.Length == 1 && missed.MissedObjects.Length == 1 && sounds == 1, "only catches enter the stack and emit sound without rendering");
        session.SetKey(39, true); clock.Advance(20); session.SetKey(39, false);
        Check(session.Capture().Plate[0].X > frozen.Plate[0].X && frozen.X == 256, "stack follows movement while detached snapshots stay unchanged");
        clock.Advance(280); session.Tick();
        Check(session.Capture().Plate.Length == 2 && !session.Ended, "combo end releases the caught stack");
        clock.Advance(750); session.Tick();
        Check(session.Ended && session.Capture().Plate.Length == 0, "final explosion finishes before automatic return");
        var drop = new CatchPlate(5);
        drop.Judge(notes[0], 256, true, false); drop.Judge(notes[^1], 256, false, true);
        var before = drop.At(500, 256).Single(); var after = drop.At(600, 400).Single();
        Check(after.Y > before.Y && after.X == before.X && after.Opacity < before.Opacity, "missed combo end drops the previous stack at its release position");
    }
    public static void MovementAndJudgement()
    {
        ConvertedCatchObject Note(double time, double x, CatchObjectKind kind = CatchObjectKind.Fruit)
            => new(Guid.NewGuid(), 0, kind, time, x, x, x, 0);
        var game = new CatchTestplay([Note(100, 256), Note(200, 306), Note(300, 406),
            Note(350, 0, CatchObjectKind.TinyDroplet), Note(400, 0, CatchObjectKind.Banana), Note(500, 0)], 5, 0);
        game.Advance(100, false, false, false); Check(game.Combo == 1, "fruit catch increments combo");
        game.Advance(200, false, true, false); Near(306, game.X); Check(game.Combo == 2, "walk catches fruit");
        game.Advance(300, false, true, true); Near(406, game.X); Check(game.Combo == 3, "dash catches fruit");
        game.Advance(400, true, true, false); Near(406, game.X); Check(game.Combo == 3, "tiny/banana misses preserve combo; opposing keys cancel");
        game.Advance(500, false, false, false); Check(game.Combo == 0 && game.Finished, "miss breaks combo and last judgement completes");
        game.Advance(2000, true, false, true); Near(0, game.X); Check(game.FacingLeft, "left movement flips catcher");
        game.Advance(3000, false, true, true); Near(512, game.X); Check(!game.FacingLeft, "right movement restores catcher");

        var skipped = new CatchTestplay([Note(0, 0), Note(100, 256), Note(200, 256, CatchObjectKind.Droplet)], 5, 100);
        skipped.Advance(100, false, false, false); Check(skipped.Combo == 1, "past notes skipped; exact start included");
        skipped.Advance(200, false, false, false); Check(skipped.Combo == 2, "droplets count towards combo");
        var edge = new CatchTestplay([Note(100, 256d + CatchSize.CatchWidth(5) / 2)], 5, 0);
        edge.Advance(100, false, false, false); Check(edge.Combo == 1, "catch boundary is inclusive like osu");
        var hyper = new CatchTestplay([Note(100, 256), Note(200, 456)], 5, 0);
        hyper.Advance(100, false, false, false); Check(hyper.HyperDashing, "caught hyper fruit boosts movement");
        hyper.Advance(180, false, true, true); Check(hyper.X > 430, "hyper speed exceeds ordinary dash");
        hyper.Advance(200, false, true, false); Check(hyper.Combo == 2, "hyper target is catchable");
        var autoplay = new CatchAutoPreview([Note(1000, 100), Note(2000, 400), Note(3000, 400)], 5);
        Check(autoplay.FacingLeftAt(1000) && !autoplay.FacingLeftAt(2000) && !autoplay.FacingLeftAt(3000), "autoplay facing follows movement and persists while idle");
        var stream = new[] { Note(100, 306), Note(200, 356), Note(300, 406) };
        var coarse = new CatchTestplay(stream, 5, 0); var fine = new CatchTestplay(stream, 5, 0);
        coarse.Advance(300, false, true, false);
        for (int i = 1; i <= 300; i++) fine.Advance(i, false, true, false);
        Check(coarse.Combo == 3 && coarse.Combo == fine.Combo, "judgements split at note times across a delayed frame"); Near(coarse.X, fine.X);
    }

    public static void EditorLifecycle()
    {
        var map = new MapDocument { CircleSize = 5 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 2000, X = 356 }, new Fruit { TimeMs = 5000, X = 256 }]);
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock); ui.LoadDocument(map);
        void Transport(double position, double duration, bool ready, bool playing, bool loading, string? error, string? filename)
        {
            clock.AudioPosition(position);
            ui.View.UpdateTransport(position, duration, ready, playing, loading, error, filename);
        }
        var snapshot = ui.View.Document.DeepClone();
        int starts = 0, pauses = 0, sounds = 0, scheduled = 0;
        ui.View.RequestTogglePlayback = () => starts++;
        ui.View.RequestPausePlayback = () => pauses++;
        ui.View.RequestHitsound = _ => sounds++;
        ui.View.RequestScheduleHitsound = (_, _) => scheduled++;
        Transport(1000, 6000, true, false, false, null, null);
        ui.Key(116); Check(ui.View.IsTestplaying && starts == 1, "F5 enters from playhead");
        Transport(1000, 6000, true, true, false, null, null);
        Check(ui.View.TestplayCombo == 0 && sounds == 0, "pending play intent must not judge the first note before the device advances");
        Transport(1000.001, 6000, true, true, false, null, null);
        Check(ui.View.TestplayCombo == 1 && sounds > 0 && scheduled == 0, "live catches produce sound without autoplay scheduling");
        ui.Key(39); ui.Key(39);
        Transport(1100, 6000, true, true, false, null, null); Near(306, ui.View.TestplayCatcherX);
        ui.View.KeyUp(39);
        Transport(1200, 6000, true, true, false, null, null); Near(306, ui.View.TestplayCatcherX);
        ui.Key(16); ui.Key(39);
        Transport(1250, 6000, true, true, false, null, null); Near(356, ui.View.TestplayCatcherX);
        ui.View.KeyUp(39); ui.View.KeyUp(16);
        Transport(2000, 6000, true, true, false, null, null); ui.Paint();
        Check(ui.View.TestplayCombo == 2 && ui.Canvas.Texts.Any(t => t.Value == "2"), "combo burst renders after live catches");
        var before = ui.Canvas.Texts.Single(t => t.Value == "2").X;
        ui.Key(39); Transport(2100, 6000, true, true, false, null, null); ui.Paint();
        Check(ui.Canvas.Texts.Single(t => t.Value == "2").X > before, "combo follows catcher");
        ui.View.PointerDoubleClick(ui.Plot.X + 10, ui.Plot.Y + 10, false, false);
        ui.Key(46); ui.Key(90, true); ui.Key(32); ui.View.Wheel(400, 400, 120, false);
        Check(ui.View.Document.ContentEquals(snapshot) && !ui.View.IsDirty, "testplay isolates editing shortcuts and pointer input");
        ui.Key(27); Check(!ui.View.IsTestplaying && pauses == 1, "Esc pauses and returns"); Near(1000, ui.View.PlayheadMs);
        Transport(1000, 6000, true, false, false, null, null); ui.Key(116);
        Transport(5000, 6000, true, true, false, null, null);
        Check(ui.View.IsTestplaying, "last catch retains its plate animation");
        Transport(5750, 6000, true, true, false, null, null);
        Check(!ui.View.IsTestplaying, "last plate animation ends testplay"); Near(1000, ui.View.PlayheadMs);
        Transport(1000, 3000, true, false, false, null, null); ui.Key(116);
        Transport(3000, 3000, true, false, false, null, null);
        Check(!ui.View.IsTestplaying, "track EOF ends testplay before trailing notes");
        Transport(1000, 6000, true, false, false, null, null); ui.Key(116); ui.View.CancelInteraction();
        Check(!ui.View.IsTestplaying && pauses == 4, "focus cancellation also stops pending playback");
        Transport(5500, 6000, true, false, false, null, null); ui.Key(116);
        Check(!ui.View.IsTestplaying, "no remaining notes does not start playback");
        ui.View.LoadDocument(new MapDocument()); ui.Key(116); Check(!ui.View.IsTestplaying, "empty map exits immediately");
        ui.View.LoadDocument(map); ui.View.StartTestplay();
        Check(ui.View.IsTestplaying, "maps without audio use a silent clock");
        clock.Advance(25); ui.Paint();
        Check(ui.View.PlayheadMs > 0 && ui.View.IsTestplaying, "silent clock advances through gaps");
        ui.View.StopTestplay(); Near(0, ui.View.PlayheadMs);
    }

    public static void InputBetweenFrames()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        var map = new MapDocument();
        map.Fruits.Add(new Fruit { TimeMs = 10000, X = 0 });
        ui.LoadDocument(map);
        ui.View.UpdateTransport(1000, 20000, true, true, false, null, null);
        ui.View.StartTestplay();
        clock.Advance(2.5); ui.View.KeyDown(39, false, false);
        clock.Advance(2.75); ui.View.KeyUp(39);
        Near(257.375, ui.View.TestplayCatcherX);
        clock.Advance(1); ui.View.KeyDown(37, false, false);
        clock.Advance(2); ui.View.KeyDown(16, false, false);
        clock.Advance(1.25); ui.View.KeyUp(37); ui.View.KeyUp(16);
        Near(255.125, ui.View.TestplayCatcherX);
        clock.Advance(2.5); ui.Paint(); Near(255.125, ui.View.TestplayCatcherX);
        Check(ui.View.PlayheadMs > 1010, "render clock advances without a fresh audio snapshot");
        ui.View.UpdateTransport(1000, 20000, true, true, false, null, null);
        double before = ui.View.PlayheadMs;
        ui.View.KeyDown(39, false, false);
        clock.Advance(1); ui.View.KeyUp(39);
        Near(255.625, ui.View.TestplayCatcherX);
        Check(ui.View.PlayheadMs > before, "repeated audio position cannot freeze the input clock");
        ui.View.StopTestplay();

        foreach (double rate in new[] { .25, .5, 1 })
        {
            var interpolation = new CatchTestplayClock(1000, rate, 0, false);
            Near(1000 + 7.5 * rate, interpolation.At(7.5));
            interpolation.Synchronize(1000 + 10 * rate, 10, 13);
            Near(1000 + 13 * rate, interpolation.At(13));
            Near(1000 + 14 * rate, interpolation.At(14));
            interpolation.Synchronize(1000 + 10 * rate, 10, 16);
            Near(1000 + 17 * rate, interpolation.At(17));
        }
        var pending = new CatchTestplayClock(1000, 1, 0, true);
        pending.Synchronize(1000, 0, 150); Near(1000, pending.At(150));
        pending.Synchronize(1005, 155, 157); Near(1007, pending.At(157));
        Near(1008, pending.At(158));
        Near(1105, pending.At(1000));
        pending.Synchronize(1100, 1000, 1000);
        Check(pending.At(1001) >= 1105 && pending.At(1001) < 1110, "device recovery keeps time monotonic without running through a stall");
    }

    public static void MissedObjectsFall()
    {
        string previousLanguage = L.Language;
        try
        {
            foreach (string language in L.AvailableLanguages)
            {
                L.SetLanguage(language);
                var clock = new ManualTime();
                var ui = new Ui(timeProvider: clock);
                var map = new MapDocument { CircleSize = 5 };
                map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 1100, X = 100 }]);
                ui.LoadDocument(map);
                ui.View.UpdateTransport(1000, 5000, true, true, false, null, null);
                ui.View.StartTestplay(); ui.Paint();
                void Advance(double milliseconds)
                {
                    clock.Advance(milliseconds);
                    ui.View.UpdateTransport(1000 + clock.GetElapsedTime(0, clock.GetTimestamp()).TotalMilliseconds, 5000, true, true, false, null, null);
                    ui.Paint();
                }
                Check(ui.View.TestplayCombo == 1, "exact start is judged");
                var combo = ui.Canvas.Texts.Single(t => t.Value == "1");
                var plate = ui.Canvas.Fills.Last(f => f.Bounds.Height < 20 && f.Bounds.Width > 20).Bounds;
                Check(combo.Y < plate.Y && combo.Y + 32 < ui.Canvas.Clips.Last().Bottom, "combo stays above the catcher and within the playfield");
                float noteRadius = ui.Canvas.Circles.Where(c => c.Color == 0xFFFFFF && c.Filled).Max(c => c.Radius);
                Check(ui.Canvas.Circles.Count(c => c.Color == 0xFFFFFF && c.Filled && c.Radius == noteRadius) == 1, "caught notes move into the smaller plate stack");
                Advance(100);
                Check(ui.View.IsTestplaying && ui.View.TestplayCombo == 0, "last miss remains visible after judgement");
                var atLine = ui.Canvas.Circles.Single(c => c.Color == 0xFFFFFF && c.Filled && c.Radius == noteRadius);
                Advance(50);
                var falling = ui.Canvas.Circles.Single(c => c.Color == 0xFFFFFF && c.Filled && c.Radius == noteRadius);
                Check(falling.Y > atLine.Y && falling.Opacity < atLine.Opacity && falling.Opacity > 0, "miss passes through catch line and fades as it falls");
                Near(atLine.X, falling.X);
                Advance(199); Check(ui.View.IsTestplaying, "miss lifetime is retained");
                Advance(1); Check(ui.View.IsTestplaying, "plate continues after missed note fades");
                Advance(500); Check(!ui.View.IsTestplaying, "completion waits for plate drop animation");
            }
        }
        finally { L.SetLanguage(previousLanguage); }
    }

    public static void Bindings()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        string folder = Path.GetFullPath("artifacts/testplay-settings");
        var settings = new LibrarySettings { Workspace = folder, TestplayLeftKey = 65, TestplayRightKey = 68, TestplayDashKey = 32 };
        string path = Path.Combine(folder, "settings.json");
        settings.Save(path); var loaded = LibrarySettings.Load(path);
        Check(loaded.TestplayLeftKey == 65 && loaded.TestplayRightKey == 68 && loaded.TestplayDashKey == 32, "bindings persist");
        var old = System.Text.Json.JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Check(old.TestplayLeftKey == 37 && old.TestplayRightKey == 39 && old.TestplayDashKey == 16, "old settings get defaults");
        ui.View.InitializeLibrary(true, loaded); ui.Paint(); ui.ClickText(L.Get("library.settings"));
        ui.ClickText("A"); ui.Key(68); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "A") && ui.Canvas.Texts.Any(t => t.Value == "D"), "conflicting binding swaps actions");
        ui.ClickText("Space"); ui.Key(27); Check(!ui.View.CapturingTestplayKey, "Escape cancels key capture");
        ui.View.CloseLibrary(); ui.View.LoadDocument(DemoMap.Create()); ui.View.UpdateTransport(1500, 20000, true, true, false, null, null);
        ui.View.StartTestplay(); ui.View.KeyDown(68, false, false); ui.View.KeyDown(32, false, false);
        clock.Advance(100);
        ui.View.UpdateTransport(1600, 20000, true, true, false, null, null); Near(356, ui.View.TestplayCatcherX);
        ui.View.StopTestplay();
    }

    public static void ComboAndTrails()
    {
        var clock = new ManualTime();
        var ui = new Ui(timeProvider: clock);
        var map = new MapDocument();
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 256 }, new Fruit { TimeMs = 3000, X = 256 },
            new Fruit { TimeMs = 4000, X = 0 }, new Fruit { TimeMs = 10000, X = 256 }]);
        ui.LoadDocument(map); ui.View.StartTestplay(); ui.Paint();
        Check(ui.Canvas.Texts.All(t => !int.TryParse(t.Value, out _)), "testplay starts without a combo counter");
        clock.Advance(1000); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "1") && ui.Canvas.Texts.All(t => !t.Value.EndsWith('x')), "legacy combo uses digits without a multiplier suffix");
        clock.Advance(400); ui.Paint();
        Check(ui.Canvas.Texts.Count(t => t.Value == "1") == 1, "burst fades while main counter remains");
        clock.Advance(901); ui.Paint();
        Check(ui.Canvas.Texts.All(t => !int.TryParse(t.Value, out _)), "idle combo fades after 1300 ms");
        clock.Advance(699); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "1") && ui.Canvas.Texts.Any(t => t.Value == "2"), "new burst overlays previous counter for first 250 ms");
        clock.Advance(1000); ui.Paint();
        Check(ui.View.TestplayCombo == 0 && ui.Canvas.Texts.Any(t => t.Value == "2"), "miss begins rolling down from the previous combo");
        clock.Advance(401); ui.Paint();
        Check(ui.Canvas.Texts.All(t => !int.TryParse(t.Value, out _)), "broken combo fades after 400 ms");

        ConvertedCatchObject Note(double time, double x) => new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, time, x, x, x, 0);
        var game = new CatchTestplay([Note(100, 306), Note(200, 506), Note(5000, 0)], 5, 0);
        game.Advance(96, false, true, true);
        Check(game.Trails.Count == 7 && game.Trails.All(t => !t.Hyper && !t.AfterImage && !t.FacingLeft), "dash samples right-facing history every 16 ms");
        var coarse = game.Trails.ToArray();
        var fine = new CatchTestplay([Note(5000, 0)], 5, 0);
        for (int i = 0; i <= 96; i++) fine.Advance(i, false, true, true);
        Check(coarse.SequenceEqual(fine.Trails), "trail positions do not depend on render frequency");
        var hyper = new CatchTestplay([Note(100, 256), Note(200, 456), Note(5000, 0)], 5, 0);
        hyper.Advance(100, false, false, false);
        Check(hyper.Trails.Count(t => t.AfterImage) == 1, "hyper onset creates one afterimage");
        hyper.Advance(140, false, true, true);
        Check(hyper.Trails.Any(t => t.Hyper && !t.AfterImage), "hyperdash creates coloured trail entries");
        hyper.Advance(160, true, false, true);
        Check(hyper.Trails.Any(t => t.FacingLeft) && hyper.Trails.Any(t => !t.FacingLeft), "turning preserves historical sprite directions");
        game.Advance(900, false, false, false);
        Check(game.Trails.Count == 0, "ordinary dash trails expire after 800 ms");

        foreach (double cs in new[] { 3d, 7 })
        {
            var shared = new Ui(timeProvider: new ManualTime());
            var document = new MapDocument { CircleSize = cs };
            document.Fruits.Add(new Fruit { TimeMs = 10000, X = 256 });
            shared.LoadDocument(document); shared.OpenPreview(); shared.Paint();
            double[] previewBody = Body();
            shared.View.StartTestplay(); shared.Paint();
            double[] liveBody = Body();
            Check(previewBody.Length == liveBody.Length, "preview and testplay use the same fallback body parts");
            for (int i = 0; i < previewBody.Length; i++) Near(previewBody[i], liveBody[i]);
            double[] Body()
            {
                var plate = shared.Canvas.Fills.Single(f => f.Color == 0xB5C9D0).Bounds;
                var head = shared.Canvas.Circles.Single(c => c.Color == 0xB5C9D0);
                var lines = shared.Canvas.Lines.Where(l => l.Color == 0xB5C9D0).ToArray();
                Check(lines.Length == 6, "default catcher includes arms, body and legs");
                double center = plate.X + plate.Width / 2;
                return new[] { (head.X - center) / plate.Width, (head.Y - plate.Y) / plate.Width, (double)head.Radius / plate.Width }
                    .Concat(lines.SelectMany(l => new[] { (l.X1 - center) / plate.Width, (l.Y1 - plate.Y) / plate.Width,
                        (l.X2 - center) / plate.Width, (l.Y2 - plate.Y) / plate.Width })).ToArray();
            }
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private double time, audio;
        public override long TimestampFrequency => 1_000_000;
        public override long GetTimestamp() => (long)(time * 1000);
        public void Advance(double milliseconds) => time += milliseconds;
        public void AudioPosition(double position) { Advance(Math.Max(0, position - audio)); audio = position; }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .001, $"Expected {expected}, got {actual}");
}
