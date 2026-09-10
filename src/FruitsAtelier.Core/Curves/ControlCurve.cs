using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public enum ControlCurveKind { Bezier, CircularArc }

// Optional exact segment geometry. Offsets share the anchor's translation, just like pen handles.
public sealed class CurveControl
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MapPoint Offset { get; set; }
    internal CurveControl DeepClone() => (CurveControl)MemberwiseClone();
}

public sealed class ControlCurve
{
    public ControlCurveKind Kind { get; set; }
    public double ReferenceScale { get; set; } = 1;
    public List<CurveControl> Controls { get; private set; } = [];
    internal ControlCurve DeepClone()
    {
        var copy = (ControlCurve)MemberwiseClone();
        copy.Controls = Controls.Select(p => p.DeepClone()).ToList();
        return copy;
    }
    internal static bool Equal(ControlCurve? a, ControlCurve? b) => a is null ? b is null : b is not null
        && a.Kind == b.Kind && a.ReferenceScale == b.ReferenceScale && a.Controls.Count == b.Controls.Count
        && a.Controls.Zip(b.Controls).All(p => p.First.Id == p.Second.Id && p.First.Offset == p.Second.Offset);
}

public static class ControlCurveMath
{
    public const int MaximumControls = 64;
    public static double ReferenceScale(double ar) => CatchScrollTiming.FallDistance / CatchScrollTiming.PreemptMs(ar);
    public static MapPoint Point(Anchor node) => new(node.TimeMs, node.X);

    public static MapPoint[] Points(CurveTrack track, int segment)
    {
        var a = track.Nodes[segment]; var b = track.Nodes[segment + 1];
        if (a.OutgoingCurve is { } curve)
            return new[] { Point(a) }.Concat(curve.Controls.Select(c => Point(a) + c.Offset)).Append(Point(b)).ToArray();
        return CurveMath.SegmentKind(track, segment) == CurveKind.Linear ? [Point(a), Point(b)]
            : [Point(a), Point(a) + a.HandleOut, Point(b) + b.HandleIn, Point(b)];
    }

    public static MapPoint Evaluate(CurveTrack track, int segment, double u)
    {
        var points = Points(track, segment);
        var curve = track.Nodes[segment].OutgoingCurve!;
        if (u == 0) return points[0];
        if (u == 1) return points[^1];
        if (curve.Kind == ControlCurveKind.CircularArc && TryArc(points, curve.ReferenceScale, out var arc))
            return arc.At(u);
        if (curve.Kind == ControlCurveKind.CircularArc) return MapPoint.Lerp(points[0], points[^1], u);
        return Bezier(points, u);
    }

    public static MapPoint Bezier(ReadOnlySpan<MapPoint> points, double u)
    {
        Span<MapPoint> work = stackalloc MapPoint[points.Length];
        points.CopyTo(work);
        for (int n = work.Length - 1; n > 0; n--)
            for (int i = 0; i < n; i++) work[i] = MapPoint.Lerp(work[i], work[i + 1], u);
        return work[0];
    }

    public readonly record struct Arc(double X, double Y, double Radius, double Angle, double Sweep, double Scale, double OriginTime)
    {
        public MapPoint At(double u) => new(OriginTime + (Y + Radius * Math.Sin(Angle + Sweep * u)) / Scale,
            X + Radius * Math.Cos(Angle + Sweep * u));
    }

    public static bool TryArc(IReadOnlyList<MapPoint> p, double scale, out Arc arc)
    {
        arc = default;
        if (p.Count != 3 || !double.IsFinite(scale) || scale <= 0) return false;
        double ax = p[0].X, bx = p[1].X - ax, by = (p[1].TimeMs - p[0].TimeMs) * scale;
        double cx = p[2].X - ax, cy = (p[2].TimeMs - p[0].TimeMs) * scale;
        double cross = bx * cy - by * cx;
        if (Math.Abs(cross) <= 1e-10 * Math.Max(1, Math.Sqrt((bx * bx + by * by) * (cx * cx + cy * cy)))) return false;
        double b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
        double x = (b2 * cy - c2 * by) / (2 * cross), y = (bx * c2 - cx * b2) / (2 * cross);
        double start = Math.Atan2(-y, -x), middle = Math.Atan2(by - y, bx - x), end = Math.Atan2(cy - y, cx - x);
        double Positive(double angle) => (angle % Math.Tau + Math.Tau) % Math.Tau;
        double sweep = Positive(end - start);
        if (Positive(middle - start) > sweep) sweep -= Math.Tau;
        arc = new(ax + x, y, Math.Sqrt(x * x + y * y), start, sweep, scale, p[0].TimeMs);
        return double.IsFinite(arc.Radius) && arc.Radius > 0 && Math.Abs(sweep) > 1e-12;
    }

    public static string? Validate(CurveTrack track, int segment)
    {
        var curve = track.Nodes[segment].OutgoingCurve;
        if (curve is null) return null;
        if (!Enum.IsDefined(curve.Kind) || !double.IsFinite(curve.ReferenceScale) || curve.ReferenceScale <= 0
            || curve.Controls.Count > MaximumControls || curve.Controls.Count < 1
            || curve.Kind == ControlCurveKind.CircularArc && curve.Controls.Count != 1)
            return L.Get("core.controlCurve.invalid");
        var points = Points(track, segment);
        for (int i = 0; i < points.Length; i++)
        {
            if (!double.IsFinite(points[i].TimeMs) || !double.IsFinite(points[i].X) || points[i].X is < 0 or > 512)
                return L.Get("core.curves.handleRange");
            if (i > 0 && points[i].TimeMs < points[i - 1].TimeMs) return L.Get("core.curves.handleOrder");
        }
        if (curve.Kind != ControlCurveKind.CircularArc || !TryArc(points, curve.ReferenceScale, out var arc)) return null;
        // Check endpoints and every coordinate extremum, not just a rendered sample grid.
        double lo = Math.Min(arc.Angle, arc.Angle + arc.Sweep), hi = Math.Max(arc.Angle, arc.Angle + arc.Sweep);
        var angles = new List<double> { lo, hi };
        for (int k = (int)Math.Ceiling(lo / (Math.PI / 2)); k * (Math.PI / 2) < hi; k++) angles.Add(k * (Math.PI / 2));
        foreach (double angle in angles)
        {
            double x = arc.X + arc.Radius * Math.Cos(angle);
            if (x < -1e-7 || x > 512 + 1e-7 || arc.Sweep * Math.Cos(angle) < -1e-9)
                return L.Get("core.controlCurve.monotone");
        }
        return null;
    }
}
