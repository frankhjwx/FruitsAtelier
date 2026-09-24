using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool TryDistanceShape(CurveTrack track, Func<bool> apply)
    {
        if (!DistanceSnapEnabled || track.StreamSnapDivisor is not null) return apply();
        var original = track.DeepClone();
        var before = CatchStreamConverter.Convert(Document, compensateTinyDroplets, editorConversionCache);
        var baseline = SliderDistanceSnap.Excesses(Document, before.Objects, track.Id);
        var strictBaseline = SliderDistanceSnap.StrictErrors(Document, original, before.Objects);
        bool accepted = false;
        try
        {
            if (apply())
            {
                // A first draft anchor has no event interval to constrain yet.
                if (track.Nodes.Count < 2) { accepted = true; return true; }
                var after = CatchStreamConverter.Convert(Document, compensateTinyDroplets, editorConversionCache);
                accepted = after.Objects.Any(item => item.SourceId == track.Id)
                    && after.Sliders.Any(slider => slider.SourceId == track.Id)
                    && SliderDistanceSnap.Allows(baseline, SliderDistanceSnap.Excesses(Document, after.Objects, track.Id))
                    && SliderDistanceSnap.Allows(strictBaseline,
                        SliderDistanceSnap.StrictErrors(Document, track, after.Objects));
            }
        }
        finally
        {
            if (!accepted) RestoreDistanceShape(track, original);
        }
        return accepted;
    }

    private static void RestoreDistanceShape(CurveTrack track, CurveTrack original)
    {
        track.Kind = original.Kind;
        track.SpanCount = original.SpanCount;
        // Input handlers retain anchor references while trying candidate positions.
        // Restore their values as well as the list, so a rejected X cannot become the clamp origin.
        var existing = track.Nodes.ToDictionary(node => node.Id);
        track.Nodes.Clear();
        foreach (var saved in original.DeepClone().Nodes)
        {
            if (!existing.TryGetValue(saved.Id, out var node)) node = saved;
            else
            {
                node.TimeMs = saved.TimeMs; node.X = saved.X;
                node.HandleIn = saved.HandleIn; node.HandleOut = saved.HandleOut;
                node.OutgoingKind = saved.OutgoingKind; node.OutgoingCurve = saved.OutgoingCurve;
            }
            track.Nodes.Add(node);
        }
    }

    private IEnumerable<MapPoint> StraightSliderCandidates(MapPoint target, MapPoint fixedPoint)
    {
        double velocity = DistanceSnap.BaseVelocity(Document, Math.Min(target.TimeMs, fixedPoint.TimeMs));
        double dt = Math.Abs(target.TimeMs - fixedPoint.TimeMs);
        return new[] { 0d }.Concat(Document.DistanceSnapRatios.Take(DistanceSnap.MaximumPresets)
                .Where(ratio => double.IsFinite(ratio) && ratio > 0))
            .SelectMany(ratio => ratio == 0 ? new[] { fixedPoint.X }
                : new[] { fixedPoint.X - dt * velocity * ratio, fixedPoint.X + dt * velocity * ratio })
            .Where(position => position is >= 0 and <= 512)
            .Distinct()
            .OrderBy(position => Math.Abs(position - target.X))
            .Select(position => target with { X = position });
    }

    private IEnumerable<MapPoint> CurvedSliderCandidates(MapPoint target, double currentX)
    {
        yield return target;
        for (int step = 1; step <= 32; step++)
            yield return target with { X = target.X + (currentX - target.X) * step / 32.0 };
    }

    private bool ClampDistanceDrag(MapPoint target, double currentX, Func<MapPoint, bool> apply)
    {
        double blocked = target.X;
        foreach (var candidate in CurvedSliderCandidates(target, currentX))
        {
            if (!apply(candidate)) { blocked = candidate.X; continue; }
            double accepted = candidate.X;
            for (int i = 0; i < 12 && Math.Abs(blocked - accepted) > .001; i++)
            {
                double middle = (blocked + accepted) / 2;
                if (apply(target with { X = middle })) accepted = middle;
                else blocked = middle;
            }
            return true;
        }
        return false;
    }

    private Anchor? AppendDistanceAnchor(CurveTrack track, MapPoint target, bool straight)
    {
        if (!DistanceSnapEnabled || track.Nodes.Count == 0) return AppendDraftAnchor(track, target, straight);
        var previous = track.Nodes[^1];
        var candidates = straight
            ? StraightSliderCandidates(target, new(previous.TimeMs, previous.X))
            : CurvedSliderCandidates(target, previous.X);
        foreach (var point in candidates)
        {
            Anchor? added = null;
            if (TryDistanceShape(track, () => (added = AppendDraftAnchor(track, point, straight)) is not null))
                return added;
        }
        return null;
    }

    private bool StrictSegment(CurveTrack track, int index)
    {
        var from = track.Nodes[index]; var to = track.Nodes[index + 1];
        double? ratio = DistanceSnap.Ratio(new(from.TimeMs, from.X), new(to.TimeMs, to.X),
            DistanceSnap.BaseVelocity(Document, from.TimeMs));
        return ratio is { } value && (value < 1e-6 || Document.DistanceSnapRatios
            .Take(DistanceSnap.MaximumPresets).Any(preset => double.IsFinite(preset) && preset > 0
                && Math.Abs(value - preset) < 1e-6));
    }

    private bool TryMoveDistanceAnchor(CurveTrack track, Anchor node, MapPoint target)
    {
        double currentX = node.X;
        int index = track.Nodes.IndexOf(node);
        int previous = index - 1, next = index;
        bool beforeStraight = previous >= 0 && track.Nodes[previous].OutgoingCurve is null
            && CurveMath.SegmentKind(track, previous) == CurveKind.Linear;
        bool afterStraight = next < track.Nodes.Count - 1 && node.OutgoingCurve is null
            && CurveMath.SegmentKind(track, next) == CurveKind.Linear;
        bool Try(MapPoint position) => TryDistanceShape(track, () =>
            CurveMath.TryMoveAnchor(track, node.Id, position.TimeMs, position.X, out _)
            && (!DistanceSnapEnabled || (!beforeStraight || StrictSegment(track, previous))
                && (!afterStraight || StrictSegment(track, next))));
        if (DistanceSnapEnabled && (beforeStraight || afterStraight))
        {
            var fixedNode = beforeStraight ? track.Nodes[previous] : track.Nodes[next + 1];
            foreach (var position in StraightSliderCandidates(target, new(fixedNode.TimeMs, fixedNode.X)))
                if (Try(position)) return true;
            return false;
        }
        return DistanceSnapEnabled ? ClampDistanceDrag(target, currentX, Try) : Try(target);
    }
}
