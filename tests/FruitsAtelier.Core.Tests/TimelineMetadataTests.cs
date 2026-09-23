using FruitsAtelier.Core;

internal static class TimelineMetadataTests
{
    public static void Run()
    {
        const string source = "osu file format v14\n[General]\nMode:2\nPreviewTime:1500\n[Editor]\nBookmarks: 100, 300\n[Events]\n//Break Periods\n2,1000,2000\n0,0,background.jpg\n[TimingPoints]\n0,500,4,1,0,100,1,1\n[HitObjects]\n256,192,100,1,0,0:0:0:0:\n";
        var document = OsuBeatmapReader.Read(source);
        Check(OsuTimeline.PreviewTime(document) == 1500, "Preview point was not read");
        Check(OsuTimeline.Bookmarks(document).SequenceEqual([100, 300]), "Bookmarks were not read");
        Check(OsuTimeline.Breaks(document).SequenceEqual([new BreakPeriod(1000, 2000)]), "Break was not read");
        var history = new EditorHistory(document);
        history.Begin("Timeline");
        OsuTimeline.ToggleBookmark(history.Document, 300);
        OsuTimeline.ToggleBookmark(history.Document, 500);
        OsuTimeline.AddBreak(history.Document, 3000, 4000);
        Check(OsuTimeline.RemoveBreak(history.Document, new(1000, 2000)), "Break removal failed");
        history.Commit();
        Check(OsuTimeline.Bookmarks(history.Document).SequenceEqual([100, 500]), "Bookmark edit failed");
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(3000, 4000)]), "Break edit failed");
        Check(history.Document.OriginalSections.Single(s => s.Name == "Events").Lines.Contains("0,0,background.jpg"), "Unrelated event changed");
        history.Undo();
        Check(OsuTimeline.Bookmarks(history.Document).SequenceEqual([100, 300]) && OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(1000, 2000)]), "Timeline undo failed");
        history.Redo();
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
        Check(OsuTimeline.PreviewTime(restored) == 1500, "Preview point was not preserved in the project");
        Check(OsuTimeline.Bookmarks(restored).SequenceEqual([100, 500]) && OsuTimeline.Breaks(restored).SequenceEqual([new BreakPeriod(3000, 4000)]), "Timeline project round trip failed");
        var exported = OsuBeatmapWriter.Serialize(restored).ReadBack;
        Check(OsuTimeline.PreviewTime(exported) == 1500, "Preview point was not preserved in osu export");
        Check(OsuTimeline.Bookmarks(exported).SequenceEqual([100, 500]) && OsuTimeline.Breaks(exported).SequenceEqual([new BreakPeriod(3000, 4000)]), "Timeline osu export round trip failed");
        OsuTimeline.ClearBookmarks(exported);
        Check(OsuTimeline.Bookmarks(exported).Count == 0 && OsuBeatmapReader.Setting(exported, "Editor", "Bookmarks") is null,
            "Reset bookmarks did not remove the Editor setting");
        var events = exported.OriginalSections.Single(s => s.Name == "Events");
        int breakLine = events.Lines.ToList().FindIndex(line => line.StartsWith("2,3000,4000", StringComparison.Ordinal));
        events.Lines[breakLine] += " // keep this comment";
        Check(OsuTimeline.ReplaceBreak(exported, new(3000, 4000), new(3200, 3800))
            && OsuTimeline.Breaks(exported).SequenceEqual([new BreakPeriod(3200, 3800)])
            && events.Lines[breakLine].EndsWith("// keep this comment", StringComparison.Ordinal),
            "Resizing a break must preserve its Events line and comment");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
