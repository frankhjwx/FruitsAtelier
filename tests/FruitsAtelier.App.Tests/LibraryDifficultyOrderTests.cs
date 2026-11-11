using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LibraryDifficultyOrderTests
{
    public static void Run()
    {
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
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
