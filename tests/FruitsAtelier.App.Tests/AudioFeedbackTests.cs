using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class AudioFeedbackTests
{
    public static void Navigation()
    {
        var ui = new Ui(); var map = new MapDocument();
        map.Fruits.Add(new() { TimeMs = 1000 }); map.Fruits.Add(new() { TimeMs = 5000 });
        ui.LoadDocument(map); ui.Key('F');
        ui.View.UpdateTransport(2000, 10000, true, false, false, null, null);
        ui.Key('V'); Near(5000, ui.View.PlayheadMs);
        Check(ui.View.ActiveTool == "Fruit", "V navigates without changing tools");
        ui.Key('V'); Near(10000, ui.View.PlayheadMs);
        ui.Key('1'); Check(ui.View.ActiveTool == "Select", "1 selects the selection tool");
        foreach (bool playing in new[] { false, true })
        {
            ui.View.UpdateTransport(3000, 10000, true, playing, false, null, null); ui.Paint();
            var plot = ui.Plot;
            double before = ui.View.PlayheadMs, beforeView = ui.View.ViewStartMs;
            ui.View.Wheel(plot.X + 10, plot.Y + 20, 120, false); ui.Paint();
            Check(ui.View.PlayheadMs < before && ui.View.ViewStartMs < beforeView, "wheel up moves both time and view earlier");
            Near(ui.View.PlayheadMs - before, ui.View.ViewStartMs - beforeView);
            ui.View.Wheel(plot.X + 10, plot.Y + 20, -120, false); ui.Paint();
            Near(before, ui.View.PlayheadMs); Near(beforeView, ui.View.ViewStartMs);
        }
        ui.View.UpdateTransport(5000, 10000, true, false, false, null, null); ui.Paint();
        float x = ui.Plot.X + 20, y = ui.Plot.Y + 30;
        ui.View.PointerDown(x, y, 1, false, false); ui.View.PointerMove(x, y + 30, false, false);
        ui.View.PointerUp(x, y + 30, 1); ui.Paint();
        double offset = ui.View.PlayheadMs - ui.View.ViewStartMs, start = ui.View.PlayheadMs;
        var seeks = new List<double>(); ui.View.RequestSeek = seeks.Add;
        for (int i = 0; i < 4; i++) { ui.View.Wheel(x, y, 30, false); ui.Paint(); }
        Near(start - TimingMap.At(map, start).BeatLengthMs / ui.View.SnapDivisor, ui.View.PlayheadMs);
        Near(offset, ui.View.PlayheadMs - ui.View.ViewStartMs);
        Check(seeks.Count == 1, "fractional wheel input accumulates into one snap step");
        ui.View.Wheel(x, y, 120000, false); ui.Paint();
        double boundaryHead = ui.View.PlayheadMs, boundaryView = ui.View.ViewStartMs;
        ui.View.Wheel(x, y, 120, false); ui.Paint();
        Near(boundaryHead, ui.View.PlayheadMs); Near(boundaryView, ui.View.ViewStartMs);
        Near(offset, ui.View.PlayheadMs - ui.View.ViewStartMs);
        Check(!ui.View.IsDirty && ui.View.Document.ContentEquals(map), "navigation leaves content unchanged");
    }
    public static void VolumeSettings()
    {
        string folder = Path.GetFullPath("artifacts/tests/volume-settings");
        var ui = new Ui();
        var settings = new LibrarySettings { Workspace = folder };
        var before = ui.View.Document.DeepClone();
        float song = -1, hit = -1; int saves = 0;
        ui.View.RequestAudioVolume = (s, h) => { song = s; hit = h; };
        ui.View.RequestAudioPreference = () => { settings.Save(Path.Combine(folder, "settings.json")); saves++; };
        ui.View.InitializeLibrary(true, settings); ui.Paint(); ui.ClickText(L.Get("library.settings"));
        Near(1, song); Near(1, hit);
        Set(0, 50); Set(1, 40); Set(2, 20);
        Near(.2, song); Near(.1, hit);
        var loaded = LibrarySettings.Load(Path.Combine(folder, "settings.json"));
        Check(loaded.MasterVolume == 50 && loaded.SongVolume == 40 && loaded.HitsoundVolume == 20 && saves == 3,
            "all three volumes persist after a drag");
        Set(1, 0); Near(0, song); Near(.1, hit);
        Set(1, 100); Set(2, 0); Near(.5, song); Near(0, hit);
        Set(0, 0); Near(0, song); Near(0, hit);
        Check(!ui.View.WantsCapture && before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "volume never edits the map");
        var old = System.Text.Json.JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Check(old.MasterVolume == 100 && old.SongVolume == 100 && old.HitsoundVolume == 100, "older settings default to full volume");
        old.MasterVolume = -5; old.HitsoundVolume = 200;
        Check(old.MasterVolume == 0 && old.HitsoundVolume == 100, "saved values are bounded");
        void Set(int channel, int value)
        {
            var r = ui.View.VolumeSliderBounds(channel);
            ui.View.PointerDown(r.X + r.Width / 2, r.Y + 12, 0, false, false);
            Check(ui.View.WantsCapture, "volume slider captures pointer");
            ui.View.PointerMove(r.X + r.Width * value / 100, r.Y + 12, false, false);
            ui.View.PointerUp(r.X + r.Width * value / 100, r.Y + 12, 0); ui.Paint();
        }
    }

    public static void SkinSamples()
    {
        string root = Path.GetFullPath("artifacts/tests/skin-samples");
        string mapFolder = Path.Combine(root, "map"), skin = Path.Combine(root, "skin"), fallback = Path.Combine(root, "fallback");
        foreach (string folder in new[] { mapFolder, skin, fallback }) Directory.CreateDirectory(folder);
        foreach (var (folder, name) in new[] { (mapFolder, "soft-hitnormal2.wav"), (mapFolder, "custom.wav"),
            (skin, "SOFT-HITNORMAL.ogg"), (skin, "drum-hitclap.wav"), (fallback, "soft-hitnormal.wav"), (fallback, "normal-hitfinish.wav") })
            File.WriteAllBytes(Path.Combine(folder, name), HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        var map = new MapDocument { SourcePath = Path.Combine(mapFolder, "map.osu") };
        map.TimingPoints.Add(new() { SampleSet = 2, SampleIndex = 2 });
        map.Fruits.Add(new() { TimeMs = 100, OriginalLine = "256,192,100,1,8,0:3:0:0:" });
        var objects = CatchStreamConverter.Convert(map).Objects;
        IReadOnlyList<Hitsound> Resolve() => new HitsoundResolver(map, objects, [skin, fallback]).Resolve(objects[0]);
        Check(Resolve()[0].FilePath == Path.Combine(mapFolder, "soft-hitnormal2.wav"), "beatmap sample overrides skin");
        Check(Resolve()[1].FilePath == Path.Combine(skin, "drum-hitclap.wav"), "missing indexed map sample uses unindexed skin addition");
        map.TimingPoints[0].SampleIndex = 0;
        Check(Resolve()[0].FilePath == Path.Combine(skin, "SOFT-HITNORMAL.ogg"), "index zero uses selected skin before fallback of another format");
        map.Fruits[0].OriginalLine = "256,192,100,1,4,0:1:0:0:custom.wav";
        Check(Resolve()[0].FilePath == Path.Combine(mapFolder, "custom.wav") && Resolve()[1].FilePath == Path.Combine(fallback, "normal-hitfinish.wav"),
            "explicit sample wins and missing skin layer uses fallback skin");
        map.Fruits[0].OriginalLine = "256,192,100,1,2,0:1:0:0:";
        Check(Resolve()[1].FilePath == HitsoundDefaults.Find(1, "hitwhistle"), "absent skin sample uses packaged sound");
        var ui = new Ui(); ui.LoadDocument(map); int preloads = 0;
        ui.View.RequestPreloadHitsounds = _ => preloads++;
        var played = new List<Hitsound>(); ui.View.RequestHitsound = played.Add;
        ui.View.LoadSkin(skin);
        ui.View.UpdateTransport(0, 5000, true, true, false, null, null);
        ui.View.UpdateTransport(100, 5000, true, true, false, null, null);
        Check(played[0].FilePath == Path.Combine(skin, "SOFT-HITNORMAL.ogg"), "preview resolves selected skin");
        played.Clear(); ui.View.LoadSkin(fallback);
        ui.View.UpdateTransport(0, 5000, true, true, false, null, null);
        ui.View.UpdateTransport(100, 5000, true, true, false, null, null);
        Check(preloads == 2 && played[0].FilePath == Path.Combine(fallback, "soft-hitnormal.wav"), "skin switch invalidates sample resolution and preloads again");
        string workspace = Path.Combine(root, "workspace");
        string oldFolder = Path.Combine(workspace, "Skins", "Imported", "v3-fixture");
        string archives = Path.Combine(workspace, "Skins", "Archives");
        Directory.CreateDirectory(oldFolder); Directory.CreateDirectory(archives);
        using (var stream = File.Create(Path.Combine(archives, "v3-fixture.osk")))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var sample = zip.CreateEntry("soft-hitnormal.wav").Open();
            sample.Write(HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        }
        ui.View.LibrarySettings.Workspace = workspace; ui.View.LibrarySettings.SelectedSkin = oldFolder;
        ui.View.InitializeSkin();
        Check(ui.View.HitsoundSkinFolders[0] != oldFolder && File.Exists(Path.Combine(ui.View.HitsoundSkinFolders[0], "soft-hitnormal.wav")),
            "older imported caches re-extract audio from their stored archive, including sound-only skins");
    }
    private static void Near(double expected, double actual) => Check(Math.Abs(expected - actual) < .00001, $"Expected {expected}, got {actual}");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
