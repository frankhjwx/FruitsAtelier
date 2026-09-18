namespace FruitsAtelier.Core;

/// <summary>Distance spacing in playfield X units, independent of viewport scale.</summary>
public static class DistanceSnap
{
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
            .OrderBy(r => r.Start.TimeMs).ThenBy(r => r.Order).ToArray();
    }

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
