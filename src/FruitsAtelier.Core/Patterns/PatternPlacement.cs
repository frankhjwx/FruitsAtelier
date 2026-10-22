using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

/// <summary>A detached, retimed pattern. Cache this per timing snapshot and snapped placement time.</summary>
public sealed class PreparedPatternPlacement
{
    private readonly MapDocument document;
    public double StartTimeMs { get; }
    public double EndTimeMs { get; }
    public double HorizontalScale { get; }
    public double MinimumOffsetX { get; }
    public double MaximumOffsetX { get; }

    internal PreparedPatternPlacement(MapDocument document, double start, double end, double scale, double minX, double maxX)
    {
        this.document = document;
        StartTimeMs = start; EndTimeMs = end; HorizontalScale = scale;
        MinimumOffsetX = -minX; MaximumOffsetX = 512 - maxX;
    }

    public double ClampOffsetX(double offsetX)
    {
        if (!double.IsFinite(offsetX)) throw new ArgumentOutOfRangeException(nameof(offsetX));
        return Math.Clamp(offsetX, MinimumOffsetX, MaximumOffsetX);
    }

    /// <summary>Materializes a new insertion; only its object collections should be appended to the target.</summary>
    public MapDocument CreateDocument(double offsetX = 0, int firstSourceOrder = 0)
    {
        var result = document.DeepClone();
        var parents = PatternPlacement.Parents(result).OrderBy(p => p.Time).ThenBy(p => p.Order).ToArray();
        if (firstSourceOrder < 0 || (long)firstSourceOrder + parents.Length >= int.MaxValue)
            throw new InvalidOperationException(L.Get("gallery.error.sourceOrder"));
        var ids = parents.Select((p, index) => (p.Id, NewId: Guid.NewGuid(), Order: firstSourceOrder + index)).ToDictionary(p => p.Id);
        offsetX = ClampOffsetX(offsetX);
        foreach (var fruit in result.Fruits)
        {
            var id = ids[fruit.Id]; fruit.Id = id.NewId; fruit.SourceOrder = id.Order; fruit.X += offsetX;
            fruit.OriginalLine = PatternPlacement.UpdateLine(fruit.OriginalLine, (0, fruit.X), (2, fruit.TimeMs));
        }
        foreach (var track in result.Tracks) RemapTrack(track, ids[track.Id].NewId, ids[track.Id].Order);
        foreach (var slider in result.ImportedSliders) RemapSlider(slider, ids[slider.Id].NewId, ids[slider.Id].Order);
        foreach (var shower in result.BananaShowers)
        {
            var id = ids[shower.Id]; shower.Id = id.NewId; shower.SourceOrder = id.Order;
        }
        for (int i = 0; i < result.HitsoundOverrides.Count; i++)
        {
            var item = result.HitsoundOverrides[i];
            result.HitsoundOverrides[i] = item with { SourceId = ids[item.SourceId].NewId };
        }
        OsuBeatmapReader.Validate(result);
        var errors = CurveMath.Validate(result);
        if (errors.Count > 0) throw new InvalidDataException(errors[0]);
        return result;

        void RemapTrack(CurveTrack track, Guid id, int order)
        {
            track.Id = id; track.SourceOrder = order;
            foreach (var node in track.Nodes)
            {
                node.Id = Guid.NewGuid(); node.X += offsetX;
                if (node.OutgoingCurve is { } curve)
                    foreach (var control in curve.Controls) control.Id = Guid.NewGuid();
            }
        }
        void RemapSlider(ImportedSlider slider, Guid id, int order)
        {
            slider.Id = id; slider.SourceOrder = order; slider.X += offsetX;
            for (int i = 0; i < slider.ControlPoints.Count; i++)
                slider.ControlPoints[i] = slider.ControlPoints[i] with { X = slider.ControlPoints[i].X + offsetX };
            // Retained source lines carry samples and combo flags; geometry is emitted from the model.
            slider.OriginalLine = PatternPlacement.UpdateLine(slider.OriginalLine, (0, slider.X), (2, slider.TimeMs));
            if (slider.RandomizationSource is { } source) RemapSlider(source, id, order);
            if (slider.RandomizationTrack is { } track) RemapTrack(track, id, order);
        }
    }
}

