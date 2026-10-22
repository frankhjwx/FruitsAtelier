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
            PaintScheduledFrames(ui);
            Check(!ui.Canvas.Operations.Any(o => o.Clip == preview && o.Segment is not null),
                "Fruit preview contains an auxiliary connector or snap grid");
            Check(ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot is not null).All(o => o.Dot!.Value.Color == 0xFFFFFF),
                "Gallery preview uses combo colouring");
            bool reverseScroll = ui.View.LibrarySettings.ReverseCanvasScroll;
            var initialThumb = ui.Canvas.Fills.Single(f => f.Color == 0x75818D && f.Bounds.X == preview.Right - 4).Bounds;
            Check(Math.Abs(initialThumb.Bottom - preview.Bottom) < .001, "Preview scrollbar does not start at the geometry's bottom");
            foreach (bool reverse in new[] { false, true })
            {
                ui.View.LibrarySettings.ReverseCanvasScroll = reverse;
                float forward = reverse ? 120 : -120;
                ui.View.Wheel(preview.X + 20, preview.Y + 20, forward, false); ui.Paint();
                Check(ui.View.PatternGalleryPreviewScroll > 0, "Preview did not follow the canvas scroll direction setting");
                var movedThumb = ui.Canvas.Fills.Single(f => f.Color == 0x75818D && f.Bounds.X == preview.Right - 4).Bounds;
                Check(movedThumb.Y < initialThumb.Y, "Preview scrollbar did not move toward later geometry above");
                ui.View.Wheel(preview.X + 20, preview.Y + 20, -forward, false); ui.Paint();
                Check(ui.View.PatternGalleryPreviewScroll == 0, "Reverse wheel did not return to the preview start");
            }
            ui.View.LibrarySettings.ReverseCanvasScroll = reverseScroll;
            int zoomBuilds = ui.View.PatternGalleryPreviewBuildCount;
            float initialRadius = ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot?.Color == 0xFFFFFF)
                .Max(o => o.Dot!.Value.Radius);
            ui.View.Wheel(preview.X + preview.Width / 2, preview.Y + preview.Height / 2, -12000, false, false, true); ui.Paint();
            var fitDots = ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot?.Color == 0xFFFFFF)
                .Select(o => o.Dot!.Value).ToArray();
            Check(fitDots.Length == 2 && fitDots.All(d => d.Y - d.Radius >= preview.Y && d.Y + d.Radius <= preview.Bottom),
                "Alt-wheel minimum did not show the entire pattern");
            float fitRadius = fitDots[0].Radius;
            Check(fitRadius < initialRadius && ui.View.PatternGalleryPreviewScroll == 0,
                "Alt-wheel did not zoom out to a fitted viewport");
            ui.View.Wheel(preview.X + 20, preview.Y + 20, -1200, false, false, true); ui.Paint();
            Check(ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot?.Color == 0xFFFFFF)
                .All(o => Math.Abs(o.Dot!.Value.Radius - fitRadius) < .001), "Preview zoomed below full-pattern fit");
            ui.View.Wheel(preview.X + preview.Width / 2, preview.Y + preview.Height / 2, 12000, false, false, true); ui.Paint();
            ui.View.Wheel(preview.X + 20, preview.Y + 20, reverseScroll ? -12000 : 12000, false); ui.Paint();
            Check(Math.Abs(ui.Canvas.Operations.Where(o => o.Clip == preview && o.Dot?.Color == 0xFFFFFF)
                .Max(o => o.Dot!.Value.Radius) - initialRadius) < .001, "Preview exceeded the original maximum zoom");
            Check(ui.View.PatternGalleryPreviewBuildCount == zoomBuilds, "Zooming rebuilt preview geometry");
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
            FixedCardResize(ui, snapshot.Id);
            Check(!ui.Canvas.Texts.Any(t => t.Value == Strings.Get("gallery.preserveSpeed")), "Gallery shows placement option before drop");
            title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            float x = title.X + 10, y = title.Y - 25;
            ui.View.PointerDown(x, y, 0, false, false);
            var target = ui.ScreenAt(2000, 256);
            ui.View.PointerMove(target.X, target.Y, false, false); ui.Paint();
            ui.View.PointerUp(target.X, target.Y, 0);
            ui.Paint();
            Check(ui.View.Document.Fruits.Count == 0, "Drop committed before choosing placement mode");
            ui.ClickText(Strings.Get("gallery.keepWidth"));
            Wait(() => ui.View.Document.Fruits.Count == 2, ui);
            Check(ui.View.Document.Fruits.All(f => f.X >= 0 && f.X <= 512), "Inserted pattern escaped playfield");
            Check(Math.Abs(ui.View.Document.Fruits[1].TimeMs - ui.View.Document.Fruits[0].TimeMs - 7200) < .001,
                "Cross-BPM gallery drop did not preserve beat spacing");
            Check(ui.View.Document.Fruits.All(f => source.Fruits.All(s => s.Id != f.Id)), "Drop reused snapshot identities");
            ui.Key(90, ctrl: true);
            Check(ui.View.Document.Fruits.Count == 0, "Drop was not one undo step");
            title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            ui.View.PointerDown(title.X + 10, title.Y - 25, 0, false, false);
            ui.View.PointerMove(target.X, target.Y, false, false); ui.Paint();
            ui.View.PointerUp(target.X, target.Y, 0); ui.Paint();
            ui.Key(27); ui.Paint();
            Check(ui.View.Document.Fruits.Count == 0 && !ui.View.WantsCapture, "Dismissing drop choices changed the map or retained capture");
            title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            ui.View.PointerDown(title.X + 10, title.Y - 25, 0, false, false);
            ui.View.PointerMove(target.X, target.Y, false, false); ui.Paint();
            ui.View.PointerUp(target.X, target.Y, 0); ui.Paint();
            ui.ClickText(Strings.Get("gallery.preserveSpeed"));
            Wait(() => ui.View.Document.Fruits.Count == 2, ui);
            Check(Math.Abs(ui.View.Document.Fruits[1].X - ui.View.Document.Fruits[0].X - 256) < .001,
                "Speed-preserving drop did not scale width with destination BPM");
            ui.Key(90, ctrl: true);
            title = ui.Canvas.Texts.Single(t => t.Value == snapshot.Name);
            ui.View.PointerDown(title.X + 10, title.Y - 25, 2, false, false);
            ui.View.PointerUp(title.X + 10, title.Y - 25, 2); ui.Paint();
            ui.ClickText(Strings.Get("gallery.rename"));
            ui.Key(65, ctrl: true);
            foreach (char ch in "Renamed gallery pattern") ui.View.TextInput(ch);
            ui.Key(13);
            Wait(() => store.Load().Any(p => p.Id == snapshot.Id && p.Name == "Renamed gallery pattern")
                && !ui.View.PatternGalleryNeedsRedraw, ui);
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Rename changed the destination map");
            ui.View.BeginPatternExport(Path.Combine(directory, "shared.fap"), false);
            Wait(() => File.Exists(Path.Combine(directory, "shared.fap")) && !ui.View.PatternGalleryNeedsRedraw, ui);
            Check(ui.View.BeginPatternEditing(snapshot), "Pattern edit session did not open");
            ui.View.OpenSongSetup(); ui.Paint();
            ui.Key(83, ctrl: true);
            Check(ui.View.PatternEditingActive && !ui.View.PatternEditingBusy && ui.View.SongSetupVisible,
                "Pattern save shortcut bypassed the Song Setup modal");
            ui.Key(27); ui.View.DiscardPatternEditing();
            ui.Paint();
            title = ui.Canvas.Texts.Single(t => t.Value == "Renamed gallery pattern");
            ui.View.PointerDown(title.X + 10, title.Y - 25, 2, false, false);
            ui.View.PointerUp(title.X + 10, title.Y - 25, 2); ui.Paint();
            ui.ClickText(Strings.Get("gallery.delete"));
            Wait(() => ui.View.PatternGalleryCount == 15 && !ui.View.PatternGalleryNeedsRedraw, ui);
            Check(store.Load().All(p => p.Id != snapshot.Id) && before.ContentEquals(ui.View.Document),
                "Context delete did not persist or changed the destination map");
            PreviewAlignment(ui, Path.Combine(directory, "alignment"));
            DenseThumbnailScheduling(ui, Path.Combine(directory, "dense"));
            LegacyOverrideDrops();
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void LegacyOverrideDrops()
    {
        var source = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:2.52\nSliderTickRate:2\n[TimingPoints]\n0,384.615384615385,4,1,0,100,1,0\n[HitObjects]\n240,192,1000,2,0,P|272:192|320:128,1,110.25,8|0,0:3|0:0,0:0:0:0:\n160,192,1600,2,0,L|320:192,2,160\n");
        var raw = PatternSnapshot.Capture(source, source.ImportedSliders.Select(s => s.Id), "Legacy curves");
        source.DerandomizeFSliderDroplets = true;
        source.SliderImportState = SliderImportState.Native;
        var processed = SliderRepresentationEditing.RefreshLegacyDroplets(source);
        Check(processed.Success, "Cannot prepare processed gallery fixture: " + string.Join("; ", processed.Diagnostics));
        var saved = PatternSnapshot.Capture(source, source.ImportedSliders.Select(s => s.Id), "Processed curves");
        var target = new MapDocument { IsDemo = false, DurationMs = 30000, BeatLengthMs = 60000.0 / 178,
            TimingOffsetMs = -26, SliderMultiplier = 1.4, OverrideSliderMultiplier = true, SliderMultiplierOverride = 1.92,
            SliderImportState = SliderImportState.Native, DerandomizeFSliderDroplets = true, DerandomizeDroplets = true };
        CheckDrops([raw, saved], target);
    }

    internal static void CheckDrops(IReadOnlyList<PatternSnapshot> patterns, MapDocument target)
    {
        string directory = Path.GetFullPath(Path.Combine("artifacts/tests/pattern-gallery-drop", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        PatternArchive.WriteFile(patterns, Path.Combine(directory, "gallery.fap"));
        var ui = new Ui();
        ui.LoadDocument(target.DeepClone());
        ui.View.ConfigurePatternGallery(directory);
        ui.ClickText(Strings.Get("dock.layout")); ui.ClickText("✓ " + Strings.Get("dock.gallery"));
        ui.ClickText(Strings.Get("dock.layout")); ui.ClickText(Strings.Get("dock.gallery"));
        PaintScheduledFrames(ui);
        foreach (var snapshot in patterns)
        {
            var before = ui.View.Document.DeepClone();
            var card = ui.View.PatternGalleryCardBounds(snapshot.Id);
            if (!ui.View.PatternGalleryGridBounds.Contains(card.X + 20, card.Y + 20))
            {
                var grid = ui.View.PatternGalleryGridBounds;
                ui.View.Wheel(grid.X + 10, grid.Y + 10, -120, false); ui.Paint(); PaintScheduledFrames(ui);
                card = ui.View.PatternGalleryCardBounds(snapshot.Id);
            }
            var destination = ui.ScreenAt(2000, 300);
            ui.View.PointerDown(card.X + 20, card.Y + 20, 0, false, false);
            ui.View.PointerMove(destination.X, destination.Y, false, false); ui.Paint();
            ui.View.PointerUp(destination.X, destination.Y, 0); ui.Paint();
            ui.ClickText(Strings.Get("gallery.keepWidth"));
            Wait(() => !ui.View.PatternGalleryNeedsRedraw, ui);
            Check(ui.View.Document.ImportedSliders.Count == before.ImportedSliders.Count + snapshot.CreateDocument().ImportedSliders.Count,
                "Legacy gallery drop failed for " + snapshot.Name + ": " + ui.View.StatusMessage);
            var output = OsuBeatmapWriter.Serialize(ui.View.Document);
            Check(output.ObjectSequenceMatches, "Dropped Legacy pattern did not survive export read-back");
            var inserted = ui.View.Document.DeepClone();
            ui.Key(90, ctrl: true);
            Check(ui.View.Document.ContentEquals(before), "Legacy drop did not undo as a single edit");
            ui.Key(89, ctrl: true);
            Check(ui.View.Document.ContentEquals(inserted), "Legacy drop redo changed processed geometry or timing");
            ui.Key(90, ctrl: true);
        }
    }

    private static void FixedCardResize(Ui ui, Guid id)
    {
        var original = ui.View.PatternGalleryCardBounds(id);
        var originalGrid = ui.View.PatternGalleryGridBounds;
        float originalWidth = ui.Width - originalGrid.X + 12;
        var positions = new HashSet<float>();
        foreach (float width in new[] { 240f, 340f, 450f, originalWidth })
        {
            var handle = ui.View.DockResizeBounds(false);
            ui.View.PointerDown(handle.X + 4, handle.Y + 60, 0, false, false);
            foreach (float offset in new[] { -2f, 0f, 2f })
            {
                ui.View.PointerMove(ui.Width - width + offset, handle.Y + 60, false, false); ui.Paint();
                var card = ui.View.PatternGalleryCardBounds(id);
                Check(card.Width == original.Width && card.Height == original.Height,
                    "Resizing the gallery changed card dimensions");
                positions.Add(ui.View.PatternGalleryGridBounds.Width);
            }
            ui.View.PointerMove(ui.Width - width, handle.Y + 60, false, false);
            ui.View.PointerUp(ui.Width - width, handle.Y + 60, 0); ui.Paint();
        }
        Check(positions.Count > 3, "Gallery resize fixture did not change dock width");
        PaintScheduledFrames(ui);
    }

    private static void PreviewAlignment(Ui ui, string directory)
    {
        Directory.CreateDirectory(directory);
        var source = new MapDocument { ApproachRate = 9, CircleSize = 5, DurationMs = 5000 };
        source.Fruits.AddRange([new() { TimeMs = 1000, X = 30 }, new() { TimeMs = 1010, X = 140 },
            new() { TimeMs = 1200, X = 80 }]);
        var saved = PatternSnapshot.Capture(source, source.Fruits.Select(f => f.Id), "Left-side import");
        PatternArchive.WriteFile([saved], Path.Combine(directory, "gallery.fap"));
        ui.View.ConfigurePatternGallery(directory); ui.Resize(1440, 900); ui.Paint();
        PaintScheduledFrames(ui);
        var card = ui.View.PatternGalleryCardBounds(saved.Id);
        var image = new FruitsAtelier.App.Rendering.Rect(card.X + 6, card.Y + 6, card.Width - 12, card.Height - 41);
        Verify(image, thumbnail: true);
        ui.Click(card.X + 20, card.Y + 20); PaintScheduledFrames(ui);
        Verify(ui.View.PatternGalleryPreviewBounds, thumbnail: false);
        ui.Key(27);

        void Verify(FruitsAtelier.App.Rendering.Rect bounds, bool thumbnail)
        {
            var dots = ui.Canvas.Operations.Where(o => o.Clip == bounds && o.Dot is not null)
                .Select(o => o.Dot!.Value).ToArray();
            var fruits = dots.Where(d => d.Color == 0xFFFFFF).OrderByDescending(d => d.Y).ToArray();
            Check(fruits.Length == 3 && dots.Any(d => d.Color == 0xFF0000), "Preview omitted red fruit or saved objects");
            Check(Math.Abs((fruits.Min(d => d.X) + fruits.Max(d => d.X)) / 2 - (bounds.X + bounds.Width / 2)) < .001,
                "Off-center import was not centered on its actual horizontal bounds");
            Check(bounds.Bottom - fruits[0].Y < fruits[0].Radius * 1.2f + 4.01f,
                "Preview did not start at the bottom");
            if (!thumbnail)
                Check(Math.Abs((fruits[0].Y - fruits[2].Y) - 200 * CatchScrollTiming.PixelsPerMs(9, bounds.Width - 28)) < .001,
                    "Large preview used the active map AR instead of saved AR");
        }
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
