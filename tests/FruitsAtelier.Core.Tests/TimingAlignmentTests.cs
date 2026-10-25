using FruitsAtelier.Core;

internal static class TimingAlignmentTests
{
    public static void Run()
    {
        Spinners();
        var map = new MapDocument { BeatLengthMs = 254.237288135593, DurationMs = 50000 };
        map.TimingPoints.AddRange([new() { TimeMs = 670, BeatLengthMs = map.BeatLengthMs },
            new() { TimeMs = 12873, BeatLengthMs = map.BeatLengthMs },
            new() { TimeMs = 38297, BeatLengthMs = -100, Uninherited = false }]);
        map.Fruits.Add(new() { TimeMs = 33212, X = 215 });
        var before = map.DeepClone();
        var plan = TimingAlignment.Analyze(map);
        Check(plan.ObjectCount == 1 && plan.TimingPointCount == 1 && map.ContentEquals(before), "Detection is read-only and counts both issue types");
        var history = new EditorHistory(map);
        history.Begin("align"); TimingAlignment.Apply(history.Document, plan); history.Commit();
        Check(history.Document.Fruits[0].TimeMs == 33211 && history.Document.TimingPoints[2].TimeMs == 38296 && history.IsDirty,
            "Alignment changes object and timing data in one transaction");
        Check(!TimingAlignment.Analyze(history.Document).HasChanges, "Alignment is idempotent");
        history.Undo(); Check(history.Document.ContentEquals(before) && !history.IsDirty, "Undo restores the original data");
        history.Redo(); Check(history.Document.Fruits[0].TimeMs == 33211, "Redo restores alignment");

        var parents = new MapDocument { BeatLengthMs = 500, DurationMs = 10000 };
        parents.Fruits.Add(new() { TimeMs = 1001 });
        parents.ImportedSliders.Add(new() { TimeMs = 2001, X = 100, Y = 192, PixelLength = 100, PathType = 'L' });
        parents.ImportedSliders[0].ControlPoints.AddRange([new(100, 192), new(200, 192)]);
        var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 3001, X = 100 }, new() { TimeMs = 3501, X = 200 }]);
        parents.Tracks.Add(track); parents.BananaShowers.Add(new() { TimeMs = 4001, EndTimeMs = 4501 });
        plan = TimingAlignment.Analyze(parents);
        Check(plan.ObjectCount == 4, "Every parent type is inspected");
        TimingAlignment.Apply(parents, plan);
        Check(parents.Fruits[0].TimeMs == 1000 && parents.ImportedSliders[0].TimeMs == 2000
            && track.Nodes[0].TimeMs == 3000 && track.Nodes[1].TimeMs == 3500
            && parents.BananaShowers[0].TimeMs == 4000 && parents.BananaShowers[0].EndTimeMs == 4500,
            "Moving a parent preserves its shape and duration");

        var reds = new MapDocument();
        reds.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 }, new() { TimeMs = 1001, BeatLengthMs = 500 },
            new() { TimeMs = 2001, BeatLengthMs = 500 }, new() { TimeMs = 1501, BeatLengthMs = -100, Uninherited = false }]);
        plan = TimingAlignment.Analyze(reds); Check(plan.TimingPointCount == 3, "Red and green lines use the corrected preceding grid");
        TimingAlignment.Apply(reds, plan);
        Check(reds.TimingPoints.Select(p => p.TimeMs).SequenceEqual(new[] { 0d, 1000, 2000, 1500 })
            && !TimingAlignment.Analyze(reds).HasChanges, "Red-line chains align without accumulating drift");
        reds.TimingPoints.Clear();
        reds.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
            new() { TimeMs = 1001, BeatLengthMs = 500 }, new() { TimeMs = 1001, BeatLengthMs = 250 },
            new() { TimeMs = 2001, BeatLengthMs = 500 }]);
        plan = TimingAlignment.Analyze(reds); TimingAlignment.Apply(reds, plan);
        Check(reds.TimingPoints[1].TimeMs == 1001 && reds.TimingPoints[2].TimeMs == 1001
            && reds.TimingPoints[3].TimeMs == 2001, "Duplicate groups retain their timing precedence");
    }
    private static void Spinners()
    {
        foreach (double start in new[] { 386d, 387d })
        {
            var map = new MapDocument { DurationMs = 1386 };
            map.TimingPoints.Add(new() { TimeMs = 387, BeatLengthMs = 500 });
            map.BananaShowers.Add(new() { TimeMs = start, EndTimeMs = 1386 });
            var before = map.DeepClone();
            var plan = TimingAlignment.Analyze(map);
            Check(plan.ObjectCount == 1 && plan.TimingPointCount == 0 && map.ContentEquals(before),
                "Spinner head/tail detection is read-only and counts each spinner once");
            var history = new EditorHistory(map);
            history.Begin("align"); TimingAlignment.Apply(history.Document, plan); history.Commit();
            Check(history.Document.BananaShowers[0].TimeMs == 387 && history.Document.BananaShowers[0].EndTimeMs == 1387
                && history.Document.DurationMs >= 1387 && !TimingAlignment.Analyze(history.Document).HasChanges,
                "Spinner head before the first red line and tail align without repeated prompts");
            history.Undo(); Check(history.Document.ContentEquals(before), "Spinner alignment undo restores both edges and duration");
            history.Redo(); Check(history.Document.BananaShowers[0].EndTimeMs == 1387, "Spinner alignment redo restores the correction");
        }
        var shared = new MapDocument();
        shared.TimingPoints.Add(new() { TimeMs = 387, BeatLengthMs = 500 });
        shared.Fruits.Add(new() { TimeMs = 386 });
        shared.BananaShowers.Add(new() { TimeMs = 386, EndTimeMs = 1387 });
        var correction = TimingAlignment.Analyze(shared);
        TimingAlignment.Apply(shared, correction);
        Check(correction.ObjectCount == 1 && shared.Fruits[0].TimeMs == 386 && shared.BananaShowers[0].TimeMs == 387,
            "The first-red-line spinner exception does not move a simultaneous pre-grid circle");
        shared.TimingPoints.Clear(); shared.Fruits[0].TimeMs = 999;
        shared.DurationMs = 1000;
        shared.BananaShowers[0].TimeMs = 999; shared.BananaShowers[0].EndTimeMs = 1000;
        correction = TimingAlignment.Analyze(shared); TimingAlignment.Apply(shared, correction);
        Check(shared.Fruits[0].TimeMs == 1000 && shared.BananaShowers[0].TimeMs == 999 && shared.BananaShowers[0].EndTimeMs == 1000 && shared.DurationMs == 1000,
            "Another parent sharing the same timestamp cannot collapse a spinner");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
