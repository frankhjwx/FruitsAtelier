using System.IO.Compression;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class LibraryImportTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/library-import", Guid.NewGuid().ToString("N")));
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = Path.Combine(root, "osu", "Songs") };
        Directory.CreateDirectory(settings.Songs);
        string source = Path.Combine(settings.Songs, "existing", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, Map);
        string sourceHash = WorkspaceProject.Hash(source);
        string osz = Path.Combine(root, "谱面.OSZ"), osk = Path.Combine(root, "皮肤.OSK"), ignored = Path.Combine(root, "notes.txt");
        using (var zip = ZipFile.Open(osz, ZipArchiveMode.Create)) Add(zip, "map.osu", Map);
        using (var zip = ZipFile.Open(osk, ZipArchiveMode.Create))
        {
            Add(zip, "skin.ini", "[General]\nName: Drop skin");
            using var image = zip.CreateEntry("fruit-pear.png").Open();
            image.Write(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZxkAAAAASUVORK5CYII="));
        }
        File.WriteAllText(ignored, "ignore");
        var view = new EditorView(loadDemo: false);
        view.InitializeLibrary(true, settings);
        int batches = 0;
        WorkspaceSession? imported = null;
        view.RequestLibraryDrop = paths =>
        {
            batches++;
            Check(paths.SequenceEqual(new[] { osz, osk }), "Drop filters unsupported files and duplicate paths");
            imported = LibraryOperations.ImportPath(paths[0], settings);
            view.ImportSkin(paths[1]);
            view.LoadWorkspace(imported);
        };
        view.OpenSettings(); view.DropLibraryFiles([osz, osk]);
        Check(batches == 0, "Settings reject drops");
        view.KeyDown(27, false, false);
        view.ShowError("test"); view.DropLibraryFiles([osz, osk]); view.KeyDown(27, false, false);
        Check(batches == 0, "Error dialogs reject drops");
        view.DropLibraryFiles([ignored, Path.Combine(root, "missing.osz")]);
        Check(batches == 0, "Unsupported and missing files are ignored");
        view.DropLibraryFiles([osz, osk, ignored, osz]);
        Check(batches == 1 && imported is not null && !view.LibraryVisible, "A drop opens its workspace project");
        Check(WorkspaceProject.Within(settings.Workspace, imported!.Directory)
            && File.Exists(Path.Combine(imported.Directory, WorkspaceProject.ManifestName)), "Imported project is saved in workspace");
        Check(imported.Manifest.Difficulties.All(d => d.ExportTarget is null), "Import does not create export associations");
        Check(view.SkinName == "Drop skin" && WorkspaceProject.Within(Path.Combine(settings.Workspace, "Skins"), settings.SelectedSkin!),
            "Dropped skin is stored and loaded from workspace");
        Check(Directory.GetFiles(settings.Songs, "*", SearchOption.AllDirectories).SequenceEqual(new[] { source })
            && WorkspaceProject.Hash(source) == sourceHash && !Directory.Exists(settings.Skins), "Neither Songs nor osu Skins is modified");
        view.DropLibraryFiles([osz, osk]);
        Check(batches == 1, "Editor rejects library drops");
        Check(LibraryOperations.ImportPath(osz, settings).Directory == imported.Directory, "Reimport reuses the workspace project");

        var fromSongs = LibraryOperations.ImportPath(source, settings);
        var database = new LibraryDatabase(settings.Workspace, settings.Songs); database.Scan();
        Check(Presence(imported.Directory) == false && Presence(fromSongs.Directory) == true,
            "Identical names and map contents do not imply a Songs association");
        view.MarkSaved(); view.ShowLibrary();
        var canvas = new RecordingCanvas();
        for (int i = 0; i < 300; i++)
        {
            canvas.Clear(); view.Render(canvas, 980, 620);
            if (!view.LibraryLoading && view.LibrarySetTotal >= 2) break;
            Thread.Sleep(10);
        }
        Check(canvas.Texts.Any(t => t.Value == L.Get("library.inSongs"))
            && canvas.Texts.Any(t => t.Value == L.Get("library.notInSongs"))
            && canvas.Fills.Any(f => f.Bounds.Height == 78 && f.Color == 0x303449),
            "Mixed library shows distinct presence labels and workspace-only backgrounds");
        string export = Path.Combine(settings.Songs, "export", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(export)!); File.WriteAllText(export, Map);
        imported.Manifest.Difficulties[0].ExportTarget = export;
        imported.Manifest.Difficulties[0].ExportHash = WorkspaceProject.Hash(export);
        WorkspaceProject.Save(imported, imported.Project);
        Check(Presence(imported.Directory) == true, "Existing export target marks project as present");
        File.Delete(export); File.Delete(source);
        Check(Presence(imported.Directory) == false && Presence(fromSongs.Directory) == false,
            "Deleted source and export files are not marked present");
        using var unconfigured = new LibraryDatabase(settings.Workspace, "").SearchSnapshot("", true);
        Check(unconfigured.Page(0).All(r => r.InSongs is null), "Unconfigured Songs has a separate state");

        bool? Presence(string project)
        {
            using var snapshot = database.SearchSnapshot("", true);
            return snapshot.Page(0).Single(r => r.Map.ProjectPath == project).InSongs;
        }
    }

    public static void Metadata()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/metadata-display", Guid.NewGuid().ToString("N")));
        string source = Path.Combine(root, "Songs", "set", "map.osu");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!); File.WriteAllText(source, Map);
        var settings = new LibrarySettings { Workspace = Path.Combine(root, "Workspace"), Songs = Path.Combine(root, "Songs") };
        Check(settings.RomanisedMetadata && JsonSerializer.Deserialize<LibrarySettings>("{}")!.RomanisedMetadata,
            "New and older settings default to romanised metadata");
        var ui = new Ui(false); ui.View.InitializeLibrary(true, settings); ui.Resize(980, 620);
        for (int i = 0; i < 300; i++)
        {
            ui.Paint();
            if (!ui.View.LibraryLoading && ui.View.LibrarySetTotal == 1) break;
            Thread.Sleep(10);
        }
        Check(ui.Canvas.Texts.Count(t => t.Value == "Romanised title") == 2, "Romanised title appears in card and details");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("library.inSongs")), "Presence badge is visible");
        Check(ui.Canvas.Fills.Any(f => f.Bounds.Height == 78 && f.Color == 0x243D36), "Songs cards use a distinct background");
        settings.RomanisedMetadata = false; ui.Paint();
        Check(ui.Canvas.Texts.Count(t => t.Value == "原始标题") == 2
            && ui.Canvas.Texts.Any(t => t.Value == "原始作者 // Mapper"), "Unicode setting updates card and details");
        settings.Save(Path.Combine(root, "settings.json"));
        Check(!LibrarySettings.Load(Path.Combine(root, "settings.json")).RomanisedMetadata, "Preference survives reload");
        using (var search = new LibraryDatabase(settings.Workspace, settings.Songs).SearchSnapshot("原始标题"))
            Check(search.Count == 1, "Metadata preference does not restrict Unicode search");
        ui.View.LoadDocument(OsuBeatmapReader.ReadFile(source)); ui.View.CloseLibrary(); ui.Paint();
        var before = ui.View.Document.DeepClone();
        Check(ui.View.WindowTitle.Contains("原始作者 - 原始标题"), "Window title follows Unicode setting");
        settings.RomanisedMetadata = true;
        Check(ui.View.WindowTitle.Contains("Romanised artist - Romanised title"), "Window title follows romanised setting");
        ui.ClickText(L.Get("library.settings")); ui.ClickText(L.Get("settings.appearance"));
        ui.ClickText(L.Get("settings.romanisedOn"));
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.romanisedOff")), "Appearance exposes metadata toggle");
        ui.Key(27);
        Check(settings.RomanisedMetadata && before.ContentEquals(ui.View.Document) && !ui.View.IsDirty,
            "Cancelling preference draft preserves settings and map content");
        var metadata = ui.View.Document.OriginalSections.Single(s => s.Name == "Metadata");
        metadata.Lines.RemoveAll(line => line.StartsWith("Title:") || line.StartsWith("Artist:"));
        Check(ui.View.WindowTitle.Contains("原始作者 - 原始标题"), "Empty romanised fields fall back to Unicode");
        metadata.Lines.AddRange(["Title:Romanised title", "Artist:Romanised artist"]);
        settings.RomanisedMetadata = false;
        metadata.Lines.RemoveAll(line => line.StartsWith("TitleUnicode:") || line.StartsWith("ArtistUnicode:"));
        Check(ui.View.WindowTitle.Contains("Romanised artist - Romanised title"), "Empty Unicode fields fall back to romanised");
    }

    private const string Map = "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Romanised title\nTitleUnicode:原始标题\nArtist:Romanised artist\nArtistUnicode:原始作者\nCreator:Mapper\nVersion:Rain\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:";
    private static void Add(ZipArchive zip, string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
