using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

public sealed record PatternPreviewData(IReadOnlyList<ConvertedCatchObject> Objects,
    IReadOnlyList<IReadOnlyList<MapPoint>> Paths, double StartTimeMs, double EndTimeMs, double CircleSize,
    double ApproachRate, double CenterX, IReadOnlySet<(Guid SourceId, int EventIndex)> HyperdashStarts);

/// <summary>Skin-independent preview geometry. Call off the pointer/render path and cache the result.</summary>
public static class PatternPreview
{
    public static PatternPreviewData Create(PatternSnapshot snapshot) => Create(snapshot.CreateDocument(), derandomize: true);

    public static PatternPreviewData Create(MapDocument source, bool derandomize = false)
    {
        var map = source.DeepClone();
        if (derandomize)
        {
            map.DerandomizeFSliderDroplets = true; map.DerandomizeDropletsForHardRock = false;
            map.RandomizeDropletSeed = 1337;
            // A materialized effect is not its baseline. Preview the saved authoring curve when available.
            foreach (var slider in map.ImportedSliders.ToArray())
            {
                if (slider.RandomizationTrack is not { } baseline) continue;
                var track = baseline.DeepClone(); track.Id = slider.Id; track.SourceOrder = slider.SourceOrder;
                map.ImportedSliders.Remove(slider); map.Tracks.Add(track);
            }
            foreach (var track in map.Tracks) track.DropletMode = SliderDropletMode.Derandomize;
        }
        var conversion = CatchStreamConverter.Convert(map);
        if (!conversion.Success) throw new InvalidDataException(L.Get("gallery.error.conversion", string.Join("; ", conversion.Diagnostics)));
        var tracks = map.Tracks.ToDictionary(t => t.Id);
        var objects = conversion.Objects.Select(o =>
        {
            if (!derandomize || o.Kind == CatchObjectKind.Banana || o.IsStandalone) return o;
            double x = tracks.TryGetValue(o.SourceId, out var track)
                ? CurveMath.PositionAtTime(track, o.TimeMs) : o.PathX;
            x = Math.Clamp(x, 0, 512);
            return o with { X = x, TargetX = x, PathX = x, RandomOffset = 0 };
        }).ToArray();
        var paths = new List<IReadOnlyList<MapPoint>>();
        long totalPathPoints = 0;
        var timing = new TimingMap.Lookup(map);
        foreach (var track in map.Tracks.Where(t => t.StreamSnapDivisor is null))
        {
            double head = track.Nodes[0].TimeMs, spanDuration = track.Nodes[^1].TimeMs - head;
            var first = new List<MapPoint>();
            int steps = Math.Clamp(8192 / Math.Max(1, track.Nodes.Count - 1), 2, 64);
            for (int segment = 0; segment + 1 < track.Nodes.Count; segment++)
            {
                if (segment == 0) first.Add(CurveMath.Evaluate(track, segment, 0));
                for (int step = 1; step <= steps; step++) first.Add(CurveMath.Evaluate(track, segment, step / (double)steps));
            }
            AddSpans(first, head, spanDuration, track.SpanCount);
        }
        foreach (var slider in map.ImportedSliders)
        {
            var geometry = slider.GetGeometry();
            double velocity = ImportedSliderConverter.VelocityAt(map, slider, timing);
            AddSpans(geometry.TimeXPoints(slider, velocity), slider.TimeMs, geometry.Distance / velocity, slider.SpanCount);
        }
        double minX = 512, maxX = 0;
        foreach (var item in objects) IncludeX(item.X);
        foreach (var path in paths)
            foreach (var point in path) IncludeX(point.X);
        return new(Array.AsReadOnly(objects), paths.AsReadOnly(), PatternPlacement.Parents(map).Min(p => p.Time),
            PatternPlacement.EndTime(map, timing), map.CircleSize, map.ApproachRate, (minX + maxX) / 2,
            HyperDashCalculator.GetHyperDashStarts(objects, map.CircleSize));

        void IncludeX(double x) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); }

        void AddSpans(IReadOnlyList<MapPoint> first, double head, double duration, int count)
        {
            // Bound cached preview memory independently of a map's allowed nested-object budget.
            totalPathPoints += (long)first.Count * count;
            if (totalPathPoints > 1_000_000) throw new InvalidDataException(L.Get("gallery.error.previewLimit"));
            for (int span = 0; span < count; span++)
            {
                var points = new MapPoint[first.Count];
                for (int i = 0; i < points.Length; i++)
                {
                    var point = first[span % 2 == 0 ? i : first.Count - 1 - i];
                    double offset = point.TimeMs - head;
                    points[i] = new(head + span * duration + (span % 2 == 0 ? offset : duration - offset), Math.Clamp(point.X, 0, 512));
                }
                paths.Add(Array.AsReadOnly(points));
            }
        }
    }
}
