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
        ApplyPosition(document, target, reference, x, compensateTinyDroplets);
    }

    public static void ApplyX(MapDocument document, ConvertedCatchObject target, double x, bool compensateTinyDroplets, CatchConversionCache? cache = null)
    {
        if (!double.IsFinite(x)) throw new ArgumentException(L.Get("editor.error.finiteNumberRequired"));
        ApplyPosition(document, target, null, Math.Clamp(x, 0, 512), compensateTinyDroplets, cache);
    }

    // Isolate a drag between its adjacent converted events; existing handles need not keep those events fixed.
    public static void ApplyIsolatedX(MapDocument document, ConvertedCatchObject target, double x, bool compensateTinyDroplets, CatchConversionCache? cache = null)
    {
        if (!double.IsFinite(x)) throw new ArgumentException(L.Get("editor.error.finiteNumberRequired"));
        x = Math.Clamp(x, 0, 512);
        if (Math.Abs(x - target.X) < .00001) return;
        if (target.Kind is not (CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet))
            throw new ArgumentException(L.Get("distance.unsupported"));

        var track = document.Tracks.FirstOrDefault(t => t.Id == target.SourceId)
            ?? throw new ArgumentException(L.Get("distance.unsupported"));
        var before = CatchStreamConverter.Convert(document, compensateTinyDroplets, cache);
        if (!before.Success) throw new ArgumentException(L.Get("coordinate.unreachable"));
        var siblings = before.Objects.Where(o => o.SourceId == target.SourceId).ToArray();
        var selected = siblings.FirstOrDefault(o => o.EventIndex == target.EventIndex);
        if (selected is null) throw new ArgumentException(L.Get("coordinate.unreachable"));
        var slider = before.Sliders.Single(s => s.SourceId == track.Id);
        double start = track.Nodes[0].TimeMs, duration = track.Nodes[^1].TimeMs - start;
        var nested = LegacyCatchRules.CreateNested(start, duration, slider.Velocity,
            slider.TickDistance, slider.Length, track.SpanCount);
        // Uncompensated tiny droplets sample path progress, which differs from their rounded event time.
        double SampleTime(ConvertedCatchObject item) => item.Kind == CatchObjectKind.TinyDroplet && !slider.TinyCompensationApplied
            ? start + nested[item.EventIndex].Progress * duration
            : CurveMath.FirstSpanTime(track, item.TimeMs);
        double time = SampleTime(selected);
        // Repeated traversals share the same path sample, so their droplets move together.
        var linkedEvents = siblings.Where(o => o.Kind == selected.Kind
            && Math.Abs(SampleTime(o) - time) < .000001).Select(o => o.EventIndex).ToHashSet();
        var times = siblings.Select(SampleTime)
            .Distinct().OrderBy(t => t).ToArray();
        double? previous = times.Where(t => t < time - CurveMath.MinimumAnchorSpacingMs).Select(t => (double?)t).LastOrDefault();
        double? next = times.Where(t => t > time + CurveMath.MinimumAnchorSpacingMs).Select(t => (double?)t).FirstOrDefault();
        if (previous is null || next is null)
            throw new ArgumentException(L.Get("coordinate.unreachable"));

        var left = AnchorAt(track, previous.Value);
        var right = AnchorAt(track, next.Value);
        var anchor = AnchorAt(track, time);
        track.Nodes.RemoveAll(n => n != anchor && n.TimeMs > left.TimeMs && n.TimeMs < right.TimeMs);
        int index = track.Nodes.IndexOf(anchor);
        if (index <= 0 || index >= track.Nodes.Count - 1 || track.Nodes[index - 1] != left || track.Nodes[index + 1] != right)
            throw new ArgumentException(L.Get("coordinate.unreachable"));
        left.OutgoingCurve = null; left.OutgoingKind = CurveKind.Linear; left.HandleOut = default;
        anchor.HandleIn = default; anchor.OutgoingCurve = null; anchor.OutgoingKind = CurveKind.Linear; anchor.HandleOut = default;
        right.HandleIn = default;
        double pathX = selected.Kind == CatchObjectKind.TinyDroplet && !slider.TinyCompensationApplied
            ? x - selected.RandomOffset : x;
        if (pathX is < 0 or > 512 || !CurveMath.TryMoveAnchor(track, anchor.Id, time, pathX, out _))
            throw new ArgumentException(L.Get("coordinate.unreachable"));

        var after = CatchStreamConverter.Convert(document, compensateTinyDroplets, cache);
        if (!after.Success) throw new ArgumentException(L.Get("coordinate.unreachable"));
        var updated = after.Objects.Where(o => o.SourceId == target.SourceId).ToDictionary(o => o.EventIndex);
        if (updated.Count != siblings.Length || siblings.Any(o => !updated.TryGetValue(o.EventIndex, out var current)
            || current.Kind != o.Kind || Math.Abs(current.TimeMs - o.TimeMs) > .001
            || Math.Abs(current.X - (linkedEvents.Contains(o.EventIndex) ? o.X + x - selected.X : o.X)) > .001))
            throw new ArgumentException(L.Get("coordinate.unreachable"));
    }

    private static void ApplyPosition(MapDocument document, ConvertedCatchObject target, ConvertedCatchObject? reference,
        double x, bool compensateTinyDroplets, CatchConversionCache? cache = null)
    {
        if (Math.Abs(x - target.X) < .00001) return;
        if (document.Fruits.FirstOrDefault(f => f.Id == target.SourceId) is { } fruit)
        {
            fruit.X = x;
            return;
        }
        if (target.Kind is not (CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet))
            throw new ArgumentException(L.Get("distance.unsupported"));
        var track = document.Tracks.FirstOrDefault(t => t.Id == target.SourceId)
            ?? ImportedSliderEditing.ConvertToTrack(document, target.SourceId).Track;
        double time = CurveMath.FirstSpanTime(track, target.TimeMs);
        if (reference is not null && reference.SourceId == target.SourceId)
        {
            double referenceTime = CurveMath.FirstSpanTime(track, reference.TimeMs);
            if (Math.Abs(referenceTime - time) < CurveMath.MinimumAnchorSpacingMs)
                throw new ArgumentException(L.Get("distance.sharedRepeat"));
            var fixedReference = AnchorAt(track, referenceTime);
            if (!CurveMath.TryMoveAnchor(track, fixedReference.Id, fixedReference.TimeMs, reference.X, out string error))
                throw new ArgumentException(error);
        }
        var anchor = AnchorAt(track, time);
        // Compensated tiny droplets follow the authored curve; uncompensated ones retain their random offset.
        double pathX = target.Kind == CatchObjectKind.TinyDroplet && Math.Abs(target.X - target.TargetX) > .001
            ? x - target.RandomOffset : x;
        if (pathX is < 0 or > 512) throw new ArgumentException(L.Get("distance.outside"));
        if (!CurveMath.TryMoveAnchor(track, anchor.Id, anchor.TimeMs, pathX, out string failure))
            throw new ArgumentException(failure);
        var converted = CatchStreamConverter.Convert(document, compensateTinyDroplets, cache);
        var moved = converted.Objects.FirstOrDefault(o => o.SourceId == target.SourceId && o.Kind == target.Kind && Math.Abs(o.TimeMs - target.TimeMs) < .001);
        var fixedObject = reference is null ? null : converted.Objects.FirstOrDefault(o => o.SourceId == reference.SourceId && o.Kind == reference.Kind && Math.Abs(o.TimeMs - reference.TimeMs) < .001);
        if (!converted.Success || moved is null || Math.Abs(moved.X - x) > .001
            || reference is not null && (fixedObject is null || Math.Abs(fixedObject.X - reference.X) > .001))
            throw new ArgumentException(L.Get(reference is null ? "coordinate.unreachable" : "distance.unreachable"));
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
