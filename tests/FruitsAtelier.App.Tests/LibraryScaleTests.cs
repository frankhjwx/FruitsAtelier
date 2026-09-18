using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using Microsoft.Data.Sqlite;

internal static class LibraryScaleTests
{
    public static void Pagination()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-pages-" + Guid.NewGuid().ToString("N"));
        try
        {
            string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
            for (int set = 0; set < 140; set++)
            {
                string directory = Path.Combine(songs, set.ToString("D3")); Directory.CreateDirectory(directory);
                for (int diff = 0; diff < 2; diff++)
                    File.WriteAllText(Path.Combine(directory, diff + ".osu"), $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Song {set:D3}\nTitleUnicode:原始 {set:D3}\nArtist:Artist\nCreator:Mapper\nVersion:Diff {diff}\n[HitObjects]\n");
            }
            var database = new LibraryDatabase(workspace, songs); database.Scan();
            using var snapshot = database.SearchSnapshot("");
            Check(snapshot.Count == 140, "difficulties share one set");
            Check(snapshot.Page(64).Count == 64 && snapshot.Page(128).Count == 12, "page boundaries");
            var last = snapshot.Page(139).Single();
            Check(last.Count == 2 && snapshot.FindIndex(last.Key) == 139, "set count and stable key lookup");
            Check(snapshot.Difficulties(last, 1).Single().Difficulty == "Diff 1", "difficulty offset");
            using var filtered = database.SearchSnapshot("原始 139");
            Check(filtered.Count == 1 && filtered.Page(0).Single().Count == 2, "unicode multiword search");
            var view = new EditorView(loadDemo: false); var canvas = new RecordingCanvas();
            view.InitializeLibrary(true, new() { Workspace = workspace, Songs = songs });
            void Paint() { canvas.Clear(); view.Render(canvas, 980, 620); }
            void WaitFor(Func<bool> predicate)
            {
                var deadline = Stopwatch.StartNew();
                do { Paint(); if (predicate() && !view.LibraryLoading) return; Thread.Sleep(10); }
                while (deadline.Elapsed.TotalSeconds < 8);
                throw new Exception("Paged library did not settle");
            }
            WaitFor(() => view.LibrarySetTotal == 140);
            Check(canvas.Texts.Any(text => text.Value == "Artist // Mapper"), "library displays mapper beside artist");
            view.PointerDown(647, 577, 0, false, false); view.PointerUp(647, 577, 0);
            WaitFor(() => canvas.Texts.Any(text => text.Value == "原始 139"));
            Check(view.LibraryCachedRows <= 512, "bounded UI pages");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static int Benchmark(string? fixture = null)
    {
        string root = Path.GetFullPath(fixture ?? Path.Combine("artifacts", "library-scale", "fixture-" + Guid.NewGuid().ToString("N")));
        string songs = Path.Combine(root, "Songs"), workspace = Path.Combine(root, "Workspace");
        Console.WriteLine(fixture is null ? "Seeding 500,000 indexed maps / distinct sets (synthetic metadata, no audio or background files)..." : "Using existing synthetic fixture...");
        var database = new LibraryDatabase(workspace, songs);
        if (fixture is null)
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(workspace, "library.db"), Pooling = false }.ToString()))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE numbers(n) AS (SELECT 0 UNION ALL SELECT n+1 FROM numbers WHERE n<499999)
                INSERT INTO maps SELECT $r||'/'||printf('%06d',n)||'/map.osu',$r,0,0,
                json_object('Path',$r||'/'||printf('%06d',n)||'/map.osu','Directory',$r||'/'||printf('%06d',n),
                    'Title','Song '||printf('%06d',n),'TitleUnicode','','Artist','Artist','ArtistUnicode','',
                    'Creator','Mapper','Difficulty','Hard','Tags','benchmark','Source','','Audio','','Background',''),
                'SONG '||printf('%06d',n)||' ARTIST MAPPER HARD BENCHMARK' FROM numbers;
                """;
            command.Parameters.AddWithValue("$r", songs);
            command.ExecuteNonQuery();
        }
        Console.WriteLine("Building disk-backed set index...");
        var timer = Stopwatch.StartNew();
        using var snapshot = database.SearchSnapshot("");
        double indexMs = timer.Elapsed.TotalMilliseconds;
        Check(snapshot.Count == 500000, "full set count");
        var timings = new List<double>(); var random = new Random(27);
        for (int i = 0; i < 500; i++)
        {
            int start = random.Next(500000 / 64) * 64;
            timer.Restart(); var page = snapshot.Page(start); timings.Add(timer.Elapsed.TotalMilliseconds);
            Check(page.Count <= 64 && page[0].Index == start && page[0].Map.Title == "Song " + start.ToString("D6"), "random page data");
        }
        Check(snapshot.Page(499999).Single().Map.Title == "Song 499999", "last page");
        using (var filtered = database.SearchSnapshot("song 499999")) Check(filtered.Count == 1, "filtered result");
        Console.WriteLine($"Index: {indexMs:F1} ms; checking UI scrolling and bounded cache...");
        var view = new EditorView(loadDemo: false);
        var canvas = new RecordingCanvas();
        view.InitializeLibrary(true, new() { Workspace = workspace, Songs = songs });
        void Paint() { canvas.Clear(); view.Render(canvas, 980, 620); }
        var deadline = Stopwatch.StartNew();
        while (view.LibrarySetTotal != 500000 || view.LibraryLoading)
        {
            Paint(); Thread.Sleep(10);
            if (deadline.Elapsed.TotalSeconds > 90) throw new Exception("Library index did not become available");
        }
        var frames = new List<double>(); int peakRows = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int jump = 0; jump < 100; jump++)
        {
            float y = 170 + (jump == 99 ? 1f : (float)random.NextDouble()) * 407;
            view.PointerDown(647, y, 0, false, false); view.PointerUp(647, y, 0);
            for (int frame = 0; frame < 20; frame++)
            {
                timer.Restart(); Paint(); frames.Add(timer.Elapsed.TotalMilliseconds);
                peakRows = Math.Max(peakRows, view.LibraryCachedRows);
                Thread.Sleep(1);
            }
        }
        long bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / frames.Count;
        Check(peakRows <= 512, "bounded metadata cache");
        Check(canvas.Texts.Any(text => text.Value == "Song 499999"), "scrollbar reaches last set");
        timings.Sort(); frames.Sort();
        var report = new { maps = 500000, sets = snapshot.Count, indexMs, pageMedianMs = timings[timings.Count / 2],
            pageP95Ms = timings[(int)(timings.Count * .95)], uiMedianMs = frames[frames.Count / 2],
            uiP95Ms = frames[(int)(frames.Count * .95)], uiMaxMs = frames[^1], peakCachedRows = peakRows,
            uiAllocatedBytesPerFrame = bytesPerFrame, managedHeapBytes = GC.GetTotalMemory(true),
            note = "Synthetic SQLite metadata; 100 random scrollbar jumps / 2,000 UI frames. RecordingCanvas excludes GPU presentation, image decoding and real filesystem scanning." };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine("artifacts", "library-scale", "benchmark.json"), json);
        Console.WriteLine(json);
        return 0;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
