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
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
