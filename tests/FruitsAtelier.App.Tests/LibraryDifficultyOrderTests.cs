using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LibraryDifficultyOrderTests
{
    public static void Run()
    {
        ColdIndex();
        string root = Path.GetFullPath("artifacts/tests/library-difficulty-order-" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), directory = Path.Combine(songs, "Set");
        Directory.CreateDirectory(directory);
        LibraryBrowser? browser = null;
        try
        {
            for (int i = 0; i < 140; i++)
                File.WriteAllText(Path.Combine(directory, $"{i:D3}.osu"), $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Song\nArtist:Artist\nCreator:Mapper\nVersion:Diff {i:D3}\n[HitObjects]\n");
            var database = new LibraryDatabase(Path.Combine(root, "Workspace"), songs); database.Scan();
            browser = LibraryBrowser.Create(database, "", false, null, null, 0);
            Check(browser.Detail(0) is not null, "The selected set publishes names with its first browser result");
            using var ratingStarted = new ManualResetEventSlim();
            using var releaseRating = new ManualResetEventSlim();
            browser.RequestDetails(0, _ =>
            {
                ratingStarted.Set();
                if (!releaseRating.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Rating gate timed out");
                return 5;
            });
            try
            {
                Check(ratingStarted.Wait(TimeSpan.FromSeconds(5)), "Cold rating worker starts");
                Check(browser.RatingProgress == (0, 140), "Blocked star work reports completed/total counts");
                var progressView = new EditorView(false);
                typeof(EditorView).GetProperty("LibraryVisible")!.SetValue(progressView, true);
                typeof(EditorView).GetField("libraryBrowser", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(progressView, browser);
                var progressCanvas = new RecordingCanvas(); progressView.Render(progressCanvas, 980, 620);
                Check(progressCanvas.Texts.Any(t => t.X == 16 && t.Value == FruitsAtelier.Localization.Strings.Get("library.ratingProgress", 0, 140).Split('\n')[1]),
                    "The lower-left status shows star progress while work is blocked");
                WaitForRow(0);
                Check(browser.Detail(0) is not null, "Difficulty names appear before cold ratings finish");
                browser.RequestDetails(128);
                WaitForRow(128);
                Check(browser.Detail(139) is not null, "Scrolling loads metadata while the rating worker is blocked");
            }
            finally { releaseRating.Set(); }
            Wait();
            Check(browser.Detail(128) is not null, "Rating completion retains the current detail page");
            browser.Retire();
            browser = LibraryBrowser.Create(database, "", false, null, null, 0);
            int calls = 0;
            browser.RequestDetails(0, map => { calls++; int value = int.Parse(Path.GetFileNameWithoutExtension(map.Path)); return value == 0 ? null : value / 10d; });
            Wait();
            Check(browser.Detail(0)?.Difficulty == "Diff 139" && browser.Detail(127)?.Difficulty == "Diff 012",
                "The full set is ranked by descending stars before selecting the first page");
            Check(browser.TryGetDetailRating(browser.Detail(0)!.Path, out var stars) && stars == 13.9, "Displayed stars use the same result as sorting");
            browser.RequestDetails(128, _ => throw new Exception("Cached ordering must not recalculate ratings"));
            Wait();
            Check(browser.Detail(128)?.Difficulty == "Diff 011" && browser.Detail(139)?.Difficulty == "Diff 000",
                "Descending order crosses page boundaries and unknown ratings come last");
            Check(calls == 140, "Each difficulty is rated once for the selected set");
            var saved = browser.SaveView()!;
            var preview = LibraryBrowser.Restore(saved);
            Check(preview.Detail(128)?.Difficulty == "Diff 011", "Persisted previews retain sorted difficulty rows");
            preview.Retire();

            browser.Retire();
            browser = LibraryBrowser.Create(database, "", false, null, null, 0);
            browser.RequestDetails(0, _ => 5);
            Wait();
            Check(browser.Detail(0)?.Difficulty == "Diff 000", "Equal ratings use a stable difficulty-name tie break");
            browser.Retire();
            string workspace = Path.Combine(root, "Projects");
            for (int i = 0; i < 2; i++)
                WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments([
                    MyMapsTests.Document("Project " + i, "Mapper"), MyMapsTests.Document("Project " + i, "Mapper")]), "");
            var projects = new LibraryDatabase(workspace, ""); projects.Scan();
            browser = LibraryBrowser.Create(projects, "", true, null, null, 0);
            var next = browser.Get(1)!;
            int oldCalls = 0;
            ratingStarted.Reset(); releaseRating.Reset();
            browser.RequestDetails(0, _ => { Interlocked.Increment(ref oldCalls); ratingStarted.Set(); releaseRating.Wait(TimeSpan.FromSeconds(10)); return 3; });
            try
            {
                Check(ratingStarted.Wait(TimeSpan.FromSeconds(5)), "Initial project rating starts");
                browser.Select(next);
                Check(browser.Detail(0)?.ProjectPath == next.Map.ProjectPath && browser.Detail(1) is not null,
                    "Selecting a project shows its names in the same frame without pumping or waiting");
                var replacement = LibraryBrowser.Create(projects, "", true, next.Key, null, 0);
                replacement.RetainDetails(browser);
                Check(replacement.Detail(1) is not null, "Refreshing a browser retains visible difficulty names");
                replacement.Retire();
            }
            finally { releaseRating.Set(); }
            Wait();
            Check(oldCalls == 1, "Selection change stops the previous set before its next difficulty");
            int newCalls = 0;
            browser.RequestDetails(0, map => { Check(map.ProjectPath == next.Map.ProjectPath, "Only the selected project is rated"); newCalls++; return 4; });
            Wait();
            Check(newCalls == 2, "The newly selected project receives rating work");
            Console.WriteLine("PASS Library difficulty star order, cross-page ranking, unknown values and cached previews");
        }
        finally
        {
            browser?.Retire();
            for (int retry = 0; retry < 20; retry++)
            {
                try { Directory.Delete(root, true); break; }
                catch (IOException) when (retry < 19) { Thread.Sleep(25); }
            }
        }
        void WaitForRow(int index)
        {
            var clock = Stopwatch.StartNew();
            while (browser!.Detail(index) is null)
            {
                if (clock.Elapsed.TotalSeconds > 2) throw new Exception("Metadata waited for cold star calculations");
                browser.Pump(); Thread.Sleep(5);
            }
        }
        void Wait()
        {
            var clock = Stopwatch.StartNew();
            while (browser!.Loading)
            {
                if (clock.Elapsed.TotalSeconds > 10) throw new Exception("Difficulty ranking timed out");
                browser.Pump(); Thread.Sleep(5);
            }
            if (browser.Error is { } error) throw new Exception(error);
        }
    }
    public static void ColdExisting()
    {
        var settings = LibrarySettings.Load();
        using var memory = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(settings.Workspace, "library-view.json")));
        string selected = memory.RootElement.GetProperty("Songs").GetProperty("Selected").GetString()!;
        if (!WorkspaceProject.Within(settings.Songs, selected) || !Directory.Exists(selected)) throw new Exception("Select a Songs folder before profiling cold startup");
        ColdIndex(selected);
    }
    private static void ColdIndex(string? existing = null)
    {
        string root = Path.GetFullPath("artifacts/tests/library-cold-index-" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), selected = Path.Combine(songs, "Selected"), other = Path.Combine(songs, "Other");
        if (existing is null) { Directory.CreateDirectory(selected); Directory.CreateDirectory(other); }
        else { selected = existing; songs = Path.GetDirectoryName(selected)!; }
        using var cancellation = new CancellationTokenSource();
        using var indexingOther = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        LibraryBrowser? browser = null, preview = null;
        Task? scan = null;
        try
        {
            if (existing is null)
            {
            for (int i = 0; i < 33; i++) File.WriteAllText(Path.Combine(selected, $"{i:D2}.osu"),
                $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Selected\nArtist:Artist\nCreator:Mapper\nVersion:Diff {i:D2}\n[HitObjects]\n");
            File.WriteAllText(Path.Combine(other, "other.osu"), "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Other\nVersion:Other\n[HitObjects]\n");
            }
            int expected = Directory.GetFiles(selected, "*.osu").Length;
            var db = new LibraryDatabase(Path.Combine(root, "Workspace"), songs);
            scan = Task.Run(() => db.ScanDirectories(null, cancellation: cancellation.Token, progress: p =>
            {
                if (p.Files == expected + 1) { indexingOther.Set(); if (!resume.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Cold scan gate timed out"); }
            }, priorityDirectory: selected));
            Check(indexingOther.Wait(TimeSpan.FromSeconds(5)), "Scan reaches unrelated files after the selected directory");
            browser = LibraryBrowser.Create(db, "", false, selected, null, 0);
            Check(!scan.IsCompleted && browser.Selected?.Count == expected && browser.Detail(expected - 1) is not null,
                "All selected difficulty names are published while the uncached full scan is blocked");
            var saved = browser.SaveView()! with { DetailKey = null, Details = [] };
            preview = LibraryBrowser.Restore(saved);
            preview.RequestDetails(0);
            var wait = Stopwatch.StartNew();
            while (preview.Detail(expected - 1) is null && wait.Elapsed.TotalSeconds < 2) { preview.Pump(); Thread.Sleep(5); }
            Check(preview.Detail(expected - 1) is not null && !scan.IsCompleted,
                "A cards-only startup preview fills names independently of the unfinished index");
            var view = new EditorView(false);
            typeof(EditorView).GetProperty("LibraryVisible")!.SetValue(view, true);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(EditorView).GetField("libraryBrowser", flags)!.SetValue(view, preview);
            var uiScan = scan.ContinueWith(t => (db, t.IsCompletedSuccessfully ? ((Task<LibraryScan>)t).Result : new LibraryScan(0, [])));
            typeof(EditorView).GetField("scanTask", flags)!.SetValue(view, uiScan);
            typeof(EditorView).GetField("libraryScanProgress", flags)!.SetValue(view, new LibraryScanProgress(expected + 1, expected + 1, 0));
            var ratings = new Dictionary<string, double?>();
            for (int i = 0; i < expected; i++) if (preview.Detail(i) is { } row) ratings[row.Path] = 0;
            typeof(EditorView).GetField("libraryRatings", flags)!.SetValue(view, ratings);
            var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
            Check(canvas.Texts.Any(t => t.X == 16 && t.Value == FruitsAtelier.Localization.Strings.Get("library.scanning"))
                && canvas.Texts.Any(t => t.X == 16 && t.Value == FruitsAtelier.Localization.Strings.Get("library.scanProgress", expected + 1, expected + 1, 0).Split('\n')[0]),
                "The lower-left status identifies the active scan and processed file count");
            Check(canvas.Texts.Any(t => t.X >= 1120 && t.Y >= 334 && t.Value == preview.Detail(0)!.Difficulty) && !scan.IsCompleted,
                "The Library actually draws selected difficulty names while the cold scan remains blocked");
            Console.WriteLine($"PASS Cold index: {expected} selected names visible before full scan completes, including cards-only startup preview; existing={existing is not null}");
        }
        finally
        {
            if (existing is not null) cancellation.Cancel();
            resume.Set();
            try { scan?.GetAwaiter().GetResult(); } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            browser?.Retire(); preview?.Retire();
            for (int i = 0; i < 30; i++)
                try { Directory.Delete(root, true); break; } catch (IOException) when (i < 29) { Thread.Sleep(20); }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
