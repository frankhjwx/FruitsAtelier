using FruitsAtelier.Core;

internal static class BreakRecalculationTests
{
    public static void Placement()
    {
        var map = Map();
        var ui = new Ui(); ui.LoadDocument(map);
        ui.Key('2'); ui.ClickMap(3000, 240);
        Check(OsuTimeline.Breaks(ui.View.Document).SequenceEqual([new BreakPeriod(1200, 2100), new BreakPeriod(3200, 6000)]),
            "A placed note did not split its break with AR lead-in and 200 ms recovery.");
        Check(OsuTimeline.Breaks(OsuBeatmapWriter.Serialize(ui.View.Document).ReadBack).SequenceEqual(OsuTimeline.Breaks(ui.View.Document)),
            "Export retained an obsolete break.");
        Check(ui.View.Document.OriginalSections.SelectMany(s => s.Lines).Contains("0,0,\"bg.jpg\",0,0"), "Break update changed background data.");
        ui.Key('Z', ctrl: true);
        Check(map.ContentEquals(ui.View.Document), "Undo failed to restore note and break together.");
        ui.Key('Y', ctrl: true);
        Check(OsuTimeline.Breaks(ui.View.Document).Count == 2, "Redo lost the split break.");
    }

    public static void DurationAndCancellation()
    {
        var map = Map();
        var history = new EditorHistory(map);
        history.Begin("Add repeated slider");
        var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 3 };
        track.Nodes.AddRange([new Anchor { TimeMs = 3000, X = 200 }, new Anchor { TimeMs = 4000, X = 200 }]);
        history.Document.Tracks.Add(track);
        history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(1200, 2100)]),
            "Break overlaps a repeated slider's duration.");
        history.Undo(); Check(map.ContentEquals(history.Document), "Repeated slider/break undo failed.");
        history.Begin("Cancelled placement"); history.Document.Fruits.Add(new() { TimeMs = 3000, X = 100 }); history.Cancel();
        Check(map.ContentEquals(history.Document), "Cancelled placement changed breaks.");
        history.Begin("Horizontal edit"); history.Document.Fruits[0].X = 400; history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual(OsuTimeline.Breaks(map)), "Horizontal editing rewrote a break.");
        history.Undo();
        history.Begin("Move into break"); history.Document.Fruits[0].TimeMs = 3000; history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(1200, 2100), new BreakPeriod(3200, 6000)]),
            "Moving an existing note into a break did not update it.");
        history.Undo();
        history.Begin("Short remnants"); history.Document.Fruits.Add(new() { TimeMs = 2000, X = 100 });
        history.Document.Fruits.Add(new() { TimeMs = 5700, X = 100 }); history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(2200, 4800)]), "Sub-400 ms break fragments remained.");
    }

    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 8000, ApproachRate = 7 };
        map.Fruits.AddRange([new Fruit { TimeMs = 1000, X = 200 }, new Fruit { TimeMs = 7000, X = 200 }]);
        OsuTimeline.AddBreak(map, 1200, 6000);
        map.OriginalSections.Single().Lines.Add("0,0,\"bg.jpg\",0,0");
        return map;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
