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
                foreach (var size in new[] { (1440, 900), (980, 620), (760, 580) })
                {
                    ui.Resize(size.Item1, size.Item2); ui.View.SupportsDisplayMode = true; ui.View.OpenSettings(); ui.Paint();
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.dropletDefaults")), "General has a separate droplet section");
                    Check(ui.Canvas.Lines.Any(l => l.Y1 == ui.View.SettingsBounds.Y + 260 && l.Y2 == l.Y1 && l.X2 > l.X1),
                        "droplet section ends with a separator");
                    ui.ClickText(L.Get("settings.newProjectDerandomizeOn")); ui.Key(27);
                    Check(ui.View.LibrarySettings.DerandomizeNewProjects, "cancel discards the new-project preference draft");
                }
                ui.View.OpenSettings(); ui.Paint();
                ui.ClickText(L.Get("settings.newProjectDerandomizeOn"));
                string path = Path.Combine(root, locale + ".json"); ui.View.ApplySettings(path); ui.Paint();
                Check(LibrarySettings.Load(path).DerandomizeDroplets && !LibrarySettings.Load(path).DerandomizeNewProjects,
                    "new-project setting persists independently of Legacy conversion");
                ui.ClickText(L.Get("settings.derandomizeOn")); ui.View.ApplySettings(path); ui.Paint();
                Check(!LibrarySettings.Load(path).DerandomizeDroplets && !LibrarySettings.Load(path).DerandomizeNewProjects,
                    "Legacy conversion setting changes independently");
                ui.Key(27); Check(ui.View.Document.ContentEquals(before), "changing defaults preserves existing beatmap content");
                foreach (bool derandomize in new[] { true, false })
                foreach (var mode in Enum.GetValues<SliderEditingMode>())
                {
                    ui.View.LibrarySettings.DerandomizeNewProjects = derandomize;
                    ui.View.NewProject(); ui.Paint();
                    Check(ui.View.Document.RandomizeNewSliders == !derandomize
                        && ui.View.Document.RandomizeDropletStrength == 20 && ui.View.Document.RandomizeDropletSeed == 1337,
                        "new catchproject captures its own droplet defaults");
                    var saved = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(ui.View.CaptureProject()));
                    ui.View.LibrarySettings.DerandomizeNewProjects = !derandomize;
                    ui.LoadDocument(saved.Difficulties.Single().Document); ui.View.SetSliderEditingMode(mode);
                    ui.Key('B'); ui.ClickMap(1000, 256); ui.ClickMap(3000, 256, ctrl: true); ui.Key(13);
                    Wait(ui);
                    Check((ui.View.Document.Tracks.Single().DropletRandomization is { Enabled: true }) == !derandomize,
                        "new FSliders follow the saved project default in both drawing modes");
                    ui.Key('Z', ctrl: true); Wait(ui);
                    Check(ui.View.Document.Tracks.Count == 0 && ui.View.Document.RandomizeNewSliders == !derandomize,
                        "drawing undo preserves project defaults");
                    ui.Key('Y', ctrl: true); Wait(ui);
                    Check((ui.View.Document.Tracks.Single().DropletRandomization is { Enabled: true }) == !derandomize,
                        "drawing redo preserves the captured slider switch");
                }
                ui.LoadDocument(before); ui.View.LibrarySettings.DerandomizeNewProjects = false;
                ui.Key('B'); ui.ClickMap(1000, 256); ui.ClickMap(3000, 256, ctrl: true); ui.Key(13); Wait(ui);
                Check(ui.View.Document.Tracks.Single().DropletRandomization is null, "existing projects retain their drawing default");
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.ConversionRefreshing && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(5); }
        Check(!ui.View.ConversionRefreshing, "draft completion publishes validated conversion"); ui.Paint();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
