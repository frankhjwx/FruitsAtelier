using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

// Fits X as a function of time, not the original osu! path's geometric Y coordinate.
internal static class ImportedCurveFitter
{
    public const double Tolerance = 0.25;
    private readonly record struct Segment(int Start, int End, bool Curved, double C1, double C2);

    public static void Fit(CurveTrack track, IReadOnlyList<MapPoint> points, double velocity)
    {
        if (points.Count < 2) throw new InvalidOperationException(L.Get("core.importEditing.zeroDuration"));
        var boundaries = new List<int> { 0 };
        for (int i = 1; i + 1 < points.Count; i++)
        {
            double left = (points[i].X - points[i - 1].X) / (points[i].TimeMs - points[i - 1].TimeMs);
            double right = (points[i + 1].X - points[i].X) / (points[i + 1].TimeMs - points[i].TimeMs);
            // Preserve reversals, plateaus and pronounced corners before fitting smooth sections.
            if (Math.Sign(left) != Math.Sign(right) || Math.Abs(left - right) > velocity * 0.35)
                boundaries.Add(i);
        }
        boundaries.Add(points.Count - 1);
        var pending = new Stack<(int Start, int End)>();
        for (int i = 0; i + 1 < boundaries.Count; i++) pending.Push((boundaries[i], boundaries[i + 1]));
        var segments = new List<Segment>(); long work = 0;
        while (pending.TryPop(out var range))
        {
            var a = points[range.Start]; var b = points[range.End];
            double c1 = a.X + (b.X - a.X) / 3, c2 = a.X + (b.X - a.X) * 2 / 3;
            var error = Error(range.Start, range.End, c1, c2);
            bool curved = false;
            if (error.Error > Tolerance)
            {
                // Least-squares cubic with exact endpoints and a linear time parameter.
                double aa = 0, ab = 0, bb = 0, ay = 0, by = 0;
                for (int i = range.Start + 1; i < range.End; i++)
                {
                    double u = (points[i].TimeMs - a.TimeMs) / (b.TimeMs - a.TimeMs), v = 1 - u;
                    double w1 = 3 * v * v * u, w2 = 3 * v * u * u;
                    double y = points[i].X - v * v * v * a.X - u * u * u * b.X;
                    aa += w1 * w1; ab += w1 * w2; bb += w2 * w2; ay += w1 * y; by += w2 * y;
                }
                double determinant = aa * bb - ab * ab;
                if (determinant > 1e-15)
                {
                    double min = Math.Min(a.X, b.X), max = Math.Max(a.X, b.X);
                    c1 = Math.Clamp((ay * bb - by * ab) / determinant, min, max);
                    c2 = Math.Clamp((by * aa - ay * ab) / determinant, min, max);
                    // Ordered control points prevent new reversals between preserved extrema.
                    if ((c2 - c1) * (b.X - a.X) < 0) c1 = c2 = (c1 + c2) / 2;
                    error = Error(range.Start, range.End, c1, c2); curved = true;
                }
            }
            if (error.Error <= Tolerance)
                segments.Add(new(range.Start, range.End, curved, c1, c2));
            else
            {
                int split = Math.Clamp(error.Index, range.Start + 1, range.End - 1);
                pending.Push((range.Start, split)); pending.Push((split, range.End));
            }
            if (segments.Count + pending.Count > 30000) throw new InvalidOperationException(L.Get("core.importEditing.anchorLimit"));
        }
        foreach (var segment in segments.OrderBy(s => s.Start))
        {
            var a = points[segment.Start]; var b = points[segment.End];
            if (track.Nodes.Count == 0) track.Nodes.Add(new Anchor { TimeMs = a.TimeMs, X = a.X });
            var start = track.Nodes[^1];
            var end = new Anchor { TimeMs = b.TimeMs, X = b.X, OutgoingKind = CurveKind.Linear };
            start.OutgoingKind = segment.Curved ? CurveKind.Bezier : CurveKind.Linear;
            if (segment.Curved)
            {
                start.HandleOut = new((b.TimeMs - a.TimeMs) / 3, segment.C1 - a.X);
                end.HandleIn = new(-(b.TimeMs - a.TimeMs) / 3, segment.C2 - b.X);
            }
            track.Nodes.Add(end);
        }
        track.Kind = track.Nodes.Any(n => n.OutgoingKind == CurveKind.Bezier) ? CurveKind.Bezier : CurveKind.Linear;

        (double Error, int Index) Error(int start, int end, double c1, double c2)
        {
            var a = points[start]; var b = points[end]; double duration = b.TimeMs - a.TimeMs;
            double c = 3 * (c1 - a.X), d = 3 * (a.X - 2 * c1 + c2), e = -a.X + 3 * c1 - 3 * c2 + b.X;
            double maximum = 0; int index = start + 1;
            for (int i = start; i < end; i++)
            {
                if (++work > 20_000_000) throw new InvalidOperationException(L.Get("core.importEditing.simplificationBudget"));
                double lo = (points[i].TimeMs - a.TimeMs) / duration, hi = (points[i + 1].TimeMs - a.TimeMs) / duration;
                double slope = (points[i + 1].X - points[i].X) / (hi - lo);
                Check(lo); Check(hi);
                // Exact extrema of cubic minus each original linear interval, not a sparse sample test.
                double qa = 3 * e, qb = 2 * d, qc = c - slope;
                if (Math.Abs(qa) < 1e-12) { if (Math.Abs(qb) > 1e-12) Check(-qc / qb); }
                else
                {
                    double discriminant = qb * qb - 4 * qa * qc;
                    if (discriminant >= 0) { double root = Math.Sqrt(discriminant); Check((-qb + root) / (2 * qa)); Check((-qb - root) / (2 * qa)); }
                }
                void Check(double u)
                {
                    if (u < lo || u > hi) return;
                    double difference = Math.Abs(((e * u + d) * u + c) * u + a.X - (points[i].X + slope * (u - lo)));
                    if (difference > maximum) { maximum = difference; index = u - lo < hi - u ? i : i + 1; }
                }
            }
            return (maximum, index);
        }
    }
}
