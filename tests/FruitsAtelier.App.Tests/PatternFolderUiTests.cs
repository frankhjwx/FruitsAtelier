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
        var first = ui.View.PatternGalleryCardBounds(patterns[0].Id);
        Click(first, ctrl: true);
        var grid = ui.View.PatternGalleryGridBounds;
        ui.View.Wheel(grid.X + 10, grid.Y + 10, -12000, false); ui.Paint(); Settle();
        Click(ui.View.PatternGalleryCardBounds(patterns[^1].Id), shift: true);
        Check(ui.View.PatternGallerySelectionCount == 24 && !ui.View.PatternGalleryPreviewVisible,
            "Shift selection must include offscreen entries and avoid opening a popup");
        ui.Key('C', ctrl: true);
        Root(); Open(a); ui.Key('V', ctrl: true); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 24 && store.Load().Count == 48,
            "Copy/paste across folders lost patterns");
        ui.Key('A', ctrl: true); ui.Key('X', ctrl: true);
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 24, "Cut removed content before paste");
        Root(); Open(b); ui.Key('V', ctrl: true); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 0
            && store.LoadContents().Folders!.Single(f => f.Id == b).PatternIds.Count == 24 && store.Load().Count == 48,
            "Cut/paste did not move the original identities");
        ui.Key('V', ctrl: true); Settle(); Check(store.Load().Count == 48, "Repeated cut paste duplicated content");
        Root();
        Click(ui.View.PatternGalleryFolderBounds(a)); Click(ui.View.PatternGalleryFolderBounds(b), ctrl: true);
        ui.ClickText(L.Get("gallery.mergeFolders")); ui.ClickText(L.Get("gallery.mergeInto", "A")); Settle();
        Check(store.LoadContents().Folders!.All(f => f.Id != b) && store.LoadContents().Folders!.Single(f => f.Id == a).PatternIds.Count == 24,
            "Selected folder merge did not retain contents");
        ui.ClickText(L.Get("gallery.newFolder")); ui.Type("Created"); ui.Key(13); Settle();
        Check(store.LoadContents().Folders!.Any(f => f.Name == "Created"), "New-folder prompt did not save");
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
        Check(ui.View.PatternGalleryCardBounds(patterns[0].Id).Width == first.Width && ui.View.PatternGalleryPreviewBuildCount == builds,
            "Zoom rebuilt preview geometry or exceeded the original size");
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

        void Root()
        {
            if (ui.View.PatternGalleryFolderId is not { } id) return;
            string name = store.LoadContents().Folders!.Single(f => f.Id == id).Name;
            ui.ClickText("‹ " + name);
        }
        void Open(Guid id)
        {
            var bounds = ui.View.PatternGalleryFolderBounds(id); Click(bounds);
            ui.View.PointerDoubleClick(bounds.X + 10, bounds.Y + 10, false, false); ui.Paint(); Settle();
            Check(ui.View.PatternGalleryFolderId == id, "Double click did not enter folder");
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
