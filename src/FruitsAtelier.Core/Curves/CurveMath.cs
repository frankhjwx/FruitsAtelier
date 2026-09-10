using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static class CurveMath
{
    public const double MinimumAnchorSpacingMs = 0.001;

    public static CurveKind SegmentKind(CurveTrack track, int segment)
    {
        CheckSegment(track, segment);
        return track.Nodes[segment].OutgoingKind ?? track.Kind;
    }

    public static double EndTimeMs(CurveTrack track)
    {
        if (track.Nodes.Count == 0) throw new ArgumentException(L.Get("core.curves.noAnchors"), nameof(track));
        return track.Nodes[0].TimeMs + (track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs) * track.SpanCount;
    }

    public static double FirstSpanTime(CurveTrack track, double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (track.Nodes.Count == 0) throw new ArgumentException(L.Get("core.curves.noAnchors"), nameof(track));
        double start = track.Nodes[0].TimeMs, duration = track.Nodes[^1].TimeMs - start;
        if (duration <= 0 || track.SpanCount < 1) return start;
        double spans = Math.Clamp((time - start) / duration, 0, track.SpanCount);
        int span = Math.Min(track.SpanCount - 1, (int)spans);
        double progress = spans - span;
        return start + (span % 2 == 0 ? progress : 1 - progress) * duration;
    }

    public static MapPoint Evaluate(CurveTrack track, int segment, double u)
    {
        CheckSegment(track, segment);
        if (!double.IsFinite(u) || u < 0 || u > 1) throw new ArgumentOutOfRangeException(nameof(u));
        var start = track.Nodes[segment];
        var end = track.Nodes[segment + 1];
        MapPoint p0 = Point(start), p3 = Point(end);
        if (start.OutgoingCurve is not null) return ControlCurveMath.Evaluate(track, segment, u);
        if (SegmentKind(track, segment) == CurveKind.Linear) return MapPoint.Lerp(p0, p3, u);
        MapPoint p1 = p0 + start.HandleOut, p2 = p3 + end.HandleIn;
        var a = MapPoint.Lerp(p0, p1, u);
        var b = MapPoint.Lerp(p1, p2, u);
        var c = MapPoint.Lerp(p2, p3, u);
        return MapPoint.Lerp(MapPoint.Lerp(a, b, u), MapPoint.Lerp(b, c, u), u);
    }

    public static double PositionAtTime(CurveTrack track, double time)
    {
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (track.Nodes.Count == 0) throw new ArgumentException(L.Get("core.curves.noAnchors"), nameof(track));
        if (track.SpanCount > 1) time = FirstSpanTime(track, time);
        if (time <= track.Nodes[0].TimeMs) return track.Nodes[0].X;
        if (time >= track.Nodes[^1].TimeMs) return track.Nodes[^1].X;
        int lower = 0, upper = track.Nodes.Count - 1;
        while (upper - lower > 1)
        {
            int middle = (lower + upper) / 2;
            if (time > track.Nodes[middle].TimeMs) lower = middle;
            else upper = middle;
        }
        int segment = lower;
        if (track.Nodes[segment].OutgoingCurve is null && SegmentKind(track, segment) == CurveKind.Linear)
        {
            var start = track.Nodes[segment]; var end = track.Nodes[segment + 1];
            return start.X + (end.X - start.X) * (time - start.TimeMs) / (end.TimeMs - start.TimeMs);
        }

        // Handle time offsets make u nonlinear in time; solve the time coordinate before reading X.
        double lo = 0, hi = 1;
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2;
            if (Evaluate(track, segment, mid).TimeMs < time) lo = mid;
            else hi = mid;
        }
        return Evaluate(track, segment, (lo + hi) / 2).X;
    }

    public static void Split(CurveTrack track, int segment, double u)
    {
        CheckSegment(track, segment);
        if (!double.IsFinite(u) || u <= 0 || u >= 1) throw new ArgumentOutOfRangeException(nameof(u));
        var start = track.Nodes[segment]; var end = track.Nodes[segment + 1];
        MapPoint p0 = Point(start), p3 = Point(end), split = Evaluate(track, segment, u);
        if (split.TimeMs <= p0.TimeMs || split.TimeMs >= p3.TimeMs
            || split.TimeMs < p0.TimeMs + MinimumAnchorSpacingMs || p3.TimeMs < split.TimeMs + MinimumAnchorSpacingMs)
            throw new ArgumentOutOfRangeException(nameof(u), L.Get("core.curves.splitSpacing"));
        var added = new Anchor { TimeMs = split.TimeMs, X = split.X, OutgoingKind = start.OutgoingKind };
        if (start.OutgoingCurve is not null)
        {
            ControlCurveEditing.Split(track, segment, u);
            return;
        }
        if (SegmentKind(track, segment) == CurveKind.Bezier)
        {
            MapPoint p1 = p0 + start.HandleOut, p2 = p3 + end.HandleIn;
            var a = MapPoint.Lerp(p0, p1, u);
            var b = MapPoint.Lerp(p1, p2, u);
            var c = MapPoint.Lerp(p2, p3, u);
            var d = MapPoint.Lerp(a, b, u);
            var e = MapPoint.Lerp(b, c, u);
            start.HandleOut = a - p0;
            end.HandleIn = c - p3;
            added.HandleIn = d - split;
            added.HandleOut = e - split;
        }
        track.Nodes.Insert(segment + 1, added);
    }

    public static bool TryMoveAnchor(CurveTrack track, Guid id, double time, double x, out string error)
    {
        var node = track.Nodes.Find(n => n.Id == id);
        if (node is null) { error = L.Get("core.curves.missingAnchor"); return false; }
        double oldTime = node.TimeMs, oldX = node.X;
        node.TimeMs = time;
        node.X = x;
        error = ValidateTrack(track, requireComplete: false).FirstOrDefault() ?? "";
        if (error.Length == 0) return true;
        node.TimeMs = oldTime;
        node.X = oldX;
        return false;
    }

    public static bool TryMoveHandle(CurveTrack track, Guid id, bool incoming, MapPoint offset, out string error)
    {
        var node = track.Nodes.Find(n => n.Id == id);
        if (node is null) { error = L.Get("core.curves.missingAnchor"); return false; }
        MapPoint previous = incoming ? node.HandleIn : node.HandleOut;
        if (incoming) node.HandleIn = offset;
        else node.HandleOut = offset;
        error = ValidateTrack(track, requireComplete: false).FirstOrDefault() ?? "";
        if (error.Length == 0) return true;
        if (incoming) node.HandleIn = previous;
        else node.HandleOut = previous;
        return false;
    }

    public static IReadOnlyList<string> Validate(MapDocument document)
    {
        var errors = new List<string>();
        if (!double.IsFinite(document.DurationMs) || document.DurationMs <= 0) errors.Add(L.Get("core.curves.duration"));
        if (!double.IsFinite(document.BeatLengthMs) || document.BeatLengthMs <= 0) errors.Add(L.Get("core.curves.beatLength"));
        if (!double.IsFinite(document.TimingOffsetMs)) errors.Add(L.Get("core.curves.timingOffset"));
        if (!double.IsFinite(document.ApproachRate) || document.ApproachRate < 0 || document.ApproachRate > 10)
            errors.Add(L.Get("core.curves.approachRate"));
        if (!double.IsFinite(document.CircleSize) || document.CircleSize < 0 || document.CircleSize > 10) errors.Add(L.Get("core.curves.circleSize"));
        if (!double.IsFinite(document.SliderMultiplier) || document.SliderMultiplier <= 0) errors.Add(L.Get("core.curves.sliderMultiplier"));
        if (!double.IsFinite(document.SliderTickRate) || document.SliderTickRate <= 0) errors.Add(L.Get("core.curves.tickRate"));
        var ids = new HashSet<Guid>();
        foreach (var timing in document.TimingPoints)
        {
            if (!double.IsFinite(timing.TimeMs) || double.IsInfinity(timing.BeatLengthMs)
                || timing.Uninherited && (!double.IsFinite(timing.BeatLengthMs) || timing.BeatLengthMs <= 0))
                errors.Add(L.Get("core.curves.timingValues"));
            if (timing.Meter < 1 || timing.Volume is < 0 or > 100) errors.Add(L.Get("core.curves.timingRange"));
        }
        foreach (var fruit in document.Fruits)
        {
            CheckId(fruit.Id, ids, errors);
            if (!IsPositionValid(fruit.TimeMs, fruit.X) || fruit.TimeMs > document.DurationMs)
                errors.Add(L.Get("core.curves.fruitRange"));
        }
        foreach (var track in document.Tracks)
        {
            CheckId(track.Id, ids, errors);
            errors.AddRange(ValidateTrack(track, requireComplete: true));
            if (track.Nodes.Count > 0 && (!double.IsFinite(EndTimeMs(track)) || EndTimeMs(track) > document.DurationMs))
                errors.Add(L.Get("core.curves.repeatEnd"));
            foreach (var node in track.Nodes)
            {
                CheckId(node.Id, ids, errors);
                if (node.OutgoingCurve is { } curve)
                    foreach (var control in curve.Controls) CheckId(control.Id, ids, errors);
                if (node.TimeMs > document.DurationMs) errors.Add(L.Get("core.curves.anchorEnd"));
            }
        }
        foreach (var slider in document.ImportedSliders)
        {
            CheckId(slider.Id, ids, errors);
            if (!double.IsFinite(slider.TimeMs) || slider.TimeMs < 0 || !double.IsFinite(slider.X) || !double.IsFinite(slider.Y)
                || !double.IsFinite(slider.PixelLength) || slider.PixelLength < 0 || slider.SpanCount is < 1 or > 9000
                || slider.ControlPoints.Count is < 1 or > 10000
                || slider.ControlPoints.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.GeometryY)))
                errors.Add(L.Get("core.curves.importedSlider"));
            if (char.ToUpperInvariant(slider.PathType) is not ('L' or 'B' or 'P' or 'C')) errors.Add(L.Get("core.curves.importedPath"));
        }
        foreach (var shower in document.BananaShowers)
        {
            CheckId(shower.Id, ids, errors);
            if (!double.IsFinite(shower.TimeMs) || !double.IsFinite(shower.EndTimeMs) || shower.TimeMs < 0 || shower.EndTimeMs < shower.TimeMs)
                errors.Add(L.Get("core.curves.bananaRange"));
        }
        return errors;
    }

    private static List<string> ValidateTrack(CurveTrack track, bool requireComplete)
    {
        var errors = new List<string>();
        if (requireComplete && track.Nodes.Count < 2) errors.Add(L.Get("core.curves.minimumAnchors"));
        if (track.SpanCount is < 1 or > 9000) errors.Add(L.Get("core.curves.spanCount"));
        if (!Enum.IsDefined(track.Kind)) errors.Add(L.Get("core.curves.curveKind"));
        for (int i = 0; i < track.Nodes.Count; i++)
        {
            var node = track.Nodes[i];
            if (!IsPositionValid(node.TimeMs, node.X)) errors.Add(L.Get("core.curves.anchorRange"));
            if (node.OutgoingKind is CurveKind kind && !Enum.IsDefined(kind)) errors.Add(L.Get("core.curves.segmentKind"));
            bool usesIn = i > 0 && track.Nodes[i - 1].OutgoingCurve is null && SegmentKind(track, i - 1) == CurveKind.Bezier;
            bool usesOut = i + 1 < track.Nodes.Count && node.OutgoingCurve is null && SegmentKind(track, i) == CurveKind.Bezier;
            if (!ValidHandle(node, node.HandleIn, incoming: true, usesIn) || !ValidHandle(node, node.HandleOut, incoming: false, usesOut))
                errors.Add(L.Get("core.curves.handleRange"));
        }
        for (int i = 0; i + 1 < track.Nodes.Count; i++)
        {
            var a = track.Nodes[i]; var b = track.Nodes[i + 1];
            if (b.TimeMs <= a.TimeMs || b.TimeMs < a.TimeMs + MinimumAnchorSpacingMs) errors.Add(L.Get("core.curves.anchorSpacing"));
            if (a.OutgoingCurve is not null && ControlCurveMath.Validate(track, i) is { } curveError) errors.Add(curveError);
            if (a.OutgoingCurve is null && SegmentKind(track, i) == CurveKind.Bezier && a.TimeMs + a.HandleOut.TimeMs > b.TimeMs + b.HandleIn.TimeMs)
                errors.Add(L.Get("core.curves.handleOrder"));
        }
        if (track.Nodes.LastOrDefault()?.OutgoingCurve is not null) errors.Add(L.Get("core.controlCurve.invalid"));
        return errors;
    }

    private static bool ValidHandle(Anchor node, MapPoint handle, bool incoming, bool active)
    {
        double controlX = node.X + handle.X;
        return double.IsFinite(handle.TimeMs) && double.IsFinite(handle.X) && double.IsFinite(node.TimeMs + handle.TimeMs)
            && (incoming ? handle.TimeMs <= 0 : handle.TimeMs >= 0) && (!active || controlX >= 0 && controlX <= 512);
    }

    private static bool IsPositionValid(double time, double x) => double.IsFinite(time) && time >= 0 && double.IsFinite(x) && x >= 0 && x <= 512;
    private static MapPoint Point(Anchor anchor) => new(anchor.TimeMs, anchor.X);

    private static void CheckSegment(CurveTrack track, int segment)
    {
        if (segment < 0 || segment >= track.Nodes.Count - 1) throw new ArgumentOutOfRangeException(nameof(segment));
    }

    private static void CheckId(Guid id, HashSet<Guid> ids, List<string> errors)
    {
        if (id == Guid.Empty || !ids.Add(id)) errors.Add(L.Get("core.curves.uniqueId"));
    }
}
