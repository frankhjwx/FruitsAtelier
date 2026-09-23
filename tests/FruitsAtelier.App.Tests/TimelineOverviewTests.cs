using FruitsAtelier.Core;

internal static class TimelineOverviewTests
{
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
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xEC4545 && l.Y1 == area.Y + 2), "Red timing point missing");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x73B92F && l.Y1 == area.Y + 2), "Green timing point missing");
        float X(double time) => area.X + (float)(time / ui.View.TimelineDurationMs) * area.Width;
        var kiai = ui.Canvas.Fills.Where(f => f.Color == 0xD7AE42).ToArray();
        Check(kiai.Length == 1 && Math.Abs(kiai[0].Bounds.X - X(0)) < .01f
            && Math.Abs(kiai[0].Bounds.Right - X(1000)) < .01f
            && kiai[0].Bounds.Y < area.Y + 20 && kiai[0].Bounds.Bottom > area.Y + 20,
            "Kiai must span the complete timing interval across notes and timing points");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xF2F4F7 && f.Bounds.Y < area.Y + 20 && f.Bounds.Bottom > area.Y + 20),
            "Break span must straddle the timeline");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x4B9EF5 && l.Y1 == area.Y + 20), "Bookmark must join the timeline");
        var calls = ui.Canvas.PaintCalls;
        int red = calls.FindIndex(c => c.Line is { Color: 0xEC4545, Y1: var y } && y == area.Y + 2);
        int green = calls.FindIndex(c => c.Line is { Color: 0x73B92F, Y1: var y } && y == area.Y + 2);
        int bookmark = calls.FindIndex(c => c.Line is { Color: 0x4B9EF5, Y1: var y } && y == area.Y + 20);
        int center = calls.FindIndex(c => c.Line is { Color: 0xF2F4F7, Y1: var y, Y2: var y2 } && y == area.Y + 20 && y2 == y);
        int kiaiFill = calls.FindIndex(c => c.Color == 0xD7AE42 && c.FillBounds is not null);
        int breakFill = calls.FindIndex(c => c.Color == 0xF2F4F7 && c.FillBounds is { Y: var y } && y < area.Y + 20);
        Check(red >= 0 && green >= 0 && bookmark >= 0 && center >= 0 && kiaiFill >= 0 && breakFill >= 0
            && center < kiaiFill && center < breakFill && kiaiFill < red && breakFill < red
            && kiaiFill < green && breakFill < green && kiaiFill < bookmark && breakFill < bookmark,
            "Timeline layers are out of order");
        Check(new[] { red, green, bookmark, center, kiaiFill, breakFill }.All(i => calls[i].Opacity == .5f),
            "Timeline markers must use 50% opacity");
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
            Check(File.Exists(ui.Canvas.Images.Single(i => i.Bounds == first).Path), "Generated toolbar asset was not packaged");
            Check(ui.Canvas.Texts.Any(t => t.Value == FruitsAtelier.Localization.Strings.Get("timeline.bookmark.addHint")), "Bookmark tooltip missing");
            ui.View.PointerMove(area.Right - 10, area.Y + 10, false, false); ui.Paint();
            var second = ui.Canvas.Images.Single(i => i.Path.EndsWith("toolbar-panel.png", StringComparison.Ordinal)).Bounds;
            Check(first == second, "Bookmark toolbar followed the mouse");
            void Press(float x)
            {
                ui.View.PointerDown(x, first.Y + 17, 0, false, false);
                ui.View.PointerUp(x, first.Y + 17, 0);
                ui.Paint();
            }
            Press(first.X + 21);
            Check(OsuTimeline.Bookmarks(ui.View.Document).Contains(0), "Add button failed");
            Press(first.X + 57);
            Check(!OsuTimeline.Bookmarks(ui.View.Document).Contains(0), "Remove button failed");
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
            ui.View.PointerMove(area.X + 20, area.Y - 10, false, false); ui.Paint();
            Check(!ui.Canvas.Images.Any(i => i.Path.EndsWith("toolbar-panel.png", StringComparison.Ordinal)), "Toolbar stayed visible after mouse exit");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
