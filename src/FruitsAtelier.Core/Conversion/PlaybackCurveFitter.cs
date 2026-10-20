using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

// Playback samples constrain positions at events; the polygon joining them is not authored geometry.
internal static class PlaybackCurveFitter
{
    public static void Fit(CurveTrack track, IReadOnlyList<MapPoint> points, double tolerance,
        CancellationToken cancellation)
    {
        if (points.Count < 2) throw new InvalidOperationException(L.Get("core.importEditing.zeroDuration"));
        var pending = new Stack<(int Start, int End)>();
        var segments = new List<(int Start, int End, ControlCurve? Curve)>();
        var boundaries = new[] { 0 }.Concat(Corners(points, tolerance)).Append(points.Count - 1).ToArray();
        for (int i = 0; i + 1 < boundaries.Length; i++) pending.Push((boundaries[i], boundaries[i + 1]));
        long work = 0;
        while (pending.TryPop(out var range))
        {
            cancellation.ThrowIfCancellationRequested();
            var a = points[range.Start]; var b = points[range.End];
            double duration = b.TimeMs - a.TimeMs;
            var samples = Enumerable.Range(range.Start, range.End - range.Start + 1)
                .Select(i => new MapPoint((points[i].TimeMs - a.TimeMs) / duration, points[i].X)).ToArray();
            var model = new CurveTrack { Kind = CurveKind.Linear };
            model.Nodes.Add(new() { TimeMs = 0, X = a.X });
            model.Nodes.Add(new() { TimeMs = 1, X = b.X });
            var best = Error(null);
            ControlCurve? chosen = null;
            if (best.Max > tolerance && samples.Length >= 3)
            {
                // A quadratic with a movable time control recovers asymmetric pen curves.
                double bestTime = .5, bestSquare = double.PositiveInfinity;
                Quadratic(.5);
                if (samples.Length > 3)
                    for (int i = 0; i <= 20; i++) Quadratic(i / 20d);
                double step = .025;
                for (int iteration = 0; samples.Length > 3 && iteration < 10; iteration++, step /= 2)
                {
                    double centre = bestTime;
                    Quadratic(Math.Max(0, centre - step));
                    Quadratic(Math.Min(1, centre + step));
                }

                void Quadratic(double time)
                {
                    double sum = 0, weight = 0;
                    foreach (var point in samples)
                    {
                        double u = Parameter(point.TimeMs, time), v = 1 - u, w = 2 * u * v;
                        sum += w * (point.X - v * v * a.X - u * u * b.X); weight += w * w;
                    }
                    if (weight < 1e-12) return;
                    var curve = new ControlCurve();
                    curve.Controls.Add(new() { Offset = new(time, sum / weight - a.X) });
                    var error = Error(curve);
                    if (error.Square < bestSquare) { bestSquare = error.Square; bestTime = time; }
                    if (error.Max < best.Max) { best = error; chosen = curve; }
                }
            }
            if (chosen is not null && samples.Length >= 4)
            {
                // An axis-aligned ellipse in time/X is a circle at its fitted reference scale.
                double aa = 0, ab = 0, bb = 0, ay = 0, by = 0;
                foreach (var p in samples)
                {
                    double q = p.X - (a.X + (b.X - a.X) * p.TimeMs), r = p.TimeMs * (p.TimeMs - 1);
                    double y = a.X * a.X + (b.X * b.X - a.X * a.X) * p.TimeMs - p.X * p.X;
                    aa += q * q; ab += q * r; bb += r * r; ay += q * y; by += r * y;
                }
                double determinant = aa * bb - ab * ab;
                if (determinant > 1e-12)
                {
                    double coefficient = (ay * bb - by * ab) / determinant;
                    double scaleSquared = (by * aa - ay * ab) / determinant;
                    if (scaleSquared > 1e-8)
                    {
                        double cx = -coefficient / 2;
                        double cy = (b.X * b.X - a.X * a.X + coefficient * (b.X - a.X) + scaleSquared) / (2 * scaleSquared);
                        double radiusSquared = (a.X - cx) * (a.X - cx) + scaleSquared * cy * cy;
                        double middleSquared = radiusSquared - scaleSquared * (.5 - cy) * (.5 - cy);
                        if (middleSquared >= 0)
                            foreach (int sign in new[] { -1, 1 })
                            {
                                var curve = new ControlCurve { Kind = ControlCurveKind.CircularArc, ReferenceScale = Math.Sqrt(scaleSquared) };
                                curve.Controls.Add(new() { Offset = new(.5, cx + sign * Math.Sqrt(middleSquared) - a.X) });
                                var error = Error(curve);
                                if (error.Max < best.Max) { best = error; chosen = curve; }
                            }
                    }
                }
            }
            if (best.Max > tolerance && samples.Length >= 4)
            {
                double bestFirst = 1d / 3, bestSecond = 2d / 3, bestSquare = double.PositiveInfinity;
                Cubic(bestFirst, bestSecond);
                if (best.Max > tolerance && samples.Length >= 6)
                {
                    for (int i = 0; i <= 4; i++)
                        for (int j = i; j <= 4; j++) Cubic(i / 4d, j / 4d);
                    double step = .125;
                    for (int iteration = 0; iteration < 10; iteration++, step /= 2)
                    {
                        double first = bestFirst, second = bestSecond;
                        for (int i = -1; i <= 1; i++)
                            for (int j = -1; j <= 1; j++)
                                Cubic(Math.Clamp(first + i * step, 0, 1), Math.Clamp(second + j * step, 0, 1));
                    }
                }
                void Cubic(double first, double second)
                {
                    if (first > second) return;
                    double aa = 0, ab = 0, bb = 0, ay = 0, by = 0;
                    foreach (var p in samples)
                    {
                        double u = CubicParameter(p.TimeMs, first, second), v = 1 - u;
                        double w1 = 3 * v * v * u, w2 = 3 * v * u * u;
                        double y = p.X - v * v * v * a.X - u * u * u * b.X;
                        aa += w1 * w1; ab += w1 * w2; bb += w2 * w2; ay += w1 * y; by += w2 * y;
                    }
                    double determinant = aa * bb - ab * ab;
                    if (determinant <= 1e-15) return;
                    var curve = new ControlCurve();
                    curve.Controls.Add(new() { Offset = new(first, (ay * bb - by * ab) / determinant - a.X) });
                    curve.Controls.Add(new() { Offset = new(second, (by * aa - ay * ab) / determinant - a.X) });
                    var error = Error(curve);
                    if (error.Square < bestSquare) { bestSquare = error.Square; bestFirst = first; bestSecond = second; }
                    if (error.Max < best.Max) { best = error; chosen = curve; }
                }
            }
            if (best.Max <= tolerance || range.End == range.Start + 1)
                segments.Add((range.Start, range.End, chosen));
            else
            {
                int split = Math.Clamp(range.Start + best.Index, range.Start + 1, range.End - 1);
                pending.Push((split, range.End)); pending.Push((range.Start, split));
            }
            if (segments.Count + pending.Count > 30000)
                throw new InvalidOperationException(L.Get("core.importEditing.anchorLimit"));

            (double Max, double Square, int Index) Error(ControlCurve? curve)
            {
                model.Nodes[0].OutgoingCurve = curve;
                if (curve is not null && ControlCurveMath.Validate(model, 0) is not null)
                    return (double.PositiveInfinity, double.PositiveInfinity, 1);
                ControlCurveMath.Arc arc = default;
                bool circular = curve is { Kind: ControlCurveKind.CircularArc }
                    && ControlCurveMath.TryArc(ControlCurveMath.Points(model, 0), curve.ReferenceScale, out arc);
                double maximum = 0, square = 0; int index = 1;
                for (int i = 0; i < samples.Length; i++)
                {
                    if (++work % 1024 == 0) cancellation.ThrowIfCancellationRequested();
                    if (work > 2_000_000) throw new InvalidOperationException(L.Get("core.importEditing.simplificationBudget"));
                    double time = samples[i].TimeMs;
                    double x;
                    if (curve is { Kind: ControlCurveKind.Bezier })
                    {
                        double u = curve.Controls.Count == 1 ? Parameter(time, curve.Controls[0].Offset.TimeMs)
                            : CubicParameter(time, curve.Controls[0].Offset.TimeMs, curve.Controls[1].Offset.TimeMs);
                        double v = 1 - u, first = a.X + curve.Controls[0].Offset.X;
                        x = curve.Controls.Count == 1 ? v * v * a.X + 2 * v * u * first + u * u * b.X
                            : v * v * v * a.X + 3 * v * v * u * first
                                + 3 * v * u * u * (a.X + curve.Controls[1].Offset.X) + u * u * u * b.X;
                    }
                    else if (circular && time > 0 && time < 1)
                    {
                        double y = (time - arc.OriginTime) * arc.Scale - arc.Y;
                        x = arc.X + Math.Sign(arc.Sweep) * Math.Sqrt(Math.Max(0, arc.Radius * arc.Radius - y * y));
                    }
                    else x = a.X + (b.X - a.X) * time;
                    if (!double.IsFinite(x)) return (double.PositiveInfinity, double.PositiveInfinity, 1);
                    double difference = x - samples[i].X;
                    square += difference * difference;
                    if (Math.Abs(difference) > maximum) { maximum = Math.Abs(difference); index = i; }
                }
                return (maximum, square, index);
            }
        }
        track.Nodes.Clear();
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            var a = points[segment.Start]; var b = points[segment.End];
            if (b.TimeMs < a.TimeMs + CurveMath.MinimumAnchorSpacingMs)
                throw new InvalidOperationException(L.Get("core.importEditing.zeroDuration"));
            if (track.Nodes.Count == 0) track.Nodes.Add(new() { TimeMs = a.TimeMs, X = a.X });
            var start = track.Nodes[^1];
            start.OutgoingKind = segment.Curve is null ? CurveKind.Linear : CurveKind.Bezier;
            start.OutgoingCurve = segment.Curve;
            if (segment.Curve is { } curve)
            {
                foreach (var control in curve.Controls)
                    control.Offset = new(control.Offset.TimeMs * (b.TimeMs - a.TimeMs), control.Offset.X);
                if (curve.Kind == ControlCurveKind.CircularArc) curve.ReferenceScale /= b.TimeMs - a.TimeMs;
            }
            track.Nodes.Add(new() { TimeMs = b.TimeMs, X = b.X });
        }
        track.Kind = CurveKind.Linear;
    }

    private static double Parameter(double time, double control)
    {
        if (time <= 0 || time >= 1) return time;
        if (Math.Abs(control - .5) < 1e-12) return time;
        return time / (control + Math.Sqrt(control * control + (1 - 2 * control) * time));
    }

    internal static IEnumerable<int> Corners(IReadOnlyList<MapPoint> points, double tolerance)
    {
        for (int i = 2; i + 2 < points.Count; i++)
        {
            double Slope(int a, int b) => (points[b].X - points[a].X) / (points[b].TimeMs - points[a].TimeMs);
            double interval = Enumerable.Range(i - 2, 4).Min(j => points[j + 1].TimeMs - points[j].TimeMs);
            if (interval < CurveMath.MinimumAnchorSpacingMs) continue;
            double left = Slope(i - 1, i), right = Slope(i, i + 1);
            double jump = Math.Abs(left - right), noise = 2 * tolerance / interval;
            // Stable slopes on both sides identify an authored corner; a smooth extremum's
            // slopes keep changing, while quantization-size reversals carry no such evidence.
            if (jump > noise * 4 && Math.Abs(Slope(i - 2, i - 1) - left) <= Math.Max(noise, jump * .12)
                && Math.Abs(Slope(i + 1, i + 2) - right) <= Math.Max(noise, jump * .12)) yield return i;
        }
    }

    private static double CubicParameter(double time, double first, double second)
    {
        if (time <= 0 || time >= 1) return time;
        double lo = 0, hi = 1;
        for (int i = 0; i < 32; i++)
        {
            double u = (lo + hi) / 2, v = 1 - u;
            if (3 * v * v * u * first + 3 * v * u * u * second + u * u * u < time) lo = u;
            else hi = u;
        }
        return (lo + hi) / 2;
    }
}
