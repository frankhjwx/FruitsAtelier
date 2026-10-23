using System.Diagnostics;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternFolderUiTests
{
    internal static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" }) RunLanguage(language);
        L.SetLanguage("en");
    }

    private static void RunLanguage(string language)
    {
        L.SetLanguage(language);
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/pattern-folders-ui", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var source = new MapDocument { IsDemo = false, DurationMs = 10000 };
        source.Fruits.Add(new() { TimeMs = 1000, X = 200 });
        var patterns = Enumerable.Range(0, 24).Select(i => PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), $"Pattern {i:00}")).ToArray();
        PatternArchive.WriteFile(patterns, Path.Combine(directory, "gallery.fap"));
        var store = new PatternGalleryStore(directory);
        Guid a = store.CreateFolder("A"), b = store.CreateFolder("B");
        var ui = new Ui(); ui.LoadDocument(source); ui.View.ConfigurePatternGallery(directory);
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.gallery")); Settle();
        var before = ui.View.Document.DeepClone();
        Check(ui.View.PatternGalleryCategoryBounds.Right < ui.View.PatternGalleryGridBounds.X
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.category")), "Categories are not beside the grid");
        var first = ui.View.PatternGalleryCardBounds(patterns[0].Id);
        Context(ui.View.PatternGalleryFolderBounds(a));
        Check(HasText("gallery.renameFolder") && HasText("gallery.deleteFolder")
            && !HasText("gallery.newFolder") && !HasText("gallery.mergeFolders") && !HasText("editor.command.paste"),
            "Single-folder menu includes irrelevant actions or incorrect labels");
        ui.Key(27);
        Context(ui.View.PatternGalleryFolderBounds(PatternFolder.DefaultId));
        Check(!HasText("gallery.deleteFolder"), "Default must not offer deletion");
        ui.Key(27);
        Click(first, ctrl: true);
        var grid = ui.View.PatternGalleryGridBounds;
        ui.View.Wheel(grid.X + 10, grid.Y + 10, -12000, false); ui.Paint(); Settle();
        Click(ui.View.PatternGalleryCardBounds(patterns[^1].Id), shift: true);
        Check(ui.View.PatternGallerySelectionCount == 24 && !ui.View.PatternGalleryPreviewVisible,
            "Shift selection must include offscreen entries and avoid opening a popup");
        ui.Key('C', ctrl: true);
        Context(ui.View.PatternGalleryFolderBounds(a));
        Check(HasText("editor.command.paste"), "Copy must expose Paste on folders"); ui.Key(27);
        Open(a); ui.Key('V', ctrl: true); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 24 && store.Load().Count == 48,
            "Copy/paste across folders lost patterns");
        ui.Key('A', ctrl: true); ui.Key('X', ctrl: true);
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 24, "Cut removed content before paste");
        Open(b); ui.Key('V', ctrl: true); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 0
            && store.LoadContents().Folders!.Single(f => f.Id == b).PatternIds.Count == 24 && store.Load().Count == 48,
            "Cut/paste did not move the original identities");
        ui.Key('V', ctrl: true); Settle(); Check(store.Load().Count == 48, "Repeated cut paste duplicated content");
        Click(ui.View.PatternGalleryFolderBounds(a)); Click(ui.View.PatternGalleryFolderBounds(b), ctrl: true);
        Context(ui.View.PatternGalleryFolderBounds(b));
        ui.ClickText(L.Get("gallery.mergeFolders")); ui.ClickText(L.Get("gallery.mergeInto", "A")); Settle();
        Check(store.LoadContents().Folders!.All(f => f.Id != b) && store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 24,
            "Selected folder merge did not retain contents");
        Context(ui.View.PatternGalleryFolderBounds(a));
        Check(!HasText("editor.command.paste") && !HasText("gallery.mergeFolders"), "Completed move and merge must hide unavailable actions");
        ui.ClickText(L.Get("gallery.renameFolder")); ui.Type("Renamed"); ui.Key(13); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).Name == "Renamed", "Folder rename did not persist");
        Open(a); Click(ui.View.PatternGalleryCardBounds(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds[0]), ctrl: true);
        ui.Key('C', ctrl: true);
        Context(ui.View.PatternGalleryFolderBounds(a)); ui.ClickText(L.Get("gallery.deleteFolder")); Settle();
        Check(store.LoadContents().Folders!.All(f => f.Id != a) && store.Load().Count == 24
            && ui.View.PatternGalleryFolderId == PatternFolder.DefaultId, "Deleting the active folder did not remove contents and return to Default");
        Context(ui.View.PatternGalleryFolderBounds(PatternFolder.DefaultId));
        Check(!HasText("editor.command.paste"), "Deleted clipboard patterns must not expose Paste"); ui.Key(27);
        Context(ui.View.PatternGalleryCategoryBounds);
        ui.ClickText(L.Get("gallery.newFolder")); ui.Type("Created"); ui.Key(13); Settle();
        Check(store.LoadContents().Folders!.Any(f => f.Name == "Created"), "New-folder prompt did not save");
        Guid created = store.LoadContents().Folders!.Single(f => f.Name == "Created").Id;
        Context(ui.View.PatternGalleryFolderBounds(created)); ui.ClickText(L.Get("gallery.deleteFolder")); Settle();
        Check(store.LoadContents().Folders!.All(f => f.Id != created), "Empty folder deletion did not persist");
        Open(PatternFolder.DefaultId);
        grid = ui.View.PatternGalleryGridBounds;
        ui.View.Wheel(grid.X + 10, grid.Y + 10, -12000, true); ui.Paint(); Settle();
        var small = ui.View.PatternGalleryCardBounds(patterns[0].Id);
        Check(Math.Abs(small.Width - first.Width / 2) < .01 && Math.Abs(small.Height - first.Height / 2) < .01,
            "Ctrl-wheel did not stop at half-size cards");
        int builds = ui.View.PatternGalleryPreviewBuildCount;
        ui.View.Wheel(grid.X + 10, grid.Y + 10, -12000, true); ui.Paint();
        Check(ui.View.PatternGalleryCardBounds(patterns[0].Id) == small, "Zoom exceeded the minimum");
        ui.View.Wheel(grid.X + 10, grid.Y + 10, 12000, true); ui.Paint(); Settle();
        var large = ui.View.PatternGalleryCardBounds(patterns[0].Id);
        Check(Math.Abs(large.Width - Math.Max(first.Width, (grid.Width - 14) / 2)) < .01
            && ui.View.PatternGalleryPreviewBuildCount == builds, "Maximum zoom did not fit two cards or rebuilt preview geometry");
        ui.Resize(2200, 1100); ResizeDock(850); Settle(); grid = ui.View.PatternGalleryGridBounds;
        ui.View.Wheel(grid.X + 10, grid.Y + 10, 12000, true); ui.Paint(); Settle();
        large = ui.View.PatternGalleryCardBounds(patterns[0].Id);
        var second = ui.View.PatternGalleryCardBounds(patterns[1].Id);
        Check(large.Width > first.Width && large.Y == second.Y && Math.Abs(second.Right - (grid.Right - 6)) < .01,
            $"Wide-panel maximum must fill the row with two enlarged cards: first={first}, large={large}, second={second}, grid={grid}");
        ResizeDock(450); ui.Resize(1440, 900); Settle(); grid = ui.View.PatternGalleryGridBounds;
        Check(ui.View.PatternGalleryCardBounds(patterns[0].Id).Width <= Math.Max(first.Width, (grid.Width - 14) / 2) + .01,
            "Resize must clamp enlarged cards to the new maximum");
        var watch = Stopwatch.StartNew(); long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            ui.View.Wheel(grid.X + 10, grid.Y + 10, i % 2 == 0 ? -120 : 120, true);
            ui.View.PointerMove(grid.X + 10, grid.Y + 10, false, false);
        }
        Console.WriteLine($"Gallery zoom/pointer ({language}): {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetAllocatedBytesForCurrentThread() - allocated} bytes / 100 interactions");
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Gallery organization changed map content");
        string package = Path.Combine(directory, "Legacy.fap"); PatternArchive.WriteFile([patterns[0]], package);
        ui.View.BeginPatternImport(package); ui.Paint(); ui.ClickText(L.Get("gallery.importInto", "Default")); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == PatternFolder.DefaultId).PatternIds.Count == 25, "Explicit import target was ignored");
        ui.View.BeginPatternImport(package); ui.Paint(); ui.ClickText(L.Get("gallery.importNewFolders")); Settle();
        Check(store.LoadContents().Folders!.Any(f => f.Name == "Legacy"), "New-folder import was ignored");
        ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle();
        Check(ui.View.PatternGalleryFolderId == PatternFolder.DefaultId && ui.View.PatternGalleryFilteredCount == 25, "Reload lost folder contents");
        for (int i = 0; i < 16; i++) store.CreateFolder($"Category {i:00}");
        ui.Resize(1440, 900);
        ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle();
        grid = ui.View.PatternGalleryGridBounds;
        ui.View.Wheel(grid.X + 10, grid.Y + 10, -120, false); ui.Paint(); Settle();
        float gridScroll = ui.View.PatternGalleryGridScroll;
        var category = ui.View.PatternGalleryCategoryBounds;
        ui.View.Wheel(category.X + 10, category.Y + 40, -12000, true); ui.Paint();
        Check(ui.View.PatternGalleryGridScroll == gridScroll
            && ui.View.PatternGalleryFolderBounds(PatternFolder.DefaultId).Width == 0,
            "Category scrolling changed the grid or failed to reach offscreen categories");
        ui.View.Wheel(category.X + 10, category.Y + 40, 12000, false); ui.Paint();
        Check(ui.View.PatternGalleryFolderBounds(PatternFolder.DefaultId).Width > 0,
            "Category scrolling did not return to Default");

        bool HasText(string key) => ui.Canvas.Texts.Any(t => t.Value == L.Get(key));

        void ResizeDock(float dockWidth)
        {
            var handle = ui.View.DockResizeBounds(false);
            ui.View.PointerDown(handle.X + 4, handle.Y + 60, 0, false, false);
            ui.View.PointerMove(ui.Width - dockWidth, handle.Y + 60, false, false);
            ui.View.PointerUp(ui.Width - dockWidth, handle.Y + 60, 0); ui.Paint();
        }

        void Context(Rect bounds)
        {
            ui.View.PointerDown(bounds.X + 10, bounds.Y + 10, 2, false, false);
            ui.View.PointerUp(bounds.X + 10, bounds.Y + 10, 2); ui.Paint();
        }
        void Open(Guid id)
        {
            var bounds = ui.View.PatternGalleryFolderBounds(id); Click(bounds);
            Settle();
            Check(ui.View.PatternGalleryFolderId == id, "Category click did not switch contents");
        }
        void Click(Rect bounds, bool ctrl = false, bool shift = false)
        {
            Check(bounds.Width > 0, "Missing card bounds");
            ui.View.PointerDown(bounds.X + 10, bounds.Y + 10, 0, shift, ctrl);
            ui.View.PointerUp(bounds.X + 10, bounds.Y + 10, 0); ui.Paint();
        }
        void Settle()
        {
            var timer = Stopwatch.StartNew();
            do { ui.Paint(); if (timer.Elapsed.TotalSeconds > 20) throw new Exception("Gallery did not settle: " + ui.View.StatusMessage); Thread.Sleep(1); }
            while (ui.View.PatternGalleryNeedsRedraw);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
