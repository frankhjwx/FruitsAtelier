using L = FruitsAtelier.Localization.Strings;
// Portions adapted from osu!lazer, Copyright (c) ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence; see LICENCE.osu.txt and UPSTREAM.md in this directory.
namespace FruitsAtelier.Core;

internal sealed class SliderGeometry
{
    public IReadOnlyList<SliderPathPoint> Points { get; }
    private readonly double[] distances;

    private SliderGeometry(List<SliderPathPoint> points)
    {
        Points = points;
        distances = new double[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            double dy = points[i].GeometryY - points[i - 1].GeometryY;
            distances[i] = distances[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public static SliderGeometry Create(IReadOnlyList<MapPoint> samples, double velocity)
    {
        var points = new List<SliderPathPoint> { new(samples[0].X, 192) };
        int direction = -1;
        for (int i = 1; i < samples.Count; i++)
        {
            var a = samples[i - 1]; var b = samples[i];
            double length = (b.TimeMs - a.TimeMs) * velocity;
            double dx = b.X - a.X;
            if (Math.Abs(dx) > length + 1e-7)
                throw new CatchConversionException(L.Get("core.sliderGeometry.horizontalSpeed"));
            double verticalTravel = Math.Sqrt(Math.Max(0, (length - Math.Abs(dx)) * (length + Math.Abs(dx))));
            if (verticalTravel < 1e-9)
            {
                points.Add(new(b.X, points[^1].GeometryY));
                continue;
            }

            double moved = 0;
            while (moved < verticalTravel)
            {
                double y = points[^1].GeometryY;
                double room = direction > 0 ? 383 - y : y - 1;
                if (room <= 1e-10) { direction = -direction; continue; }
                double step = Math.Min(room, verticalTravel - moved);
                moved += step;
                double x = moved >= verticalTravel ? b.X : a.X + dx * (moved / verticalTravel);
                points.Add(new(x, y + direction * step));
                if (points.Count > 65_536) throw new CatchConversionException(L.Get("core.sliderGeometry.pointLimit"));
            }
        }
        return new(points);
    }

    public double XAtDistance(double distance)
    {
        if (distance <= 0) return Points[0].X;
        if (distance >= distances[^1]) return Points[^1].X;
        int index = Array.BinarySearch(distances, distance);
        if (index >= 0) return Points[index].X;
        index = ~index;
        double length = distances[index] - distances[index - 1];
        if (length <= 0) return Points[index].X;
        double progress = (distance - distances[index - 1]) / length;
        return Points[index - 1].X + (Points[index].X - Points[index - 1].X) * progress;
    }
}
