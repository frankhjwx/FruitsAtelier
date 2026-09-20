using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class DistanceSpacingEditing
{
    // Call inside an editing transaction: conversion and geometry validation may reject the change.
    public static void Apply(MapDocument document, ConvertedCatchObject target, ConvertedCatchObject reference,
        bool next, double ratio, bool compensateTinyDroplets)
    {
        if (!double.IsFinite(ratio) || ratio < 0) throw new ArgumentException(L.Get("distance.nonnegative"));
        double duration = next ? reference.TimeMs - target.TimeMs : target.TimeMs - reference.TimeMs;
        if (duration <= 0) throw new ArgumentException(L.Get("distance.interval"));
        double distance = duration * DistanceSnap.BaseVelocity(document, next ? target.TimeMs : reference.TimeMs) * ratio;
        int direction = Math.Sign(target.X - reference.X);
        if (direction == 0 && distance > 0) throw new ArgumentException(L.Get("distance.direction"));
        double x = reference.X + direction * distance;
        if (!double.IsFinite(x) || x is < 0 or > 512) throw new ArgumentException(L.Get("distance.outside"));
        if (Math.Abs(x - target.X) < .00001) return;
        if (document.Fruits.FirstOrDefault(f => f.Id == target.SourceId) is { } fruit)
        {
            fruit.X = x;
            return;
        }
        if (target.Kind is not (CatchObjectKind.Fruit or CatchObjectKind.Droplet))
            throw new ArgumentException(L.Get("distance.unsupported"));
        var track = document.Tracks.FirstOrDefault(t => t.Id == target.SourceId)
            ?? ImportedSliderEditing.ConvertToTrack(document, target.SourceId).Track;
        double time = CurveMath.FirstSpanTime(track, target.TimeMs);
        if (reference.SourceId == target.SourceId)
        {
            double referenceTime = CurveMath.FirstSpanTime(track, reference.TimeMs);
            if (Math.Abs(referenceTime - time) < CurveMath.MinimumAnchorSpacingMs)
                throw new ArgumentException(L.Get("distance.sharedRepeat"));
            var fixedReference = AnchorAt(track, referenceTime);
            if (!CurveMath.TryMoveAnchor(track, fixedReference.Id, fixedReference.TimeMs, reference.X, out string error))
                throw new ArgumentException(error);
        }
        var anchor = AnchorAt(track, time);
        if (!CurveMath.TryMoveAnchor(track, anchor.Id, anchor.TimeMs, x, out string failure))
            throw new ArgumentException(failure);
        var converted = CatchStreamConverter.Convert(document, compensateTinyDroplets);
        var moved = converted.Objects.FirstOrDefault(o => o.SourceId == target.SourceId && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
        var fixedObject = converted.Objects.FirstOrDefault(o => o.SourceId == reference.SourceId && o.Kind == reference.Kind && Math.Abs(o.TimeMs - reference.TimeMs) < .001);
        if (!converted.Success || moved is null || fixedObject is null || Math.Abs(moved.X - x) > .001 || Math.Abs(fixedObject.X - reference.X) > .001)
            throw new ArgumentException(L.Get("distance.unreachable"));
    }

    private static Anchor AnchorAt(CurveTrack track, double time)
    {
        var existing = track.Nodes.FirstOrDefault(n => Math.Abs(n.TimeMs - time) < .000001);
        if (existing is not null) return existing;
        int segment = track.Nodes.FindIndex(n => n.TimeMs > time) - 1;
        if (segment < 0 || segment + 1 >= track.Nodes.Count) throw new ArgumentException(L.Get("distance.unreachable"));
        double low = 0, high = 1;
        for (int i = 0; i < 60; i++)
        {
            double mid = (low + high) / 2;
            if (CurveMath.Evaluate(track, segment, mid).TimeMs < time) low = mid; else high = mid;
        }
        CurveMath.Split(track, segment, (low + high) / 2);
        return track.Nodes[segment + 1];
    }
}
