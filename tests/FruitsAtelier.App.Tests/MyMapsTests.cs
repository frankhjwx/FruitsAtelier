using System.Text.Json;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class MyMapsTests
{
    public static void Run()
    {
        var defaults = JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Check(defaults.ScanStandardMaps && defaults.MapperUsernames.Count == 0, "Compatible defaults");
        string root = Path.GetFullPath("artifacts/tests/my-maps-" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(songs);
        string exact = Write("mixed", "mine", "Mapper", 2);
        Write("mixed", "guest", "Guest", 2); Write("case", "case", "mapper", 2);
        Write("partial", "partial", "Mapper guest", 2); Write("old", "old", "Old Name", 2);
        string standard = Write("standard", "std", "Mapper", 0);
        var db = new LibraryDatabase(workspace, songs); db.Scan();
        var options = new LibraryOrganization(Creators: new[] { "Mapper", "Old Name" });
        using (var snapshot = db.SearchSnapshot("", organization: options))
        {
            Check(snapshot.SetCount == 3, "Exact current/former names, including standard maps");
            var mixed = snapshot.Page(0).Single(r => r.Map.Directory == Path.GetDirectoryName(exact));
            Check(mixed.Count == 1 && snapshot.Difficulties(mixed, 0).Single().Path == exact, "Guest difficulties stay outside My Maps");
            snapshot.Reorganize(options with { Sort = LibraryOrder.Creator });
            Check(snapshot.Page(0).Count == 3, "My Maps supports cached sorting");
        }
        using (var empty = db.SearchSnapshot("", organization: new(Creators: Array.Empty<string>())))
            Check(empty.SetCount == 0, "Missing usernames do not expose maps");
        db.ScanDirectories(null, scanStandardMaps: false);
        Check(db.Search("").All(m => m.Mode == 2) && File.Exists(standard), "Disabling standard scanning removes cached entries and preserves sources");
        db.ScanDirectories(null, scanStandardMaps: true);
        Check(db.Search("").Any(m => m.Path == standard), "Re-enabling restores unchanged standard maps");
        var project = WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments(new[] { Document("Other", "Guest"), Document("Owned", "Mapper") }), songs);
        db.ReindexProjects();
        using (var snapshot = db.SearchSnapshot("Owned", organization: options))
        {
            Check(snapshot.SetCount == 1, "A later native difficulty can match independently");
            var set = snapshot.Page(0).Single();
            Check(set.Map.ProjectPath == project.Directory && snapshot.Difficulties(set, 0).Single().Creator == "Mapper", "Native details contain only exact matches");
        }
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (760, 580), (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, Guid.NewGuid().ToString("N"));
                ui.View.OpenSettings(); ui.Paint(); Scroll();
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.mapperUsernames"));
                ui.Click(label.X + 20, label.Y + 40); ui.Type(" Mapper; Old Name; Mapper; ");
                ui.ClickText(L.Get("settings.scanStandardMaps"));
                Check(ui.View.LibrarySettings.ScanStandardMaps && ui.View.LibrarySettings.MapperUsernames.Count == 0, "Changes wait for Apply");
                string path = Path.Combine(root, Guid.NewGuid() + ".json");
                ui.View.ApplySettings(path); ui.Paint();
                var saved = LibrarySettings.Load(path);
                Check(!saved.ScanStandardMaps && saved.MapperUsernames.SequenceEqual(new[] { "Mapper", "Old Name" }), "Names and scanning preference persist");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint(); Scroll();
                ui.ClickText(L.Get("settings.scanStandardMaps")); ui.Key(27);
                Check(!ui.View.LibrarySettings.ScanStandardMaps, "Cancel discards drafts");
                ui.View.CloseLibrary();
                void Scroll() { var r = ui.View.SettingsBounds; ui.View.Wheel(r.Right - 60, r.Y + 200, -12000, false); ui.Paint(); }
            }
        }
        finally { L.SetLanguage(language); }
        Console.WriteLine("PASS My Maps, settings and standard scanning");
        string Write(string folder, string difficulty, string mapper, int mode)
        {
            string directory = Path.Combine(songs, folder); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, difficulty + ".osu");
            File.WriteAllText(path, $"osu file format v14\n[General]\nMode:{mode}\n[Metadata]\nTitle:{folder}\nCreator:{mapper}\nVersion:{difficulty}\n[HitObjects]\n");
            return path;
        }
    }
    internal static MapDocument Document(string title, string creator)
    {
        var document = new MapDocument { Name = title, IsDemo = false };
        document.OriginalSections.Add(new OsuSection { Name = "Metadata", Lines = { "Title:" + title, "Creator:" + creator, "Version:" + title } });
        return document;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
