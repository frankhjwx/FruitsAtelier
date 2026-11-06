using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class BookmarkPanelTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var map = new MapDocument { DurationMs = 120000 };
                OsuTimeline.AddBookmark(map, 15000); OsuTimeline.AddBookmark(map, 30000); OsuTimeline.AddBookmark(map, 60000);
                var ui = new Ui(overview: false); ui.LoadDocument(map); ui.Resize(1440, 900);
                ui.View.ShowBookmarkPanel(); ui.Paint();
                Check(!ui.View.IsDirty && map.ContentEquals(ui.View.Document), "Opening the panel changed content");
                Check(ui.View.Document.BookmarkAppearanceAt(15000) == new BookmarkAppearance(), "Default bookmark appearance");
                var addLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get("bookmark.new"));
                Check(addLabel.Y > ui.View.BookmarkTimeBounds(60000).Bottom && addLabel.Y < ui.View.BookmarkTimeBounds(60000).Bottom + 30, "New bookmark follows the list");
                ui.ClickText(L.Get("bookmark.enableIntervals"));
                Check(ui.View.Document.BookmarkAppearanceAt(15000).ShowInterval && ui.View.Document.BookmarkAppearanceAt(30000).ShowInterval
                    && !ui.View.Document.BookmarkAppearanceAt(60000).ShowInterval, "Enable every existing interval");
                ui.Key('Z', true); Check(ui.View.Document.BookmarkAppearances.Count == 0, "Bulk enable is one undo step");
                ui.Key('Y', true); ui.ClickText(L.Get("bookmark.disableIntervals"));
                Check(ui.View.Document.BookmarkAppearances.Count == 0, "Disable every interval");
                ui.Key('Z', true); Check(ui.View.Document.BookmarkAppearanceAt(15000).ShowInterval, "Bulk disable undo");
                ui.Key('Y', true);
                var first = ui.View.BookmarkTimeBounds(15000);
                Click(first); ui.Type("00:20:000"); ui.Key(13);
                Check(OsuTimeline.Bookmarks(ui.View.Document).SequenceEqual(new[] { 20000, 30000, 60000 }), "Time editing and sorting");
                ui.Key('Z', true); Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(15000), "Time undo");
                ui.Key('Y', true); Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(20000), "Time redo");
                first = ui.View.BookmarkTimeBounds(20000);
                Click(first); ui.Type("00:30:000"); ui.Key(13);
                Check(ui.View.IsEditingText && OsuTimeline.Bookmarks(ui.View.Document).Contains(20000), "Duplicate time rejected");
                ui.Key(27);
                Click(ui.View.BookmarkTimeBounds(20000)); ui.Type("-1"); ui.Key(13);
                Check(ui.View.IsEditingText && OsuTimeline.Bookmarks(ui.View.Document).Contains(20000), "Negative time rejected");
                ui.Key(27);
                var panel = ui.View.BookmarkPanelViewport;
                Click(ui.View.BookmarkTimeBounds(20000)); ui.Type("00:21:000");
                Click(ui.View.BookmarkColourBounds(20000));
                Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(21000)
                    && ui.Canvas.Texts.Any(t => t.Value == L.Get("bookmark.colourAt", "00:21:000")), "Pending time commits before the row action");
                ui.Key(27); ui.Key('Z', true);
                first = ui.View.BookmarkTimeBounds(20000);
                Click(ui.View.BookmarkIntervalBounds(20000));
                Check(ui.View.Document.BookmarkAppearanceAt(20000).ShowInterval, "Interval toggle");
                string copied = "";
                ui.View.RequestCopyText = text => copied = text;
                RightClick(ui.View.BookmarkColourBounds(20000)); ui.ClickText(L.Get("bookmark.copyColour"));
                Check(copied == "#4B9EF5", "Swatch context copy");
                ui.View.RequestPasteBookmarkColour = () => ui.View.PasteBookmarkColour("65C9B1", ui.View.BookmarkColourPasteSession);
                RightClick(ui.View.BookmarkColourBounds(20000)); ui.ClickText(L.Get("bookmark.pasteColour"));
                Check(ui.View.Document.BookmarkAppearanceAt(20000).Colour == 0x65C9B1, "Swatch context paste");
                ui.Key('Z', true);
                Check(ui.View.Document.BookmarkAppearanceAt(20000).Colour == 0x4B9EF5, "Pasted colour undo");
                ui.View.RequestPasteBookmarkColour = () => ui.View.PasteBookmarkColour("invalid", ui.View.BookmarkColourPasteSession);
                RightClick(ui.View.BookmarkColourBounds(20000)); ui.ClickText(L.Get("bookmark.pasteColour"));
                Check(ui.View.Document.BookmarkAppearanceAt(20000).Colour == 0x4B9EF5, "Invalid colour paste leaves content intact");
                int delayedSession = 0;
                ui.View.RequestPasteBookmarkColour = () => delayedSession = ui.View.BookmarkColourPasteSession;
                RightClick(ui.View.BookmarkColourBounds(20000)); ui.ClickText(L.Get("bookmark.pasteColour"));
                ui.Click(panel.Right - 12, ui.View.BookmarkTimeBounds(20000).Y + 12);
                var deleted = ui.View.Document.DeepClone();
                ui.View.PasteBookmarkColour("#123456", delayedSession);
                Check(deleted.ContentEquals(ui.View.Document), "Delayed paste cannot recreate a deleted bookmark");
                ui.Key('Z', true);
                Click(ui.View.BookmarkColourBounds(20000));
                ui.ClickText("#4B9EF5"); ui.Type("#F0785A"); ui.Key(13);
                Check(ui.View.Document.BookmarkAppearanceAt(20000).Colour == 0xF0785A, "Hex colour editing");
                ui.Click(panel.Right - 20, panel.Bottom - 230);
                Check(ui.Canvas.Lines.Any(line => line.Color == 0xF0785A), "Custom timeline colour");
                var saved = ProjectSerializer.Serialize(ui.View.Document);
                Check(saved.Contains("\"SchemaVersion\": 49") && ProjectSerializer.Read(saved).ContentEquals(ui.View.Document), "Single project persistence");
                var project = BeatmapProject.FromDocuments([ui.View.Document]);
                var multi = ProjectSerializer.Serialize(project);
                Check(multi.Contains("\"SchemaVersion\": 50") && ProjectSerializer.ReadProject(multi).Difficulties[0].Document.ContentEquals(ui.View.Document), "Multi project persistence");
                var osu = OsuBeatmapWriter.Serialize(ui.View.Document);
                Check(osu.Text.Contains("Bookmarks: 20000,30000,60000") && !osu.Text.Contains("BookmarkAppearances"), "osu bookmark compatibility");
                first = ui.View.BookmarkTimeBounds(20000);
                ui.View.PointerDown(first.X + 10, first.Y + 10, 2, false, false); ui.Paint();
                ui.ClickText(L.Get("bookmark.insertBelow"));
                Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(25000) && ui.View.IsEditingText, "Insert below and focus");
                ui.Type("00:24:000"); ui.Key(13);
                Check(ui.View.Document.BookmarkAppearanceAt(24000) == new BookmarkAppearance(), "Inserted bookmark defaults");
                first = ui.View.BookmarkTimeBounds(24000);
                ui.View.PointerDown(first.X + 10, first.Y + 10, 2, false, false); ui.Paint();
                ui.ClickText(L.Get("bookmark.insertAbove")); ui.Key(13);
                Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(22000), "Insert above");
                ui.View.UpdateTransport(42500, 120000, true, false, false, null, null); ui.Paint();
                ui.ClickText(L.Get("bookmark.new")); ui.Key(13);
                Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(42500), "Add new bookmark");
                ui.Key('Z', true); Check(!OsuTimeline.Bookmarks(ui.View.Document).Contains(42500), "Add undo");
                var clone = ui.View.Document.DeepClone();
                OsuTimeline.MoveBookmark(clone, 20000, 21000);
                Check(clone.BookmarkAppearanceAt(21000) == new BookmarkAppearance(0xF0785A, true) && !clone.BookmarkAppearances.ContainsKey(20000), "Appearance follows time");
                OsuTimeline.ToggleBookmark(clone, 21000); OsuTimeline.AddBookmark(clone, 21000);
                Check(clone.BookmarkAppearanceAt(21000) == new BookmarkAppearance(), "Deleted appearance is removed");
                Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).BookmarkAppearances.Count == 0, "Older/default project compatibility");
                var timing = map.DeepClone();
                var red = new TimingPoint { TimeMs = 0, BeatLengthMs = 500 };
                timing.TimingPoints.Add(red);
                OsuTimeline.SetBookmarkAppearance(timing, 15000, new(0x123456, true));
                var movedRed = TimingEditing.Copy(red); movedRed.TimeMs = 1000;
                TimingEditing.Apply(timing, [movedRed], [(red, movedRed)], new(OffsetMarkers: true));
                Check(timing.BookmarkAppearanceAt(16000) == new BookmarkAppearance(0x123456, true)
                    && !timing.BookmarkAppearances.ContainsKey(15000), "Timing offsets preserve bookmark appearance");
                var many = new MapDocument { DurationMs = 200000 };
                SongSetup.Set(many, "Editor", "Bookmarks", string.Join(',', Enumerable.Range(0, 10000).Select(i => i * 20)));
                ui.LoadDocument(many); ui.View.ShowBookmarkPanel(); ui.Resize(980, 620);
                Check(ui.Canvas.Texts.Count(t => t.Value == L.Get("bookmark.jump")) < 20, "Visible-row rendering");
                ui.View.Wheel(ui.View.BookmarkPanelViewport.X + 10, ui.View.BookmarkPanelViewport.Y + 160, -1200, false, false); ui.Paint();
                Check(ui.View.BookmarkTimeBounds(0).Width == 0, "Bookmark scrolling");
                var resize = ui.View.DockResizeBounds(false);
                ui.View.PointerDown(resize.X + 4, resize.Y + 60, 0, false, false);
                ui.View.PointerMove(ui.Width - 180, resize.Y + 60, false, false);
                ui.View.PointerUp(ui.Width - 180, resize.Y + 60, 0); ui.Paint();
                Check(ui.View.BookmarkPanelViewport.Width < 280
                    && ui.Canvas.Texts.Count(t => t.Value == L.Get("bookmark.jump")) < 20, "Minimum dock layout");
                void Click(Rect bounds) => ui.Click(bounds.X + 8, bounds.Y + 10);
                void RightClick(Rect bounds)
                { ui.View.PointerDown(bounds.X + 8, bounds.Y + 10, 2, false, false); ui.View.PointerUp(bounds.X + 8, bounds.Y + 10, 2); ui.Paint(); }
            }
        }
        finally { L.SetLanguage(language); }
        Console.WriteLine("PASS bookmark panel editing, colours, intervals, insertion, undo, persistence and scrolling");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
