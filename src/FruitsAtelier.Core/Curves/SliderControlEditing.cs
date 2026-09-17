using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public enum SliderCurveType { Linear, Bezier, CircularArc }

// An editing projection, never a second authoritative path. Null type denotes an interior control.
public sealed record SliderVertex(Guid Id, MapPoint Point, SliderCurveType? Type, double ReferenceScale = 1);

public static class SliderControlEditing
{
    public static List<SliderVertex> Vertices(CurveTrack track)
    {
        var result = new List<SliderVertex>();
        for (int i = 0; i < track.Nodes.Count; i++)
        {
            var node = track.Nodes[i];
            var point = ControlCurveMath.Point(node);
            var curve = node.OutgoingCurve;
            var type = curve is not null ? curve.Kind == ControlCurveKind.CircularArc ? SliderCurveType.CircularArc : SliderCurveType.Bezier
                : i + 1 < track.Nodes.Count && CurveMath.SegmentKind(track, i) == CurveKind.Bezier ? SliderCurveType.Bezier : SliderCurveType.Linear;
            result.Add(new(node.Id, point, type, curve?.ReferenceScale ?? 1));
            if (i + 1 == track.Nodes.Count) break;
            if (curve is not null)
                result.AddRange(curve.Controls.Select(c => new SliderVertex(c.Id, point + c.Offset, null)));
            else if (type == SliderCurveType.Bezier)
            {
                result.Add(new(HandleId(node.Id, false), point + node.HandleOut, null));
                var end = track.Nodes[i + 1];
                result.Add(new(HandleId(end.Id, true), ControlCurveMath.Point(end) + end.HandleIn, null));
            }
        }
        return result;
    }

    private static Guid HandleId(Guid id, bool incoming)
    {
        var bytes = id.ToByteArray();
        bytes[0] ^= incoming ? (byte)0xA7 : (byte)0x59;
        bytes[15] ^= 0xDB;
        return new Guid(bytes);
    }

    public static SliderCurveType AutomaticType(int pointCount) => pointCount <= 2 ? SliderCurveType.Linear
        : pointCount == 3 ? SliderCurveType.CircularArc : SliderCurveType.Bezier;

    public static void Apply(CurveTrack track, IReadOnlyList<SliderVertex> vertices, bool allowArcFallback = false)
    {
        if (vertices.Count < 2) throw new ArgumentException(L.Get("core.points.minimumRemaining"));
        var working = track.DeepClone();
        working.Nodes.Clear();
        var old = track.Nodes.ToDictionary(n => n.Id);
        var original = Vertices(track);
        var boundaries = Enumerable.Range(0, vertices.Count).Where(i => i == 0 || i == vertices.Count - 1 || vertices[i].Type is not null).ToArray();
        foreach (int i in boundaries)
        {
            var p = vertices[i];
            var node = old.TryGetValue(p.Id, out var existing) ? existing.DeepClone() : new Anchor { Id = p.Id };
            node.TimeMs = p.Point.TimeMs; node.X = p.Point.X;
            working.Nodes.Add(node);
        }
        for (int s = 0; s < boundaries.Length - 1; s++)
        {
            int from = boundaries[s], to = boundaries[s + 1];
            var a = working.Nodes[s]; var b = working.Nodes[s + 1];
            int oldFrom = original.FindIndex(v => v.Id == a.Id), oldTo = original.FindIndex(v => v.Id == b.Id);
            if (oldFrom >= 0 && oldTo > oldFrom && original.Skip(oldFrom).Take(oldTo - oldFrom + 1).SequenceEqual(vertices.Skip(from).Take(to - from + 1))) continue;
            var type = vertices[from].Type ?? SliderCurveType.Bezier;
            int count = to - from - 1;
            if (count == 0) type = SliderCurveType.Linear;
            if (type == SliderCurveType.CircularArc && count != 1) type = SliderCurveType.Bezier;
            if (type == SliderCurveType.Linear && count > 0) type = SliderCurveType.Bezier;
            a.HandleOut = default; b.HandleIn = default; a.OutgoingCurve = null;
            a.OutgoingKind = type == SliderCurveType.Linear ? CurveKind.Linear : CurveKind.Bezier;
            if (type == SliderCurveType.Linear) continue;
            // Existing cubic pen controls keep their exact native representation and stable projected IDs.
            if (type == SliderCurveType.Bezier && count == 2
                && vertices[from + 1].Id == HandleId(a.Id, false) && vertices[from + 2].Id == HandleId(b.Id, true))
            {
                a.HandleOut = vertices[from + 1].Point - ControlCurveMath.Point(a);
                b.HandleIn = vertices[from + 2].Point - ControlCurveMath.Point(b);
                continue;
            }
            a.OutgoingCurve = new ControlCurve
            {
                Kind = type == SliderCurveType.CircularArc ? ControlCurveKind.CircularArc : ControlCurveKind.Bezier,
                ReferenceScale = vertices[from].ReferenceScale
            };
            for (int i = from + 1; i < to; i++)
                a.OutgoingCurve.Controls.Add(new CurveControl { Id = vertices[i].Id, Offset = vertices[i].Point - ControlCurveMath.Point(a) });
        }
        if (working.Nodes[^1].Id != track.Nodes.LastOrDefault()?.Id)
        { working.Nodes[^1].OutgoingCurve = null; working.Nodes[^1].HandleOut = default; }
        if (working.Nodes[0].Id != track.Nodes.FirstOrDefault()?.Id) working.Nodes[0].HandleIn = default;
        if (allowArcFallback)
            for (int i = 0; i + 1 < working.Nodes.Count; i++)
                if (working.Nodes[i].OutgoingCurve is { Kind: ControlCurveKind.CircularArc } curve
                    && ControlCurveMath.Validate(working, i) is not null)
                    curve.Kind = ControlCurveKind.Bezier;
        Validate(working);
        track.Nodes.Clear(); track.Nodes.AddRange(working.Nodes);
    }

