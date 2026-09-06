using System.IO.Compression;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ExternalResourceTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-external-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace") };
            string external = Path.Combine(root, "External"), set = Path.Combine(external, "Set");
            Directory.CreateDirectory(set);
            File.WriteAllText(Path.Combine(set, "Rain.osu"), Map("External song", "Rain"));
            File.WriteAllText(Path.Combine(set, "Cup.osu"), Map("External song", "Cup"));
            File.WriteAllText(Path.Combine(set, "audio.ogg"), "audio");
            File.WriteAllText(Path.Combine(set, "background.jpg"), "background");
            string original = WorkspaceProject.Hash(Path.Combine(set, "Rain.osu"));
            LibraryOperations.ImportFolder(external, settings);
            LibraryOperations.ImportFolder(external, settings);
            var db = new LibraryDatabase(settings.Workspace, "");
            Check(db.SourceDirectories().Count == 1 && db.Scan().Count == 2, "folder registration persists and is idempotent without Songs");
            Check(db.Search("External romanised piano").Count == 2 && db.Search("原始标题 水果").Count == 2, "external metadata searchable in both scripts");
            var session = LibraryOperations.Open(db.Search("").First(), settings);
            Check(session.Project.Difficulties.Count == 2 && session.Manifest.ExternalSourceDirectory == set, "folder project remembers source");
            Check(Directory.GetFiles(session.Directory).All(p => p.EndsWith(".catchdiff")), "project folder stores only project files");
            session.Project.Difficulties[0].Document.Fruits[0].X = 222;
            WorkspaceProject.Save(session, session.Project);
            db.Scan();
            Check(db.Search("").All(m => m.ProjectPath == session.Directory), "folder rows link to existing project");
            var reopened = LibraryOperations.ImportPath(Path.Combine(set, "Rain.osu"), settings);
            Check(reopened.Directory == session.Directory && reopened.Project.Difficulties[0].Document.Fruits[0].X == 222, "re-import continues authored work");
            Check(WorkspaceProject.Hash(Path.Combine(set, "Rain.osu")) == original, "save never rewrites external source");
            string legacyPath = Path.Combine(root, "legacy.catchproj");
            var legacy = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(session.Project));
            legacy.Name = "Explicit legacy project"; legacy.Difficulties[0].Document.Fruits[0].X = 333;
            File.WriteAllText(legacyPath, ProjectSerializer.Serialize(legacy));
            var legacySession = LibraryOperations.ImportPath(legacyPath, settings);
            Check(legacySession.Directory != session.Directory && legacySession.Project.Difficulties[0].Document.Fruits[0].X == 333, "opening a legacy project preserves its own authored content");
            Directory.Move(external, external + " moved");
            Check(new LibraryDatabase(settings.Workspace, "").Scan().Errors.Count > 0 && db.Search("").Count == 2, "missing source reported without forgetting index");
            Check(WorkspaceProject.MissingResources(WorkspaceProject.Open(session.Directory).Project).Count > 0, "project reports broken external references");

            string archive = Path.Combine(root, "Complete.osz");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                zip.CreateEntry("set/empty/");
                Add(zip, "set/Rain.osu", Map("Archive song", "Rain"));
                Add(zip, "set/Cup.osu", Map("Archive song", "Cup"));
                foreach (string name in new[] { "audio.ogg", "background.jpg", "video.mp4", "scene.osb", "notes.txt", "nested/effect.wav" }) Add(zip, "set/" + name, name);
            }
            var imported = LibraryOperations.ImportPath(archive, settings);
            string resources = Path.GetDirectoryName(imported.Project.Difficulties[0].Document.SourcePath!)!;
            Check(WorkspaceProject.Within(Path.Combine(settings.Workspace, "Resources"), resources), "archive resources persist inside workspace Resources");
            Check(Directory.Exists(Path.Combine(resources, "empty")), "empty archive directories retained");
            Check(File.ReadAllText(Path.Combine(resources, "video.mp4")) == "video.mp4" && File.Exists(Path.Combine(resources, "scene.osb")) && File.Exists(Path.Combine(resources, "notes.txt")), "complete archive retained, including non-preview resources");
            Check(LibraryOperations.ImportPath(archive, settings).Directory == imported.Directory, "same archive reuses project and resource directory");
            File.Delete(archive);
            var restarted = new LibraryDatabase(settings.Workspace, ""); restarted.Scan();
            Check(restarted.Search("Archive").Count == 2 && restarted.Search("Archive").All(m => m.ProjectPath == imported.Directory), "archive remains indexed after original OSZ is deleted");
            Check(WorkspaceProject.MissingResources(WorkspaceProject.Open(imported.Directory).Project).Count == 0, "reopened archive references remain valid");
            WorkspaceProject.Save(imported, imported.Project);
            Check(File.Exists(Path.Combine(resources, "nested", "effect.wav")), "project snapshot saves do not remove managed resources");
            Reject(() => LibraryOperations.ImportFolder(settings.Workspace, settings));
            string bad = Path.Combine(root, "Bad.osz");
            using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) { Add(zip, "map.osu", Map("Bad", "Rain")); Add(zip, "../escape.txt", "bad"); }
            Reject(() => LibraryOperations.ImportPath(bad, settings));
            Check(!File.Exists(Path.Combine(root, "escape.txt")), "full extraction rejects path traversal");

            var view = new EditorView(); view.InitializeLibrary(true, settings);
            var canvas = new RecordingCanvas();
            for (int i = 0; i < 200; i++)
            {
                canvas.Clear(); view.Render(canvas, 980, 620);
                if (!view.LibraryLoading && i > 20) break;
                Thread.Sleep(20);
            }
            Check(!view.LibraryLoading, "library background work completed");
            bool? folderClicked = null; view.RequestLibraryImport = value => folderClicked = value;
            foreach (var (key, expected) in new[] { ("library.importFolder", true), ("library.importFile", false) })
            {
                var text = canvas.Texts.Single(t => t.Value == L.Get(key));
                view.PointerDown(text.X + 2, text.Y + 2, 0, false, false); view.PointerUp(text.X + 2, text.Y + 2, 0);
                Check(folderClicked == expected, "library import button is connected");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private static string Map(string title, string difficulty) => $"osu file format v14\n[General]\nMode:2\nAudioFilename:audio.ogg\n[Metadata]\nTitle:{title}\nTitleUnicode:原始标题\nArtist:romanised artist\nArtistUnicode:水果工坊\nCreator:Mapper\nVersion:{difficulty}\nTags:piano\n[Events]\n0,0,\"background.jpg\",0,0\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:";
    private static void Add(ZipArchive zip, string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException) { return; } throw new Exception("Invalid source accepted"); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
