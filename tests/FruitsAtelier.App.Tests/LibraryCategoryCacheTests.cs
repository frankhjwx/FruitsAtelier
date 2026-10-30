using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LibraryCategoryCacheTests
{
    private sealed record Category(bool Mine, bool Favourite, bool Recent, string? Collection, string[] Titles, string Query, LibraryOrder Sort);
    public static void Run()
    {
        string root = Path.GetFullPath("artifacts/tests/library-category-cache-" + Guid.NewGuid().ToString("N"));
        string workspace = Path.Combine(root, "Workspace"), songs = Path.Combine(root, "osu", "Songs");
        Directory.CreateDirectory(workspace);
        string[] names = ["One", "Two", "Three", "Four"];
        var sources = names.Select((name, index) =>
        {
            string folder = Path.Combine(songs, name); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "map.osu");
            File.WriteAllText(file, $"osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:{name}\nArtist:Artist\nCreator:{(index < 2 ? "Mapper" : "Guest")}\nVersion:Difficulty {name}\n[HitObjects]\n");
            return file;
        }).ToArray();
        var settings = new LibrarySettings { Workspace = workspace, Songs = songs, MapperUsernames = ["Mapper"] };
        string DirectoryOf(int i) => Path.GetDirectoryName(sources[i])!;
        var categories = new[]
        {
            new Category(false, false, false, null, names, "Artist", LibraryOrder.Title),
            new Category(true, false, false, null, names[..2], "Mapper", LibraryOrder.DateAdded),
            new Category(false, true, false, null, [names[2]], "Guest", LibraryOrder.Creator),
            new Category(false, false, true, null, [names[0], names[3]], "", LibraryOrder.RecentVisited),
            new Category(false, false, false, "fa:first", [names[1]], "", LibraryOrder.Title),
            new Category(false, false, false, "fa:second", [names[0], names[2], names[3]], "", LibraryOrder.DateAdded),
            new Category(false, false, false, "osu:first", [names[3]], "", LibraryOrder.Artist),
            new Category(false, false, false, "osu:second", [names[1], names[2]], "", LibraryOrder.Creator)
        };
        object Position(Category c) => new { c.Query, c.Sort };
        File.WriteAllText(Path.Combine(workspace, "library-view.json"), JsonSerializer.Serialize(new
        {
            FavouriteKeys = new[] { DirectoryOf(2) },
            Visits = new Dictionary<string, long> { [DirectoryOf(0)] = DateTime.UtcNow.Ticks, [DirectoryOf(3)] = DateTime.UtcNow.AddDays(-1).Ticks },
            Collections = new[]
            {
                new { Id = "fa:first", Name = "Same name", Keys = new[] { DirectoryOf(1) } },
                new { Id = "fa:second", Name = "Same name", Keys = new[] { DirectoryOf(0), DirectoryOf(2), DirectoryOf(3) } }
            },
            Songs = Position(categories[0]), MyProjects = Position(categories[1]), FavouritePosition = Position(categories[2]),
            Positions = categories.Skip(3).ToDictionary(c => c.Collection ?? "recent", Position)
        }));
        var catalog = new OsuCollectionCatalog(settings.OsuRoot, "fixture", true,
            [new("osu:first", "Same name", [], [sources[3]]), new("osu:second", "Same name", [], [sources[1], sources[2]])]);
        var view = new EditorView(loadDemo: false);
        var canvas = new RecordingCanvas();
        view.InitializeLibrary(true, settings); ConfigureCollections(view, catalog);
        var expectedFiles = new Dictionary<string, string>();
        foreach (var category in categories)
        {
            Switch(category);
            Wait(() => !view.LibraryLoading && view.LibrarySetTotal == category.Titles.Length);
            CheckContents(category);
            Invoke(view, "SaveLibraryPreviewOnLeave");
            ((Task)Field(view, "libraryPreviewSave")!).GetAwaiter().GetResult();
            string path = (string)Property(view, "LibraryPreviewPath")!;
            expectedFiles[path] = File.ReadAllText(path);
        }
        Check(expectedFiles.Count == categories.Length, "Every tab and Collection has a distinct disk cache");
        foreach (var pair in expectedFiles) Check(File.ReadAllText(pair.Key) == pair.Value, "Saving another page does not overwrite this cache");
        view.SaveLibraryMemory(); view.CompleteLibraryCacheWrites(); view.StopFileMonitoring();
        view = new EditorView(loadDemo: false); view.InitializeLibrary(true, settings); ConfigureCollections(view, catalog);
        var startup = Field(view, "libraryStartupTask");
        var blocked = new TaskCompletionSource<(LibraryDatabase Database, string Workspace, string Songs)>();
        Set(view, "libraryStartupTask", blocked.Task);
        Set(view, "osuCollections", catalog with { Collections = catalog.Collections.Reverse().ToArray() });
        foreach (var category in categories.Reverse())
        {
            Switch(category); canvas.Clear(); view.Render(canvas, 1440, 900);
            CheckContents(category);
            Check(Field(view, "libraryBrowser") is LibraryBrowser { IsPreview: true }, "Switching after restart uses the page's saved cache without a database query");
            Check(!canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("library.scanning")), "Cached pages do not show a full-page loading message");
        }
        Set(view, "osuCollections", catalog);
        foreach (var category in categories)
        {
            Switch(category); canvas.Clear(); view.Render(canvas, 1440, 900); CheckContents(category);
        }
        Set(view, "libraryStartupTask", startup);
        Wait(() => !view.LibraryLoading);
        Switch(categories[3]);
        Set(view, "libraryDescending", true);
        Invoke(view, "QueueLibrarySearch");
        Wait(() => !view.LibraryLoading);
        var recentBrowser = (LibraryBrowser)Field(view, "libraryBrowser")!;
        var visited = Enumerable.Range(0, recentBrowser.Count).Select(recentBrowser.Get).Single(r => r?.Map.Title == "Four")!.Map;
        typeof(EditorView).GetProperty("LibraryVisible")!.SetValue(view, false);
        Visit(visited);
        Wait(() => !ReferenceEquals(recentBrowser, Field(view, "libraryBrowser")) && !view.LibraryLoading);
        view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 1440, 900);
        Check(canvas.Texts.First(t => t.X == 314 && names.Contains(t.Value)).Value == "Four", "First returning frame already shows the refreshed visit order");

        var clicked = canvas.Texts.First(t => t.X == 314 && t.Value == "Four");
        view.PointerDown(clicked.X + 2, clicked.Y + 2, 0, false, false);
        view.PointerUp(clicked.X + 2, clicked.Y + 2, 0);
        recentBrowser = (LibraryBrowser)Field(view, "libraryBrowser")!;
        var other = Enumerable.Range(0, recentBrowser.Count).Select(recentBrowser.Get).Single(r => r?.Map.Title == "One")!.Map;
        Visit(other);
        Wait(() => !view.LibraryLoading);
        Check(canvas.Texts.First(t => t.X == 314 && names.Contains(t.Value)).Value == "One", "Background visit refresh moves a different card under the pointer");
        LibraryMap? opened = null;
        view.RequestLibraryOpen = (map, _) => opened = map;
        view.PointerDoubleClick(clicked.X + 2, clicked.Y + 2, false, false);
        Check(opened?.Path == visited.Path, "Double click opens the originally clicked map after reordering");

        void Visit(LibraryMap map) => typeof(EditorView).GetMethod("RecordLibraryVisit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [map, map.Directory]);
        view.StopFileMonitoring();
        Console.WriteLine("PASS independent four-tab and FA/osu! Collection caches across restart, query preferences and reordered Collections");

        void Switch(Category c) => typeof(EditorView).GetMethod("SwitchLibraryCategory", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [c.Mine, c.Favourite, c.Recent, c.Collection]);
        void CheckContents(Category c)
        {
            Check((string)Field(view, "libraryQuery")! == c.Query && (LibraryOrder)Field(view, "librarySort")! == c.Sort, "Page retains its own search and sort");
            var browser = (LibraryBrowser)Field(view, "libraryBrowser")!;
            var actual = Enumerable.Range(0, browser.Count).Select(browser.Get).Where(r => r?.Header is null).Select(r => r!.Map.Title).Order().ToArray();
            Check(actual.SequenceEqual(c.Titles.Order()), "Page and Collection memberships remain independent");
        }
        void Wait(Func<bool> ready)
        {
            var timer = Stopwatch.StartNew();
            do { canvas.Clear(); view.Render(canvas, 1440, 900); if (ready()) return; Thread.Sleep(10); }
            while (timer.Elapsed < TimeSpan.FromSeconds(20));
            throw new Exception("Library category background work timed out");
        }
    }
    private static void ConfigureCollections(EditorView view, OsuCollectionCatalog catalog)
    { Set(view, "osuCollections", catalog); Set(view, "nextOsuCollectionCheck", DateTime.MaxValue); }
    private static object? Field(EditorView view, string name) => typeof(EditorView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);
    private static object? Property(EditorView view, string name) => typeof(EditorView).GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);
    private static void Set(EditorView view, string name, object? value) => typeof(EditorView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, value);
    private static void Invoke(EditorView view, string name) => typeof(EditorView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
