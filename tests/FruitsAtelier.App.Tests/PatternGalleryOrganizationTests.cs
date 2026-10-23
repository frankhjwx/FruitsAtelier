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
        DragTag("Tag08", "Tag10", after: true);
        Check(emptyStore.LoadContents().Tags.Skip(8).Take(3).Select(t => t.Name).SequenceEqual(["Tag09", "Tag10", "Tag08"]),
            "Dragging a tag did not persist its insertion position");
        var unchangedOrder = emptyStore.LoadContents().Tags.ToArray();
        var cancelLabel = ui.Canvas.Texts.Single(t => t.Value == "Tag09" && ui.View.GalleryTagListBounds.Contains(t.X, t.Y));
        ui.View.PointerDown(cancelLabel.X + 4, cancelLabel.Y + 8, 0, false, false);
        ui.View.PointerMove(0, 0, false, false); ui.View.PointerUp(0, 0, 0); ui.Paint();
        Check(unchangedOrder.SequenceEqual(emptyStore.LoadContents().Tags) && !ui.View.WantsCapture,
            "Dropping a tag outside the list reordered tags or retained capture");
        var dragLabel = ui.Canvas.Texts.Single(t => t.Value == "Tag09" && ui.View.GalleryTagListBounds.Contains(t.X, t.Y));
        ui.View.PointerDown(dragLabel.X + 4, dragLabel.Y + 8, 0, false, false);
        var previousPage = ui.View.GalleryTagPreviousBounds;
        ui.View.PointerMove(previousPage.X + 10, previousPage.Y + 10, false, false); ui.Paint();
        var firstLabel = ui.Canvas.Texts.Single(t => t.Value == "Tag00" && ui.View.GalleryTagListBounds.Contains(t.X, t.Y));
        ui.View.PointerMove(firstLabel.X + 4, firstLabel.Y - 5, false, false); ui.Paint();
        ui.View.PointerUp(firstLabel.X + 4, firstLabel.Y - 5, 0); ui.Paint(); Settle();
        Check(emptyStore.LoadContents().Tags[0].Name == "Tag09" && !ui.View.WantsCapture, "Dragging across tag pages lost the insertion or pointer capture");
        ui.Key(27); ManageTags();
        Check(ui.Canvas.Texts.Where(t => ui.View.GalleryTagListBounds.Contains(t.X, t.Y)).First().Value == "Tag09",
            "Reopening Manage Tags reset the custom order");
        ui.Key(27); ui.View.ConfigurePatternGallery(directory); ui.Paint(); Settle();
        ui.Key('F', ctrl: true); ui.Type("ＳＮＯＷ mapper"); ui.Paint();
        Check(ui.View.PatternGalleryFilteredCount == 3, "Gallery search does not use Library normalization and all-word matching");
        ui.Key('A', ctrl: true); ui.View.TextInput('\u0001'); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value == "ＳＮＯＷ mapper"), "A shortcut control character deleted the search query");
        ui.Type("snow unknown"); ui.Paint();
        Check(ui.View.PatternGalleryFilteredCount == 0 && ui.View.Document.ContentEquals(before), "Search changed content or ignored unmatched words");
        ui.Key('A', ctrl: true); ui.Key(8); ui.Key(27);
        ui.ClickText(L.Get("gallery.tagFilters"));
        var allLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("gallery.allTags"));
        var noneLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("gallery.noTags"));
        var manageLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("gallery.manageTags"));
        var tagLabel = ui.Canvas.Texts.Last(t => t.Value == "Jump");
        Check(allLabel.Y == noneLabel.Y && noneLabel.Y == manageLabel.Y && allLabel.X < noneLabel.X && noneLabel.X < manageLabel.X
            && tagLabel.Y > manageLabel.Y + 32, "Tag filter actions are not one row above the tag separator");
        float hoverX = noneLabel.X + 4, hoverY = noneLabel.Y + 5;
        ui.Key(27); ui.View.PointerMove(hoverX, hoverY, false, false); ui.Paint();
        Check(ui.Canvas.Fills.Any(f => f.Color == 0x3D495Au && f.Bounds.Contains(hoverX, hoverY)),
            "Hover regression fixture does not overlap an interactive toolbar button");
        ui.ClickText(L.Get("gallery.tagFilters"));
        ui.View.PointerMove(hoverX, hoverY, false, false); ui.Paint();
        Check(ui.Canvas.Fills.Any(f => f.Color == 0x343E4Du && f.Bounds.Contains(hoverX, hoverY))
            && !ui.Canvas.Fills.Any(f => f.Color == 0x3D495Au && f.Bounds.Contains(hoverX, hoverY)),
            "Hovering the filter menu also highlights a covered toolbar button");
        ui.ClickText(L.Get("gallery.allTags"));
        Check(ui.View.PatternGalleryFilteredCount == 3, "All did not match patterns with any selected tag");
        ui.ClickText(L.Get("gallery.noTags"));
        Check(ui.View.PatternGalleryFilteredCount == 3, "None did not clear the tag selection"); ui.Key(27);
        ui.ClickText(L.Get("gallery.tagFilters")); ui.ClickObjectAction("Jump"); ui.Key(27);
        Check(ui.View.PatternGalleryFilteredCount == 2, "Tag filtering did not retain assigned patterns");
        ui.ClickText(L.Get("gallery.tagFilters")); ui.ClickObjectAction("Flow"); ui.Key(27);
        Check(ui.View.PatternGalleryFilteredCount == 3 && ui.View.PatternGalleryCardBounds(jump.Id).Width > 0
            && ui.View.PatternGalleryCardBounds(stream.Id).Width > 0 && ui.View.PatternGalleryCardBounds(both.Id).Width > 0,
            "Multiple tags must match any selected tag");
        ui.ClickText(L.Get("gallery.tagFilters")); ui.ClickText(L.Get("gallery.noTags")); ui.Key(27);
        var importLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("gallery.import"));
        ui.View.PointerMove(importLabel.X + 4, importLabel.Y + 5, false, false); ui.Paint();
        Check(ui.Canvas.Fills.Any(f => f.Color == 0x3D495Au && f.Bounds.Contains(importLabel.X + 4, importLabel.Y + 5)),
            "Toolbar hover does not return after closing the filter menu");
        int exports = 0;
        ui.View.RequestPatternExport = selected => { Check(!selected, "Export still requests selected-only output"); exports++; };
        ui.ClickText(L.Get("gallery.export"));
        Check(exports == 0 && ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.exportSelected")), "Export does not offer selection and folder scopes");
        ui.ClickText(L.Get("gallery.exportAll"));
        Check(exports == 1, "Whole-gallery export did not request the complete package");

        Open(jump);
        var card = ui.View.PatternGalleryCardBounds(jump.Id);
        ui.View.PointerDoubleClick(card.X + 20, card.Y + 20, false, false); ui.Paint();
        Check(!ui.View.PatternEditingActive && ui.View.PatternGalleryPreviewVisible, "Double click entered editing");
        Check(ui.Canvas.Texts.Any(t => t.Value == "Studio Fruit - Snowlight (Mapper) [Expert]")
            && !ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.exportSelected")), "Detail footer does not match requested layout");
        ui.ClickText(L.Get("gallery.editPattern"));
        ui.Key('A', ctrl: true);
        Check(ui.View.SelectedObjectIds.Count == ui.View.Document.Fruits.Count + ui.View.Document.Tracks.Count
            + ui.View.Document.ImportedSliders.Count + ui.View.Document.BananaShowers.Count,
            "Entering pattern editing retained gallery shortcut focus");
        Check(ui.View.PatternEditingActive, "Edit Pattern button did not enter editing");
        ui.View.DiscardPatternEditing(); ui.Paint();
        Open(jump);
        int builds = ui.View.PatternGalleryPreviewBuildCount;
        ui.ClickText(L.Get("gallery.editTags"));
        Check(ui.View.GalleryTagsVisible, "Edit Tags did not open the dedicated dialog");
        NewTag(); ui.Type("Custom"); ui.Key(13); Settle();
        Check(ui.View.GalleryTagSaveBounds.Width == 0, "Existing tag editing still has a Save Changes button");
        SetColour("#65A9FF"); Settle();
        Check(store.Load().Single(p => p.Id == jump.Id).Tags.Any(t => t.Name == "Custom" && t.Colour == 0x65A9FF), "New tag assignment did not persist");
        Check(ui.View.PatternGalleryPreviewBuildCount == builds, "Tag assignment rebuilt geometry");
        ui.Key(27); Check(!ui.View.GalleryTagsVisible && ui.View.PatternGalleryPreviewVisible, "Closing tags closed the detail preview");
        ui.Key(27); Open(stream);
        ui.ClickText(L.Get("gallery.editTags"));
        Check(new[] { "Custom", "Flow", "Jump", "Unused" }.All(name => ui.Canvas.Texts.Any(t => t.Value == name && ui.View.GalleryTagListBounds.Contains(t.X, t.Y))),
            "Edit Tags does not show every available tag");
        SelectTag("Custom"); Settle();
        Check(store.Load().Single(p => p.Id == stream.Id).Tags.Any(t => t.Name == "Custom" && t.Colour == 0x65A9FF), "Selecting a tag did not assign it");
        SelectTag("Custom"); Settle();
        Check(store.Load().Single(p => p.Id == stream.Id).Tags.All(t => t.Name != "Custom")
            && store.Load().Single(p => p.Id == jump.Id).Tags.Any(t => t.Name == "Custom"), "Deselecting a tag changed other patterns");
        SelectTag("Custom"); Settle();
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
        Settle();
        uint draggedColour = store.Load().Single(p => p.Id == jump.Id).Tags.Single(t => t.Name == "Custom").Colour;
        Check(draggedColour != 0x65A9FF && store.Load().Count(p => p.Tags.Any(t => t.Name == "Custom" && t.Colour == draggedColour)) == 2,
            "Colour picker changes did not automatically persist to all assignments");
        ui.Key(27); ManageTags(); SelectTag("Custom");
        Check(ui.Canvas.Texts.Any(t => t.Value == $"#{draggedColour:X6}"), "Auto-saved colour was lost when closing the dialog");
        SetColour("#123ABC"); ui.Key(27); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Custom" && t.Colour == 0x123ABC)) == 2,
            "Closing the dialog discarded a pending automatic colour save");
        ManageTags(); SelectTag("Custom");
        var nameField = ui.View.GalleryTagNameBounds;
        ui.Click(nameField.X + 10, nameField.Y + 12); ui.Key('A', ctrl: true); ui.Type("Flow"); ui.Paint(); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Custom")) == 2
            && ui.Canvas.Texts.Any(t => t.Value == L.Get("gallery.tagExists")), "Invalid duplicate auto-rename changed a valid tag");
        ui.Key('A', ctrl: true); ui.Type("Pra"); ui.Paint();
        Thread.Sleep(275); ui.Paint(); ui.Type("ctice"); ui.Paint(); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Practice")) == 2, "Tag rename did not persist across patterns");
        SetColour("#ABC123"); SelectTag("Flow"); SetColour("#223344"); ui.Key(27); Settle();
        Check(store.Load().Count(p => p.Tags.Any(t => t.Name == "Practice" && t.Colour == 0xABC123)) == 2
            && store.Load().Count(p => p.Tags.Any(t => t.Name == "Flow" && t.Colour == 0x223344)) == 2,
            "Switching between tags lost queued auto-saves");
        ManageTags(); SelectTag("Practice"); ui.ClickText(L.Get("gallery.deleteTag")); Settle();
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
            ui.ClickText(L.Get("gallery.tagFilters")); ui.ClickText(L.Get("gallery.manageTags"));
        }
        void SelectTag(string name)
        {
            var row = ui.Canvas.Texts.First(t => t.Value == name && ui.View.GalleryTagListBounds.Contains(t.X, t.Y));
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
        void DragTag(string from, string to, bool after)
        {
            var row = ui.Canvas.Texts.Single(t => t.Value == from && ui.View.GalleryTagListBounds.Contains(t.X, t.Y));
            var target = ui.Canvas.Texts.Single(t => t.Value == to && ui.View.GalleryTagListBounds.Contains(t.X, t.Y));
            float x = row.X + 4, y = target.Y + (after ? 22 : -5);
            ui.View.PointerDown(x, row.Y + 8, 0, false, false);
            ui.View.PointerMove(x, y, false, false); ui.Paint();
            ui.View.PointerUp(x, y, 0); ui.Paint(); Settle();
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
