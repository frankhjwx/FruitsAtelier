using System.IO.Compression;
using System.Text.Json;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SkinSelectorTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-skins-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string osu = Path.Combine(root, "osu"), workspace = Path.Combine(root, "Workspace"), config = Path.Combine(root, "library.json");
            Directory.CreateDirectory(Path.Combine(osu, "Songs"));
            File.WriteAllText(config, JsonSerializer.Serialize(new { Workspace = workspace, Songs = Path.Combine(osu, "Songs") }));
            var settings = LibrarySettings.Load(config);
            Check(settings.OsuRoot == osu && settings.Skins == Path.Combine(osu, "Skins"), "Legacy Songs setting migrates to the stable root");
            settings.Save(config);
            using (var json = JsonDocument.Parse(File.ReadAllText(config)))
                Check(json.RootElement.GetProperty("OsuRoot").GetString() == osu && !json.RootElement.TryGetProperty("Songs", out _), "Settings persist the root rather than derived Songs");
            settings = LibrarySettings.Load(config);
            Check(settings.Songs == Path.Combine(osu, "Songs"), "Derived Songs survives reload");
            string native = Path.Combine(settings.Skins, "Native skin"); Directory.CreateDirectory(native);
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZxkAAAAASUVORK5CYII=");
            File.WriteAllBytes(Path.Combine(native, "fruit-pear.png"), png);
            File.WriteAllText(Path.Combine(native, "skin.ini"), "[General]\nName: Native skin");
            var ui = new Ui(false);
            string version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(FruitsAtelier.App.Editor.EditorView).Assembly)!.InformationalVersion.Split('+')[0];
            Check(new FruitsAtelier.App.Editor.EditorView(loadDemo: false).WindowTitle == L.Get("window.initialTitle") + " v" + version, "Initial window title includes the application version");
            ui.View.LibrarySettings.Workspace = workspace; ui.View.LibrarySettings.OsuRoot = osu;
            ui.View.RequestSkinPreference = () => ui.View.LibrarySettings.Save(config);
            var before = ui.View.Document.DeepClone();
            void Menu() { ui.Paint(); var b = ui.View.SkinSelectorBounds; ui.Click(b.X + 5, b.Y + 5); }
            void Choose(string name)
            {
                var label = ui.Canvas.Texts.Last(t => t.Value == name);
                ui.Click(label.X + 2, label.Y + 2);
            }
            Menu(); Choose("Native skin");
            Check(ui.View.SkinName == "Native skin" && LibrarySettings.Load(config).SelectedSkin == native, "Skin selection loads and persists");
            string archive = Path.Combine(root, "fixture.osk");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry("fruit-pear.png").Open()) stream.Write(png);
                using var writer = new StreamWriter(zip.CreateEntry("skin.ini").Open()); writer.Write("[General]\nName: Imported fixture");
            }
            ui.View.ImportSkin(archive); ui.Paint();
            Check(Directory.GetFiles(Path.Combine(workspace, "Skins", "Archives"), "*.osk").Length == 1, "Imported archive is stored in workspace");
            ui.View.ImportSkin(archive);
            Check(Directory.GetDirectories(Path.Combine(workspace, "Skins", "Imported")).Length == 1, "Repeated imports reuse the skin");
            Menu();
            var imported = ui.Canvas.Texts.Single(t => t.Value.Contains("Imported fixture") && t.Value.Contains(L.Get("skin.imported")));
            var stable = ui.Canvas.Texts.Single(t => t.Value == "Native skin");
            Check(imported.Color != stable.Color, "Imported and stable skins have distinct colors");
            ui.Key(27);
            var restarted = new FruitsAtelier.App.Editor.EditorView(loadDemo: false);
            restarted.LibrarySettings.SelectedSkin = LibrarySettings.Load(config).SelectedSkin;
            restarted.InitializeSkin();
            Check(restarted.SkinName == "Imported fixture", "Saved skin restores without a bundled default archive");
            settings.DefaultSkin = archive; settings.Save(config);
            Check(LibrarySettings.Load(config).DefaultSkin == archive, "Default skin archive preference persists");
            restarted.LibrarySettings.Workspace = workspace;
            restarted.LibrarySettings.DefaultSkin = archive; restarted.LibrarySettings.SelectedSkin = null;
            restarted.InitializeSkin();
            Check(restarted.SkinName == "Imported fixture", "Default choice extracts and loads the configured osk");
            restarted.LibrarySettings.DefaultSkin = Path.Combine(root, "missing.osk");
            restarted.InitializeSkin();
            Check(restarted.SkinName is null, "Unavailable default archive falls back to geometric rendering");
            Check(ui.View.Document.ContentEquals(before) && !ui.View.IsDirty, "Skin selection does not edit the beatmap");
            var document = new MapDocument();
            var metadata = new OsuSection { Name = "Metadata" };
            metadata.Lines.AddRange(["Artist:Artist", "Title:Title", "Creator:Mapper", "Version:Difficulty"]);
            document.OriginalSections.Add(metadata); ui.LoadDocument(document);
            Check(ui.View.WindowTitle.Contains("Artist - Title (Mapper) [Difficulty]") && !ui.View.WindowTitle.Contains("M2"), "Window title identifies the current difficulty");
            Check(ui.View.WindowTitle.StartsWith(L.Get("window.initialTitle") + " v" + version + " · "), "Project window title retains the application version");
            Check(!ui.Canvas.Texts.Any(t => t.Y == 11 && t.X == 286), "Header does not repeat the project title");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
