using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternGalleryInteractionTests
{
    internal static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" }) RunLanguage(language);
        L.SetLanguage("en");
    }

    private static void RunLanguage(string language)
    {
        L.SetLanguage(language);
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/gallery-interaction", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var map = new MapDocument { IsDemo = false, DurationMs = 10000 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 200 });
        var patterns = Enumerable.Range(0, 3).Select(i => PatternSnapshot.Capture(map, map.Fruits.Select(f => f.Id), $"Pattern {i}")).ToArray();
        PatternArchive.WriteFile(patterns, Path.Combine(directory, "gallery.fpt"));
        var store = new PatternGalleryStore(directory);
        Guid target = store.CreateFolder("Target");
        var ui = new Ui(); ui.LoadDocument(map); ui.View.ConfigurePatternGallery(directory);
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.gallery")); Settle();
        var before = ui.View.Document.DeepClone();
        Click(ui.View.PatternGalleryCardBounds(patterns[0].Id)); Settle();
        var popup = ui.View.PatternGalleryPopupBounds;
        ui.Click(popup.X + 20, popup.Y + 20);
        Check(ui.View.IsEditingText, "Preview title is not editable");
        ui.Key('A', ctrl: true); ui.Type("Renamed"); ui.Key(13); Settle();
        Check(store.Load().Single(p => p.Id == patterns[0].Id).Name == "Renamed", "Title rename did not persist");
        ui.Key(27);

        string exportPath = Path.Combine(directory, "selected.fpt");
        ui.View.RequestPatternExport = selectedOnly => { Check(selectedOnly, "Context export must use selection"); ui.View.BeginPatternExport(exportPath, selectedOnly); };
        Context(ui.View.PatternGalleryCardBounds(patterns[0].Id)); ui.ClickObjectAction(L.Get("gallery.export")); Settle();
        Check(PatternArchive.ReadContentsFile(exportPath).Patterns.Count == 1, "Single selection export included other patterns");
        Click(ui.View.PatternGalleryCardBounds(patterns[1].Id), ctrl: true);
        Context(ui.View.PatternGalleryCardBounds(patterns[0].Id)); ui.ClickObjectAction(L.Get("gallery.export")); Settle();
        Check(PatternArchive.ReadContentsFile(exportPath).Patterns.Select(p => p.Id).ToHashSet().SetEquals(patterns.Take(2).Select(p => p.Id)), "Multi-selection export lost selection");
        Drag(patterns[0].Id, target, cancel: true);
        Check(store.LoadContents().Folders!.Single(f => f.Id == target).PatternIds.Count == 0, "Escape moved patterns");
        Drag(patterns[0].Id, target);
        Check(store.LoadContents().Folders!.Single(f => f.Id == target).PatternIds.Count == 2 && store.Load().Count == 3,
            "Dragging selected card must move every selected pattern");
        Click(ui.View.PatternGalleryFolderBounds(target)); Settle();
        Click(ui.View.PatternGalleryCardBounds(patterns[0].Id), ctrl: true);
        Drag(patterns[0].Id, PatternFolder.DefaultId);
        Check(store.LoadContents().Folders!.Single(f => f.Id == target).PatternIds.SequenceEqual([patterns[1].Id]), "Single-card drag moved the wrong patterns");
        Click(ui.View.PatternGalleryFolderBounds(PatternFolder.DefaultId)); Settle();

        string importPath = Path.Combine(directory, "incoming.FPT"); PatternArchive.WriteFile([patterns[2]], importPath);
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        Check(ui.View.CanDropFile(importPath), "Editor rejects external FPT drop with hidden gallery");
        string legacyPath = Path.Combine(directory, "legacy.FAP"); File.Copy(importPath, legacyPath);
        Check(ui.View.CanDropFile(legacyPath), "Legacy pattern packages remain droppable");
        importPath = legacyPath;
        ui.View.DropLibraryFiles([importPath]); Settle();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.importNewFolders"))
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.importExistingFolders")), "Drop must show the two import choices");
        ui.ClickText(L.Get("gallery.importExistingFolders")); ui.ClickObjectAction("Target"); Settle();
        Check(store.LoadContents().Folders!.Single(f => f.Id == target).PatternIds.Count == 2, "Dropped archive did not import into selected folder");
        ui.View.DropLibraryFiles([importPath]); Settle(); ui.ClickText(L.Get("gallery.importNewFolders")); Settle();
        Check(store.LoadContents().Folders!.Any(f => f.Name == "legacy"), "New-folder drop import failed");
        ui.View.ShowDiscardConfirmation(_ => { }); ui.Paint();
        Check(!ui.View.CanDropFile(importPath), "External import bypassed a modal"); ui.Key(27);
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty && !ui.View.WantsCapture, "Gallery actions changed map content or retained capture");

        void Drag(Guid pattern, Guid destination, bool cancel = false)
        {
            var card = ui.View.PatternGalleryCardBounds(pattern); var folder = ui.View.PatternGalleryFolderBounds(destination);
            ui.View.PointerDown(card.X + 15, card.Y + 15, 0, false, false);
            ui.View.PointerMove(folder.X + 15, folder.Y + 15, false, false); ui.Paint();
            Check(ui.View.PatternGallerySelectionCount > 0 && ui.View.WantsCapture, "Drag lost selection or capture");
            if (cancel) ui.Key(27);
            ui.View.PointerUp(folder.X + 15, folder.Y + 15, 0); Settle();
        }
        void Click(Rect bounds, bool ctrl = false) { ui.View.PointerDown(bounds.X + 10, bounds.Y + 10, 0, false, ctrl); ui.View.PointerUp(bounds.X + 10, bounds.Y + 10, 0); ui.Paint(); }
        void Context(Rect bounds) { ui.View.PointerDown(bounds.X + 10, bounds.Y + 10, 2, false, false); ui.View.PointerUp(bounds.X + 10, bounds.Y + 10, 2); ui.Paint(); }
        void Settle()
        {
            var timer = Stopwatch.StartNew();
            do { ui.Paint(); if (timer.Elapsed.TotalSeconds > 20) throw new Exception("Gallery interaction timed out"); Thread.Sleep(1); }
            while (ui.View.PatternGalleryNeedsRedraw);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
