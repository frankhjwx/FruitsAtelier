using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class SelectionRotation
{
    public static void Apply(MapDocument map, IReadOnlySet<Guid> ids, double degrees, bool osu,
        bool selectionCentre, double timeScale = 1, double playfieldTime = 0)
    {
        if (!double.IsFinite(degrees) || !double.IsFinite(timeScale) || timeScale <= 0)
            throw new ArgumentException(L.Get("editor.error.finiteNumberRequired"));
        if (Math.Abs(degrees % 360) < 1e-10) return;
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
        if (points.Count == 0) return;
        double cx = selectionCentre ? (points.Min(p => p.X) + points.Max(p => p.X)) / 2 : 256;
        double cy = selectionCentre ? (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2 : osu ? 192 : -playfieldTime * timeScale;
        double radians = degrees * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        (double X, double Y) Rotate(double x, double y) =>
            (cx + (x - cx) * cos - (y - cy) * sin, cy + (x - cx) * sin + (y - cy) * cos);
        MapPoint RotatePoint(MapPoint p)
        {
            var next = Rotate(p.X, -p.TimeMs * timeScale);
            return new(-next.Y / timeScale, next.X);
        }
        MapPoint RotateOffset(MapPoint p) => new(p.TimeMs * cos - p.X * sin / timeScale,
            p.X * cos + p.TimeMs * timeScale * sin);
        foreach (var f in map.Fruits.Where(f => ids.Contains(f.Id)))
        {
            var next = Rotate(f.X, osu ? f.Y : -f.TimeMs * timeScale);
            f.X = osu ? Math.Round(next.X, MidpointRounding.AwayFromZero) : next.X;
            if (osu) f.Y = Math.Round(next.Y, MidpointRounding.AwayFromZero); else f.TimeMs = -next.Y / timeScale;
        }
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
        }
        map.DurationMs = Math.Max(map.DurationMs, map.Fruits.Select(f => f.TimeMs)
            .Concat(map.Tracks.Where(t => t.Nodes.Count > 0).Select(CurveMath.EndTimeMs)).DefaultIfEmpty(0).Max());
        if (osu) return;
        if (map.Fruits.Any(f => ids.Contains(f.Id) && (f.TimeMs < 0 || f.X < 0 || f.X > 512))
            || map.Tracks.Where(t => ids.Contains(t.Id)).Any(t => t.Nodes.Any(n => n.X < 0 || n.X > 512)))
            throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        var errors = CurveMath.Validate(map);
        if (errors.Count > 0) throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        var converted = CatchStreamConverter.Convert(map);
        if (!converted.Success) throw new InvalidOperationException(L.Get("rotation.catchLimit"));
        var fruits = converted.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).OrderBy(o => o.TimeMs).ToArray();
        for (int i = 1; i < fruits.Length; i++)
            if ((ids.Contains(fruits[i - 1].SourceId) || ids.Contains(fruits[i].SourceId))
                && OsuBeatmapWriter.QuantizeTime(fruits[i - 1].TimeMs) == OsuBeatmapWriter.QuantizeTime(fruits[i].TimeMs))
                throw new InvalidOperationException(L.Get("rotation.catchLimit"));
    }
}
