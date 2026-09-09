using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-library-ui-" + Guid.NewGuid());
        string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
        try
        {
            StandaloneExport(root);
            OptionalSongs(root);
            var view = new EditorView(); view.NewProject();
            view.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); view.LibrarySettings.Songs = songs;
            Check(view.SaveWorkspace(), "save creates workspace project");
            view.ChangeAudioPath(Path.Combine(songs, "missing.mp3"));
            view.SaveWorkspace();
            var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
            Check(canvas.Texts.Any(t => t.Value.Contains("missing.mp3")), "missing references visible inside editor");
            var before = view.CaptureProject();
            view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.title")), "separate library page");
            view.KeyDown(70, false, false); view.PointerDown(700, 480, 0, false, false); view.PointerUp(700, 480, 0);
            Check(view.CaptureProject().Difficulties[0].Document.ContentEquals(before.Difficulties[0].Document), "library input cannot edit map");
            view.CloseLibrary(); view.ShowWorkspaceExport(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.exportNew")) && canvas.Texts.Any(t => t.Value == L.Get("library.exportOverride")), "explicit export modes");
            view.CloseLibrary();
            Check(Directory.GetFiles(songs).Length == 0, "navigation and saving never write Songs");
            WaitForLibrary(view);
        }
        finally { Directory.Delete(root, true); }
    }
    private static void StandaloneExport(string root)
    {
        var view = new EditorView(); view.NewProject();
        view.LibrarySettings.Songs = "";
        bool saveRequested = false;
        view.RequestSave = () => saveRequested = true;
        var before = view.CaptureProject();
        string? requestedName = null;
        view.RequestOsuExport = name => requestedName = name;
        view.ShowWorkspaceExport();
        var canvas = new RecordingCanvas(); view.Render(canvas, 980, 620);
        view.PointerDown(600, 328, 0, false, false); view.PointerUp(600, 328, 0);
        Check(requestedName is not null && !saveRequested && view.WorkspaceSession is null, "standalone export works without Songs or workspace save");
        var document = FruitsAtelier.App.Platform.LibraryOperations.StandaloneExportDocument(before.Difficulties[0], "Standalone");
        string destination = Path.Combine(root, "standalone.osu");
        OsuBeatmapWriter.WriteFile(document, destination);
        var read = OsuBeatmapReader.ReadFile(destination);
        Check(OsuBeatmapReader.Setting(read, "Metadata", "Version") == "Standalone", "standalone version is applied");
        Check(OsuBeatmapReader.Setting(read, "Metadata", "BeatmapID") == "0", "standalone resets beatmap ID");
        Check(view.Document.ContentEquals(before.Difficulties[0].Document), "standalone export leaves document unchanged");
        Check(Directory.GetFiles(root).Length == 1, "standalone exports only one file");
    }
    private static void OptionalSongs(string root)
    {
        string workspace = Path.Combine(root, "Unbound workspace"), config = Path.Combine(root, "settings.json");
        new LibrarySettings { Workspace = workspace, Songs = "   " }.Save(config);
        var settings = LibrarySettings.Load(config);
        Check(settings.Songs == "", "empty Songs persists as unbound");
        var view = new EditorView(); view.InitializeLibrary(true, settings);
        var canvas = new RecordingCanvas(); view.Render(canvas, 980, 620);
        Check(!canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "startup does not force settings");
        view.NewProject(); view.CloseLibrary();
        Check(view.SaveWorkspace(), "new project saves without Songs");
        string directory = view.WorkspaceSession!.Directory;
        Check(WorkspaceProject.Open(directory).Manifest.SongsRoot is null, "unbound manifest has no invented path");
        Check(view.SaveWorkspace(true) && view.WorkspaceSession!.Directory != directory, "save as without Songs");
        var db = new LibraryDatabase(workspace, ""); db.Scan();
        Check(db.Search("", true).Count == 2, "unbound project library remains available");
        for (int i = 0; i < 3; i++)
        {
            view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(!canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "reopening library does not force settings");
            view.CloseLibrary();
        }
        bool exported = false; view.RequestWorkspaceExport = (_, _) => exported = true;
        view.ShowWorkspaceExport(); canvas.Clear(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == L.Get("library.bindForExport")), "export explains optional binding requirement");
        view.PointerDown(70, 328, 0, false, false);
        Check(!exported, "unbound export is disabled");
        view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
        view.PointerDown(620, 30, 0, false, false); canvas.Clear(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "settings remain manually accessible");
        string songs = Path.Combine(root, "Later Songs"); Directory.CreateDirectory(songs);
        settings.Songs = songs; settings.Save(config);
        Check(LibrarySettings.Load(config).Songs == songs, "later binding persists");
        WaitForLibrary(view);
    }
    private static void WaitForLibrary(EditorView view)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (view.LibraryLoading && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Check(!view.LibraryLoading, "background work completed");
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
