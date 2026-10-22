using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using Microsoft.Data.Sqlite;

internal static class LibraryPersistentCacheTests
{
    public static void ProfileExisting()
    {
        var settings = LibrarySettings.Load();
        string originalWorkspace = settings.Workspace;
        string root = Path.GetFullPath("artifacts/tests/library-startup-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(originalWorkspace, "library.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var target = new SqliteConnection($"Data Source={Path.Combine(root, "library.db")};Pooling=False"))
        {
            source.Open(); target.Open(); source.BackupDatabase(target);
        }
        File.Copy(Path.Combine(originalWorkspace, "library-view.json"), Path.Combine(root, "library-view.json"));
        settings.Workspace = root;
        var view = new EditorView(loadDemo: false);
        Prepare(view);
        var db = new LibraryDatabase(root, settings.Songs);
        var watch = Stopwatch.StartNew();
        var browser = LibraryBrowser.Create(db, (string)Field(view, "libraryQuery")!, false,
            (string?)Field(view, "selectedLibraryGroup"), null, (float)Field(view, "libraryScroll")!,
            (IReadOnlyCollection<string>?)Property(view, "LibraryFilterKeys"), (LibraryOrganization)Property(view, "CurrentLibraryOrganization")!);
        double queryMs = watch.Elapsed.TotalMilliseconds;
        browser.RequestDetails((int)Field(view, "libraryDiffScroll")!);
        var detailsWait = Stopwatch.StartNew();
        while (browser.Loading && detailsWait.Elapsed < TimeSpan.FromSeconds(15)) { browser.Pump(); Thread.Sleep(10); }
        browser.Pump();
        Set("libraryBrowser", browser);
        Set("libraryBrowserQuery", Field(view, "libraryQuery"));
        Set("runningOrganizationKey", Property(view, "LibrarySearchKey"));
        Set("searchTaskQuery", Property(view, "LibrarySearchKey"));
        Invoke(view, "SaveLibraryPreview");
        ((Task)Field(view, "libraryPreviewSave")!).GetAwaiter().GetResult();
        view = new EditorView(loadDemo: false);
        watch.Restart(); Prepare(view); Invoke(view, "LoadLibraryPreview");
        typeof(EditorView).GetProperty("LibraryVisible")!.SetValue(view, true);
        var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
        double firstFrameMs = watch.Elapsed.TotalMilliseconds;
        Check(view.LibrarySetTotal == browser.SetCount && view.LibrarySetTotal > 0, "Existing library cards restore without database initialization or search");
        var report = new { sets = browser.SetCount, cachedRows = view.LibraryCachedRows, queryMs, firstFrameMs,
            note = "Read-only copy of the configured catalog; RecordingCanvas excludes GPU and image decoding. No source/project files are modified." };
        string json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "report.json"), json); Console.WriteLine(json);
        browser.Retire();

        void Prepare(EditorView editor)
        {
            typeof(EditorView).GetProperty("LibrarySettings")!.SetValue(editor, settings);
            Invoke(editor, "LoadLibraryMemory");
        }
        void Set(string field, object? value) => typeof(EditorView).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, value);
    }
    private static object? Property(EditorView view, string name) => typeof(EditorView).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);
    private static void Invoke(EditorView view, string name) => typeof(EditorView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);

    public static void Run()
    {
        string root = Path.GetFullPath("artifacts/tests/library-persistent-" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(songs);
        string source = Path.Combine(songs, "map.osu");
        File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Cached\nArtist:Artist\nCreator:Mapper\nVersion:Hard\n[Difficulty]\nCircleSize:4\nSliderMultiplier:1.4\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,64,1000,1,0,0:0:0:0:\n400,64,1100,1,0,0:0:0:0:\n");
        var settings = new LibrarySettings { Workspace = workspace, Songs = songs };
        var db = new LibraryDatabase(workspace, songs); db.Scan();

        var cache = new LibraryFileCache("test", "1", Path.Combine(root, "Cache"));
        var fingerprint = cache.Fingerprint(source);
        cache.Write(source, fingerprint, [1, 2, 3]);
        Check(new LibraryFileCache("test", "1", Path.Combine(root, "Cache")).Read(source)!.SequenceEqual(new byte[] { 1, 2, 3 }), "Disk cache survives a new owner");
        Check(new LibraryFileCache("test", "2", Path.Combine(root, "Cache")).Read(source) is null, "Algorithm versions isolate derived results");
        File.AppendAllText(source, "\n");
        Check(cache.Read(source) is null, "Changed sources invalidate cached bytes");
        cache.Write(source, fingerprint, [4]);
        Check(cache.Read(source) is null, "A source changed during calculation cannot publish stale cache data");
        cache.Write(source, cache.Fingerprint(source), [5]);
        File.WriteAllBytes(Directory.GetFiles(Path.Combine(root, "Cache"), "*.cache").Single(), [0]);
        Check(cache.Read(source) is null, "Truncated caches fall back to generation");
        db.Scan();

        var view = new EditorView(loadDemo: false);
        var canvas = new RecordingCanvas();
        view.InitializeLibrary(true, settings);
        ((Task)Field(view, "libraryStartupTask")!).GetAwaiter().GetResult();
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(workspace, "library.db")};Pooling=False"))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandText = "UPDATE maps SET stamp=stamp"; command.ExecuteNonQuery();
            Wait(() => view.LibrarySetTotal == 1, "Cached cards display while reconciliation is blocked by a writer");
            transaction.Rollback();
        }
        Wait(() => !view.LibraryLoading && Ratings(view).ContainsKey(source), "Cold rating completes");
        double? first = Ratings(view)[source];
        Check(first is not null, "Fixture has a rating");
        Wait(() => Field(view, "libraryPreviewSave") is Task { IsCompleted: true }
            && File.Exists(Path.Combine(workspace, "library-preview.json"))
            && ReferenceEquals(Field(view, "savedPreviewBrowser"), Field(view, "libraryBrowser"))
            && (long)Field(view, "savedPreviewRevision")! == ((LibraryBrowser)Field(view, "libraryBrowser")!).ContentRevision,
            "Visible page and selected difficulty are persisted");
        view.SaveLibraryMemory();
        string previewPath = Path.Combine(workspace, "library-preview.json");
        string validPreview = File.ReadAllText(previewPath);
        File.WriteAllText(previewPath, "{");
        var invalid = new EditorView(loadDemo: false);
        typeof(EditorView).GetProperty("LibrarySettings")!.SetValue(invalid, settings);
        Invoke(invalid, "LoadLibraryMemory"); Invoke(invalid, "LoadLibraryPreview");
        Check(invalid.LibraryCachedRows == 0, "Damaged previews fall back to the database");
        File.WriteAllText(previewPath, validPreview);
        typeof(EditorView).GetField("libraryQuery", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(invalid, "Different query");
        Invoke(invalid, "LoadLibraryPreview");
        Check(invalid.LibraryCachedRows == 0, "A saved page cannot appear under a different search");
        typeof(EditorView).GetField("libraryQuery", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(invalid, "");
        var memory = Field(invalid, "libraryMemory")!;
        ((Dictionary<string, long>)memory.GetType().GetProperty("Visits")!.GetValue(memory)!).Add("A newly visited map", DateTime.UtcNow.Ticks);
        Invoke(invalid, "LoadLibraryPreview");
        Check(invalid.LibraryCachedRows > 0, "Visits recorded while editing do not discard the previous visible page");
        view.StopFileMonitoring();
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(workspace, "library.db")};Pooling=False"))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandText = "UPDATE maps SET stamp=stamp"; command.ExecuteNonQuery();
            view = new EditorView(loadDemo: false);
            var timer = Stopwatch.StartNew();
            view.InitializeLibrary(true, settings);
            var originalStartup = Field(view, "libraryStartupTask");
            var blockedStartup = new TaskCompletionSource<(LibraryDatabase Database, string Workspace, string Songs)>();
            typeof(EditorView).GetField("libraryStartupTask", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, blockedStartup.Task);
            canvas.Clear(); view.Render(canvas, 980, 620);
            Check(view.LibrarySetTotal == 1 && canvas.Texts.Any(t => t.Value == "Cached") && canvas.Texts.Any(t => t.Value == "Hard"), "First frame contains cached cards while database initialization is blocked");
            Check(!canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.scanning")), "Warm startup never replaces cached cards with a full-page loading message");
            Check(((Task)Field(view, "libraryStartupTask")!).IsCompleted == false, "Cached cards do not wait for database initialization");
            Console.WriteLine($"Warm first frame with blocked database: {timer.Elapsed.TotalMilliseconds:0.00} ms");
            typeof(EditorView).GetField("libraryStartupTask", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, originalStartup);
            transaction.Rollback();
        }
        Wait(() => !view.LibraryLoading && Ratings(view).ContainsKey(source), "Cached preview is reconciled");
        view.StopFileMonitoring();
        view = new EditorView(loadDemo: false);
        using (var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            view.InitializeLibrary(true, settings);
            Wait(() => !view.LibraryLoading && Ratings(view).ContainsKey(source), "Restart uses cached metadata and stars without reading the source");
            Check(Ratings(view)[source] == first, "Restart retains the computed stars");
            view.RefreshLibrary();
            Check(Ratings(view)[source] == first, "Starting a scan retains displayed stars");
            Wait(() => !view.LibraryLoading, "Warm scan finishes without dropping stars");
            Check(Ratings(view)[source] == first, "Warm scan preserves stars");
        }
        File.AppendAllText(source, "100,64,1200,1,0,0:0:0:0:\n400,64,1250,1,0,0:0:0:0:\n100,64,1300,1,0,0:0:0:0:\n");
        view.RefreshLibrary();
        Wait(() => !view.LibraryLoading && Ratings(view).GetValueOrDefault(source) != first, "Changed maps refresh their persisted stars");
        view.StopFileMonitoring();

        var document = MyMapsTests.Document("Native cover", "Mapper");
        OsuTimeline.SetBackgroundFilename(document, "cover.png");
        var project = WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments([document]), songs);
        db.ReindexProjects();
        using (var snapshot = db.SearchSnapshot("Native cover", organization: new(Creators: ["Mapper"])))
            Check(snapshot.Page(0).Single().Map.Background == Path.Combine(project.Directory, "cover.png"), "My Maps resolves a native project's cover");

        using (var connection = new SqliteConnection($"Data Source={Path.Combine(workspace, "library.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE project_metadata SET data=json_set(data,'$.Background',''); UPDATE project_difficulty_metadata SET data=json_set(data,'$.Background',''); PRAGMA user_version=8;";
            command.ExecuteNonQuery();
        }
        var upgraded = new LibraryDatabase(workspace, songs);
        using (var snapshot = upgraded.SearchSnapshot("Native cover", organization: new(Creators: ["Mapper"])))
            Check(snapshot.SetCount == 1, "Cache migration retains existing project listings before scanning");
        upgraded.Scan();
        using (var snapshot = upgraded.SearchSnapshot("Native cover", organization: new(Creators: ["Mapper"])))
            Check(snapshot.Page(0).Single().Map.Background == Path.Combine(project.Directory, "cover.png"), "Existing project caches acquire covers without editing their documents");

        if (typeof(EditorView).Assembly.GetType("FruitsAtelier.App.Rendering.D2DCanvas") is { } renderer)
        {
            string image = Path.Combine(root, "cover.png");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"), image);
            var load = renderer.GetMethod("LoadThumbnail", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check(load.Invoke(null, [image]) is not null, "Thumbnail is generated");
            using var locked = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.None);
            Check(load.Invoke(null, [image]) is not null, "A new thumbnail request reuses disk pixels without decoding its locked source");
        }
        Console.WriteLine("PASS Library persistent caches, warm startup, retained stars and My Maps cover");

        void Wait(Func<bool> ready, string message)
        {
            var timer = Stopwatch.StartNew();
            do { canvas.Clear(); view.Render(canvas, 980, 620); if (ready()) return; Thread.Sleep(10); }
            while (timer.Elapsed < TimeSpan.FromSeconds(15));
            throw new Exception(message);
        }
    }
    private static object? Field(EditorView view, string name) => typeof(EditorView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);
    private static Dictionary<string, double?> Ratings(EditorView view) => (Dictionary<string, double?>)Field(view, "libraryRatings")!;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
