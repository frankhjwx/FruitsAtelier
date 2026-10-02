using FruitsAtelier.Core;

internal static class CurveEvaluationTests
{
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
