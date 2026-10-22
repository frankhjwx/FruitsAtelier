using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using FruitsAtelier.Localization;

internal static class PatternGalleryUiTests
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fa-gallery-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Strings.SetLanguage("en");
            var source = new MapDocument { IsDemo = false, DurationMs = 20000, BeatLengthMs = 500 };
            source.Fruits.Add(new() { TimeMs = 1000, X = 100 });
            source.Fruits.Add(new() { TimeMs = 10000, X = 420 });
            var snapshot = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "00 Long gallery fixture");
            var store = new PatternGalleryStore(directory);
            store.Add(snapshot);
            for (int i = 1; i < 16; i++) store.Add(PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), $"Fixture {i:00}"));
            var ui = new Ui();
            ui.LoadDocument(new MapDocument { IsDemo = false, DurationMs = 30000, BeatLengthMs = 400 });
            ui.View.ConfigurePatternGallery(directory);
            // Re-adding selects the panel even when the default tab strip is scrolled.
            ui.ClickText(Strings.Get("dock.layout")); ui.ClickText("✓ " + Strings.Get("dock.gallery"));
            ui.ClickText(Strings.Get("dock.layout")); ui.ClickText(Strings.Get("dock.gallery"));
            Wait(() => ui.View.PatternGalleryCount == 16 && !ui.View.PatternGalleryNeedsRedraw, ui);
            // External reload and pointer input each request one frame; subsequent frames must be scheduled by the view.
            ui.View.ConfigurePatternGallery(directory); ui.Paint();
            PaintScheduledFrames(ui);
            Check(ui.View.PatternGalleryCount == 16 && ui.View.PatternGalleryPreviewCacheCount > 0,
                "Gallery load stopped scheduling before visible previews were prepared");
            var initialGrid = ui.View.PatternGalleryGridBounds;
            ui.View.Wheel(initialGrid.X + 10, initialGrid.Y + 100, -12000, false, false, false); ui.Paint();
            PaintScheduledFrames(ui);
            ui.View.Wheel(initialGrid.X + 10, initialGrid.Y + 100, 12000, false, false, false); ui.Paint();
            PaintScheduledFrames(ui);
            int builds = ui.View.PatternGalleryPreviewBuildCount;
            var before = ui.View.Document.DeepClone();
            ui.Paint(); ui.Paint();
            Check(ui.View.PatternGalleryPreviewBuildCount == builds, "Idle paint rebuilt gallery geometry");
            var title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            ui.Click(title.X + 10, title.Y - 25);
            var preview = ui.View.PatternGalleryPreviewBounds;
            Check(preview.Width > 0 && preview.Height > 0, "Selecting did not open floating preview");
            Check(!ui.Canvas.Operations.Any(o => o.Clip == preview && o.Segment is not null),
                "Fruit preview contains an auxiliary connector or snap grid");
            Check(ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot is not null).All(o => o.Dot!.Value.Color == 0xFFFFFF),
                "Gallery preview uses combo colouring");
            float scrollBeforeModal = ui.View.PatternGalleryGridScroll;
            int confirmation = -1;
            ui.View.ShowDiscardConfirmation(answer => confirmation = answer);
            var grid = ui.View.PatternGalleryGridBounds;
            ui.View.Wheel(grid.X + 10, grid.Y + 100, -120, false, false, false);
            Check(ui.View.PatternGalleryGridScroll == scrollBeforeModal, "Modal wheel input leaked into gallery");
            ui.Key(27);
            Check(confirmation == 2 && !ui.View.DiscardConfirmationVisible, "Gallery consumed the modal cancel key");
            title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            ui.Click(title.X + 10, title.Y - 25);
            int navigations = 0;
            ui.View.RequestPatternSource = p => { Check(p.Id == snapshot.Id, "Wrong source pattern"); navigations++; };
            ui.Paint(); ui.ClickText(Strings.Get("gallery.jumpSource"));
            Check(navigations == 1, "Source button did not route selected snapshot");
            ui.Key(27);
            Check(ui.View.PatternGalleryPreviewBounds.Width == 0, "Escape did not close the preview");
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Browsing changed map content");
            title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            float x = title.X + 10, y = title.Y - 25;
            ui.View.PointerDown(x, y, 0, false, false);
            var target = ui.ScreenAt(2000, 256);
            ui.View.PointerMove(target.X, target.Y, false, false); ui.Paint();
            ui.View.PointerUp(target.X, target.Y, 0);
            Wait(() => ui.View.Document.Fruits.Count == 2, ui);
            Check(ui.View.Document.Fruits.All(f => f.X >= 0 && f.X <= 512), "Inserted pattern escaped playfield");
            Check(Math.Abs(ui.View.Document.Fruits[1].TimeMs - ui.View.Document.Fruits[0].TimeMs - 7200) < .001,
                "Cross-BPM gallery drop did not preserve beat spacing");
            Check(ui.View.Document.Fruits.All(f => source.Fruits.All(s => s.Id != f.Id)), "Drop reused snapshot identities");
            ui.Key(90, ctrl: true);
            Check(ui.View.Document.Fruits.Count == 0, "Drop was not one undo step");
            ui.View.BeginPatternExport(Path.Combine(directory, "shared.fap"), false);
            Wait(() => File.Exists(Path.Combine(directory, "shared.fap")) && !ui.View.PatternGalleryNeedsRedraw, ui);
            Check(ui.View.BeginPatternEditing(snapshot), "Pattern edit session did not open");
            ui.View.OpenSongSetup(); ui.Paint();
            ui.Key(83, ctrl: true);
            Check(ui.View.PatternEditingActive && !ui.View.PatternEditingBusy && ui.View.SongSetupVisible,
                "Pattern save shortcut bypassed the Song Setup modal");
            ui.Key(27); ui.View.DiscardPatternEditing();
            DenseThumbnailScheduling(ui, Path.Combine(directory, "dense"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void DenseThumbnailScheduling(Ui ui, string directory)
    {
        Directory.CreateDirectory(directory);
        var source = new MapDocument { IsDemo = false, DurationMs = 501000, BeatLengthMs = 500 };
        var track = new CurveTrack { SpanCount = 1000 };
        track.Nodes.Add(new Anchor { TimeMs = 1000, X = 100 });
        track.Nodes.Add(new Anchor { TimeMs = 1500, X = 420 });
        source.Tracks.Add(track);
        var patterns = Enumerable.Range(0, 80).Select(i => PatternSnapshot.Capture(source, [track.Id], $"Dense {i:00}")).ToArray();
        var full = PatternPreview.Create(patterns[0]);
        Check(full.Paths.Sum(path => path.Count) > 60000 && full.Objects.Count > 512, "Dense fixture does not exercise full geometry eviction");
        PatternArchive.WriteFile(patterns, Path.Combine(directory, "gallery.fap"));
        ui.View.ConfigurePatternGallery(directory); ui.Paint();
        PaintScheduledFrames(ui);
        Check(ui.View.PatternGalleryVisibleCardCount >= 4, "Dense fixture has too few visible cards");
        Check(ui.View.PatternGalleryThumbnailCost <= 64 * 3072, "Thumbnail geometry exceeded its bounded cache");
        int builds = ui.View.PatternGalleryPreviewBuildCount;
        ui.Paint(); ui.Paint();
        Check(ui.View.PatternGalleryPreviewBuildCount == builds, "Dense idle gallery regenerated previews");
        var card = ui.View.PatternGalleryCardBounds(patterns[0].Id);
        ui.Click(card.X + 20, card.Y + 20); PaintScheduledFrames(ui);
        Check(ui.View.PatternGalleryFullPreviewObjectCount == full.Objects.Count, "Large popup used sampled thumbnail objects");
        ui.Key(27);
        Check(ui.View.PatternGalleryFullPreviewObjectCount == 0, "Closed popup retained full geometry");
        ui.Resize(1440, 10000); PaintScheduledFrames(ui);
        Check(ui.View.PatternGalleryVisibleCardCount <= 48, "Tall gallery exceeded its bounded visible working set");
        var grid = ui.View.PatternGalleryGridBounds;
        ui.View.Wheel(grid.X + 10, grid.Y + 100, -120000, false, false, false); ui.Paint(); PaintScheduledFrames(ui);
        Check(ui.View.PatternGalleryPreviewCacheCount <= 64, "Scrolling exceeded thumbnail entry capacity");
    }

    private static void Wait(Func<bool> ready, Ui ui)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            if (timeout.Elapsed.TotalSeconds > 20) throw new Exception("Gallery did not settle: " + ui.View.StatusMessage);
            Thread.Sleep(5); ui.Paint();
        }
        ui.Paint();
    }
    private static void PaintScheduledFrames(Ui ui)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (ui.View.PatternGalleryNeedsRedraw)
        {
            if (timeout.Elapsed.TotalSeconds > 20) throw new Exception("Gallery never became idle after scheduled frames");
            Thread.Sleep(5); ui.Paint();
        }
        var bounds = ui.View.PatternGalleryGridBounds;
        Check(!ui.Canvas.Texts.Any(t => bounds.Contains(t.X, t.Y) && t.Value == "…"),
            "Visible preview stayed pending after gallery stopped requesting frames");
        Check(!ui.View.PatternGalleryNeedsRedraw, "Warmed gallery kept requesting idle frames");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
