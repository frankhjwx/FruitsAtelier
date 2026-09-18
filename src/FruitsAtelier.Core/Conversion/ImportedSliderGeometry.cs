using L = FruitsAtelier.Localization.Strings;
// Portions adapted from osu!lazer and osu!framework, Copyright (c) ppy Pty Ltd <contact@ppy.sh>.
// Licensed under MIT; see LICENCE.osu.txt, LICENCE.osu-framework.txt and UPSTREAM.md in this directory.
using System.Numerics;

namespace FruitsAtelier.Core;

internal sealed class ImportedSliderGeometry
{
    private const int maximumPoints = 200_000;
    private readonly Vector2[] points;
    private readonly double[] distances;
    public double Distance => distances[^1];

    public ImportedSliderGeometry(ImportedSlider slider)
    {
        if (slider.ControlPoints.Count is < 1 or > ImportedSlider.MaximumControlPoints) throw new CatchConversionException(L.Get("core.importGeometry.pointCount"));
        var head = new Vector2((float)slider.X, (float)slider.Y);
        Vector2[] controls = slider.ControlPoints.Select(p => new Vector2((float)p.X, (float)p.GeometryY) - head).ToArray();
        char type = char.ToUpperInvariant(slider.PathType);
        if (type is not ('L' or 'B' or 'P' or 'C')) throw new CatchConversionException(L.Get("core.importGeometry.pathType", slider.PathType));
        if (type == 'P' && controls.Length != 3) type = 'B';
        else if (type == 'P' && Math.Abs(Cross(controls[1] - controls[0], controls[2] - controls[0])) <= 0.001f) type = 'L';
        var path = new List<Vector2>();
        int start = 0;
        for (int index = 1; index < controls.Length; index++)
        {
            if (controls[index] != controls[index - 1] || index == controls.Length - 1 || type == 'C' && index > 1) continue;
            Append(Approximate(type, controls[start..index]));
            start = index;
        }
        Append(Approximate(type, controls[start..]));
        if (path.Count == 0) path.Add(Vector2.Zero);
        if (path.Count > maximumPoints) throw new CatchConversionException(L.Get("core.importGeometry.sampleLimit"));
        var cumulative = new List<double> { 0 };
        for (int i = 1; i < path.Count; i++) cumulative.Add(cumulative[^1] + (path[i] - path[i - 1]).Length());
        if (!double.IsFinite(cumulative[^1])) throw new CatchConversionException(L.Get("core.importGeometry.finiteGeometry"));

        double expected = slider.PixelLength > 0 ? slider.PixelLength : cumulative[^1];
        if (expected != cumulative[^1] && path.Count >= 2 && !(path[^1] == path[^2] && expected > cumulative[^1]))
        {
            if (expected < cumulative[^1])
            {
                int end = cumulative.FindIndex(d => d >= expected);
                if (end <= 0) { path = [path[0]]; cumulative = [0]; }
                else
                {
                    var direction = Vector2.Normalize(path[end] - path[end - 1]);
                    var final = path[end - 1] + direction * (float)(expected - cumulative[end - 1]);
                    path.RemoveRange(end, path.Count - end);
                    cumulative.RemoveRange(end, cumulative.Count - end);
                    path.Add(final); cumulative.Add(expected);
                }
            }
            else
            {
                var direction = Vector2.Normalize(path[^1] - path[^2]);
                path[^1] = path[^2] + direction * (float)(expected - cumulative[^2]);
                cumulative[^1] = expected;
            }
        }
        points = path.ToArray();
        distances = cumulative.ToArray();

        void Append(List<Vector2> section)
        {
            int skip = path.Count > 0 && section.Count > 0 && path[^1] == section[0] ? 1 : 0;
            for (int i = skip; i < section.Count; i++) path.Add(section[i]);
        }
    }

    public Vector2 PositionAt(double progress)
    {
        double distance = Math.Clamp(progress, 0, 1) * Distance;
        int index = Array.BinarySearch(distances, distance);
        if (index < 0) index = ~index;
        if (index <= 0) return points[0];
        if (index >= points.Length) return points[^1];
        double interval = distances[index] - distances[index - 1];
        if (Math.Abs(interval) <= 1e-7) return points[index - 1];
        return points[index - 1] + (points[index] - points[index - 1]) * (float)((distance - distances[index - 1]) / interval);
    }

    public IReadOnlyList<SliderPathPoint> AbsolutePoints(ImportedSlider slider)
        => points.Select(p => new SliderPathPoint((float)slider.X + p.X, (float)slider.Y + p.Y)).ToArray();

