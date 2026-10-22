using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using Microsoft.Data.Sqlite;

internal static class LibraryPersistentCacheTests
{
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
