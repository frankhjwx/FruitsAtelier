using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DropletDefaultsTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/droplet-defaults", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var defaults = new LibrarySettings();
            Check(defaults.DefaultDropletMode == SliderDropletMode.FollowMap && defaults.DefaultDropletStrength == 20
                && defaults.DefaultDropletSeed == 1337 && !defaults.DefaultDropletHardRock, "Missing settings retain the established defaults");
            Check(defaults.DerandomizeDroplets && defaults.DerandomizeNewProjects, "both derandomization preferences default to On");
            L.SetLanguage("en");
            Check(L.Get("settings.dropletDefaults") == "Droplet Derandomize Settings"
                && L.Get("settings.derandomizeOn") == "Enable Derandomization for Legacy slider to FSlider Conversion: On"
                && L.Get("settings.newProjectDerandomizeOn") == "Enable Derandomization for new catchprojects: On", "English droplet settings use explicit names");
            string oldSettings = Path.Combine(root, "old.json");
            File.WriteAllText(oldSettings, "{\"DerandomizeDroplets\":false}");
            Check(!LibrarySettings.Load(oldSettings).DerandomizeDroplets && LibrarySettings.Load(oldSettings).DerandomizeNewProjects,
                "older settings keep conversion policy and default new projects to derandomized");
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(); ui.LoadDocument(new MapDocument { IsDemo = false });
                ui.View.LibrarySettings.Workspace = Path.Combine(root, locale);
                ui.View.LibrarySettings.OsuRoot = ""; ui.View.LibrarySettings.SelectedSkin = ui.View.LibrarySettings.DefaultSkin = null;
                ui.View.LibrarySettings.DerandomizeDroplets = ui.View.LibrarySettings.DerandomizeNewProjects = true;
                var before = ui.View.Document.DeepClone();
                string path = Path.Combine(root, locale + ".json");
                int saves = 0;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                foreach (var size in new[] { (1440, 900), (980, 620), (760, 580) })
                {
                    ui.Resize(size.Item1, size.Item2); OpenSettings(ui);
                    Check(!ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.dropletDefaults")), "General omits droplet defaults");
                    string pendingWorkspace = Path.Combine(root, locale, size.Item1.ToString());
                    ui.View.SetLibraryFolder(true, pendingWorkspace);
                    ui.ClickText(L.Get("settings.openDropletSettings"));
                    Check(ui.View.SongSetupVisible, "General link opens Droplet Settings");
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("randomize.currentDifficulty"))
                        && ui.Canvas.Texts.Any(t => t.Value == L.Get("randomize.globalDefaults")), "map and global scopes are labelled separately");
                    ui.ClickText(L.Get("randomize.defaultProjects"));
                    Check(!LibrarySettings.Load(path).DerandomizeNewProjects && LibrarySettings.Load(path).DerandomizeDroplets,
                        "new-project preference saves independently");
                    ui.ClickText(L.Get("randomize.defaultConversion"));
                    Check(!LibrarySettings.Load(path).DerandomizeDroplets && saves > 0, "conversion preference saves immediately");
                    ui.Key(27); ui.View.ApplySettings(path); ui.Paint();
                    Check(LibrarySettings.Load(path).Workspace == pendingWorkspace, "General draft survives the Droplet Settings round trip");
                    ui.Key(27);
                    Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "global defaults leave current map and history unchanged");
                    OpenSettings(ui); ui.View.ApplySettings(path, force: true); ui.Key(27);
                    Check(!ui.View.LibrarySettings.DerandomizeDroplets && !ui.View.LibrarySettings.DerandomizeNewProjects,
                        "General Apply retains droplet preferences");
                    ui.View.OpenSongSetup(); ui.Paint(); ui.ClickText(L.Get("randomize.title"));
                    ui.ClickText(L.Get("randomize.defaultProjects")); ui.ClickText(L.Get("randomize.defaultConversion")); ui.Key(27);
                }
                ui.View.ShowRandomDock(); ui.Paint();
                var bounds = ui.View.RandomDockViewport;
                ui.View.Wheel(bounds.X + 10, bounds.Y + 40, -12000, false, false); ui.Paint();
                ui.ClickText(L.Get("randomize.defaultProjects"));
                Check(!ui.View.LibrarySettings.DerandomizeNewProjects && ui.View.Document.ContentEquals(before),
                    "dock global toggle persists without changing the map");
                ui.View.RequestViewPreference = null;
                ui.LoadDocument(before);
                foreach (bool derandomize in new[] { true, false })
                foreach (var mode in Enum.GetValues<SliderEditingMode>())
                {
                    ui.View.LibrarySettings.DerandomizeNewProjects = derandomize;
                    ui.View.NewProject(); ui.Paint();
                    Check(ui.View.Document.DerandomizeFSliderDroplets == derandomize && ui.View.Document.RandomizeNewSliders == !derandomize
                        && ui.View.Document.RandomizeDropletStrength == 20 && ui.View.Document.RandomizeDropletSeed == 1337,
                        "new catchproject captures its own droplet defaults");
                    var saved = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
                    ui.View.LibrarySettings.DerandomizeNewProjects = !derandomize;
                    ui.LoadDocument(saved.Difficulties.Single().Document); ui.View.SetSliderEditingMode(mode);
                    ui.Key('B'); ui.ClickMap(1000, 256); ui.ClickMap(3000, 256, ctrl: true); ui.Key(13);
                    Wait(ui);
                    Check(ui.View.Document.Tracks.Single().DropletMode == SliderDropletMode.FollowMap
                        && ui.View.Document.Tracks.Single().RandomizesDroplets(ui.View.Document) == !derandomize,
                        "new FSliders follow the saved project default in both drawing modes");
                    ui.Key('Z', ctrl: true); Wait(ui);
                    Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.RandomizeNewSliders == !derandomize,
                        "drawing undo preserves project defaults");
                    ui.Key('Y', ctrl: true); Wait(ui);
                    Check(ui.View.Document.Tracks.Single().DropletMode == SliderDropletMode.FollowMap
                        && ui.View.Document.Tracks.Single().RandomizesDroplets(ui.View.Document) == !derandomize,
                        "drawing redo preserves the captured slider switch");
                }
                ui.LoadDocument(before); ui.View.LibrarySettings.DerandomizeNewProjects = false;
                ui.Key('B'); ui.ClickMap(1000, 256); ui.ClickMap(3000, 256, ctrl: true); ui.Key(13); Wait(ui);
                Check(ui.View.Document.Tracks.Single().DropletRandomization is null, "existing projects retain their drawing default");
            }
            var libraryUi = new Ui(false);
            libraryUi.View.LibrarySettings.Workspace = Path.Combine(root, "library-only");
            libraryUi.View.LibrarySettings.DerandomizeNewProjects = true;
            libraryUi.View.RequestViewPreference = () => libraryUi.View.LibrarySettings.Save(Path.Combine(root, "library-only.json"));
            libraryUi.View.ShowLibrary(); libraryUi.Paint();
            OpenSettings(libraryUi); libraryUi.ClickText(L.Get("settings.openDropletSettings"));
            Check(!libraryUi.View.HasEditorProject && libraryUi.View.SongSetupVisible, "General opens global Droplet Settings without creating a project");
            libraryUi.ClickText(L.Get("randomize.defaultProjects")); libraryUi.Key(13);
            Check(!libraryUi.View.SongSetupVisible && !libraryUi.View.HasEditorProject
                && !libraryUi.View.LibrarySettings.DerandomizeNewProjects, "global dialog returns to General and preserves its saved choice");
            libraryUi.Paint(); Check(libraryUi.Canvas.Texts.Any(t => t.Value == L.Get("settings.openDropletSettings")), "dialog returns to General");
            RememberSettings(root);
            RandomizationRoundTrip();
            CompatibleDrawingDefaults();
        }
        finally { L.SetLanguage(language); }
    }
    private static void RememberSettings(string root)
    {
        var ui = new Ui(); ui.LoadDocument(new MapDocument { IsDemo = false });
        string path = Path.Combine(root, "remember.json");
        ui.View.RequestViewPreference = () => ui.View.LibrarySettings.Save(path);
        ui.View.ShowRandomDock(); ui.Paint();
        ui.ClickText(L.Get("randomize.derandomizeOff"));
        var saved = LibrarySettings.Load(path);
        Check(saved.DerandomizeNewProjects, "Droplet Settings mode did not save globally");
        ui.ClickText(L.Get("randomize.derandomizeHrOff"));
        var field = ui.View.RandomDockValueBounds(0);
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true); ui.View.TextInput('3'); ui.View.TextInput('7'); ui.Key(13); ui.Paint();
        field = ui.View.RandomDockValueBounds(1);
        ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true); ui.View.TextInput('4'); ui.View.TextInput('2'); ui.Key(13); ui.Paint();
        saved = LibrarySettings.Load(path);
        Check(saved.DefaultDropletSeed == 42, "Droplet seed did not save globally");
        Check(saved.DefaultDropletHardRock && saved.DefaultDropletStrength == 37, "Droplet Settings parameters did not save globally");
        OpenSettings(ui); ui.View.ApplySettings(path, force: true); ui.Key(27);
        saved = LibrarySettings.Load(path);
        Check(saved.DefaultDropletHardRock && saved.DefaultDropletStrength == 37 && saved.DefaultDropletSeed == 42,
            "General Settings Apply reset global droplet parameters");
        var restarted = new Ui(false); restarted.View.InitializeLibrary(false, saved);
        restarted.View.NewProject(); restarted.Paint();
        Check(restarted.View.Document.DerandomizeFSliderDroplets && restarted.View.Document.DerandomizeDropletsForHardRock
            && restarted.View.Document.RandomizeDropletStrength == 37 && restarted.View.Document.RandomizeDropletSeed == 42, "Restart did not restore new project droplet defaults");
        restarted.LoadDocument(new MapDocument { RandomizeDropletStrength = 11, IsDemo = false });
        Check(restarted.View.Document.RandomizeDropletStrength == 11, "Global defaults overwrote an existing project");
    }

    private static void RandomizationRoundTrip()
    {
        L.SetLanguage("en");
        foreach (bool derandomize in new[] { true, false })
        foreach (double strength in new[] { 20d, 37d })
        {
            var map = new MapDocument { IsDemo = false, DurationMs = 8000,
                DerandomizeFSliderDroplets = derandomize, RandomizeDropletStrength = strength };
            var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.FollowMap };
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 3000, X = 256 }]); map.Tracks.Add(track);
            var ui = new Ui(); ui.LoadDocument(map);
            ui.View.LibrarySettings.DerandomizeNewProjects = !derandomize;
            ui.SelectTrack(track.Id); ui.View.ShowSliderInspector(); ui.Paint();
            string path = Path.GetFullPath(Path.Combine("artifacts/tests/droplet-defaults", Guid.NewGuid().ToString("N") + ".json"));
            ui.View.RequestViewPreference = () => ui.View.LibrarySettings.Save(path);
            var current = SliderDropletMode.FollowMap;
            foreach (var next in new[] { SliderDropletMode.Randomize, SliderDropletMode.Derandomize, SliderDropletMode.FollowMap, SliderDropletMode.Randomize })
            {
                var bounds = ui.View.SliderInspectorViewport;
                ui.View.Wheel(bounds.X + 10, bounds.Y + 40, -12000, false, false); ui.Paint();
                string modeLabel = L.Get("inspector.dropletMode", L.Get("droplet.mode." + current));
                var modeButton = ui.Canvas.Texts.Single(t => t.Value == modeLabel && t.Y < ui.Height - 28);
                ui.Click(modeButton.X + 4, modeButton.Y + 5);
                var choice = ui.Canvas.Texts.Last(t => t.Value == L.Get("droplet.mode." + next));
                ui.Click(choice.X + 4, choice.Y + 4); Wait(ui);
                var expected = map.DeepClone(); expected.Tracks[0].DropletMode = next;
                double[] Tiny(MapDocument document) => OsuBeatmapWriter.Serialize(document).PlayableObjects
                    .Where(o => o.Kind == CatchObjectKind.TinyDroplet).Select(o => o.X).ToArray();
                Check(Tiny(expected).SequenceEqual(Tiny(ui.View.Document)), "Inspector mode round trips restore exported tiny-droplet positions");
                Check(ui.View.Document.Tracks[0].Nodes.Count == 2 && ui.View.Document.DerandomizeFSliderDroplets == derandomize,
                    "Inspector overrides preserve authored geometry and map defaults");
                var restored = LibrarySettings.Load(path);
                Check(restored.DefaultDropletMode == next, "Inspector mode did not persist globally");
                foreach (var drawingMode in Enum.GetValues<SliderEditingMode>())
                {
                    var restarted = new Ui(false); restarted.View.InitializeLibrary(false, restored);
                    restarted.View.NewProject(); restarted.Paint(); restarted.View.SetSliderEditingMode(drawingMode);
                    restarted.Key('B'); restarted.ClickMap(1000, 256); restarted.ClickMap(3000, 256, ctrl: true); restarted.Key(13); Wait(restarted);
                    Check(restarted.View.Document.Tracks.Single().DropletMode == next, "Restarted drawing did not use the remembered mode");
                }
                current = next;
            }
        }
    }

    private static void CompatibleDrawingDefaults()
    {
        var old = new MapDocument { IsDemo = false, DurationMs = 5000 };
        var existing = new CurveTrack { Kind = CurveKind.Linear, DropletRandomization = new() { Enabled = true } };
        existing.Nodes.AddRange([new() { TimeMs = 0, X = 128 }, new() { TimeMs = 500, X = 128 }]);
        old.Tracks.Add(existing);
        string single = ProjectSerializer.Serialize(old);
        string multi = ProjectSerializer.Serialize(BeatmapProject.FromDocuments([old]));
        Check(!single.Contains("\"DropletMode\"") && !single.Contains("\"RandomizeNewSliders\"")
            && !single.Contains("\"DerandomizeFSliderDroplets\""), "Legacy fixture omits new drawing policy and map derandomization");
        var imported = OsuBeatmapReader.Read("""
            osu file format v14
            [General]
            Mode:2
            [Difficulty]
            SliderMultiplier:1.4
            SliderTickRate:1
            [TimingPoints]
            0,500,4,1,0,100,1,0
            [HitObjects]
            128,192,0,2,0,L|128:292,1,140
            """);
        foreach (var document in new[] { ProjectSerializer.Read(single), ProjectSerializer.ReadProject(multi).Difficulties.Single().Document, imported })
        foreach (bool randomizeNew in new[] { false, true })
        foreach (var preference in Enum.GetValues<SliderDropletMode>())
        foreach (var drawingMode in Enum.GetValues<SliderEditingMode>())
        {
            var source = document.DeepClone(); source.RandomizeNewSliders = randomizeNew;
            var ui = new Ui(); ui.LoadDocument(source);
            ui.View.LibrarySettings.DefaultDropletMode = preference;
            ui.View.LibrarySettings.DerandomizeNewProjects = false;
            ui.View.SetSliderEditingMode(drawingMode); ui.Paint();
            var before = ui.View.Document.DeepClone();
            var beforeEvents = CatchStreamConverter.Convert(before).Objects.ToArray();
            ui.Key('B'); ui.ClickMap(1000, 256); ui.Key(27); Wait(ui);
            Check(ui.View.Document.ContentEquals(before), "Cancelling a compatible slider draft preserves the source");
            ui.Key('B'); ui.ClickMap(1000, 256); ui.ClickMap(3000, 256, ctrl: true); ui.Key(13); Wait(ui);
            var added = ui.View.Document.Tracks.Single(t => before.Tracks.All(previous => previous.Id != t.Id));
            var expected = preference == SliderDropletMode.FollowMap && !randomizeNew
                ? SliderDropletMode.Derandomize : preference;
            Check(added.DropletMode == expected, "Old projects and imports retain safe drawing defaults and explicit randomization choices");
            var converted = CatchStreamConverter.Convert(ui.View.Document);
            Check(converted.Success && beforeEvents.SequenceEqual(converted.Objects.Where(o => o.SourceId != added.Id)),
                "Drawing preserves existing randomized and imported object output");
            var exported = OsuBeatmapWriter.Serialize(ui.View.Document);
            Check(exported.ObjectSequenceMatches, "Compatible drawing exports the validated object sequence");
            var tinies = converted.Objects.Where(o => o.SourceId == added.Id && o.Kind == CatchObjectKind.TinyDroplet).ToArray();
            bool random = preference == SliderDropletMode.Randomize || preference == SliderDropletMode.FollowMap && randomizeNew;
            Check(tinies.Length > 0 && (random ? tinies.Any(o => Math.Abs(o.TargetX - 256) > .1) : tinies.All(o => o.TargetX == 256)),
                "Drawn tiny droplets follow the compatible policy");
            var after = ui.View.Document.DeepClone();
            Check(after.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(after))), "Drawing policy survives project reopening");
            ui.Key('Z', ctrl: true); Wait(ui);
            Check(ui.View.Document.ContentEquals(before), "Drawing undo restores legacy content and policy");
            ui.Key('Y', ctrl: true); Wait(ui);
            Check(ui.View.Document.ContentEquals(after), "Drawing redo restores the resolved mode");
        }
    }

    private static void OpenSettings(Ui ui)
    {
        ui.View.OpenSettings(); ui.Paint();
        var bounds = ui.View.SettingsBounds;
        ui.View.Wheel(bounds.Right - 100, bounds.Y + 150, -1200, false); ui.Paint();
    }

    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.ConversionRefreshing && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(5); }
        Check(!ui.View.ConversionRefreshing, "draft completion publishes validated conversion"); ui.Paint();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
