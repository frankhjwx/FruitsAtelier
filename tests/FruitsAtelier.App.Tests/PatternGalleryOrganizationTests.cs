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

        Check(ui.View.PatternGalleryTagsBounds.Y == ui.View.PatternGallerySearchBounds.Y
            && ui.View.PatternGalleryTagsBounds.X > ui.View.PatternGallerySearchBounds.Right
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.manageTags")), "Gallery toolbar does not place Tags beside Search with Manage Tags in its menu");
        ManageTags();
        NewTag(); ui.Type("Unused"); SetColour("#AB12CD"); SaveTag(); Settle();
        Check(store.LoadContents().Tags.Any(t => t.Name == "Unused" && t.Colour == 0xAB12CD)
            && store.Load().All(p => p.Tags.All(t => t.Name != "Unused")), "Independent tag creation assigned a pattern or lost its colour");
        ui.Key(27);
        ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle();
        ManageTags(); SelectTag("Unused");
        Check(ui.Canvas.Texts.Any(t => t.Value == "#AB12CD"), "Unassigned tags did not survive gallery reload");
        ui.Key(27);
        string emptyDirectory = Path.Combine(directory, "Empty");
        var emptyStore = new PatternGalleryStore(emptyDirectory);
        ui.View.ConfigurePatternGallery(emptyDirectory); ui.Paint(); Settle(); ManageTags();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.emptyTags"))
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.noMatches")), "Empty tag list shows a pattern search diagnostic");
        Check(ui.View.GalleryTagPreviousBounds.Width == 0 && ui.View.GalleryTagDeleteBounds.Width == 0,
            "Empty tag list shows paging or destructive actions");
        SaveTag(); Settle(); Check(emptyStore.LoadContents().Tags.Count == 0, "Blank draft was created");
        ui.Key(27);
        for (int i = 0; i < 18; i++) emptyStore.AddTag(new($"Tag{i:00}", 0x58D8C1));
        ui.View.ConfigurePatternGallery(emptyDirectory); ui.Paint(); Settle(); ManageTags();
        Check(ui.View.GalleryTagNextBounds.Width > 0, "Multi-page tags have no next-page control");
        var next = ui.View.GalleryTagNextBounds;
        ui.Click(next.X + 10, next.Y + 10);
        Check(ui.Canvas.Texts.Any(t => t.Value == "Tag08") && !ui.Canvas.Texts.Any(t => t.Value == "Tag01"), "Paging does not replace the visible tag list");
        ui.Key(27); ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle();
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
        NewTag(); ui.Type("Custom"); ui.Key(13); Settle();
        SelectTag("Custom"); SetColour("#65A9FF"); SaveTag(); Settle();
        Check(store.Load().Single(p => p.Id == jump.Id).Tags.Any(t => t.Name == "Custom" && t.Colour == 0x65A9FF), "New tag assignment did not persist");
        Check(ui.View.PatternGalleryPreviewBuildCount == builds, "Tag assignment rebuilt geometry");
        ui.Key(27); Check(!ui.View.GalleryTagsVisible && ui.View.PatternGalleryPreviewVisible, "Closing tags closed the detail preview");
        ui.Key(27); Open(stream);
        ui.ClickText(L.Get("gallery.editTags"));
        Check(!ui.Canvas.Texts.Any(t => t.Value == "Custom" || t.Value == "✓"), "Edit Tags includes unrelated tags or assignment checkboxes");
        NewTag(); ui.Type("Custom"); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.addTag")), "Existing tag is not offered for direct addition");
        SaveTag(); Settle();
        Check(store.Load().Single(p => p.Id == stream.Id).Tags.Any(t => t.Name == "Custom" && t.Colour == 0x65A9FF), "Adding an existing tag lost its saved colour");
        ui.ClickText(L.Get("gallery.removeTag")); Settle();
        Check(store.Load().Single(p => p.Id == stream.Id).Tags.All(t => t.Name != "Custom")
            && store.Load().Single(p => p.Id == jump.Id).Tags.Any(t => t.Name == "Custom"), "Removing a pattern tag changed other patterns");
        NewTag(); ui.Type("Custom"); ui.Key(13); Settle();
        ui.Key(27); ui.Key(27);
        ManageTags();
        Check(ui.View.GalleryTagsVisible, "Manage Tags did not open the shared dialog");
        Check(ui.View.GalleryTagPreviousBounds.Width == 0 && ui.View.GalleryTagNextBounds.Width == 0,
            "Single-page tag list shows unnecessary paging controls");
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
        ui.Key(27); ManageTags(); SelectTag("Custom");
        Check(ui.Canvas.Texts.Any(t => t.Value == "#65A9FF"), "Closing the dialog persisted unsaved colour changes");
        SetColour("#123ABC"); SaveTag(); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Custom" && t.Colour == 0x123ABC)) == 2,
            "Tag colour editing did not update all assignments");
        var nameField = ui.View.GalleryTagNameBounds;
        ui.Click(nameField.X + 10, nameField.Y + 12); ui.Key('A', ctrl: true); ui.Type("Practice"); SaveTag(); Settle();
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
        void NewTag()
        {
            var bounds = ui.View.GalleryNewTagBounds;
            Check(ui.View.GalleryTagListBounds.Contains(bounds.X, bounds.Y)
                && bounds.Bottom <= ui.View.GalleryTagListBounds.Bottom, "New Tag is outside the tag list");
            ui.Click(bounds.X + 10, bounds.Y + 10);
        }
        void SaveTag()
        {
            ui.Paint();
            var bounds = ui.View.GalleryTagSaveBounds;
            ui.Click(bounds.X + 10, bounds.Y + 10);
        }
        void ManageTags()
        {
            ui.ClickText(L.Get("gallery.filterTags", 0)); ui.ClickText(L.Get("gallery.manageTags"));
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
            ui.Key('A', ctrl: true); ui.Type(hex); ui.Paint();
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
