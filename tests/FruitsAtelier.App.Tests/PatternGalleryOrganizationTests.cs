using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

internal static class PatternGalleryOrganizationTests
{
    internal static void Run()
    {
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/pattern-gallery-organization", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var source = new MapDocument { IsDemo = false, Name = "Source", DurationMs = 10000 };
        source.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 300 }]);
        source.OriginalSections.Add(new() { Name = "Metadata", Lines = { "Artist:Studio Fruit", "Title:Snowlight", "Creator:Mapper", "Version:Expert" } });
        var jump = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "Wide jump").WithTags([new("Jump", 0xFFB45C)]);
        var stream = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "Stream").WithTags([new("Flow", 0x58D8C1)]);
        var both = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "Both").WithTags([new("Flow", 0x58D8C1), new("Jump", 0xFFB45C)]);
        PatternArchive.WriteFile([jump, stream, both], Path.Combine(directory, "gallery.fap"));
        var store = new PatternGalleryStore(directory);
        var ui = new Ui();
        var map = new MapDocument { IsDemo = false, DurationMs = 10000 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 300 }]);
        ui.LoadDocument(map); ui.View.ConfigurePatternGallery(directory);
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.gallery")); Settle();
        var before = ui.View.Document.DeepClone();
        int prompts = 0;
        var namePrompt = ui.View.RequestPatternName;
        ui.View.RequestPatternName = (_, _) => prompts++;
        ui.ClickText(L.Get("gallery.save"));
        Check(prompts == 0, "Save selection is enabled without selected objects");
        ui.View.RequestPatternName = namePrompt;

        ui.Key('F', ctrl: true); ui.Type("ＳＮＯＷ mapper"); ui.Paint();
        Check(ui.View.PatternGalleryFilteredCount == 3, "Gallery search does not use Library normalization and all-word matching");
        ui.Key('A', ctrl: true); ui.View.TextInput('\u0001'); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "ＳＮＯＷ mapper"), "A shortcut control character deleted the search query");
        ui.Type("snow unknown"); ui.Paint();
        Check(ui.View.PatternGalleryFilteredCount == 0 && ui.View.Document.ContentEquals(before), "Search changed content or ignored unmatched words");
        ui.Key('A', ctrl: true); ui.Key(8); ui.Key(27);
        ui.ClickText(L.Get("gallery.filterTags", 0)); ui.ClickText("Jump"); ui.Key(27);
        Check(ui.View.PatternGalleryFilteredCount == 2, "Tag filtering did not retain assigned patterns");
        ui.ClickText(L.Get("gallery.filterTags", 1)); ui.ClickText("Flow"); ui.Key(27);
        Check(ui.View.PatternGalleryFilteredCount == 1 && ui.View.PatternGalleryCardBounds(both.Id).Width > 0,
            "Multiple tags must match all selected tags");
        ui.ClickText(L.Get("gallery.filterTags", 2)); ui.ClickText(L.Get("gallery.allTags"));
        int exports = 0;
        ui.View.RequestPatternExport = selected => { Check(!selected, "Export still requests selected-only output"); exports++; };
        ui.ClickText(L.Get("gallery.export"));
        Check(exports == 1 && !ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.exportSelected")), "Export did not directly export the gallery");

        Open(jump);
        var card = ui.View.PatternGalleryCardBounds(jump.Id);
        ui.View.PointerDoubleClick(card.X + 20, card.Y + 20, false, false); ui.Paint();
        Check(!ui.View.PatternEditingActive && ui.View.PatternGalleryPreviewVisible, "Double click entered editing");
        Check(ui.Canvas.Texts.Any(t => t.Value == "Studio Fruit - Snowlight (Mapper) [Expert]")
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.exportSelected")), "Detail footer does not match requested layout");
        ui.ClickText(L.Get("gallery.editPattern"));
        Check(ui.View.PatternEditingActive, "Edit Pattern button did not enter editing");
        ui.View.DiscardPatternEditing(); ui.Paint();
        Open(jump);
        int builds = ui.View.PatternGalleryPreviewBuildCount;
        ui.ClickText(L.Get("gallery.editTags"));
        Check(ui.View.GalleryTagsVisible, "Edit Tags did not open the dedicated dialog");
        ui.ClickText(L.Get("gallery.newTag")); ui.Type("Custom"); ui.Key(13); Settle();
        SelectTag("Custom"); SetColour("#65A9FF"); ui.ClickText(L.Get("mac.save")); Settle();
        Check(store.Load().Single(p => p.Id == jump.Id).Tags.Any(t => t.Name == "Custom" && t.Colour == 0x65A9FF), "New tag assignment did not persist");
        Check(ui.View.PatternGalleryPreviewBuildCount == builds, "Tag assignment rebuilt geometry");
        ui.Key(27); Check(!ui.View.GalleryTagsVisible && ui.View.PatternGalleryPreviewVisible, "Closing tags closed the detail preview");
        ui.Key(27); Open(stream);
        ui.ClickText(L.Get("gallery.editTags"));
        var tagText = ui.Canvas.Texts.First(t => t.Value == "Custom");
        ui.Click(tagText.X - 20, tagText.Y + 4); Settle();
        ui.Key(27); ui.Key(27);
        ui.ClickText(L.Get("gallery.manageTags"));
        Check(ui.View.GalleryTagsVisible, "Manage Tags did not open the shared dialog");
        SelectTag("Custom");
        var hue = ui.View.GalleryTagHueBounds;
        var palette = ui.View.GalleryTagPaletteBounds;
        ui.View.PointerDown(hue.X + hue.Width / 3, hue.Y + 10, 0, false, false);
        ui.View.PointerMove(hue.X + hue.Width / 2, hue.Y + 10, false, false);
        ui.View.PointerUp(hue.X + hue.Width / 2, hue.Y + 10, 0); ui.Paint();
        ui.View.PointerDown(palette.Right - 1, palette.Y + 1, 0, false, false);
        Check(ui.View.WantsCapture, "Colour picker did not capture dragging");
        ui.View.PointerMove(palette.X + palette.Width / 2, palette.Y + palette.Height / 2, false, false);
        ui.View.PointerUp(palette.X + palette.Width / 2, palette.Y + palette.Height / 2, 0); ui.Paint();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Custom" && t.Colour == 0x65A9FF)) == 2,
            "Colour dragging wrote before Save");
        ui.Key(27); ui.ClickText(L.Get("gallery.manageTags")); SelectTag("Custom");
        Check(ui.Canvas.Texts.Any(t => t.Value == "#65A9FF"), "Closing the dialog persisted unsaved colour changes");
        SetColour("#123ABC"); ui.ClickText(L.Get("mac.save")); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Custom" && t.Colour == 0x123ABC)) == 2,
            "Tag colour editing did not update all assignments");
        ui.ClickText(L.Get("gallery.renameTag")); ui.Key('A', ctrl: true); ui.Type("Practice"); ui.Key(13); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Practice")) == 2, "Tag rename did not persist across patterns");
        ui.ClickText(L.Get("gallery.deleteTag")); Settle();
        Check(store.Load().All(p => p.Tags.All(t => t.Name != "Practice")), "Tag removal left assignments");
        ui.Key(27);
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Gallery organization changed map content");
        ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle();
        Check(ui.View.PatternGalleryCount == 3 && store.Load().Single(p => p.Id == jump.Id).Tags.Single().Name == "Jump",
            "Tag changes did not survive gallery reload");

        ui.Key('1'); ui.ClickMap(1000, 100); ui.MoveMap(1000, 100);
        Capture("Single object", 1);
        ui.Key('A', ctrl: true); ui.MoveMap(1000, 100);
        Capture("Multiple objects", 2);
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Context capture changed map content");
        ui.ClickText(L.Get("dock.layout")); ui.ClickText("✓ " + L.Get("dock.gallery"));
        ui.MoveMap(1000, 100); ui.Key('F', shift: true);
        ui.ClickObjectAction(L.Get("gallery.saveToGallery")); ui.Type("Hidden panel capture"); ui.Key(13); Settle();
        Check(store.Load().Single(p => p.Name == "Hidden panel capture").ObjectCount == 2,
            "Object capture requires the gallery panel to be visible");

        void Open(PatternSnapshot pattern)
        {
            var bounds = ui.View.PatternGalleryCardBounds(pattern.Id);
            ui.Click(bounds.X + 20, bounds.Y + 20); Settle();
        }
        void SelectTag(string name)
        {
            var row = ui.Canvas.Texts.First(t => t.Value == name);
            ui.Click(row.X + 5, row.Y + 5);
        }
        void SetColour(string hex)
        {
            var swatch = ui.Canvas.Texts.Last(t => t.Value.StartsWith("#", StringComparison.Ordinal) && t.Value.Length == 7);
            ui.Click(swatch.X + 20, swatch.Y + 5);
            ui.Key('A', ctrl: true); ui.Type(hex); ui.Key(13); ui.Paint();
        }
        void Capture(string name, int count)
        {
            ui.Key('F', shift: true);
            Check(!new[] { "conversion", "droplets", "structure" }.Any(k => ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.section." + k))),
                "Object menu contains inspector category labels");
            ui.ClickObjectAction(L.Get("gallery.saveToGallery")); ui.Type(name); ui.Key(13); Settle();
            Check(store.Load().Single(p => p.Name == name).ObjectCount == count, "Object menu captured the wrong selection");
        }
        void Settle()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                ui.Paint();
                if (!ui.View.PatternGalleryNeedsRedraw) return;
                if (watch.Elapsed.TotalSeconds > 20) throw new Exception("Gallery organization did not settle: " + ui.View.StatusMessage);
                Thread.Sleep(5);
            } while (true);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