public static class PatternPlacement
{
    public static PreparedPatternPlacement Prepare(PatternSnapshot snapshot, MapDocument target,
        double snappedStartTimeMs, bool preserveAbsoluteSpeed = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);
        if (!double.IsFinite(snappedStartTimeMs) || snappedStartTimeMs < 0 || snappedStartTimeMs > int.MaxValue)
            throw new InvalidDataException(L.Get("gallery.error.timeRange"));
        var source = snapshot.CreateDocument();
        var sourceTiming = new TimingMap.Lookup(source);
        var targetTiming = new TimingMap.Lookup(target);
        double origin = snapshot.StartTimeMs;
        double sourceEnd = EndTime(source, sourceTiming);
        double MapTime(double time) => targetTiming.OffsetBeats(snappedStartTimeMs, BeatsBetween(sourceTiming, origin, time));
        double xScale = preserveAbsoluteSpeed ? AffineScale(origin, sourceEnd) : 1;
        double xOrigin = Parents(source).OrderBy(p => p.Time).ThenBy(p => p.Order).Select(p =>
            source.Fruits.FirstOrDefault(f => f.Id == p.Id)?.X
            ?? source.Tracks.FirstOrDefault(t => t.Id == p.Id)?.Nodes[0].X
            ?? source.ImportedSliders.FirstOrDefault(s => s.Id == p.Id)?.X).FirstOrDefault(x => x is not null) ?? 256;
        var result = source.DeepClone();
        foreach (var fruit in result.Fruits)
        {
            fruit.TimeMs = MapTime(fruit.TimeMs); fruit.X = ScaleX(fruit.X);
            fruit.OriginalLine = UpdateLine(fruit.OriginalLine, (0, fruit.X), (2, fruit.TimeMs));
        }
        foreach (var track in result.Tracks) TransformTrack(track);
        for (int i = 0; i < result.ImportedSliders.Count; i++)
        {
            var slider = result.ImportedSliders[i];
            if (slider.RandomizationSource is { } original)
            {
                // Processed SV/path may be unrepresentable in the target; refresh from the authored baseline.
                var restored = original.DeepClone();
                restored.Id = slider.Id; restored.SourceOrder = slider.SourceOrder;
                restored.RandomizationSource = original.DeepClone();
                restored.RandomizationTrack = slider.RandomizationTrack?.DeepClone();
                restored.RandomizationBaseVelocity = slider.RandomizationBaseVelocity;
                restored.ProcessedSliderVelocityMultiplier = null;
                restored.SliderVelocityMultiplier = slider.SliderVelocityMultiplier;
                result.ImportedSliders[i] = slider = restored;
            }
            TransformSlider(slider);
        }
        foreach (var shower in result.BananaShowers)
        {
            shower.TimeMs = MapTime(shower.TimeMs); shower.EndTimeMs = MapTime(shower.EndTimeMs);
            shower.OriginalLine = UpdateLine(shower.OriginalLine, (2, shower.TimeMs), (5, shower.EndTimeMs));
        }
        double end = MapTime(sourceEnd);
        if (!double.IsFinite(end) || end > int.MaxValue) throw new InvalidDataException(L.Get("gallery.error.timeRange"));
        result.BeatLengthMs = target.BeatLengthMs; result.TimingOffsetMs = target.TimingOffsetMs;
        result.TimingPoints.Clear(); result.TimingPoints.AddRange(target.TimingPoints.Select(p => p.DeepClone()));
        result.SliderMultiplier = target.SliderMultiplier; result.SliderMultiplierOverride = target.SliderMultiplierOverride;
        result.OverrideSliderMultiplier = target.OverrideSliderMultiplier;
        result.SliderTickRate = target.SliderTickRate; result.LegacySliderTicks = target.LegacySliderTicks;
        result.CircleSize = target.CircleSize; result.ApproachRate = target.ApproachRate;
        result.DerandomizeFSliderDroplets = target.DerandomizeFSliderDroplets;
        result.DerandomizeDropletsForHardRock = target.DerandomizeDropletsForHardRock;
        result.RandomizeDropletStrength = target.RandomizeDropletStrength; result.RandomizeDropletSeed = target.RandomizeDropletSeed;
        result.RandomizeNewSliders = target.RandomizeNewSliders; result.DerandomizeDroplets = target.DerandomizeDroplets;
        result.DurationMs = Math.Max(1, end);
        var (minX, maxX) = Bounds(result);
        if (maxX - minX > 512) throw new InvalidDataException(L.Get("gallery.error.tooWide"));
        return new(result, snappedStartTimeMs, end, xScale, minX, maxX);

