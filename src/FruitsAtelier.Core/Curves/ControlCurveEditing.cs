using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class ControlCurveEditing
{
    public static void Split(CurveTrack track, int segment, double u)
    {
        var a = track.Nodes[segment]; var curve = a.OutgoingCurve!;
        var p = ControlCurveMath.Points(track, segment);
        var mid = CurveMath.Evaluate(track, segment, u);
        var inserted = new Anchor { TimeMs = mid.TimeMs, X = mid.X, OutgoingKind = CurveKind.Bezier };
        MapPoint[] left, right;
        if (curve.Kind == ControlCurveKind.CircularArc)
        {
            left = [p[0], CurveMath.Evaluate(track, segment, u / 2), mid];
            right = [mid, CurveMath.Evaluate(track, segment, (1 + u) / 2), p[^1]];
        }
        else
        {
            left = new MapPoint[p.Length]; right = new MapPoint[p.Length];
            left[0] = p[0]; right[^1] = p[^1];
            for (int n = p.Length - 1; n > 0; n--)
            {
                for (int i = 0; i < n; i++) p[i] = MapPoint.Lerp(p[i], p[i + 1], u);
                left[p.Length - n] = p[0]; right[n - 1] = p[n - 1];
            }
        }
        a.OutgoingCurve = Create(curve.Kind, curve.ReferenceScale, left);
        inserted.OutgoingCurve = Create(curve.Kind, curve.ReferenceScale, right);
        track.Nodes.Insert(segment + 1, inserted);
    }

    private static ControlCurve Create(ControlCurveKind kind, double scale, IReadOnlyList<MapPoint> points)
    {
        var curve = new ControlCurve { Kind = kind, ReferenceScale = scale };
        for (int i = 1; i < points.Count - 1; i++) curve.Controls.Add(new() { Offset = points[i] - points[0] });
        return curve;
    }

    public readonly record struct Cubic(MapPoint Start, MapPoint Out, MapPoint In, MapPoint End);

    public static IReadOnlyList<Cubic> PenSegments(CurveTrack track, int segment)
    {
        var curve = track.Nodes[segment].OutgoingCurve;
        var points = ControlCurveMath.Points(track, segment);
        if (curve is null) return [new(points[0], points.Length == 2 ? MapPoint.Lerp(points[0], points[^1], 1.0 / 3) : points[1],
            points.Length == 2 ? MapPoint.Lerp(points[0], points[^1], 2.0 / 3) : points[2], points[^1])];
        if (curve.Kind == ControlCurveKind.Bezier && points.Length <= 4)
        {
            while (points.Length < 4)
            {
                var elevated = new MapPoint[points.Length + 1]; elevated[0] = points[0]; elevated[^1] = points[^1];
                for (int i = 1; i < points.Length; i++) elevated[i] = MapPoint.Lerp(points[i], points[i - 1], (double)i / points.Length);
                points = elevated;
            }
            return [new(points[0], points[1], points[2], points[3])];
        }
        ControlCurveMath.Arc arc = default;
        bool isArc = curve.Kind == ControlCurveKind.CircularArc && ControlCurveMath.TryArc(points, curve.ReferenceScale, out arc);
        var output = new List<Cubic>();
        Fit(0, 1, 0);
        return output;

        MapPoint Derivative(double u)
        {
            if (curve.Kind == ControlCurveKind.CircularArc)
            {
                if (!isArc) return points[^1] - points[0];
                double angle = arc.Angle + arc.Sweep * u;
                return new(arc.Radius * Math.Cos(angle) * arc.Sweep / arc.Scale, -arc.Radius * Math.Sin(angle) * arc.Sweep);
            }
            var d = new MapPoint[points.Length - 1];
            for (int i = 0; i < d.Length; i++) d[i] = (points[i + 1] - points[i]) * d.Length;
            return ControlCurveMath.Bezier(d, u);
        }
        void Fit(double from, double to, int depth)
        {
            if (output.Count >= 4096) throw new ArgumentException(L.Get("core.controlCurve.fitLimit"));
            var start = CurveMath.Evaluate(track, segment, from); var end = CurveMath.Evaluate(track, segment, to);
            double tangent = isArc ? 4.0 / 3 * Math.Tan(arc.Sweep * (to - from) / 4) / arc.Sweep : (to - from) / 3;
            var p1 = start + Derivative(from) * tangent;
            var p2 = end - Derivative(to) * tangent;
            var original1 = p1; var original2 = p2;
            p1 = new(Math.Clamp(p1.TimeMs, start.TimeMs, end.TimeMs), p1.X);
            p2 = new(Math.Clamp(p2.TimeMs, p1.TimeMs, end.TimeMs), p2.X);
            double errorTime = 0, errorX = 0;
            if (curve.Kind == ControlCurveKind.CircularArc)
            {
                if (isArc)
                {
                    double sweep = arc.Sweep * (to - from);
                    double half = sweep / 2, k = 4.0 / 3 * Math.Tan(sweep / 4);
                    double coefficient = Math.Pow(3 * k * Math.Cos(half) - 2 * Math.Sin(half), 2);
                    // Squared radial excess is C*t²*(1-t)²*(2t-1)², whose maximum is C/108.
                    double excess = coefficient / 108;
                    double radialError = arc.Radius * excess / (Math.Sqrt(1 + excess) + 1);
                    errorTime = radialError / arc.Scale;
                    double side = Math.Sign(Math.Cos(arc.Angle + arc.Sweep * (from + to) / 2));
                    double[] x = [side * (start.X - arc.X) / arc.Radius, side * (p1.X - arc.X) / arc.Radius,
                        side * (p2.X - arc.X) / arc.Radius, side * (end.X - arc.X) / arc.Radius];
                    errorX = Math.Abs(sweep) <= Math.PI / 2 + 1e-12 && ArcPositionErrorFits(x, coefficient, .001 / arc.Radius) ? 0 : double.PositiveInfinity;
                }
                errorX += Math.Max(Math.Abs(original1.X - p1.X), Math.Abs(original2.X - p2.X));
                errorTime += Math.Max(Math.Abs(original1.TimeMs - p1.TimeMs), Math.Abs(original2.TimeMs - p2.TimeMs));
            }
            else
            {
                var exact = Subcurve(points, from, to);
                MapPoint[] cubic = [start, p1, p2, end];
                while (cubic.Length < exact.Length)
                {
                    var elevated = new MapPoint[cubic.Length + 1]; elevated[0] = cubic[0]; elevated[^1] = cubic[^1];
                    for (int i = 1; i < cubic.Length; i++) elevated[i] = MapPoint.Lerp(cubic[i], cubic[i - 1], (double)i / cubic.Length);
                    cubic = elevated;
                }
                // The difference polynomial lies in the convex hull of its Bernstein coefficients.
                for (int i = 0; i < exact.Length; i++)
                {
                    errorX = Math.Max(errorX, Math.Abs(exact[i].X - cubic[i].X));
                    errorTime = Math.Max(errorTime, Math.Abs(exact[i].TimeMs - cubic[i].TimeMs));
                }
            }
            bool fits = errorTime <= 0.01 && errorX <= 0.001;
            if (fits) { output.Add(new(start, p1, p2, end)); return; }
            double middle = (from + to) / 2;
            double middleTime = CurveMath.Evaluate(track, segment, middle).TimeMs;
            if (depth >= 24 || middleTime - start.TimeMs < CurveMath.MinimumAnchorSpacingMs || end.TimeMs - middleTime < CurveMath.MinimumAnchorSpacingMs)
                throw new ArgumentException(L.Get("core.controlCurve.fitLimit"));
            Fit(from, middle, depth + 1); Fit(middle, to, depth + 1);
        }
    }

    private static bool ArcPositionErrorFits(double[] x, double radialCoefficient, double tolerance)
    {
        // At the same time coordinate, |delta X| = radial excess / (|Xcubic| + |Xcircle|).
        // A relative excess bound gives a lower bound for |Xcircle| without dividing at endpoint tangents.
        for (int i = 0; i < x.Length; i++) x[i] = Math.Max(0, x[i]);
        ReadOnlySpan<double> cubicWeights = [1, 3, 3, 1], squaredWeights = [1, 6, 15, 20, 15, 6, 1];
        var relative = new double[7];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                relative[i + j] += .01 * x[i] * x[j] * cubicWeights[i] * cubicWeights[j] / squaredWeights[i + j];
        relative[2] -= radialCoefficient / 15; relative[3] += radialCoefficient / 10; relative[4] -= radialCoefficient / 15;
        if (Nonnegative(relative, 0)) tolerance *= 1 + Math.Sqrt(.99);
        while (x.Length < 7)
        {
            var elevated = new double[x.Length + 1]; elevated[0] = x[0]; elevated[^1] = x[^1];
            for (int i = 1; i < x.Length; i++) elevated[i] = x[i] * (1 - (double)i / x.Length) + x[i - 1] * i / x.Length;
            x = elevated;
        }
        for (int i = 0; i < x.Length; i++) x[i] *= tolerance;
        x[2] -= radialCoefficient / 15; x[3] += radialCoefficient / 10; x[4] -= radialCoefficient / 15;
        return Nonnegative(x, 0);

        static bool Nonnegative(ReadOnlySpan<double> coefficients, int depth)
        {
            bool positive = true;
            foreach (double value in coefficients) positive &= value >= 0;
            if (positive) return true;
            if (depth == 8) return false;
            Span<double> work = stackalloc double[7], left = stackalloc double[7], right = stackalloc double[7];
            coefficients.CopyTo(work); left[0] = work[0]; right[^1] = work[^1];
            for (int n = 6; n > 0; n--)
            {
                for (int i = 0; i < n; i++) work[i] = (work[i] + work[i + 1]) / 2;
                left[7 - n] = work[0]; right[n - 1] = work[n - 1];
            }
            return Nonnegative(left, depth + 1) && Nonnegative(right, depth + 1);
        }
    }

    private static MapPoint[] Subcurve(MapPoint[] source, double from, double to)
    {
        static (MapPoint[] Left, MapPoint[] Right) Divide(MapPoint[] points, double u)
        {
            var work = points.ToArray(); var left = new MapPoint[points.Length]; var right = new MapPoint[points.Length];
            left[0] = work[0]; right[^1] = work[^1];
            for (int n = work.Length - 1; n > 0; n--)
            {
                for (int i = 0; i < n; i++) work[i] = MapPoint.Lerp(work[i], work[i + 1], u);
                left[work.Length - n] = work[0]; right[n - 1] = work[n - 1];
            }
            return (left, right);
        }
        var prefix = to == 1 ? source : Divide(source, to).Left;
        return from == 0 ? prefix : Divide(prefix, from / to).Right;
    }

    // Call inside the same transaction as the first real pen edit, never when changing tools.
    public static bool ConvertToPen(CurveTrack track, int segment)
    {
        var start = track.Nodes[segment]; var end = track.Nodes[segment + 1];
        if (start.OutgoingCurve is null) return false;
        var cubics = PenSegments(track, segment);
        var inserted = new List<Anchor>();
        for (int i = 0; i < cubics.Count; i++)
        {
            var cubic = cubics[i];
            var a = i == 0 ? start : inserted[^1];
            var b = i == cubics.Count - 1 ? end : new Anchor { TimeMs = cubic.End.TimeMs, X = cubic.End.X };
            a.OutgoingCurve = null; a.OutgoingKind = CurveKind.Bezier;
            a.HandleOut = cubic.Out - cubic.Start; b.HandleIn = cubic.In - cubic.End;
            if (i != cubics.Count - 1) inserted.Add(b);
        }
        track.Nodes.InsertRange(segment + 1, inserted);
        return true;
    }
}
