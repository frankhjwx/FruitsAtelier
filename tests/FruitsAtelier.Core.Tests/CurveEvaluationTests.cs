using FruitsAtelier.Core;

internal static class CurveEvaluationTests
{
    public static void SnapshotEquality()
    {
        var map = new MapDocument();
        for (int i = 0; i < 600; i++)
        {
            var curve = new ControlCurve { Kind = ControlCurveKind.Bezier };
            curve.Controls.Add(new() { Offset = new(200, 50) });
            var track = new CurveTrack();
            track.Nodes.AddRange([new() { TimeMs = i * 500, X = 100, OutgoingCurve = curve }, new() { TimeMs = i * 500 + 300, X = 200 }]);
            track.StreamNewComboOffsets.Add(100);
            map.Tracks.Add(track);
        }
        var snapshot = map.DeepClone();
        for (int i = 0; i < 10; i++) _ = map.ContentEquals(snapshot);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            if (!map.ContentEquals(snapshot)) throw new Exception("Equal exact-curve snapshots differ.");
        if (GC.GetAllocatedBytesForCurrentThread() - bytes > 64 * 1024)
            throw new Exception("Unchanged curve comparisons allocate per track or control.");
        snapshot.Tracks[599].Nodes[0].OutgoingCurve!.Controls[0].Offset = new(201, 50);
        if (map.ContentEquals(snapshot)) throw new Exception("A control edit did not invalidate the snapshot.");
        snapshot = map.DeepClone(); snapshot.Tracks[599].StreamNewComboOffsets[0] = 101;
        if (map.ContentEquals(snapshot)) throw new Exception("An internal NC edit did not invalidate the snapshot.");
    }

    public static void Run()
    {
        foreach (var kind in new[] { ControlCurveKind.Bezier, ControlCurveKind.CircularArc })
        foreach (bool straight in new[] { false, true })
        {
            var track = new CurveTrack { SpanCount = 3 };
            var curve = new ControlCurve { Kind = kind, ReferenceScale = .6 };
            curve.Controls.Add(new() { Offset = new(500, straight ? 50 : 150) });
            track.Nodes.AddRange([new() { TimeMs = 1000, X = 100, OutgoingCurve = curve }, new() { TimeMs = 2000, X = 200 }]);
            for (double time = 1001; time < 4000; time += 17.25)
            {
                double target = CurveMath.FirstSpanTime(track, time);
                double low = 0, high = 1;
                for (int i = 0; i < 60; i++)
                {
                    double middle = (low + high) / 2;
                    if (CurveMath.Evaluate(track, 0, middle).TimeMs < target) low = middle; else high = middle;
                }
                double expected = CurveMath.Evaluate(track, 0, (low + high) / 2).X;
                if (CurveMath.PositionAtTime(track, time) != expected)
                    throw new Exception("Prepared control evaluation must exactly match independent point-by-point evaluation.");
            }
        }
    }
}
