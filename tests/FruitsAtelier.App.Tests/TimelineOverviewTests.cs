using FruitsAtelier.Core;

internal static class TimelineOverviewTests
{
    public static void ComboColours()
    {
        var map = new MapDocument { DurationMs = 3000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 1000, X = 100, OriginalLine = "100,192,1000,1,0,0:0:0:0:" });
        map.Fruits.Add(new() { TimeMs = 1500, X = 200, OriginalLine = "200,192,1500,1,0,0:0:0:0:" });
        map.Fruits.Add(new() { TimeMs = 2000, X = 300, OriginalLine = "300,192,2000,5,0,0:0:0:0:" });
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(1500, 3000, true, false, false, null, null); ui.Paint();
        var timeline = ui.View.ObjectTimelineBounds;
        float X(double time) => timeline.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        uint Color(double time) => ui.Canvas.Circles.Single(c => c.Radius == 19 && c.Y == timeline.Y + 27
            && Math.Abs(c.X - X(time)) < .01f).Color;
        Check(Color(1000) == Color(1500) && Color(1500) != Color(2000),
            "Object timeline must use one combo colour until New Combo");
        Check(ui.Canvas.Fills.Any(f => f.Color == Color(1000) && Math.Abs(f.Bounds.X - (X(1000) - 19)) < .01f),
            "Object timeline note fill must use its combo colour");
    }

    public static void BreakEdgeEditing()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 200 });
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        map.Fruits.Add(new() { TimeMs = 4000, X = 200 });
        OsuTimeline.AddBreak(map, 1200, 3250);
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(2500, 5000, true, false, false, null, null); ui.Paint();
        var timeline = ui.View.ObjectTimelineBounds;
        float X(double time) => timeline.X + (float)((time - ui.View.ObjectTimelineStartMs) * ui.View.ObjectTimelinePixelsPerMs);
        bool Band(uint color, double from, double to) => ui.Canvas.Fills.Any(f => f.Color == color
            && f.Bounds.Y == timeline.Y && Math.Abs(f.Bounds.X - X(from)) < .01f
            && Math.Abs(f.Bounds.Right - X(to)) < .01f);
        Check(Band(0xF1F1F1, 1000, 1200) && Band(0x858585, 1200, 3250)
            && Band(0xA7CFA1, 3250, 4000), "Break transitions must reach their surrounding objects");
        float y = timeline.Y + 40;
        ui.View.PointerMove(X(1200), y, false, false); ui.Paint();
        Check(ui.View.TimelineResizeCursor, "Break start needs a resize cursor");
        ui.View.PointerDown(X(1200), y, 0, false, false);
        ui.View.PointerMove(X(1600), y, false, false); ui.Paint();
        Check(Band(0x858585, 1600, 3250), "Break resize preview did not follow the pointer");
        ui.View.PointerUp(X(1600), y, 0); ui.Paint();
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1600, 3250)]),
            "Dragging the start did not edit the break range");
        ui.Key(90, ctrl: true);
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1200, 3250)]), "Break resize did not undo");
        ui.View.PointerDown(X(1200), y, 0, false, false);
        ui.View.PointerMove(X(1637), y, false, false); ui.Paint();
        Check(Band(0x858585, 1650, 3250), "Break resize preview must follow the timeline snap grid");
        ui.View.PointerUp(X(1637), y, 0);
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1650, 3250)]),
            "Break resize must save the snapped boundary");
        ui.Key(90, ctrl: true);
        ui.View.PointerDown(X(1200), y, 0, false, false);
        ui.View.PointerMove(X(1700), y, false, false);
        ui.Key(27);
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1200, 3250)]), "Esc did not cancel break resize");
        ui.View.PointerDown(X(3250), y, 0, false, false);
        ui.View.PointerMove(X(1300), y, false, false);
        ui.View.PointerUp(X(1300), y, 0); ui.Paint();
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 0, "A break shortened below the minimum must disappear");
        ui.Key(90, ctrl: true);
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1200, 3250)]), "Shortened break deletion did not undo");
    }

    public static void InsertBreakButton()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        map.Fruits.Add(new() { TimeMs = 4000, X = 200 });
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(2500, 5000, true, false, false, null, null); ui.Paint();
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("timeline.insertBreak"));
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1200, 3250)]),
            "Insert Break Time must use the gap between surrounding objects and the next object's approach time");
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("timeline.insertBreak"));
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 1, "Insert Break Time duplicated an overlapping break");
        ui.Key(90, ctrl: true);
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 0, "Inserted break did not undo");
    }

    public static void PreviewPointMenu()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        map.Fruits.Add(new() { TimeMs = 3000, X = 100 });
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(2500, 5000, true, false, false, null, null); ui.Paint();
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("timeline.timingMenu"));
        Check(ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("timeline.setPreviewPoint")),
            "Timing menu did not open");
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("timeline.setPreviewPoint"));
        Check(OsuTimeline.PreviewTime(ui.View.Document) == 2500 && ui.View.IsDirty, "Timing menu did not set the preview point");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xFFD34A), "New preview point was not drawn");
        ui.Key(90, ctrl: true);
        Check(OsuTimeline.PreviewTime(ui.View.Document) is null && !ui.Canvas.Lines.Any(l => l.Color == 0xFFD34A),
            "Preview point did not undo");
        ui.Key(89, ctrl: true);
        Check(OsuTimeline.PreviewTime(ui.View.Document) == 2500, "Preview point did not redo");
    }

    public static void DetailMarkers()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        map.TimingPoints.Add(new() { TimeMs = 1500, BeatLengthMs = 500 });
        map.TimingPoints.Add(new() { TimeMs = 1800, BeatLengthMs = -100, Uninherited = false });
        map.TimingPoints.Add(new() { TimeMs = 1900, BeatLengthMs = -100, Uninherited = false });
        map.Fruits.Add(new() { TimeMs = 1800, X = 100 });
        OsuTimeline.AddBreak(map, 1500, 2200);
        SongSetup.Set(map, "General", "PreviewTime", "1800");
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(1800, 5000, true, false, false, null, null); ui.Paint();
        var overview = ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
        float previewX = overview.X + (float)(1800d / ui.View.TimelineDurationMs) * overview.Width;
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xFFD34A && Math.Abs(l.X1 - previewX) < .01f
            && l.Y1 < overview.Y && l.Y2 > overview.Bottom && l.Width == 2), "Preview marker must cross the full overview");
        var timeline = ui.View.ObjectTimelineBounds;
        var plot = ui.View.CanvasPlotBounds;
        foreach (uint color in new uint[] { 0xEA2222, 0x7BC600 })
        {
            Check(ui.Canvas.Lines.Any(l => l.Color == color && l.Y1 == timeline.Y && l.Y2 == timeline.Bottom - 1),
                "Timing marker missing from the upper timeline");
            Check(!ui.Canvas.Lines.Any(l => l.Color == color && l.X1 == 108 && l.X2 == plot.X),
                "Canvas time axis should only show break bands");
        }
        var fruit = ui.Canvas.Circles.Single(c => c.Radius == 19 && c.Y == timeline.Y + 27);
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x7BC600 && l.Y1 == timeline.Y
            && Math.Abs(l.X1 - fruit.X) < .01f), "Upper timing marker and object center must share the same time coordinate");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0x858585 && f.Bounds.Y == timeline.Y
            && f.Bounds.Height == timeline.Height - 1), "Break must fill the upper timeline height");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0x858585 && f.Bounds.X == 108
            && f.Bounds.Right == plot.X && f.Bounds.Y >= plot.Y && f.Bounds.Bottom <= plot.Bottom),
            "Break must be clipped to the canvas time axis");
        Check(ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("timeline.breakLabel")),
            "Visible break needs a label");
    }

    public static void TransportControls()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false, AudioPath = "fixture.wav" };
        map.Fruits.Add(new() { TimeMs = 750, X = 100 });
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        ui.View.UpdateTransport(1000, 5000, true, false, false, null, map.AudioPath); ui.Paint();
        var area = ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
        var test = ui.View.TestplayButtonBounds;
        Check(test.Right < area.X && test.Y >= area.Y && test.Bottom <= area.Bottom, "Transport controls must sit left of the timeline");
        Check(ui.View.TimeDisplayBounds.Bottom < test.Y, "Time display must sit above transport controls");
        var currentLabel = ui.Canvas.Texts.Single(t => t.X == 20 && t.Y == ui.Height - 110);
        var durationLabel = ui.Canvas.Texts.Single(t => t.Value.StartsWith("/ ") && t.X > currentLabel.X && t.X < 210);
        Check(Math.Abs(currentLabel.Y - durationLabel.Y) <= 4, "Time display should use one line");
        float durationX = durationLabel.X;
        ui.View.UpdateTransport(1888, 5000, true, false, false, null, map.AudioPath); ui.Paint();
        Check(ui.Canvas.Texts.Any(t => t.Value.StartsWith("/ ") && t.X == durationX),
            "Total duration shifted when the current timestamp changed");
        Check(!ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("testplay.start")), "Old Testplay button remains above the timeline");
        int toggles = 0, pauses = 0; double? seek = null;
        ui.View.RequestTogglePlayback = () => toggles++;
        ui.View.RequestPausePlayback = () => pauses++;
        ui.View.RequestSeek = time => seek = time;
        float y = test.Y + test.Height / 2;
        ui.Click(test.X - 147 + 21, y);
        Check(toggles == 1, "Play control did not start playback");
        ui.View.UpdateTransport(1200, 5000, true, true, false, null, map.AudioPath); ui.Paint();
        ui.Click(test.X - 98 + 21, y);
        Check(pauses == 1, "Pause control did not pause playback");
        ui.Click(test.X - 49 + 21, y);
        Check(pauses == 2 && seek == 0 && ui.View.PlayheadMs == 0, "Stop control did not pause and seek to the start");
        ui.View.UpdateTransport(0, 5000, true, false, false, null, map.AudioPath); ui.Paint();
        ui.Click(test.X + 21, y);
        Check(ui.View.IsTestplaying, "Test control did not enter testplay");
        ui.View.StopTestplay();
    }

    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Effects = 1 });
        map.TimingPoints.Add(new() { TimeMs = 500, BeatLengthMs = -100, Uninherited = false, Effects = 1 });
        map.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, Effects = 0 });
        map.Fruits.Add(new() { TimeMs = 750, X = 100 });
        OsuTimeline.AddBreak(map, 1500, 2000);
        OsuTimeline.ToggleBookmark(map, 2500);
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        var area = ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xEA2222 && l.Y1 == area.Y + 2), "Red timing point missing");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x7BC600 && l.Y1 == area.Y + 2), "Green timing point missing");
        float X(double time) => area.X + (float)(time / ui.View.TimelineDurationMs) * area.Width;
        var kiai = ui.Canvas.Fills.Where(f => f.Color == 0xD89532).ToArray();
        Check(kiai.Length == 1 && Math.Abs(kiai[0].Bounds.X - X(0)) < .01f
            && Math.Abs(kiai[0].Bounds.Right - X(1000)) < .01f
            && kiai[0].Bounds.Y < area.Y + 20 && kiai[0].Bounds.Bottom > area.Y + 20,
            "Kiai must span the complete timing interval across notes and timing points");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xBCB1AE && f.Bounds.Y < area.Y + 20 && f.Bounds.Bottom > area.Y + 20),
            "Break span must straddle the timeline");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x4B9EF5 && l.Y1 == area.Y + 20), "Bookmark must join the timeline");
        var calls = ui.Canvas.PaintCalls;
        int red = calls.FindIndex(c => c.Line is { Color: 0xEA2222, Y1: var y } && y == area.Y + 2);
        int green = calls.FindIndex(c => c.Line is { Color: 0x7BC600, Y1: var y } && y == area.Y + 2);
        int bookmark = calls.FindIndex(c => c.Line is { Color: 0x4B9EF5, Y1: var y } && y == area.Y + 20);
        int center = calls.FindIndex(c => c.Line is { Color: 0xA0A0A0, Y1: var y, Y2: var y2 } && y == area.Y + 20 && y2 == y);
        int kiaiFill = calls.FindIndex(c => c.Color == 0xD89532 && c.FillBounds is not null);
        int breakFill = calls.FindIndex(c => c.Color == 0xBCB1AE && c.FillBounds is { Y: var y } && y < area.Y + 20);
        Check(red >= 0 && green >= 0 && bookmark >= 0 && center >= 0 && kiaiFill >= 0 && breakFill >= 0
            && center < kiaiFill && center < breakFill && kiaiFill < red && breakFill < red
            && kiaiFill < green && breakFill < green && kiaiFill < bookmark && breakFill < bookmark,
            "Timeline layers are out of order");
        Check(new[] { red, green, bookmark, center, kiaiFill, breakFill }.All(i => calls[i].Opacity == .8f),
            "Timeline markers must use 80% opacity");
        ui.View.PointerDown(X(2500), area.Y + 34, 0, false, true);
        ui.View.PointerUp(X(2500), area.Y + 34, 0);
        Check(OsuTimeline.Bookmarks(ui.View.Document).Count == 0, "Ctrl-click did not remove bookmark");
        ui.View.PointerDown(X(3000), area.Y + 25, 0, true, false);
        ui.View.PointerMove(X(3500), area.Y + 25, true, false);
        ui.View.PointerUp(X(3500), area.Y + 25, 0);
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 2, "Shift-drag did not add break");
        ui.View.PointerDown(X(1700), area.Y + 25, 2, false, false);
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 1, "Right-click did not remove break");
        ui.Key(90, ctrl: true);
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 2, "Break removal did not undo");
        int bookmarkCount = OsuTimeline.Bookmarks(ui.View.Document).Count;
        ui.Key(66, ctrl: true);
        Check(OsuTimeline.Bookmarks(ui.View.Document).Count == bookmarkCount + 1, "Ctrl+B did not add a bookmark");
        ui.Key(66, ctrl: true, shift: true);
        Check(OsuTimeline.Bookmarks(ui.View.Document).Count == bookmarkCount, "Ctrl+Shift+B did not remove the nearest bookmark");
        Toolbar();

        void Toolbar()
        {
            ui.View.PointerMove(area.X + 20, area.Y + 10, false, false); ui.Paint();
            var first = ui.Canvas.Images.Single(i => i.Path.EndsWith("toolbar-panel.png", StringComparison.Ordinal)).Bounds;
            Check(first.Bottom <= area.Y - 4, "Bookmark toolbar overlaps the editable timeline");
            Check(Math.Abs(first.X - (area.X + 8)) < .01f,
                "Bookmark toolbar is not left aligned above the timeline");
            Check(ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("timeline.bookmark.resetLabel")
                && t.Y == first.Y + 8), "Reset label is not vertically aligned with its icon");
            Check(ui.Canvas.Fills.Where(f => f.Color is 0x19232D or 0x405065)
                .All(f => f.Bounds.Y >= first.Y + 5 && f.Bounds.Bottom <= first.Bottom - 5),
                "Bookmark button backgrounds protrude past the panel");
            Check(File.Exists(ui.Canvas.Images.Single(i => i.Bounds == first).Path), "Generated toolbar asset was not packaged");
            ui.View.PointerMove(first.X + 21, first.Y + 17, false, false); ui.Paint();
            Check(ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("timeline.bookmark.addHint")), "Bookmark tooltip missing");
            ui.View.PointerMove(area.Right - 10, area.Y + 10, false, false); ui.Paint();
            var second = ui.Canvas.Images.Single(i => i.Path.EndsWith("toolbar-panel.png", StringComparison.Ordinal)).Bounds;
            Check(first == second, "Bookmark toolbar followed the mouse");
            ui.View.PointerDown(area.Right - 10, area.Y + 10, 0, false, false); ui.Paint();
            Check(ui.Canvas.Images.Any(i => i.Path.EndsWith("toolbar-panel.png", StringComparison.Ordinal)),
                "Bookmark toolbar disappeared while holding the timeline");
            ui.View.PointerUp(area.Right - 10, area.Y + 10, 0); ui.Paint();
            int currentBookmark = (int)Math.Round(ui.View.PlayheadMs);
            void Press(float x)
            {
                ui.View.PointerDown(x, first.Y + 17, 0, false, false);
                ui.View.PointerUp(x, first.Y + 17, 0);
                ui.Paint();
            }
            Press(first.X + 21);
            Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(currentBookmark), "Add button failed");
            Press(first.X + 57);
            Check(!OsuTimeline.Bookmarks(ui.View.Document).Contains(currentBookmark), "Remove button failed");
            OsuTimeline.AddBookmark(ui.View.Document, 1000);
            OsuTimeline.AddBookmark(ui.View.Document, 2500);
            ui.Click(X(1500), area.Y + 20);
            ui.View.PointerMove(first.X + 93, first.Y + 17, false, false); ui.Paint();
            Press(first.X + 93);
            Check(ui.View.PlayheadMs == 1000, "Previous bookmark button failed");
            Press(first.X + 129);
            Check(ui.View.PlayheadMs == 2500, "Next bookmark button failed");
            ui.Click(X(1500), area.Y + 20);
            ui.Key(37, ctrl: true);
            Check(ui.View.PlayheadMs == 1000, "Ctrl+Left did not seek previous bookmark");
            ui.Key(39, ctrl: true);
            Check(ui.View.PlayheadMs == 2500, "Ctrl+Right did not seek next bookmark");
            Press(first.X + 195);
            Check(OsuTimeline.Bookmarks(ui.View.Document).Count == 0, "Reset button failed");
            ui.Key(90, ctrl: true);
            Check(OsuTimeline.Bookmarks(ui.View.Document).Count == 2, "Reset did not undo");
            ui.View.PointerMove(area.X + 20, area.Y - 45, false, false); ui.Paint();
            Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("toolbar-panel.png", StringComparison.Ordinal)), "Toolbar stayed visible after mouse exit");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
