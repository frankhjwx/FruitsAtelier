using FruitsAtelier.Core;

internal static class TimelineMetadataTests
{
    public static void Run()
    {
        DeletedSectionBreak();
        CachedBreakIntervals();
        FractionalBreakBoundary();
        UnboundedBreaks();
        ApproachAndOverlappingDurations();
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

    private static void FractionalBreakBoundary()
    {
        var map = new MapDocument { DurationMs = 260000, ApproachRate = 9.8 };
        map.Fruits.AddRange([new() { TimeMs = 230000, X = 200 }, new() { TimeMs = 257090.35278514592, X = 200 }]);
        OsuTimeline.AddBreak(map, 240000, 256536);
        var history = new EditorHistory(map);
        history.Begin("Add fractional slider");
        var track = new CurveTrack { Kind = CurveKind.Linear };
        track.Nodes.AddRange([new() { TimeMs = 236719, X = 100 }, new() { TimeMs = 246904.67639257296, X = 400 }]);
        history.Document.Tracks.Add(track);
        history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(247104, 256536)]),
            "Fractional slider recovery must use the stable integer boundary.");
        history.Undo(); Check(history.Document.ContentEquals(map), "Undo must restore the break and slider together.");
        history.Redo();
        Check(OsuTimeline.Breaks(history.Document).Single().StartMs == 247104, "Redo lost the fractional boundary.");
        OsuTimeline.ReplaceBreak(history.Document, new(247104, 256536), new(247105, 256536));
        history.Begin("Remove slider beside legacy break");
        history.Document.Tracks.Clear(); history.Commit();
        Check(OsuTimeline.Breaks(history.Document).Single().StartMs == 230200,
            "Removing a slider must also extend a legacy ceil-rounded break.");
    }

    private static void CachedBreakIntervals()
    {
        var map = new MapDocument { DurationMs = 12000 };
        var slider = new ImportedSlider { TimeMs = 1000, X = 100, Y = 192, PathType = 'L', PixelLength = 200 };
        slider.ControlPoints.AddRange([new(100, 192), new(300, 192)]);
        map.ImportedSliders.Add(slider);
        OsuTimeline.AddBreak(map, 2000, 9000);
        var history = new EditorHistory(map);
        Compare(d => d.Fruits.Add(new() { TimeMs = 100, X = 80 }));
        Compare(d => d.ImportedSliders[0].SpanCount = 4);
        Compare(d => d.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 800 }));
        Compare(d => { d.ImportedSliders[0].PixelLength = 400; d.ImportedSliders[0].ControlPoints[1] = new(500, 100); });
        history.Undo();
        Compare(d => d.ImportedSliders[0].TimeMs = 1500);

        void Compare(Action<MapDocument> change)
        {
            var baseline = history.Document.DeepClone();
            var fresh = new EditorHistory(baseline);
            history.Begin("Edit"); fresh.Begin("Edit");
            change(history.Document);
            // Share authored values and IDs while keeping the fresh history's duration cache empty.
            var edited = history.Document.DeepClone();
            fresh.Document.Fruits.Clear(); fresh.Document.Fruits.AddRange(edited.Fruits);
            fresh.Document.ImportedSliders.Clear(); fresh.Document.ImportedSliders.AddRange(edited.ImportedSliders);
            fresh.Document.TimingPoints.Clear(); fresh.Document.TimingPoints.AddRange(edited.TimingPoints);
            history.Commit(); fresh.Commit();
            Check(history.Document.ContentEquals(fresh.Document), "Warm break interval cache changed reconciliation after an edit or undo.");
        }
    }

    private static void DeletedSectionBreak()
    {
        var map = new MapDocument { DurationMs = 20000, ApproachRate = 9 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 5000, X = 200 }, new() { TimeMs = 10000, X = 300 }]);
        var history = new EditorHistory(map);
        history.Begin("Delete middle section"); history.Document.Fruits.RemoveAt(1); history.Commit();
        var expected = new BreakPeriod(1200, 10000 - (int)CatchScrollTiming.PreemptMs(map.ApproachRate));
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([expected]), "Deleting a section did not create its newly unoccupied break.");
        history.Undo(); Check(history.Document.ContentEquals(map), "Undo did not restore the section and break together.");
        history.Redo(); Check(OsuTimeline.Breaks(history.Document).SequenceEqual([expected]), "Redo lost the new break.");
        var restored = ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document));
        Check(OsuTimeline.Breaks(restored).SequenceEqual([expected]), "Gap break did not survive project persistence.");
        var output = OsuBeatmapWriter.Serialize(history.Document);
        Check(OsuTimeline.Breaks(output.ReadBack).SequenceEqual([expected]), "Gap break did not survive osu export.");
        var overlapping = map.DeepClone();
        overlapping.BananaShowers.Add(new() { TimeMs = 2000, EndTimeMs = 11000 });
        history = new EditorHistory(overlapping);
        history.Begin("Delete inside an occupied shower"); history.Document.Fruits.RemoveAt(1); history.Commit();
        Check(OsuTimeline.Breaks(history.Document).Count == 0, "Deletion created a break across an occupied interval.");
        history = new EditorHistory(map);
        history.Begin("Delete final object"); history.Document.Fruits.RemoveAt(2); history.Commit();
        Check(OsuTimeline.Breaks(history.Document).Count == 0, "Deletion created an unbounded trailing break.");
    }

    private static void UnboundedBreaks()
    {
        foreach (bool leading in new[] { false, true })
        foreach (bool resized in new[] { false, true })
        foreach (bool move in new[] { false, true })
        {
            var map = new MapDocument { DurationMs = 20000, ApproachRate = 7 };
            map.Fruits.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 11000, X = 300 }]);
            OsuTimeline.AddBreak(map, resized ? 2000 : 1200, resized ? 9000 : 10100);
            map.OriginalSections.Single().Lines.Add("// retained event comment");
            var history = new EditorHistory(map);
            history.Begin("Edit outer object");
            int index = leading ? 0 : 1;
            if (move) history.Document.Fruits[index].TimeMs = leading ? 12000 : 500;
            else history.Document.Fruits.RemoveAt(index);
            history.Commit();
            Check(OsuTimeline.Breaks(history.Document).Count == 0, "An unbounded break survived an outer-object edit.");
            Check(history.Document.OriginalSections.Single().Lines.Contains("// retained event comment"), "Break cleanup changed unrelated Events data.");
            Check(OsuTimeline.Breaks(ProjectSerializer.Read(ProjectSerializer.Serialize(history.Document))).Count == 0,
                "Project persistence restored an unbounded break.");
            Check(OsuTimeline.Breaks(OsuBeatmapWriter.Serialize(history.Document).ReadBack).Count == 0,
                "Export restored an unbounded break.");
            history.Undo(); Check(map.ContentEquals(history.Document), "Outer-object edit and break cleanup did not undo together.");
            history.Redo(); Check(OsuTimeline.Breaks(history.Document).Count == 0, "Redo restored an unbounded break.");
        }
        var empty = new MapDocument();
        empty.Fruits.AddRange([new() { TimeMs = 1000 }, new() { TimeMs = 11000 }]);
        OsuTimeline.AddBreak(empty, 1200, 9000);
        var cleared = new EditorHistory(empty);
        cleared.Begin("Clear objects"); cleared.Document.Fruits.Clear(); cleared.Commit();
        Check(OsuTimeline.Breaks(cleared.Document).Count == 0, "Clearing all objects retained a break.");
        foreach (int kind in new[] { 0, 1, 2 })
        {
            var map = new MapDocument { DurationMs = 20000, ApproachRate = 7 };
            map.Fruits.Add(new() { TimeMs = 1000 });
            if (kind == 0)
            {
                var track = new CurveTrack { Kind = CurveKind.Linear, SpanCount = 3 };
                track.Nodes.AddRange([new() { TimeMs = 11000, X = 100 }, new() { TimeMs = 12000, X = 200 }]);
                map.Tracks.Add(track);
            }
            else if (kind == 1)
            {
                var slider = new ImportedSlider { TimeMs = 11000, X = 100, Y = 192, PathType = 'L', PixelLength = 100, SpanCount = 3 };
                slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
                map.ImportedSliders.Add(slider);
            }
            else map.BananaShowers.Add(new() { TimeMs = 11000, EndTimeMs = 14000 });
            OsuTimeline.AddBreak(map, 1200, 10100);
            var history = new EditorHistory(map);
            history.Begin("Remove final duration object");
            history.Document.Tracks.Clear(); history.Document.ImportedSliders.Clear(); history.Document.BananaShowers.Clear();
            history.Commit();
            Check(OsuTimeline.Breaks(history.Document).Count == 0, "Removing the final duration object retained a break.");
            history.Undo(); Check(map.ContentEquals(history.Document), "Duration object and break did not undo together.");
        }
    }

    private static void ApproachAndOverlappingDurations()
    {
        var map = new MapDocument { DurationMs = 20000, ApproachRate = 7 };
        map.Fruits.AddRange([new() { TimeMs = 1000 }, new() { TimeMs = 7000 }]);
        OsuTimeline.AddBreak(map, 1200, 6100);
        var history = new EditorHistory(map);
        history.Begin("Change approach rate"); history.Document.ApproachRate = 0; history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(1200, 5200)]),
            "AR change left a break inside the next object's approach interval.");
        history.Undo(); Check(map.ContentEquals(history.Document), "AR and break did not undo together.");
        history.Redo(); Check(OsuTimeline.Breaks(history.Document).Single().EndMs == 5200, "Redo lost the AR break adjustment.");
        map = new MapDocument { DurationMs = 5000, ApproachRate = 10 };
        map.Fruits.AddRange([new() { TimeMs = 1000 }, new() { TimeMs = 3000 }]);
        OsuTimeline.AddBreak(map, 1200, 2550);
        history = new EditorHistory(map);
        history.Begin("Lengthen approach beyond break"); history.Document.ApproachRate = 0; history.Commit();
        Check(OsuTimeline.Breaks(history.Document).Count == 0, "AR change retained a break with no usable clearance.");

        map = new MapDocument { DurationMs = 20000, ApproachRate = 7 };
        map.BananaShowers.Add(new() { TimeMs = 1000, EndTimeMs = 4000 });
        map.Fruits.AddRange([new() { TimeMs = 2000 }, new() { TimeMs = 5000 }, new() { TimeMs = 15000 }]);
        OsuTimeline.AddBreak(map, 5200, 14000);
        history = new EditorHistory(map);
        history.Begin("Delete after overlapping objects"); history.Document.Fruits.RemoveAt(1); history.Commit();
        Check(OsuTimeline.Breaks(history.Document).SequenceEqual([new BreakPeriod(4200, 14000)]),
            "Break extension did not use the maximum preceding object end.");
        history.Undo(); Check(map.ContentEquals(history.Document), "Overlapping duration and break did not undo together.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
