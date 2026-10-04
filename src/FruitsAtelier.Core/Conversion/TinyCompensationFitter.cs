using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

internal static class TinyCompensationFitter
{
    public static List<MapPoint> Fit(CurveTrack track, IReadOnlyList<NestedCatchEvent> events, double velocity,
        Func<double, double> positionAtTime, Func<double, double>? tinyTargetAtTime = null)
    {
        double start = track.Nodes[0].TimeMs;
        double duration = track.Nodes[^1].TimeMs - start;
        // Repeated events share one path position. Evaluating their absolute times
        // separately can introduce round-off that makes fixed targets disagree.
        var knots = events.GroupBy(e => start + e.Progress * duration).OrderBy(g => g.Key)
            .Select(g => new Knot(g.Key, g.Select(e => new Target(
                Math.Clamp(e.Kind == CatchObjectKind.TinyDroplet ? (tinyTargetAtTime ?? positionAtTime)(e.TimeMs) : positionAtTime(g.Key), 0, 512),
                e.RawOffset, e.Kind == CatchObjectKind.TinyDroplet)).ToArray()))
            .ToArray();
        var lower = new double[knots.Length];
        var upper = new double[knots.Length];

        // Feasible intervals propagate the arc-length speed bound without moving fruit or ticks.
        // Binary search minimizes the largest playable tiny error, including repeated path constraints.
        if (!Feasible(512)) throw new CatchConversionException(L.Get("core.sliderGeometry.horizontalSpeed"));
        double low = 0, high = 512;
        for (int iteration = 0; iteration < 36; iteration++)
        {
            double middle = (low + high) / 2;
            if (Feasible(middle)) high = middle;
            else low = middle;
        }
        Feasible(high);
        var result = new MapPoint[knots.Length];
        for (int i = knots.Length - 1; i >= 0; i--)
        {
            double min = lower[i], max = upper[i];
            if (i + 1 < knots.Length)
            {
                double travel = (knots[i + 1].Time - knots[i].Time) * velocity;
                min = Math.Max(min, result[i + 1].X - travel);
                max = Math.Min(max, result[i + 1].X + travel);
            }
            double preferred = knots[i].Targets.Average(t => t.X - (t.Tiny ? t.Offset : 0));
            result[i] = new(knots[i].Time, Math.Max(min, Math.Min(max, preferred)));
        }
        return result.ToList();

        bool Feasible(double error)
        {
            for (int i = 0; i < knots.Length; i++)
            {
                double min = 0, max = 512;
                foreach (var target in knots[i].Targets)
                {
                    if (!target.Tiny)
                    {
                        min = Math.Max(min, target.X);
                        max = Math.Min(max, target.X);
                    }
                    else
                    {
                        // Gameplay clamps the offset result at the playfield edges.
                        if (target.X - error > 0) min = Math.Max(min, target.X - error - target.Offset);
                        if (target.X + error < 512) max = Math.Min(max, target.X + error - target.Offset);
                    }
                }
                if (i > 0)
                {
                    double travel = (knots[i].Time - knots[i - 1].Time) * velocity;
                    min = Math.Max(min, lower[i - 1] - travel);
                    max = Math.Min(max, upper[i - 1] + travel);
                }
                if (min > max) return false;
                lower[i] = min; upper[i] = max;
            }
            return true;
        }
    }

    private sealed record Knot(double Time, Target[] Targets);
    private readonly record struct Target(double X, int Offset, bool Tiny);
}
