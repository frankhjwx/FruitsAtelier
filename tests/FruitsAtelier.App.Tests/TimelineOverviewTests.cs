using FruitsAtelier.Core;

internal static class TimelineOverviewTests
{
    public static void Run()
    {
        var map = new MapDocument { DurationMs = 5000, IsDemo = false };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500, Effects = 1 });
        map.TimingPoints.Add(new() { TimeMs = 1000, BeatLengthMs = -100, Uninherited = false, Effects = 0 });
        OsuTimeline.AddBreak(map, 1500, 2000);
        OsuTimeline.ToggleBookmark(map, 2500);
        var ui = new Ui(overview: false);
        ui.LoadDocument(map);
        var area = ui.Canvas.Fills.Single(f => f.Color == 0x141922).Bounds;
        Check(ui.Canvas.Lines.Any(l => l.Color == 0xEC4545 && l.Y1 == area.Y + 2), "Red timing point missing");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x73B92F && l.Y1 == area.Y + 2), "Green timing point missing");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xD7AE42 && f.Bounds.Y == area.Y + 21), "Kiai span missing");
        Check(ui.Canvas.Fills.Any(f => f.Color == 0xF2F4F7 && f.Bounds.Y == area.Y + 21), "Break span missing");
        Check(ui.Canvas.Lines.Any(l => l.Color == 0x4B9EF5 && l.Y1 == area.Y + 31), "Bookmark missing");
        float X(double time) => area.X + (float)(time / ui.View.TimelineDurationMs) * area.Width;
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
