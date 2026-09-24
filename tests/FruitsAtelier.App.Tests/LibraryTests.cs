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
            ArchiveAndDelete(root);
            ExportOverlay();
            OptionalSongs(root);
            var view = new EditorView(); view.NewProject();
            view.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); view.LibrarySettings.Songs = songs;
            Check(view.SaveWorkspace(), "save creates workspace project");
            view.ChangeAudioPath(Path.Combine(songs, "missing.mp3"));
            view.SaveWorkspace();
            var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
            Check(canvas.Texts.Any(t => t.Value.Contains("missing.mp3")), "missing references visible inside editor");
            string savedDirectory = view.WorkspaceSession!.Directory;
            view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.title")), "separate library page");
            view.PointerDown(250, 100, 0, false, false); view.PointerUp(250, 100, 0);
            canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Lines.Any(l => l.X1 == 226 && l.X2 == 226 && l.Y1 == 96), "empty focused search shows a drawn caret");
            view.KeyDown(70, false, false); view.PointerDown(700, 480, 0, false, false); view.PointerUp(700, 480, 0);
            Check(!view.HasEditorProject && view.WorkspaceSession is null, "returning to library closes the editor project");
            view.LoadWorkspace(WorkspaceProject.Open(savedDirectory)); view.ShowWorkspaceExport(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.exportNew")) && canvas.Texts.Any(t => t.Value == L.Get("library.exportOverride")), "explicit export modes");
            view.CloseLibrary();
            Check(Directory.GetFiles(songs).Length == 0, "navigation and saving never write Songs");
            WaitForLibrary(view);
        }
        finally
        {
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Delete(root, true); break; }
                catch (IOException) when (attempt < 20) { Thread.Sleep(50); }
            }
        }
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
        Click(view, canvas, L.Get("library.exportFile"));
        Click(view, canvas, L.Get("library.exportChooseLocation"));
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
    private static void ArchiveAndDelete(string root)
    {
        string songs = Path.Combine(root, "Archive Songs"); Directory.CreateDirectory(songs);
        var view = new EditorView(); view.NewProject();
        string workspace = Path.Combine(root, "Archive workspace");
        var project = view.CaptureProject();
        string archive = Path.Combine(root, "standalone.osz");
        FruitsAtelier.App.Platform.LibraryOperations.ExportOsz(project, archive, false);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
            Check(zip.Entries.Count(e => e.FullName.EndsWith(".osu")) == project.Difficulties.Count,
                "OSZ contains every project difficulty");
        string sourceFolder = Path.Combine(root, "External set"); Directory.CreateDirectory(sourceFolder);
        string first = Path.Combine(sourceFolder, "first.osu");
        OsuBeatmapWriter.WriteFile(project.Difficulties[0].Document, first);
        OsuBeatmapWriter.WriteFile(FruitsAtelier.App.Platform.LibraryOperations.StandaloneExportDocument(project.Difficulties[0], "Extra"),
            Path.Combine(sourceFolder, "extra.osu"));
        File.WriteAllText(Path.Combine(sourceFolder, "storyboard.osb"), "[Events]");
        File.WriteAllBytes(Path.Combine(sourceFolder, "clip.mp4"), [1, 2, 3]);
        var map = LibraryDatabase.ReadMetadata(first)!;
        string libraryArchive = Path.Combine(root, "library.osz");
        FruitsAtelier.App.Platform.LibraryOperations.ExportOsz(
            FruitsAtelier.App.Platform.LibraryOperations.ExportProject(map), libraryArchive, false);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(libraryArchive))
            Check(zip.Entries.Count(e => e.FullName.EndsWith(".osu")) == 2
                && zip.Entries.Any(e => e.FullName == "storyboard.osb")
                && zip.Entries.Any(e => e.FullName == "clip.mp4"),
                "library OSZ includes all Catch difficulties and optional resources");
        var session = WorkspaceProject.Create(workspace, project, songs);
        FruitsAtelier.App.Platform.LibraryOperations.DeleteProject(session.Directory,
            new LibrarySettings { Workspace = workspace, Songs = songs });
        Check(!Directory.Exists(session.Directory), "workspace-only project can be deleted");
        string source = Path.Combine(songs, "retained.osu");
        OsuBeatmapWriter.WriteFile(project.Difficulties[0].Document, source);
        var linked = WorkspaceProject.Create(workspace,
            BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
        bool rejected = false;
        try { FruitsAtelier.App.Platform.LibraryOperations.DeleteProject(linked.Directory,
            new LibrarySettings { Workspace = workspace, Songs = songs }); }
        catch (IOException) { rejected = true; }
        Check(rejected && Directory.Exists(linked.Directory) && File.Exists(source),
            "deletion protects projects linked to existing Songs files");
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
        Check(view.SaveWorkspace() && view.WorkspaceSession!.Directory == directory, "repeated save keeps the same workspace project");
        var db = new LibraryDatabase(workspace, ""); db.Scan();
        Check(db.Search("", true).Count == 1, "unbound project library remains available");
        for (int i = 0; i < 3; i++)
        {
            view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(!canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "reopening library does not force settings");
            view.CloseLibrary();
        }
        view.LoadWorkspace(WorkspaceProject.Open(directory));
        bool exported = false; view.RequestWorkspaceExport = (_, _) => exported = true;
        view.ShowWorkspaceExport(); canvas.Clear(); view.Render(canvas, 980, 620);
        Click(view, canvas, L.Get("library.exportCreate"));
        Check(!exported, "unbound export is disabled");
        view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
        Click(view, canvas, L.Get("library.settings"));
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
    private static void Click(EditorView view, RecordingCanvas canvas, string label)
    {
        var text = canvas.Texts.Last(t => t.Value == label);
        view.PointerDown(text.X + 4, text.Y + 4, 0, false, false);
        view.PointerUp(text.X + 4, text.Y + 4, 0);
        canvas.Clear(); view.Render(canvas, 980, 620);
    }

    private static void ExportOverlay()
    {
        var view = new EditorView();
        var canvas = new RecordingCanvas(); view.Render(canvas, 980, 620);
        var before = view.Document.DeepClone();
        var bounds = view.CanvasPlotBounds;
        var zoom = view.CanvasZoom;
        view.RequestSave = view.ShowWorkspaceExport;
        view.KeyDown(83, true, false);
        Check(view.ExportVisible && !view.LibraryVisible, "Ctrl+S opens overlay without switching to library");
        canvas.Clear(); view.Render(canvas, 980, 620);
        Check(canvas.Texts.Any(t => t.Value == L.Get("ui.file")), "editor chrome remains beneath overlay");
        Check(view.CanvasPlotBounds == bounds, "overlay keeps canvas layout");
        view.KeyDown(70, false, false);
        view.PointerDown(bounds.X, bounds.Bottom - 20, 0, false, false);
        view.PointerMove(bounds.Right, bounds.Y, false, false);
        view.PointerUp(bounds.Right, bounds.Y, 0);
        view.PointerDoubleClick(bounds.X, bounds.Y, false, false);
        view.Wheel(bounds.X, bounds.Y, 120, true);
        view.KeyDown(46, false, false); view.KeyDown(90, true, false);
        Check(view.Document.ContentEquals(before) && view.CanvasZoom == zoom, "modal blocks edits, undo and canvas zoom");
        Click(view, canvas, L.Get("library.exportOverride"));
        Check(!canvas.Texts.Any(t => t.Value == L.Get("library.newDifficultyName")), "update mode hides name field");
        Click(view, canvas, L.Get("library.exportFile"));
        Check(!canvas.Texts.Any(t => t.Value == L.Get("library.exportNewTarget") || t.Value == L.Get("library.exportReplaceTarget")), "standalone hides target preview");
        var label = canvas.Texts.Last(t => t.Value == L.Get("library.newDifficultyName"));
        view.PointerDown(label.X + 12, label.Y + 40, 0, false, false);
        view.KeyDown(65, true, false); view.PasteLibraryText("Overlay export");
        view.KeyDown(83, true, false);
        string? name = null; view.RequestOsuExport = value => name = value;
        canvas.Clear(); view.Render(canvas, 980, 620);
        Click(view, canvas, L.Get("library.exportChooseLocation"));
        Check(name == "Overlay export", "repeated save shortcut preserves typed export name");
        view.KeyDown(27, false, false);
        Check(!view.ExportVisible && view.Document.ContentEquals(before), "Escape dismisses without changing content");
        view.ShowWorkspaceExport(); canvas.Clear(); view.Render(canvas, 980, 620);
        Click(view, canvas, L.Get("mac.cancel"));
        Check(!view.ExportVisible, "cancel dismisses overlay");
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