    public IReadOnlyList<MapPoint> TimeXPoints(ImportedSlider slider, double velocity)
    {
        var result = new List<MapPoint>();
        float head = Math.Clamp((float)slider.X, 0, 512);
        for (int i = 0; i < points.Length; i++)
        {
            double time = slider.TimeMs + distances[i] / velocity;
            double x = head + points[i].X;
            if (i > 0)
            {
                double previousX = head + points[i - 1].X;
                double previousTime = slider.TimeMs + distances[i - 1] / velocity;
                if (previousX != x)
                {
                    var crossings = new[] { 0d, 512d }.Select(edge => (Progress: (edge - previousX) / (x - previousX), X: edge))
                        .Where(c => c.Progress > 0 && c.Progress < 1).OrderBy(c => c.Progress);
                    foreach (var crossing in crossings)
                        Add(new(previousTime + (time - previousTime) * crossing.Progress, crossing.X));
                }
            }
            Add(new(time, Math.Clamp(x, 0, 512)));
        }
        return result;

        void Add(MapPoint point)
        {
            // Zero-length polyline duplicates carry no elapsed time; repeat events remain in SpanCount.
            if (result.Count == 0 || point.TimeMs > result[^1].TimeMs) result.Add(point);
        }
    }

    private static List<Vector2> Approximate(char type, Vector2[] controls)
    {
        if (controls.Length < 2 || type == 'L') return controls.ToList();
        return type switch { 'C' => Catmull(controls), 'P' => Circular(controls), _ => Bezier(controls) };
    }

    private static List<Vector2> Bezier(Vector2[] controls)
    {
        var output = new List<Vector2>();
        var pending = new Stack<(Vector2[] Points, int Depth)>();
        pending.Push((controls, 0));
        while (pending.TryPop(out var item))
        {
            var p = item.Points;
            bool flat = true;
            for (int i = 1; i < p.Length - 1; i++)
                if ((p[i - 1] - 2 * p[i] + p[i + 1]).LengthSquared() > 0.25f) { flat = false; break; }
            Split(p, out var left, out var right);
            if (flat)
            {
                var joined = left.Concat(right.Skip(1)).ToArray();
                output.Add(p[0]);
                for (int i = 1; i < p.Length - 1; i++)
                    output.Add(0.25f * (joined[2 * i - 1] + 2 * joined[2 * i] + joined[2 * i + 1]));
            }
            else
            {
                if (item.Depth >= 24) throw new CatchConversionException(L.Get("core.importGeometry.bezierConvergence"));
                pending.Push((right, item.Depth + 1));
                pending.Push((left, item.Depth + 1));
            }
            if (output.Count > maximumPoints) throw new CatchConversionException(L.Get("core.importGeometry.bezierLimit"));
        }
        output.Add(controls[^1]);
        return output;
    }

    private static void Split(Vector2[] points, out Vector2[] left, out Vector2[] right)
    {
        int count = points.Length;
        left = new Vector2[count]; right = new Vector2[count];
        var middle = (Vector2[])points.Clone();
        for (int i = 0; i < count; i++)
        {
            left[i] = middle[0]; right[count - i - 1] = middle[count - i - 1];
            for (int j = 0; j < count - i - 1; j++) middle[j] = (middle[j] + middle[j + 1]) / 2;
        }
    }

    private static List<Vector2> Catmull(Vector2[] points)
    {
        if ((points.Length - 1) * 100L > maximumPoints) throw new CatchConversionException(L.Get("core.importGeometry.catmullLimit"));
        var result = new List<Vector2>();
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 a = i > 0 ? points[i - 1] : points[i], b = points[i], c = points[i + 1];
            Vector2 d = i < points.Length - 2 ? points[i + 2] : c + c - b;
            for (int j = 0; j < 50; j++)
            { result.Add(At(j / 50f)); result.Add(At((j + 1) / 50f)); }

            Vector2 At(float t)
            {
                float t2 = t * t, t3 = t * t2;
                return 0.5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
            }
        }
        return result;
    }

    private static List<Vector2> Circular(Vector2[] p)
    {
        var a = p[0]; var b = p[1]; var c = p[2];
        float divisor = 2 * (a.X * (b - c).Y + b.X * (c - a).Y + c.X * (a - b).Y);
        if (Math.Abs(divisor) <= 0.001f) return Bezier(p);
        float aa = a.LengthSquared(), bb = b.LengthSquared(), cc = c.LengthSquared();
        var center = new Vector2(aa * (b - c).Y + bb * (c - a).Y + cc * (a - b).Y,
            aa * (c - b).X + bb * (a - c).X + cc * (b - a).X) / divisor;
        var da = a - center; var dc = c - center;
        float radius = da.Length();
        double start = Math.Atan2(da.Y, da.X), end = Math.Atan2(dc.Y, dc.X);
        while (end < start) end += Math.PI * 2;
        double range = end - start, direction = 1;
        var ac = c - a;
        if (Vector2.Dot(new(ac.Y, -ac.X), b - a) < 0) { direction = -1; range = Math.PI * 2 - range; }
        double required = 2 * radius <= 0.1f ? 2 : Math.Ceiling(range / (2 * Math.Acos(1 - 0.1f / radius)));
        if (!double.IsFinite(required) || required > maximumPoints) throw new CatchConversionException(L.Get("core.importGeometry.arcLimit"));
        int count = Math.Max(2, (int)required);
        var result = new List<Vector2>(count);
        for (int i = 0; i < count; i++)
        {
            double angle = start + direction * i / (count - 1.0) * range;
            result.Add(center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius);
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