    public static bool TryMove(CurveTrack track, IReadOnlyCollection<Guid> ids, MapPoint delta, out string error)
    {
        var vertices = Vertices(track);
        for (int i = 0; i < vertices.Count; i++) if (ids.Contains(vertices[i].Id)) vertices[i] = vertices[i] with { Point = vertices[i].Point + delta };
        try { Apply(track, vertices); error = ""; return true; }
        catch (ArgumentException ex) { error = ex.Message; return false; }
    }

    public static Guid Insert(CurveTrack track, int segment, MapPoint point, double? referenceScale = null, bool allowArcFallback = false)
    {
        var vertices = Vertices(track);
        int start = vertices.FindIndex(v => v.Id == track.Nodes[segment].Id);
        int end = vertices.FindIndex(v => v.Id == track.Nodes[segment + 1].Id);
        int index = start + 1;
        while (index < end && vertices[index].Point.TimeMs < point.TimeMs) index++;
        var added = new SliderVertex(Guid.NewGuid(), point, null);
        vertices.Insert(index, added);
        // Adding to an explicit Bezier keeps it Bezier; an arc with too many points becomes Bezier.
        var type = vertices[start].Type;
        if (type == SliderCurveType.Linear) vertices[start] = vertices[start] with { Type = SliderCurveType.CircularArc, ReferenceScale = referenceScale ?? vertices[start].ReferenceScale };
        Apply(track, vertices, allowArcFallback);
        return added.Id;
    }

    public static void ToggleBoundary(CurveTrack track, Guid id)
    {
        var vertices = Vertices(track);
        int index = vertices.FindIndex(v => v.Id == id);
        if (index <= 0 || index >= vertices.Count - 1) return;
        if (vertices[index].Type is not null)
        {
            vertices[index] = vertices[index] with { Type = null };
            int start = index - 1; while (start > 0 && vertices[start].Type is null) start--;
            vertices[start] = vertices[start] with { Type = SliderCurveType.Bezier };
        }
        else
        {
            int start = index - 1; while (start > 0 && vertices[start].Type is null) start--;
            // Split the control polygon. This is intentionally a shape edit, not de Casteljau splitting.
            vertices[index] = vertices[index] with { Type = SliderCurveType.Bezier, ReferenceScale = vertices[start].ReferenceScale };
        }
        Apply(track, vertices);
    }

    public static bool Remove(CurveTrack track, IReadOnlyCollection<Guid> ids)
    {
        var vertices = Vertices(track);
        var first = vertices[0];
        vertices.RemoveAll(v => ids.Contains(v.Id));
        if (vertices.Count < 2) return false;
        if (vertices[0].Type is null) vertices[0] = vertices[0] with { Type = first.Type, ReferenceScale = first.ReferenceScale };
        Apply(track, vertices);
        return true;
    }

    public static void SetType(CurveTrack track, int segment, SliderCurveType type, double referenceScale)
    {
        var vertices = Vertices(track);
        int start = vertices.FindIndex(v => v.Id == track.Nodes[segment].Id), end = vertices.FindIndex(v => v.Id == track.Nodes[segment + 1].Id);
        if (type == SliderCurveType.CircularArc && end - start != 2) throw new ArgumentException(L.Get("core.controlCurve.threePoints"));
        if (type == SliderCurveType.Linear) vertices.RemoveRange(start + 1, end - start - 1);
        vertices[start] = vertices[start] with { Type = type, ReferenceScale = referenceScale };
        Apply(track, vertices);
    }

    public static void Reverse(CurveTrack track)
    {
        var vertices = Vertices(track);
        double sum = vertices[0].Point.TimeMs + vertices[^1].Point.TimeMs;
        var reverse = new List<SliderVertex>();
        for (int s = track.Nodes.Count - 2; s >= 0; s--)
        {
            int from = vertices.FindIndex(v => v.Id == track.Nodes[s].Id), to = vertices.FindIndex(v => v.Id == track.Nodes[s + 1].Id);
            for (int i = to; i > from; i--)
                reverse.Add(vertices[i] with { Point = new(sum - vertices[i].Point.TimeMs, vertices[i].Point.X),
                    Type = i == to ? vertices[from].Type : null, ReferenceScale = vertices[from].ReferenceScale });
        }
        reverse.Add(vertices[0] with { Point = new(sum - vertices[0].Point.TimeMs, vertices[0].Point.X), Type = SliderCurveType.Linear });
        Apply(track, reverse);
    }

    public static void Validate(CurveTrack track)
    {
        var doc = new MapDocument { DurationMs = Math.Max(1, CurveMath.EndTimeMs(track)) };
        doc.Tracks.Add(track);
        var error = CurveMath.Validate(doc).FirstOrDefault();
        if (error is not null) throw new ArgumentException(error);
    }
}
