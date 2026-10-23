using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.Core.Patterns;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class PatternGalleryRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string language = L.Language;
        string folder = Path.GetFullPath("artifacts/tests/pattern-gallery-native");
        Directory.CreateDirectory(folder);
        string run = Path.Combine(folder, "fixtures", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                string gallery = Path.Combine(run, $"{locale}-{size.Item1}-{dpi}");
                Directory.CreateDirectory(gallery);
                var patterns = Fixtures(locale);
                PatternArchive.WriteFile(patterns, Path.Combine(gallery, "gallery.fap"));
                var map = new MapDocument { Name = "Gallery placement", DurationMs = 30000, BeatLengthMs = 1000.0 / 3,
                    SliderMultiplier = 1.4, OverrideSliderMultiplier = true, SliderMultiplierOverride = 1.92,
                    SliderImportState = SliderImportState.Native, DerandomizeFSliderDroplets = true };
                map.Fruits.AddRange([new() { TimeMs = 1000, X = 160 }, new() { TimeMs = 1500, X = 300 }, new() { TimeMs = 2000, X = 200 }]);
                var editor = new EditorView(false);
                editor.LoadDocument(map); editor.ConfigurePatternGallery(gallery);
                editor.UpdateTransport(4000, 30000, true, false, false, null, null);
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                Paint();
                Click(editor.PreviewToggleBounds);
                var previewTab = editor.DockTabBounds(EditorView.DockPanel.Preview);
                editor.Wheel(previewTab.X + 10, previewTab.Y + 10, -12000, false); Paint();
                Click(editor.DockTabBounds(EditorView.DockPanel.PatternGallery));
                Wait(() => editor.PatternGalleryCount == patterns.Count && editor.PatternGalleryPreviewCacheCount >= 4
                    && !editor.PatternGalleryNeedsRedraw, "load and preview gallery");
                Paint("grid");
                if (editor.PatternGallerySearchBounds.Y != editor.PatternGalleryTagsBounds.Y)
                    throw new InvalidOperationException("Search and Tags are not on the same row.");
                Click(editor.PatternGalleryTagsBounds); Paint("tags-menu");
                Click(editor.PatternGalleryManageTagsMenuBounds); Paint("manage-tags");
                if (!editor.GalleryTagsVisible) throw new InvalidOperationException("Tags menu did not open Manage Tags.");
                Click(editor.GalleryNewTagBounds);
                foreach (char character in locale == "en" ? "Practice" : "\u7ec3\u4e60") editor.TextInput(character);
                Click(editor.GalleryTagHueBounds); Paint("new-tag");
                editor.KeyDown(27, false, false); Paint();
                if (new PatternGalleryStore(gallery).LoadContents().Tags.Count != 2)
                    throw new InvalidOperationException("Closing a new tag draft persisted it.");
                Click(editor.PatternGallerySearchBounds);
                foreach (char character in "Studio Fruit") editor.TextInput(character);
                Paint("search");
                if (editor.PatternGalleryFilteredCount != patterns.Count) throw new InvalidOperationException("Gallery search lost source metadata.");
                editor.KeyDown('A', true, false); editor.KeyDown(8, false, false); editor.KeyDown(27, false, false); Paint();
                int rasterBuilds = canvas.PatternRasterBuildCount;
                for (int frame = 0; frame < 24; frame++) Paint();
                if (rasterBuilds == 0 || canvas.PatternRasterBuildCount != rasterBuilds
                    || canvas.CachedPatternRasterBytes > 16L * 1024 * 1024)
                    throw new InvalidOperationException("Gallery thumbnails did not reuse their bounded raster cache.");
                var originalCard = editor.PatternGalleryCardBounds(patterns[0].Id);
                float originalDockWidth = size.Item1 - editor.PatternGalleryGridBounds.X + 12;
                foreach (float dockWidth in new[] { 240f, 450f, originalDockWidth })
                {
                    var handle = editor.DockResizeBounds(false);
                    editor.PointerDown(handle.X + 4, handle.Y + 60, 0, false, false);
                    editor.PointerMove(size.Item1 - dockWidth, handle.Y + 60, false, false); Paint();
                    editor.PointerUp(size.Item1 - dockWidth, handle.Y + 60, 0); Paint();
                    var resized = editor.PatternGalleryCardBounds(patterns[0].Id);
                    if (resized.Width != originalCard.Width || resized.Height != originalCard.Height)
                        throw new InvalidOperationException("Resizing the dock changed gallery card dimensions.");
                    Wait(() => !editor.PatternGalleryNeedsRedraw, "reflow gallery");
                    Paint($"reflow-{dockWidth:0}");
                }

                var longCard = editor.PatternGalleryCardBounds(patterns[0].Id);
                Click(longCard);
                Wait(() => editor.PatternGalleryPreviewVisible && !editor.PatternGalleryNeedsRedraw, "open long preview");
                Paint("popup");
                var preview = editor.PatternGalleryPreviewBounds;
                editor.Wheel(preview.X + 30, preview.Y + 30, -600, false); Paint("popup-scrolled");
                editor.PointerDown(preview.X + preview.Width / 2, preview.Y + 40, 0, false, false);
                editor.PointerMove(preview.X + preview.Width / 2, preview.Y + 110, false, false);
                editor.PointerUp(preview.X + preview.Width / 2, preview.Y + 110, 0); Paint("popup-panned");
                editor.Wheel(preview.X + preview.Width / 2, preview.Y + preview.Height / 2, -12000, false, false, true); Paint("popup-fit");
                editor.Wheel(preview.X + preview.Width / 2, preview.Y + preview.Height / 2, 12000, false, false, true); Paint("popup-max-zoom");
                editor.KeyDown(27, false, false); Paint();
                if (editor.WantsCapture || !map.ContentEquals(editor.Document)) throw new InvalidOperationException("Gallery browsing changed map content or retained capture.");

                editor.KeyDown('A', true, false); Paint();
                var grid = editor.PatternGalleryGridBounds;
                Click(new(grid.X, grid.Y - 36, 70, 28));
                foreach (char character in locale == "en" ? "Saved selection" : "\u4fdd\u5b58\u7684\u9009\u6bb5") editor.TextInput(character);
                Paint("name");
                editor.KeyDown(13, false, false);
                Wait(() => editor.PatternGalleryCount == patterns.Count + 1 && editor.PatternGalleryPreviewCacheCount >= 4
                    && !editor.PatternGalleryNeedsRedraw, "save selection");
                if (!map.ContentEquals(editor.Document)) throw new InvalidOperationException("Saving a selection changed the map.");

                var shortCard = editor.PatternGalleryCardBounds(patterns[1].Id);
                var plot = editor.CanvasPlotBounds;
                float px = plot.X + plot.Width / 2, py = plot.Y + plot.Height / 2;
                editor.PointerDown(shortCard.X + 20, shortCard.Y + 20, 0, false, false);
                editor.PointerMove(px, py, false, false);
                // Dragging requests continuous redraw; allow the local candidate worker to publish.
                var until = DateTime.UtcNow.AddMilliseconds(400);
                while (DateTime.UtcNow < until) { Thread.Sleep(5); Paint(); }
                Paint("drag");
                editor.PointerUp(px, py, 0);
                Paint("drop-options");
                // The placement menu is clamped at the canvas pointer and its first row keeps spacing.
                Click(new(Math.Clamp(px, 8, Math.Max(8, size.Item1 - 308)) + 6,
                    Math.Clamp(py, 8, Math.Max(8, size.Item2 - 116)) + 6, 288, 30));
                Wait(() => !editor.PatternGalleryNeedsRedraw, "commit drop");
                if (editor.Document.Fruits.Count <= map.Fruits.Count) throw new InvalidOperationException("Gallery drag did not insert fruit pattern: " + editor.StatusMessage);
                Paint("inserted");
                editor.KeyDown('Z', true, false); Paint();
                if (!map.ContentEquals(editor.Document)) throw new InvalidOperationException("Pattern drop did not undo as one operation.");

                var legacyCard = editor.PatternGalleryCardBounds(patterns[2].Id);
                editor.PointerDown(legacyCard.X + 20, legacyCard.Y + 20, 0, false, false);
                editor.PointerMove(px, py, false, false); Paint();
                editor.PointerUp(px, py, 0); Paint();
                Click(new(Math.Clamp(px, 8, Math.Max(8, size.Item1 - 308)) + 6,
                    Math.Clamp(py, 8, Math.Max(8, size.Item2 - 116)) + 6, 288, 30));
                Wait(() => !editor.PatternGalleryNeedsRedraw, "commit Legacy drop with base SV override");
                if (editor.Document.ImportedSliders.Count != 1)
                    throw new InvalidOperationException("Legacy gallery drop failed: " + editor.StatusMessage);
                Paint("legacy-inserted");
                editor.KeyDown('Z', true, false); Paint();
                if (!map.ContentEquals(editor.Document)) throw new InvalidOperationException("Legacy drop undo did not restore the map.");

                shortCard = editor.PatternGalleryCardBounds(patterns[1].Id);
                Click(shortCard);
                editor.PointerDoubleClick(shortCard.X + 20, shortCard.Y + 20, false, false); Paint("details");
                if (editor.PatternEditingActive) throw new InvalidOperationException("Double click entered pattern editing.");
                Click(editor.PatternGalleryEditTagsBounds); Paint("tags");
                if (!editor.GalleryTagsVisible) throw new InvalidOperationException("Edit Tags did not open the tag dialog.");
                Click(editor.GalleryTagHueBounds); Paint("tag-colour");
                editor.KeyDown(27, false, false); Paint();
                Click(editor.PatternGalleryEditPatternBounds); Paint("edit");
                if (!editor.PatternEditingActive) throw new InvalidOperationException("Edit Pattern did not enter pattern editing.");
                editor.DiscardPatternEditing(); Paint("returned");
                if (editor.PatternEditingActive || !map.ContentEquals(editor.Document)) throw new InvalidOperationException("Pattern edit return did not restore original map.");
                shortCard = editor.PatternGalleryCardBounds(patterns[1].Id);
                float contextX = shortCard.X + 20, contextY = shortCard.Y + 20;
                editor.PointerDown(contextX, contextY, 2, false, false);
                editor.PointerUp(contextX, contextY, 2); Paint("context-menu");
                ClickContext(0);
                editor.KeyDown('A', true, false);
                foreach (char character in locale == "en" ? "Renamed pattern" : "\u91cd\u547d\u540d\u7684\u56fe\u6848") editor.TextInput(character);
                Paint("rename"); editor.KeyDown(13, false, false);
                Wait(() => !editor.PatternGalleryNeedsRedraw, "rename pattern");
                if (new PatternGalleryStore(gallery).Load().Single(p => p.Id == patterns[1].Id).Name == patterns[1].Name)
                    throw new InvalidOperationException("Context rename did not persist.");
                editor.PointerDown(contextX, contextY, 2, false, false);
                editor.PointerUp(contextX, contextY, 2); Paint();
                ClickContext(1);
                Wait(() => editor.PatternGalleryCount == patterns.Count && !editor.PatternGalleryNeedsRedraw, "delete pattern");
                Paint("deleted");
                if (!map.ContentEquals(editor.Document)) throw new InvalidOperationException("Gallery management changed the map.");
                Console.WriteLine($"PASS native Pattern Gallery {locale} {size.Item1}x{size.Item2} {dpi} DPI");

                void ClickContext(int row) => Click(new(Math.Clamp(contextX, 8, Math.Max(8, size.Item1 - 308)) + 6,
                    Math.Clamp(contextY, 8, Math.Max(8, size.Item2 - 84)) + 6 + row * 32, 288, 30));

                void Click(Rect rect)
                {
                    if (rect.Width <= 0 || rect.Height <= 0) throw new InvalidOperationException("Missing Pattern Gallery input bounds.");
                    float x = rect.X + Math.Min(10, rect.Width / 2), y = rect.Y + Math.Min(10, rect.Height / 2);
                    editor.PointerDown(x, y, 0, false, false); editor.PointerUp(x, y, 0); Paint();
                }
                void Wait(Func<bool> ready, string operation)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    do { Paint(); if (ready()) return; Thread.Sleep(5); } while (DateTime.UtcNow < deadline);
                    throw new InvalidOperationException($"Pattern Gallery timed out: {operation}. {editor.StatusMessage}");
                }
                void Paint(string? name = null)
                {
                    canvas.Begin(); editor.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{name}-{locale}-{size.Item1}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static List<PatternSnapshot> Fixtures(string locale)
    {
        var patterns = new List<PatternSnapshot>();
        string[] names = locale == "en"
            ? ["Long staircase", "Fruit triangle", "Soft curve", "Alternating stack", "Quick stream", "Banana shower", "Wide jump", "Reverse curve", "Short stack", "Even stream", "Crossing", "Quiet ending"]
            : ["\u957f\u9636\u68af", "\u4e09\u89d2\u679c\u4e32", "\u67d4\u548c\u66f2\u7ebf", "\u4ea4\u66ff\u53e0\u4e32", "\u5feb\u901f\u4e32", "\u9999\u8549\u96e8", "\u5927\u8df3", "\u53cd\u5411\u66f2\u7ebf", "\u77ed\u53e0\u4e32", "\u5747\u5300\u4e32", "\u4ea4\u53c9", "\u6536\u5c3e"];
        for (int index = 0; index < names.Length; index++)
        {
            var map = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Metadata]\nArtist:Studio Fruit\nTitle:Snowlight\nVersion:Gallery study\nCreator:Atelier\n[Difficulty]\nApproachRate:9\nSliderMultiplier:1.92\nSliderTickRate:2\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n");
            map.DurationMs = 30000;
            if (index == 0)
                map.Fruits.AddRange(Enumerable.Range(0, 65).Select(i => new Fruit { TimeMs = 1000 + i * 250, X = 256 + 150 * Math.Sin(i * .6) }));
            else if (index == 2)
                map.ImportedSliders.AddRange(OsuBeatmapReader.Read("osu file format v14\n[TimingPoints]\n0,500\n[HitObjects]\n240,192,1000,2,0,P|272:192|320:128,1,110.25\n").ImportedSliders);
            else if (index is 7 or 10)
            {
                var track = new CurveTrack { Kind = CurveKind.Bezier, SpanCount = index == 7 ? 2 : 1 };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 120, HandleOut = new(500, 260) }, new() { TimeMs = 2500, X = 360, HandleIn = new(-500, -260) }]);
                map.Tracks.Add(track);
            }
            else if (index is 3 or 4 or 8 or 9)
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4,
                    Stack = index is 3 or 8 ? new StackEnvelope() : null };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 180 }, new() { TimeMs = 2500, X = 340 }]);
                map.Tracks.Add(track);
            }
            else if (index == 5) map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 2500 });
            else map.Fruits.AddRange([new() { TimeMs = 1000, X = 140 }, new() { TimeMs = 1100, X = 360 }, new() { TimeMs = 1500, X = 256 }]);
            patterns.Add(PatternSnapshot.Capture(map, map.Fruits.Select(f => f.Id).Concat(map.Tracks.Select(t => t.Id))
                .Concat(map.ImportedSliders.Select(s => s.Id)).Concat(map.BananaShowers.Select(b => b.Id)), names[index])
                .WithTags([index % 2 == 0 ? new("Flow", 0x58D8C1) : new("Jump", 0xFFB45C)]));
        }
        return patterns;
    }
}
