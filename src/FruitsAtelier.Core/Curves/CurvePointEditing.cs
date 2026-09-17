using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static class CurvePointEditing
{
    public static Anchor InsertCorner(CurveTrack track, int segment, double parameter)
    {
        var working = CloneValidated(track);
        if (working.Nodes[segment].OutgoingCurve is not null)
        {
            double time = CurveMath.Evaluate(working, segment, parameter).TimeMs;
            ControlCurveEditing.ConvertToPen(working, segment);
            segment = working.Nodes.FindIndex(n => n.TimeMs > time) - 1;
            double lo = 0, hi = 1;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                if (CurveMath.Evaluate(working, segment, mid).TimeMs < time) lo = mid; else hi = mid;
            }
            parameter = (lo + hi) / 2;
        }
        ClearInactiveSegment(working, segment);
        CurveMath.Split(working, segment, parameter);
        var inserted = working.Nodes[segment + 1];
        inserted.HandleIn = inserted.HandleOut = default;
        RecomputeSegment(working, segment);
        RecomputeSegment(working, segment + 1);
        Validate(working);
        Apply(track, working);
        return inserted;
    }

    public static void SetCurved(CurveTrack track, Guid nodeId, bool curved)
    {
        var working = CloneValidated(track);
        int index = FindNode(working, nodeId);
        if (index + 1 < working.Nodes.Count) ControlCurveEditing.ConvertToPen(working, index);
        index = FindNode(working, nodeId);
        if (index > 0) ControlCurveEditing.ConvertToPen(working, index - 1);
        index = FindNode(working, nodeId);
        if (curved && IsCurved(working, nodeId)) return;
        if (index > 0) ClearInactiveSegment(working, index - 1);
        if (index + 1 < working.Nodes.Count) ClearInactiveSegment(working, index);
        var node = working.Nodes[index];
        node.HandleIn = node.HandleOut = default;
        if (curved) CreateTangent(working, index);
        if (index > 0) RecomputeSegment(working, index - 1);
        if (index + 1 < working.Nodes.Count) RecomputeSegment(working, index);
        Validate(working);
        if (curved && !IsCurved(working, nodeId)) throw new ArgumentException(L.Get("core.points.noHandleRoom"), nameof(nodeId));
        Apply(track, working);
    }

    public static void Remove(CurveTrack track, Guid nodeId)
    {
        var working = CloneValidated(track);
        int index = FindNode(working, nodeId);
        if (working.Nodes.Count < 3 || index == 0 || index == working.Nodes.Count - 1)
            throw new ArgumentException(L.Get("core.points.internalOnly"), nameof(nodeId));
        RemoveMany(working, [nodeId]);
        Apply(track, working);
    }

    public static void RemoveMany(CurveTrack track, IEnumerable<Guid> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(nodeIds);
        var selected = nodeIds.ToHashSet();
        if (selected.Count == 0) return;
        var working = CloneValidated(track);
        var existing = working.Nodes.Select(n => n.Id).ToHashSet();
        if (!selected.IsSubsetOf(existing)) throw new ArgumentException(L.Get("core.points.unknownSelection"), nameof(nodeIds));
        for (int i = working.Nodes.Count - 2; i >= 0; i--)
            if (selected.Contains(working.Nodes[i].Id) || selected.Contains(working.Nodes[i + 1].Id)) ControlCurveEditing.ConvertToPen(working, i);
        var retained = Enumerable.Range(0, working.Nodes.Count).Where(i => !selected.Contains(working.Nodes[i].Id)).ToArray();
        if (retained.Length < 2) throw new ArgumentException(L.Get("core.points.minimumRemaining"), nameof(nodeIds));
        var originalKinds = Enumerable.Range(0, working.Nodes.Count - 1).Select(i => CurveMath.SegmentKind(working, i)).ToArray();
        for (int i = 0; i + 1 < retained.Length; i++)
        {
            int leftIndex = retained[i], rightIndex = retained[i + 1];
            if (rightIndex == leftIndex + 1) continue;
            var left = working.Nodes[leftIndex]; var right = working.Nodes[rightIndex];
            // Only new adjacency can activate dormant handles; unchanged segments retain their complete editing state.
            if (originalKinds[leftIndex] == CurveKind.Linear) left.HandleOut = default;
            if (originalKinds[rightIndex - 1] == CurveKind.Linear) right.HandleIn = default;
            left.OutgoingKind = Nonzero(left.HandleOut) || Nonzero(right.HandleIn) ? CurveKind.Bezier : CurveKind.Linear;
        }
        if (retained[0] != 0) working.Nodes[retained[0]].HandleIn = default;
        if (retained[^1] != working.Nodes.Count - 1) working.Nodes[retained[^1]].HandleOut = default;
        working.Nodes.RemoveAll(n => selected.Contains(n.Id));
        Validate(working);
        Apply(track, working);
    }

    public static bool IsCurved(CurveTrack track, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(track);
        int index = FindNode(track, nodeId);
        var node = track.Nodes[index];
        if (node.OutgoingCurve is not null || index > 0 && track.Nodes[index - 1].OutgoingCurve is not null) return true;
        return index > 0 && CurveMath.SegmentKind(track, index - 1) == CurveKind.Bezier && Nonzero(node.HandleIn)
            || index + 1 < track.Nodes.Count && CurveMath.SegmentKind(track, index) == CurveKind.Bezier && Nonzero(node.HandleOut);
    }

    private static void CreateTangent(CurveTrack track, int index)
    {
        var node = track.Nodes[index];
        var previous = index > 0 ? track.Nodes[index - 1] : null;
        var next = index + 1 < track.Nodes.Count ? track.Nodes[index + 1] : null;
        var start = previous ?? node;
        var end = next ?? node;
        double slope = (end.X - start.X) / (end.TimeMs - start.TimeMs);
        double incoming = previous is null ? 0 : Math.Min((node.TimeMs - previous.TimeMs) / 3,
            (node.TimeMs - (previous.TimeMs + previous.HandleOut.TimeMs)) / 2);
        double outgoing = next is null ? 0 : Math.Min((next.TimeMs - node.TimeMs) / 3,
            (next.TimeMs + next.HandleIn.TimeMs - node.TimeMs) / 2);
        double lower = double.NegativeInfinity, upper = double.PositiveInfinity;
        if (incoming > 0) { lower = Math.Max(lower, (node.X - 512) / incoming); upper = Math.Min(upper, node.X / incoming); }
        if (outgoing > 0) { lower = Math.Max(lower, -node.X / outgoing); upper = Math.Min(upper, (512 - node.X) / outgoing); }
        slope = Math.Clamp(slope, lower, upper);
        node.HandleIn = previous is null ? default : new(-incoming, Math.Clamp(node.X - slope * incoming, 0, 512) - node.X);
        node.HandleOut = next is null ? default : new(outgoing, Math.Clamp(node.X + slope * outgoing, 0, 512) - node.X);
        if (Nonzero(node.HandleIn) || Nonzero(node.HandleOut)) return;

        // Neighbouring active handles can consume all time room; a horizontal tangent still fits at that time.
        double direction = slope < 0 ? -1 : 1;
        for (int attempt = 0; attempt < 2; attempt++, direction = -direction)
        {
            if (previous is not null) node.HandleIn = new(0, Math.Clamp(node.X - direction * 32, 0, 512) - node.X);
            if (next is not null) node.HandleOut = new(0, Math.Clamp(node.X + direction * 32, 0, 512) - node.X);
            if (Nonzero(node.HandleIn) || Nonzero(node.HandleOut)) return;
        }
    }

    private static void ClearInactiveSegment(CurveTrack track, int segment)
    {
        if (CurveMath.SegmentKind(track, segment) != CurveKind.Linear) return;
        // These values do not affect the current line and must not become active when this edit creates a curve.
        track.Nodes[segment].HandleOut = default;
        track.Nodes[segment + 1].HandleIn = default;
    }

    private static void RecomputeSegment(CurveTrack track, int segment)
        => track.Nodes[segment].OutgoingKind = Nonzero(track.Nodes[segment].HandleOut) || Nonzero(track.Nodes[segment + 1].HandleIn)
            ? CurveKind.Bezier : CurveKind.Linear;

    private static bool Nonzero(MapPoint point) => point.TimeMs != 0 || point.X != 0;

    private static int FindNode(CurveTrack track, Guid id)
    {
        int index = track.Nodes.FindIndex(n => n.Id == id);
        return index >= 0 ? index : throw new ArgumentException(L.Get("core.points.missingAnchor"), nameof(id));
    }

    private static CurveTrack CloneValidated(CurveTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        Validate(track);
        var document = new MapDocument();
        document.Tracks.Add(track);
        return document.DeepClone().Tracks[0];
    }

    private static void Validate(CurveTrack track)
    {
        var document = new MapDocument { DurationMs = Math.Max(1, CurveMath.EndTimeMs(track)) };
        document.Tracks.Add(track);
        string? error = CurveMath.Validate(document).FirstOrDefault();
        if (error is not null) throw new ArgumentException(error, nameof(track));
    }

    private static void Apply(CurveTrack target, CurveTrack source)
    {
        var existing = target.Nodes.ToDictionary(n => n.Id);
        var nodes = new List<Anchor>(source.Nodes.Count);
        foreach (var changed in source.Nodes)
        {
            if (existing.TryGetValue(changed.Id, out var node))
            {
                node.HandleIn = changed.HandleIn;
                node.HandleOut = changed.HandleOut;
                node.OutgoingKind = changed.OutgoingKind;
                node.OutgoingCurve = changed.OutgoingCurve;
                nodes.Add(node);
            }
            else nodes.Add(changed);
        }
        target.Nodes.Clear();
        target.Nodes.AddRange(nodes);
    }
}
