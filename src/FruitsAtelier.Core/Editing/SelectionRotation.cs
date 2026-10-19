using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class SelectionRotation
{
    public static CatchConversionResult? Apply(MapDocument map, IReadOnlySet<Guid> ids, double degrees, bool osu,
        bool selectionCentre, double timeScale = 1, double playfieldTime = 0, CatchConversionCache? cache = null,
        bool compensateTinyDroplets = true, (double X, double Y)? selectionOrigin = null)
    {
        if (!double.IsFinite(degrees) || !double.IsFinite(timeScale) || timeScale <= 0)
            throw new ArgumentException(L.Get("editor.error.finiteNumberRequired"));
        if (Math.Abs(degrees % 360) < 1e-10) return null;
        if (map.BananaShowers.Any(s => ids.Contains(s.Id))
            || (osu ? map.Tracks.Any(t => ids.Contains(t.Id)) : map.ImportedSliders.Any(s => ids.Contains(s.Id))))
            throw new InvalidOperationException(L.Get("osu.readOnlySlider"));
        var points = new List<(double X, double Y)>();
        foreach (var f in map.Fruits.Where(f => ids.Contains(f.Id))) points.Add((f.X, osu ? f.Y : -f.TimeMs * timeScale));
        foreach (var s in map.ImportedSliders.Where(s => ids.Contains(s.Id)))
        {
            var geometry = s.RandomizationSource ?? s;
            points.AddRange(geometry.ControlPoints.Select(p => (p.X, p.GeometryY)));
        }
        foreach (var t in map.Tracks.Where(t => ids.Contains(t.Id)))
        {
            if (t.SpanCount != 1 || t.Stack is not null || t.DropletRandomization is not null)
                throw new InvalidOperationException(L.Get("rotation.catchPattern"));
            points.AddRange(t.Nodes.Select(n => (n.X, -n.TimeMs * timeScale)));
        }
        if (points.Count == 0) return null;
        double cx = selectionCentre ? (points.Min(p => p.X) + points.Max(p => p.X)) / 2 : 256;
        double cy = selectionCentre ? (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2 : osu ? 192 : -playfieldTime * timeScale;
        if (selectionCentre && selectionOrigin is { } origin) { cx = origin.X; cy = origin.Y; }
        double radians = degrees * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        (double X, double Y) Rotate(double x, double y) =>
            (cx + (x - cx) * cos - (y - cy) * sin, cy + (x - cx) * sin + (y - cy) * cos);
        MapPoint RawPoint(MapPoint p)
        {
            var next = Rotate(p.X, -p.TimeMs * timeScale);
            return new(-next.Y / timeScale, next.X);
        }
        double factor = 1, originalStart = 0, rotatedStart = 0;
        void FixTimeRange(double start, double end, double startX, double endX)
        {
            var first = RawPoint(new(start, startX)); var last = RawPoint(new(end, endX));
            double duration = last.TimeMs - first.TimeMs;
            if (end <= start || duration < CurveMath.MinimumAnchorSpacingMs)
                throw new InvalidOperationException(L.Get("rotation.catchLimit"));
            factor = (end - start) / duration; originalStart = start; rotatedStart = first.TimeMs;
        }
        MapPoint RotatePoint(MapPoint p)
        {
            var next = RawPoint(p);
            return new(originalStart + (next.TimeMs - rotatedStart) * factor, cx + (next.X - cx) * factor);
        }
        MapPoint RotateOffset(MapPoint p) => new((p.TimeMs * cos - p.X * sin / timeScale) * factor,
            (p.X * cos + p.TimeMs * timeScale * sin) * factor);
        var selectedFruits = map.Fruits.Where(f => ids.Contains(f.Id)).OrderBy(f => f.TimeMs).ToArray();
        double fruitStart = selectedFruits.FirstOrDefault()?.TimeMs ?? 0, fruitEnd = selectedFruits.LastOrDefault()?.TimeMs ?? 0;
        if (!osu && selectedFruits.Length > 1) FixTimeRange(fruitStart, fruitEnd, selectedFruits[0].X, selectedFruits[^1].X);
        foreach (var f in selectedFruits)
        {
            var next = Rotate(f.X, osu ? f.Y : -f.TimeMs * timeScale);
            f.X = osu ? Math.Round(next.X, MidpointRounding.AwayFromZero) : next.X;
            if (osu) f.Y = Math.Round(next.Y, MidpointRounding.AwayFromZero);
            else if (selectedFruits.Length > 1)
            {
                f.X = cx + (next.X - cx) * factor;
                f.TimeMs = originalStart + (-next.Y / timeScale - rotatedStart) * factor;
            }
        }
        if (!osu && selectedFruits.Length > 1) { selectedFruits[0].TimeMs = fruitStart; selectedFruits[^1].TimeMs = fruitEnd; }
        if (!osu)
            for (int i = 1; i < selectedFruits.Length; i++)
                if (selectedFruits[i].TimeMs <= selectedFruits[i - 1].TimeMs)
                    throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        foreach (var s in map.ImportedSliders.Where(s => ids.Contains(s.Id)))
        {
            if (s.RandomizationSource is { } original)
            {
                s.X = original.X; s.Y = original.Y; s.PathType = original.PathType; s.PixelLength = original.PixelLength;
                s.ControlPoints.Clear(); s.ControlPoints.AddRange(original.ControlPoints);
            }
            s.RandomizationSource = null; s.RandomizationTrack = null; s.RandomizationBaseVelocity = null;
            s.ProcessedSliderVelocityMultiplier = null; s.PreserveNativeGeometry = true;
            var head = Rotate(s.X, s.Y);
            s.X = Math.Round(head.X, MidpointRounding.AwayFromZero); s.Y = Math.Round(head.Y, MidpointRounding.AwayFromZero);
            for (int i = 0; i < s.ControlPoints.Count; i++)
            {
                var p = Rotate(s.ControlPoints[i].X, s.ControlPoints[i].GeometryY);
                s.ControlPoints[i] = new(Math.Round(p.X, MidpointRounding.AwayFromZero), Math.Round(p.Y, MidpointRounding.AwayFromZero));
            }
        }
        foreach (var t in map.Tracks.Where(t => ids.Contains(t.Id)))
        {
            double start = t.Nodes[0].TimeMs, end = t.Nodes[^1].TimeMs;
            // Uniformly fit the rotated curve back to its authored interval, retaining its new leaning angle.
            FixTimeRange(start, end, t.Nodes[0].X, t.Nodes[^1].X);
            for (int i = t.Nodes.Count - 2; i >= 0; i--)
                if (t.Nodes[i].OutgoingCurve is { Kind: ControlCurveKind.CircularArc } arc
                    && Math.Abs(arc.ReferenceScale - timeScale) > 1e-10)
                    // A rotation in another aspect ratio turns the stored circle into an ellipse.
                    ControlCurveEditing.ConvertToPen(t, i);
            foreach (var n in t.Nodes)
            {
                var p = RotatePoint(new(n.TimeMs, n.X)); n.TimeMs = p.TimeMs; n.X = p.X;
                n.HandleIn = RotateOffset(n.HandleIn); n.HandleOut = RotateOffset(n.HandleOut);
                if (n.OutgoingCurve is { } curve)
                    foreach (var control in curve.Controls) control.Offset = RotateOffset(control.Offset);
            }
            t.Nodes[0].TimeMs = start; t.Nodes[^1].TimeMs = end;
        }
        if (osu) return null;
        if (map.Fruits.Any(f => ids.Contains(f.Id) && (f.TimeMs < 0 || f.X < 0 || f.X > 512))
            || map.Tracks.Where(t => ids.Contains(t.Id)).Any(t => t.Nodes.Any(n => n.X < 0 || n.X > 512)))
            throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        var errors = CurveMath.Validate(map);
        if (errors.Count > 0) throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        var converted = CatchStreamConverter.Convert(map, compensateTinyDroplets, cache);
        if (!converted.Success) throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        ConvertedCatchObject? previous = null;
        foreach (var fruit in converted.Objects)
        {
            if (fruit.Kind != CatchObjectKind.Fruit) continue;
            if (previous is not null && (ids.Contains(previous.SourceId) || ids.Contains(fruit.SourceId))
                && OsuBeatmapWriter.QuantizeTime(previous.TimeMs) == OsuBeatmapWriter.QuantizeTime(fruit.TimeMs))
                throw new InvalidOperationException(L.Get("rotation.catchLimit"));
            previous = fruit;
        }
        return converted;
    }
}
