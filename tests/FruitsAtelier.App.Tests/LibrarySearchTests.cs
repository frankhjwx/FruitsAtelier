using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LibrarySearchTests
{
    public static void Run()
    {
        string root = Path.GetFullPath("artifacts/tests/library-search-" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
        string first = Write("First", "Blue Moon", "Night", "ＦｕｌｌＷｉｄｔｈ 原始", "[Hard]", "a%b_c");
        Write("First", "Blue Moon", "Day", "ＦｕｌｌＷｉｄｔｈ 原始", "[Easy]", "a%b_c");
        Write("Second", "Moonlight", "Night", "", "Expert", "other");
        string external = Path.Combine(root, "External"); Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "external.osu"), File.ReadAllText(first).Replace("Blue Moon", "External Moon"));
        var db = new LibraryDatabase(workspace, songs); db.RegisterSource(external); db.Scan();
        foreach (string query in new[] { "", " ", "o", "MOON", "blue night", "fullwidth", "原始", "[hard]", "a%b_c", "\" OR 1=1", "missing" })
        {
            var expected = db.Search(query).GroupBy(m => m.Directory).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            using var snapshot = db.SearchSnapshot(query);
            var rows = snapshot.Page(0).OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
            Check(rows.Length == expected.Length, "Search set count matches the independent scan: " + query);
            for (int i = 0; i < rows.Length; i++)
            {
                Check(rows[i].Key == expected[i].Key && rows[i].Count == expected[i].Count(), "Search groups and difficulty counts: " + query);
                Check(snapshot.Difficulties(rows[i], 0).Select(m => m.Path).Order().SequenceEqual(expected[i].Select(m => m.Path).Order()), "Search difficulty subset: " + query);
            }
        }
        using (var filtered = db.SearchSnapshot("moon", organization: new(DifficultyPaths: [first])))
            Check(filtered.Page(0).Single().Count == 1, "Collection search retains exact difficulty membership");
        File.WriteAllText(first, File.ReadAllText(first).Replace("Blue Moon", "Changed Song")); db.Scan();
        using (var changed = db.SearchSnapshot("changed")) Check(changed.SetCount == 1, "Changed metadata refreshes the search index");
        File.Delete(first); db.Scan();
        using (var deleted = db.SearchSnapshot("changed")) Check(deleted.SetCount == 0, "Deleted maps leave the search index");

        var view = new EditorView(loadDemo: false);
        var canvas = new RecordingCanvas();
        view.InitializeLibrary(true, new() { Workspace = workspace, Songs = songs });
        Wait(() => !view.LibraryLoading);
        var pending = new TaskCompletionSource<LibraryBrowser>();
        Set("searchTask", pending.Task);
        Set("libraryField", 2);
        view.TextInput('x');
        DateTime scheduled = (DateTime)Get("searchAfter")!;
        pending.SetResult(LibraryBrowser.Create(db, "", false, null, null, 0));
        Paint();
        Check((DateTime)Get("searchAfter")! == scheduled, "Discarding an obsolete query preserves the latest input deadline");
        Wait(() => !view.LibraryLoading && (string)Get("libraryBrowserQuery")! == "x");
        Check(view.LibrarySetTotal == db.Search("x").Select(m => m.Directory).Distinct().Count(), "Latest query publishes after the obsolete result is discarded");
        view.CompleteLibraryCacheWrites();
        CheckFaOnlyDifficulties();
        Console.WriteLine("PASS Library indexed substring search, membership, updates and superseded input");

        string Write(string folder, string title, string artist, string unicode, string difficulty, string tags)
        {
            string directory = Path.Combine(songs, folder); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, artist + ".osu");
            File.WriteAllText(path, $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:{title}\nTitleUnicode:{unicode}\nArtist:{artist}\nCreator:Mapper\nVersion:{difficulty}\nTags:{tags}\n[HitObjects]\n");
            return path;
        }
        object? Get(string name) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
        void Set(string name, object value) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
        void Paint() { canvas.Clear(); view.Render(canvas, 980, 620); }
        void Wait(Func<bool> ready)
        {
            var watch = Stopwatch.StartNew();
            do { Paint(); if (ready()) return; Thread.Sleep(5); } while (watch.Elapsed < TimeSpan.FromSeconds(10));
            throw new Exception("Library search did not settle");
        }
    }

    private static void CheckFaOnlyDifficulties()
    {
        string root = Path.GetFullPath("artifacts/tests/library-fa-only-" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), set = Path.Combine(songs, "Set"), workspace = Path.Combine(root, "Workspace");
        Directory.CreateDirectory(set);
        string source = Path.Combine(set, "Cup.osu");
        File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Local HS song\nArtist:Artist\nCreator:Mapper\nVersion:Cup\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n");
        var session = WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
        session.Project.Difficulties.Add(new() { Name = "HS", FaOnly = true, Document = new MapDocument() });
        WorkspaceProject.Save(session, session.Project);
        File.WriteAllText(Path.Combine(set, "Rain.osu"), File.ReadAllText(source).Replace("Version:Cup", "Version:Rain"));
        var db = new LibraryDatabase(workspace, songs); db.Scan();
        foreach (string query in new[] { "", "Local HS song" })
        {
            using var snapshot = db.SearchSnapshot(query);
            var row = snapshot.Page(0).Single();
            var details = snapshot.Difficulties(row, 0);
            Check(row.Count == 3 && details.Count == 3, "All songs counts saved and unimported difficulties exactly once");
            Check(details.Select(d => d.Difficulty).Order().SequenceEqual(new[] { "Cup", "HS", "Rain" }), "All songs includes FA-only HS alongside source difficulties");
            var hs = details.Single(d => d.Difficulty == "HS");
            Check(hs.Path.EndsWith(".catchdiff") && File.Exists(hs.Path) && hs.ProjectPath == session.Directory,
                "FA-only row identifies its saved workspace difficulty");
            Check(snapshot.Difficulties(row, 1, 1).Single() == details[1], "Workspace difficulty pagination agrees with the full list");
        }
        using var collection = db.SearchSnapshot("", organization: new(DifficultyPaths: [source]));
        var collected = collection.Page(0).Single();
        Check(collected.Count == 1 && collection.Difficulties(collected, 0).Single().Difficulty == "Cup",
            "osu collections retain exact membership for projects with FA-only difficulties");
    }

    public static void Profile(string workspace, string songs)
    {
        var db = new LibraryDatabase(workspace, songs);
        foreach (string query in new[] { "", "a", "love", "xi", "camellia", "zzzzzz" })
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var browser = LibraryBrowser.Create(db, query, false, null, null, 0);
            Console.WriteLine(JsonSerializer.Serialize(new { query, sets = browser.SetCount,
                elapsedMs = watch.Elapsed.TotalMilliseconds,
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated }));
            browser.Retire();
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
