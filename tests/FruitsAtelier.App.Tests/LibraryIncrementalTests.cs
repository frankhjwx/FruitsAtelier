using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;

internal static class LibraryIncrementalTests
{
    private static string Map(int mode, string title) => $"osu file format v14\n[General]\nMode:{mode}\n[Metadata]\nTitle:{title}\nArtist:Artist\nCreator:Mapper\nVersion:Hard\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,64,1000,1,0,0:0:0:0:\n";

    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "library-incremental", Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
        string first = Path.Combine(songs, "First.set"), other = Path.Combine(songs, "Other");
        Directory.CreateDirectory(first); Directory.CreateDirectory(other);
        string source = Path.Combine(first, "map.osu"), unrelated = Path.Combine(other, "map.osu");
        File.WriteAllText(source, Map(0, "Standard")); File.WriteAllText(unrelated, Map(2, "Catch"));
        var db = new LibraryDatabase(workspace, songs);
        Check(db.Scan().Count == 2, "Both standard and Catch are indexed");
        File.WriteAllText(source, Map(0, "Changed"));
        using (var inaccessible = new FileStream(unrelated, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var progress = new List<LibraryScanProgress>();
            var scan = db.ScanDirectories([first], progress: progress.Add);
            Check(scan.Count == 1 && scan.Errors.Count == 0 && progress[^1].Files == 1, "Only the changed directory is examined");
        }
        Check(db.Search("Changed").Count == 1 && db.Search("Catch").Count == 1, "Incremental scan retains unrelated rows");
        File.Delete(source); db.ScanDirectories([first]);
        Check(db.Search("").Count == 1, "Scoped scan removes deleted maps");
        File.WriteAllText(source, Map(0, "Standard")); db.ScanDirectories([first]);
        var settings = new LibrarySettings { Workspace = workspace, Songs = songs };
        var map = db.Search("Standard").Single();
        var session = LibraryOperations.Open(map, settings);
        string original = File.ReadAllText(source);
        try { WorkspaceExport.Plan(session, session.Project.Difficulties[0], songs, true, "", true); throw new Exception("Standard overwrite was accepted"); }
        catch (IOException) { }
        Check(File.ReadAllText(source) == original, "Import and export validation preserve the standard source");
        Check(OsuBeatmapReader.Setting(OsuBeatmapWriter.Serialize(session.Project.Difficulties[0].Document).ReadBack, "General", "Mode") == "2", "Output remains Catch");

        var view = new EditorView(loadDemo: false); var canvas = new RecordingCanvas();
        view.InitializeLibrary(true, settings);
        var timer = Stopwatch.StartNew();
        do { canvas.Clear(); view.Render(canvas, 980, 620); if (!view.LibraryLoading && view.LibrarySetTotal == 2) break; Thread.Sleep(10); }
        while (timer.Elapsed < TimeSpan.FromSeconds(15));
        Check(view.LibrarySetTotal == 2, "Library cached rows available");
        var pending = new TaskCompletionSource<(LibraryDatabase Database, LibraryScan Scan)>();
        typeof(EditorView).GetField("scanTask", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, pending.Task);
        bool opened = false; view.RequestLibraryOpen = (_, _) => opened = true;
        canvas.Clear(); view.Render(canvas, 980, 620);
        view.PointerDown(240, 222, 0, false, false); view.PointerUp(240, 222, 0);
        view.PointerDoubleClick(240, 222, false, false);
        Check(opened, "Indexed maps remain clickable during a background refresh");
        pending.SetResult((db, new(2, [])));
        canvas.Clear(); view.Render(canvas, 980, 620); view.StopFileMonitoring();

        using var monitor = new WorkspaceFileMonitor();
        typeof(EditorView).GetField("fileMonitor", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, monitor);
        typeof(EditorView).GetField("nextMonitorConfiguration", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, DateTime.MaxValue);
        view.LoadDocument(new MapDocument { IsDemo = false }); view.CloseLibrary();
        string renamed = Path.GetFullPath(Path.Combine(songs, "Renamed.set"));
        Check(WorkspaceProject.Within(root, first) && WorkspaceProject.Within(root, renamed), "Fixture move stays within its root");
        Directory.Move(first, renamed); monitor.Record(first); monitor.Record(renamed); Thread.Sleep(800);
        canvas.Clear(); view.Render(canvas, 980, 620);
        Check(db.Search("Standard").Single().Directory == first, "Editor defers queued catalog changes");
        var queued = (HashSet<string>)typeof(EditorView).GetField("libraryPendingDirectories", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        Check(queued.Contains(first) && queued.Contains(renamed), "Dotted directory renames queue both index scopes");
        view.ShowLibrary(); timer.Restart();
        do { canvas.Clear(); view.Render(canvas, 980, 620); if (!view.LibraryLoading && db.Search("Standard").SingleOrDefault()?.Directory == renamed) break; Thread.Sleep(10); }
        while (timer.Elapsed < TimeSpan.FromSeconds(15));
        Check(db.Search("Standard").Single().Directory == renamed && db.Search("Catch").Count == 1,
            "Returning to Library applies only queued source changes");
        view.StopFileMonitoring();
    }

    public static int Benchmark()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "library-loading", "fixture-" + Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
        Console.WriteLine("Creating 10,000 standard/Catch difficulties across 1,000 sets...");
        for (int set = 0; set < 1000; set++)
        {
            string directory = Path.Combine(songs, set.ToString("D4")); Directory.CreateDirectory(directory);
            for (int diff = 0; diff < 10; diff++) File.WriteAllText(Path.Combine(directory, diff + ".osu"), Map(diff % 2 == 0 ? 0 : 2, "Song " + set.ToString("D4")));
            if (set % 100 == 0) Console.WriteLine($"Created {set * 10:N0}/10,000 difficulties");
        }
        var db = new LibraryDatabase(workspace, songs); var timer = Stopwatch.StartNew();
        Check(db.Scan().Count == 10000, "Cold scan count"); double coldMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); Check(db.Scan().Count == 10000, "Warm scan count"); double warmMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart(); using var snapshot = db.SearchSnapshot(""); var page = snapshot.Page(0); double searchMs = timer.Elapsed.TotalMilliseconds;
        string changedDirectory = Path.Combine(songs, "0500");
        File.WriteAllText(Path.Combine(changedDirectory, "0.osu"), Map(0, "Changed"));
        var progress = new List<LibraryScanProgress>(); timer.Restart(); db.ScanDirectories([changedDirectory], progress: progress.Add); double incrementalMs = timer.Elapsed.TotalMilliseconds;
        Check(progress[^1].Files == 10 && snapshot.Count == 1000, "Incremental work scales with one set");
        var view = new EditorView(loadDemo: false); var canvas = new RecordingCanvas();
        timer.Restart(); view.InitializeLibrary(true, new() { Workspace = workspace, Songs = songs });
        while (view.LibrarySetTotal != 1000)
        {
            canvas.Clear(); view.Render(canvas, 980, 620); Thread.Sleep(1);
            if (timer.Elapsed.TotalSeconds > 30) throw new Exception("Cached startup timed out");
        }
        double cachedVisibleMs = timer.Elapsed.TotalMilliseconds;
        while (view.LibraryLoading) { canvas.Clear(); view.Render(canvas, 980, 620); Thread.Sleep(1); }
        view.StopFileMonitoring();
        var report = new { maps = 10000, sets = snapshot.Count, coldMs, warmMs, searchMs, incrementalMs, incrementalFiles = progress[^1].Files,
            cachedVisibleMs, note = "Real synthetic .osu files; RecordingCanvas excludes native rendering, audio and images." };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.Combine("artifacts", "library-loading")); File.WriteAllText(Path.Combine("artifacts", "library-loading", "benchmark.json"), json);
        Console.WriteLine(json); return 0;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
