namespace FruitsAtelier.Core;

/// <summary>Distance spacing in playfield X units, independent of viewport scale.</summary>
public static class DistanceSnap
{
    public const int MaximumPresets = 8;

    public static MapPoint SnapMultiple(MapPoint point, Reference? previous, IReadOnlyList<double> presets, double fallback, out bool outside)
    {
        outside = false;
        if (previous is null || point.TimeMs <= previous.End.TimeMs) return point;
        double nearest = previous.End.X is >= 0 and <= 512 ? Math.Abs(point.X - previous.End.X) : double.PositiveInfinity;
        var result = double.IsFinite(nearest) ? point with { X = previous.End.X } : point;
        IEnumerable<double> choices = presets.Count == 0 ? [fallback] : presets.Take(MaximumPresets);
        foreach (var preset in choices)
        {
            if (!double.IsFinite(preset) || preset <= 0) continue;
            double distance = (point.TimeMs - previous.End.TimeMs) * previous.Velocity * preset;
            foreach (double x in new[] { previous.End.X - distance, previous.End.X + distance })
                if (x is >= 0 and <= 512 && Math.Abs(point.X - x) < nearest)
                { nearest = Math.Abs(point.X - x); result = point with { X = x }; }
        }
        outside = double.IsPositiveInfinity(nearest);
        return result;
    }

    public sealed record Reference(Guid Id, MapPoint Start, MapPoint End, double Velocity, int Order);

    public static Reference[] References(MapDocument document, CatchConversionResult conversion)
    {
        var timing = new TimingMap.Lookup(document);
        var sliderOrders = document.Tracks.Select(t => (t.Id, t.SourceOrder))
            .Concat(document.ImportedSliders.Select(t => (t.Id, t.SourceOrder))).ToDictionary(t => t.Id, t => t.SourceOrder);
        return document.Fruits.Select(f => new Reference(f.Id, new(f.TimeMs, f.X), new(f.TimeMs, f.X),
                100 * document.SliderMultiplier / timing.At(f.TimeMs).BeatLengthMs, f.SourceOrder))
            .Concat(conversion.Sliders.Where(s => s.Path.Count > 0).Select(s => new Reference(s.SourceId,
                new(s.StartTimeMs, s.Path[0].X),
                new(s.StartTimeMs + s.DurationMs, s.SpanCount % 2 == 0 ? s.Path[0].X : s.Path[^1].X),
                s.Velocity, sliderOrders.GetValueOrDefault(s.SourceId))))
            .Concat(document.Tracks.Where(t => t.StreamSnapDivisor is not null && t.Nodes.Count >= 2).Select(t => new Reference(t.Id,
                new(t.Nodes[0].TimeMs, t.Nodes[0].X), new(CurveMath.EndTimeMs(t), CurveMath.PositionAtTime(t, CurveMath.EndTimeMs(t))),
                100 * document.SliderMultiplier / timing.At(t.Nodes[0].TimeMs).BeatLengthMs, t.SourceOrder)))
            .OrderBy(r => r.Start.TimeMs).ThenBy(r => r.Order).ToArray();
    }

    public static double BaseVelocity(MapDocument document, double time)
        => 100 * document.SliderMultiplier / TimingMap.At(document, time).BeatLengthMs;

    public static double? Ratio(MapPoint from, MapPoint to, double velocity)
    {
        double distance = (to.TimeMs - from.TimeMs) * velocity;
        return distance > 0 && double.IsFinite(distance) ? Math.Abs(to.X - from.X) / distance : null;
    }

    public static MapPoint Snap(MapPoint point, Reference? previous, double spacing, out bool outside)
    {
        outside = false;
        if (previous is null || point.TimeMs <= previous.End.TimeMs) return point;
        double distance = (point.TimeMs - previous.End.TimeMs) * previous.Velocity * spacing;
        double left = previous.End.X - distance, right = previous.End.X + distance;
        double target = point.X < previous.End.X ? left : right;
        if (target is < 0 or > 512)
        {
            double other = target == left ? right : left;
            if (other is >= 0 and <= 512) target = other;
            else { outside = true; return point; }
        }
        return point with { X = target };
    }
}