        double ScaleX(double x) => xOrigin + (x - xOrigin) * xScale;
        double AffineScale(double start, double finish)
        {
            double mapped = MapTime(start);
            double scale = targetTiming.At(mapped).BeatLengthMs / sourceTiming.At(start).BeatLengthMs;
            if (finish <= start) return scale;
            bool Matches(double t) => Math.Abs(MapTime(t) - (mapped + (t - start) * scale)) <= 1e-6;
            if (!Matches(finish)) throw new InvalidDataException(L.Get("gallery.error.variableTiming"));
            foreach (double boundary in sourceTiming.RedTimes)
                if (boundary > start && boundary < finish && !Matches(boundary))
                    throw new InvalidDataException(L.Get("gallery.error.variableTiming"));
            double mappedEnd = MapTime(finish);
            foreach (double boundary in targetTiming.RedTimes)
            {
                if (boundary <= mapped || boundary >= mappedEnd) continue;
                double original = sourceTiming.OffsetBeats(origin, BeatsBetween(targetTiming, snappedStartTimeMs, boundary));
                if (!Matches(original)) throw new InvalidDataException(L.Get("gallery.error.variableTiming"));
            }
            return scale;
        }
        void TransformTrack(CurveTrack track)
        {
            double head = track.Nodes[0].TimeMs;
            double scale = AffineScale(head, CurveMath.EndTimeMs(track));
            TimingEditing.TransformTrack(track, head, MapTime(head), scale);
            track.DropletMode = SliderDropletMode.FollowMap;
            track.CompensateTinyDroplets = null;
            foreach (var node in track.Nodes)
            {
                node.X = ScaleX(node.X);
                node.HandleIn = node.HandleIn with { X = node.HandleIn.X * xScale };
                node.HandleOut = node.HandleOut with { X = node.HandleOut.X * xScale };
                if (node.OutgoingCurve is { } curve)
                {
                    // Scaling this reference with X preserves circular arcs under time/X transforms.
                    curve.ReferenceScale *= xScale;
                    foreach (var control in curve.Controls) control.Offset = control.Offset with { X = control.Offset.X * xScale };
                }
            }
            if (track.Stack is { } stack)
            {
                stack.Points = stack.Points.Select(p => p with { Distance = p.Distance * xScale }).ToList();
                stack.FruitAdjustments = stack.FruitAdjustments.Select(p => p with { Offset = p.Offset * xScale }).ToList();
            }
            if (track.DropletRandomization is { } effect)
                effect.Adjustments = effect.Adjustments.Select(p => p with { Offset = p.Offset * xScale }).ToList();
            track.SliderVelocitySeed = null; track.FixedSliderVelocityMultiplier = null;
            track.OriginalLine = UpdateLine(track.OriginalLine, (2, track.Nodes[0].TimeMs));
        }
        void TransformSlider(ImportedSlider slider)
        {
            double head = slider.TimeMs;
            double scale = AffineScale(head, head + ImportedSliderConverter.DurationMs(source, slider, sourceTiming));
            double svScale = source.SliderMultiplier / target.SliderMultiplier * xScale
                * targetTiming.At(MapTime(head)).BeatLengthMs / sourceTiming.At(head).BeatLengthMs / scale;
            double own = slider.SliderVelocityMultiplier ?? sourceTiming.At(head).SliderVelocityMultiplier
                * source.EffectiveSliderMultiplier / source.SliderMultiplier;
            slider.SliderVelocityMultiplier = own * svScale;
            if (slider.ProcessedSliderVelocityMultiplier is { } processed) slider.ProcessedSliderVelocityMultiplier = processed * svScale;
            if (slider.RandomizationBaseVelocity is { } baseline) slider.RandomizationBaseVelocity = baseline * svScale;
            slider.TimeMs = MapTime(head); slider.X = ScaleX(slider.X);
            // osu geometric Y is not time. Uniform path scaling keeps the original arc-length progression.
            for (int i = 0; i < slider.ControlPoints.Count; i++)
            {
                var point = slider.ControlPoints[i];
                slider.ControlPoints[i] = new(ScaleX(point.X), slider.Y + (point.GeometryY - slider.Y) * xScale);
            }
            slider.PixelLength *= xScale;
            slider.OriginalLine = UpdateLine(slider.OriginalLine, (0, slider.X), (2, slider.TimeMs), (7, slider.PixelLength));
            if (slider.RandomizationSource is { } original) TransformSlider(original);
            if (slider.RandomizationTrack is { } track) TransformTrack(track);
        }
    }

    internal static double BeatsBetween(TimingMap.Lookup timing, double start, double end)
    {
        if (end < start) return -BeatsBetween(timing, end, start);
        double beats = 0, previous = start;
        foreach (double boundary in timing.RedTimes)
        {
            if (boundary <= start) continue;
            if (boundary >= end) break;
            beats += (boundary - previous) / timing.At(previous).BeatLengthMs; previous = boundary;
        }
        return beats + (end - previous) / timing.At(previous).BeatLengthMs;
    }

    internal static IEnumerable<(Guid Id, double Time, int Order)> Parents(MapDocument map)
        => map.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder))
            .Concat(map.Tracks.Select(t => (t.Id, t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(map.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
            .Concat(map.BananaShowers.Select(s => (s.Id, s.TimeMs, s.SourceOrder)));

    internal static double EndTime(MapDocument map, TimingMap.Lookup timing)
        => map.Fruits.Select(f => f.TimeMs).Concat(map.Tracks.Select(CurveMath.EndTimeMs))
            .Concat(map.ImportedSliders.Select(s => s.TimeMs + ImportedSliderConverter.DurationMs(map, s, timing)))
            .Concat(map.BananaShowers.Select(s => s.EndTimeMs)).DefaultIfEmpty(0).Max();

    internal static string? UpdateLine(string? line, params (int Index, double Value)[] updates)
    {
        if (line is null) return null;
        var fields = line.Split(',');
        foreach (var (index, value) in updates)
            if (index < fields.Length) fields[index] = value.ToString("R", CultureInfo.InvariantCulture);
        return string.Join(',', fields);
    }

    private static (double Min, double Max) Bounds(MapDocument map)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        void Include(double x) { min = Math.Min(min, x); max = Math.Max(max, x); }
        foreach (var fruit in map.Fruits) Include(fruit.X);
        foreach (var slider in map.ImportedSliders)
            foreach (var point in ImportedSliderConverter.Path(slider)) Include(point.X);
        foreach (var track in map.Tracks)
        {
            double trackMin = double.PositiveInfinity, trackMax = double.NegativeInfinity;
            void TrackInclude(double x) { trackMin = Math.Min(trackMin, x); trackMax = Math.Max(trackMax, x); }
            foreach (var node in track.Nodes) TrackInclude(node.X);
            for (int segment = 0; segment + 1 < track.Nodes.Count; segment++)
            {
                var points = ControlCurveMath.Points(track, segment);
                var curve = track.Nodes[segment].OutgoingCurve;
                if (curve?.Kind == ControlCurveKind.CircularArc)
                {
                    if (!ControlCurveMath.TryArc(points, curve.ReferenceScale, out var arc)) continue;
                    double lo = Math.Min(arc.Angle, arc.Angle + arc.Sweep), hi = Math.Max(arc.Angle, arc.Angle + arc.Sweep);
                    for (int k = (int)Math.Ceiling(lo / Math.PI); k * Math.PI <= hi; k++) TrackInclude(arc.X + arc.Radius * Math.Cos(k * Math.PI));
                }
                else BoundBezier(points.Select(p => p.X).ToArray(), TrackInclude, 0);
            }
            double envelope = track.Stack?.Points.Max(p => p.Distance) ?? 0;
            double correction = track.Stack?.FruitAdjustments.Select(p => Math.Abs(p.Offset)).DefaultIfEmpty(0).Max() ?? 0;
            Include(trackMin - envelope - correction); Include(trackMax + envelope + correction);
        }
        return double.IsFinite(min) ? (min, max) : (256, 256);
    }

    private static void BoundBezier(double[] points, Action<double> include, int depth)
    {
        double min = points.Min(), max = points.Max();
        double endpointMin = Math.Min(points[0], points[^1]), endpointMax = Math.Max(points[0], points[^1]);
        if (depth >= 20 || endpointMin - min < 1e-5 && max - endpointMax < 1e-5)
        { include(min); include(max); return; }
        var work = (double[])points.Clone(); var left = new double[points.Length]; var right = new double[points.Length];
        left[0] = work[0]; right[^1] = work[^1];
        for (int count = work.Length - 1; count > 0; count--)
        {
            for (int i = 0; i < count; i++) work[i] = (work[i] + work[i + 1]) / 2;
            left[work.Length - count] = work[0]; right[count - 1] = work[count - 1];
        }
        BoundBezier(left, include, depth + 1); BoundBezier(right, include, depth + 1);
    }
}
