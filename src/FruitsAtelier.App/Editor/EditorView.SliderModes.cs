using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public enum SliderEditingMode { PenTool, OsuLegacy }

public sealed partial class EditorView
{
    public SliderEditingMode SliderMode { get; private set; } = SliderEditingMode.PenTool;
    private float inspectorScroll, inspectorContentHeight;
    private bool LegacyMode => SliderMode == SliderEditingMode.OsuLegacy;
    private List<SliderVertex>? legacyDraft;
    private List<SliderVertex>? legacyPreviewVertices;
    private List<SliderVertex>? legacyDragStart;
    private MapPoint legacyDragPoint;
    private bool legacyPreviewValid;
    private bool legacyFinishOnRelease;
    private Guid legacyDeselectOnRelease;
    private bool LegacyControlsActive => LegacyMode && showTargets && tool is Tool.Select or Tool.Slider
        && (objectSelection.Count <= 1) && SelectedTrack is not null;
    private readonly Dictionary<Guid, (ControlCurve Curve, MapPoint Start, MapPoint End, IReadOnlyList<ControlCurveEditing.Cubic> Cubics)> penPreview = [];

    private void OpenSliderModeMenu(float x, float y)
    {
        contextItems.Clear();
        contextItems.Add(new(L.Get("ui.osuLegacyMode"), () => SetSliderEditingMode(SliderEditingMode.OsuLegacy)));
        contextItems.Add(new(L.Get("ui.penToolMode"), () => SetSliderEditingMode(SliderEditingMode.PenTool)));
        contextBounds = new(Math.Min(x, width - 228), y, 220, 76);
    }

    public void SetSliderEditingMode(SliderEditingMode mode)
    {
        if (!Enum.IsDefined(mode) || mode == SliderMode || drag != DragKind.None) return;
        if (editField >= 0 && !CommitField()) return;
        if (draftTrack != Guid.Empty) FinishCurve();
        if (draftTrack != Guid.Empty) return;
        SliderMode = mode;
        legacyDraft = null; legacyDragStart = null;
        if (SelectedTrack is { } track && tool == Tool.Slider) SelectAnchors(track, []);
        StatusMessage = "";
    }

    private IReadOnlyList<ControlCurveEditing.Cubic> PenPreview(CurveTrack track, int segment)
    {
        var node = track.Nodes[segment];
        var start = Point(node); var end = Point(track.Nodes[segment + 1]);
        if (node.OutgoingCurve is { } curve && penPreview.TryGetValue(node.Id, out var cache)
            && ReferenceEquals(cache.Curve, curve) && cache.Start == start && cache.End == end) return cache.Cubics;
        var result = ControlCurveEditing.PenSegments(track, segment);
        if (node.OutgoingCurve is { } current) penPreview[node.Id] = (current, start, end, result);
        return result;
    }

    private MapPoint PenHandle(CurveTrack track, int index, bool incoming)
    {
        int segment = incoming ? index - 1 : index;
        if (segment < 0 || segment >= track.Nodes.Count - 1 || track.Nodes[segment].OutgoingCurve is null)
            return incoming ? track.Nodes[index].HandleIn : track.Nodes[index].HandleOut;
        try
        {
            var cubics = PenPreview(track, segment);
            return incoming ? cubics[^1].In - cubics[^1].End : cubics[0].Out - cubics[0].Start;
        }
        catch (ArgumentException) { return default; }
    }

    private (float X, float Y) PenHandleScreen(CurveTrack track, int index, bool incoming)
    {
        var node = track.Nodes[index]; var p = Screen(Point(node));
        var h = Screen(Point(node) + PenHandle(track, index, incoming));
        int segment = index - (incoming ? 1 : 0);
        if (segment < 0 || segment >= track.Nodes.Count - 1 || track.Nodes[segment].OutgoingCurve is null) return h;
        float dx = h.X - p.X, dy = h.Y - p.Y, length = MathF.Sqrt(dx * dx + dy * dy);
        // Exact curves can need short endpoint cubics. Keep their provisional handles visible and clickable.
        return length > 0 && length < 18 ? (p.X + dx * 18 / length, p.Y + dy * 18 / length) : h;
    }

    private bool HitPenHandle(CurveTrack track, int index, bool incoming, float x, float y)
    {
        var h = PenHandleScreen(track, index, incoming);
        return (h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y) <= 49;
    }

    private void PlaceLegacyPoint(float x, float y)
    {
        var point = MapAt(x, y, true);
        if (draftTrack == Guid.Empty)
        {
            history.Begin(L.Get("editor.command.drawTrack"));
            var track = new CurveTrack { Kind = CurveKind.Linear, CompensateTinyDroplets = true,
                Name = L.Get("editor.track.defaultName", Document.Tracks.Count + 1) };
            var node = new Anchor { TimeMs = point.TimeMs, X = point.X };
            track.Nodes.Add(node); Document.Tracks.Add(track); draftTrack = track.Id;
            legacyDraft = [new(node.Id, point, SliderCurveType.Linear, ControlCurveMath.ReferenceScale(Document.ApproachRate))];
            SelectAnchors(track, []);
            Document.DurationMs = Math.Max(Document.DurationMs, point.TimeMs);
        }
        else if (legacyDraft is { Count: > 0 })
        {
            if (Near(legacyDraft[^1].Point, x, y, 8) || point == legacyDraft[^1].Point)
            {
                if (legacyDraft.Count > 1)
                {
                    legacyDraft[^1] = legacyDraft[^1] with { Type = SliderCurveType.Linear };
                    UpdateLegacyPreview(x, y);
                }
                return;
            }
            if (!UpdateLegacyPreview(x, y)) return;
            var vertices = legacyPreviewVertices!.ToList();
            // Retain authored identities, including the currently previewed endpoint.
            legacyDraft = vertices;
            legacyDraft[^1] = legacyDraft[^1] with { Type = null };
        }
        StatusMessage = "";
    }

    private bool UpdateLegacyPreview(float x, float y)
    {
        if (legacyDraft is not { Count: > 0 } || SelectedTrack is not { } track || !plot.Contains(x, y)) return false;
        var point = MapAt(x, y, true);
        var candidate = legacyDraft.ToList();
        if (Near(candidate[^1].Point, x, y, 8) || point == candidate[^1].Point)
        {
            if (candidate.Count < 2)
            {
                track.Nodes.RemoveRange(1, track.Nodes.Count - 1);
                track.Nodes[0].OutgoingCurve = null;
                return legacyPreviewValid = false;
            }
            candidate[^1] = candidate[^1] with { Type = SliderCurveType.Linear };
        }
        else
        {
            if (point.TimeMs < candidate[^1].Point.TimeMs + CurveMath.MinimumAnchorSpacingMs)
            { StatusMessage = L.Get("editor.error.anchorMustBeLater"); return legacyPreviewValid = false; }
            candidate.Add(new(Guid.NewGuid(), point, SliderCurveType.Linear, legacyDraft[0].ReferenceScale));
        }
        int start = candidate.Count - 2;
        while (start > 0 && candidate[start].Type is null) start--;
        candidate[start] = candidate[start] with { Type = SliderControlEditing.AutomaticType(candidate.Count - start) };
        try
        {
            SliderControlEditing.Apply(track, candidate, allowArcFallback: true);
            Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
            legacyPreviewVertices = candidate;
            legacyPreviewValid = true;
            return true;
        }
        catch (ArgumentException ex) { StatusMessage = ex.Message; return legacyPreviewValid = false; }
    }

    private bool HandleLegacyPointerDown(float x, float y, int button, bool ctrl)
    {
        if (!LegacyMode || !plot.Contains(x, y) || tool != Tool.Slider && !LegacyControlsActive) return false;
        if (draftTrack != Guid.Empty)
        {
            if (button == 0) { PlaceLegacyPoint(x, y); return true; }
            if (button == 2) { legacyFinishOnRelease = true; return true; }
            return false;
        }
        if (SelectedTrack is not { } track)
        {
            if (button == 0 && !ctrl) { PlaceLegacyPoint(x, y); return true; }
            return false;
        }
        var vertices = SliderControlEditing.Vertices(track);
        var hit = vertices.OrderBy(p => PointerDistance(p.Point, x, y)).FirstOrDefault(p => Near(p.Point, x, y, 8));
        if (hit is not null)
        {
            if (button == 2)
            {
                tool = Tool.Slider;
                SelectAnchors(track, [hit.Id]);
                DeleteLegacyPoints(); return true;
            }
            if (button != 0) return false;
            if (ctrl)
            {
                var ids = anchorSelection.ToHashSet();
                legacyDeselectOnRelease = ids.Contains(hit.Id) ? hit.Id : Guid.Empty;
                ids.Add(hit.Id);
                tool = Tool.Slider;
                SelectAnchors(track, ids, hit.Id);
                BeginLegacyDrag(track, hit.Point, x, y);
                return true;
            }
            tool = Tool.Slider;
            if (!anchorSelection.Contains(hit.Id)) SelectAnchors(track, [hit.Id]);
            BeginLegacyDrag(track, hit.Point, x, y);
            return true;
        }
        if (ctrl && button == 0 && track.Nodes.Count >= 2)
        {
            var point = MapAt(x, y, anchorSnap);
            if (point.TimeMs < track.Nodes[0].TimeMs || point.TimeMs > CurveMath.EndTimeMs(track)) return true;
            if (track.SpanCount > 1) point = new(CurveMath.FirstSpanTime(track, point.TimeMs), point.X);
            if (point.TimeMs <= track.Nodes[0].TimeMs || point.TimeMs >= track.Nodes[^1].TimeMs) return true;
            int segment = track.Nodes.FindIndex(n => n.TimeMs > point.TimeMs) - 1;
            history.Begin(L.Get("editor.command.insertPoint"));
            try
            {
                Guid id = SliderControlEditing.Insert(track, segment, point, ControlCurveMath.ReferenceScale(Document.ApproachRate), allowArcFallback: true);
                tool = Tool.Slider;
                SelectAnchors(track, [id]);
                BeginLegacyDrag(track, point, x, y, alreadyBegun: true);
            }
            catch (ArgumentException ex) { history.Cancel(); StatusMessage = ex.Message; }
            return true;
        }
        return false;
    }

    private void BeginLegacyDrag(CurveTrack track, MapPoint point, float x, float y, bool alreadyBegun = false)
    {
        if (!alreadyBegun) history.Begin(L.Get("editor.command.moveAnchor"));
        legacyDragStart = SliderControlEditing.Vertices(track);
        legacyDragPoint = point; dragOffset = Transform.ToMap(x, y) - point;
        drag = DragKind.LegacyControl; BeginPointerDrag(x, y);
    }

    private void MoveLegacyPoints(float x, float y)
    {
        if (legacyDragStart is null || SelectedTrack is not { } track) return;
        var raw = Transform.ToMap(x, y) - dragOffset;
        bool endpoint = anchorSelection.Contains(legacyDragStart[0].Id) || anchorSelection.Contains(legacyDragStart[^1].Id);
        bool snapped = endpoint ? snap : anchorSnap;
        var point = new MapPoint(snapped ? TimingMap.Snap(Document, raw.TimeMs, divisor) : raw.TimeMs, raw.X);
        var delta = point - legacyDragPoint;
        bool Try(MapPoint offset)
        {
            var candidate = legacyDragStart.Select(v => anchorSelection.Contains(v.Id) ? v with { Point = v.Point + offset } : v).ToList();
            try { SliderControlEditing.Apply(track, candidate, allowArcFallback: true); return true; }
            catch (ArgumentException ex) { StatusMessage = ex.Message; return false; }
        }
        if (!Try(delta))
        {
            if (endpoint && snapped) Try(new(0, delta.X));
            else ClampMove(default, delta, Try);
        }
        Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
    }

    private void DeleteLegacyPoints()
    {
        if (SelectedTrack is not { } track || anchorSelection.Count == 0 || draftTrack != Guid.Empty) return;
        var ids = anchorSelection.ToArray();
        bool removed = false;
        if (!Edit(L.Get("editor.command.deleteAnchors"), () =>
        {
            if (!SliderControlEditing.Remove(track, ids)) { Document.Tracks.Remove(track); removed = true; }
        })) return;
        if (removed) Select(Guid.Empty); else SelectAnchors(track, []);
    }

    private bool LegacyDoubleClick(float x, float y)
    {
        if (!LegacyControlsActive || !plot.Contains(x, y) || SelectedTrack is not { } track) return false;
        if (legacyDraft is { Count: > 1 } && draftTrack != Guid.Empty && Near(legacyDraft[^1].Point, x, y, 8))
        {
            legacyDraft[^1] = legacyDraft[^1] with { Type = SliderCurveType.Linear };
            return true;
        }
        var hit = SliderControlEditing.Vertices(track).FirstOrDefault(v => Near(v.Point, x, y, 8));
        if (hit is null || draftTrack != Guid.Empty) return false;
        tool = Tool.Slider;
        if (drag == DragKind.LegacyControl) { history.Commit(); drag = DragKind.None; }
        if (Edit(L.Get("editor.command.toggleBoundary"), () => SliderControlEditing.ToggleBoundary(track, hit.Id))) SelectAnchors(track, [hit.Id]);
        return true;
    }

    private void DrawLegacyControls(ICanvas c, CurveTrack track)
    {
        var vertices = SliderControlEditing.Vertices(track);
        for (int i = 0; i < vertices.Count; i++)
        {
            var p = Screen(vertices[i].Point);
            if (i > 0)
            {
                var q = Screen(vertices[i - 1].Point);
                c.Line(q.X, q.Y, p.X, p.Y, 0x625E7C, 1, 0.7f);
            }
            if (p.Y < plot.Y - 9 || p.Y > plot.Bottom + 9) continue;
            bool selected = anchorSelection.Contains(vertices[i].Id);
            uint color = vertices[i].Type is null ? Foreground : Error;
            c.Circle(p.X, p.Y, selected ? 7 : 5, color);
            if (selected) c.Circle(p.X, p.Y, 10, Accent, false, 1.5f);
        }
    }

    private void ReverseSelectedPath()
    {
        if (draftTrack != Guid.Empty || SelectedTrack is not { } track) return;
        if (Edit(L.Get("editor.command.reversePath"), () => SliderControlEditing.Reverse(track))) SelectAnchors(track, []);
    }

    private void DrawLegacyInspector(ICanvas c, CurveTrack track, float x, ref float y, float w)
    {
        var vertices = SliderControlEditing.Vertices(track);
        var vertex = vertices.FirstOrDefault(v => v.Id == selection);
        if (vertex is null)
        {
            c.Text(L.Get("ui.anchorCount", vertices.Count), x, y, 12, Muted, w); y += 25;
            return;
        }
        Field(c, x, ref y, w, L.Get("ui.timeField"), vertex.Point.TimeMs, value => Move(value, vertex.Point.X));
        Field(c, x, ref y, w, L.Get("ui.xField"), vertex.Point.X, value => Move(vertex.Point.TimeMs, value));
        bool interior = vertex.Id != vertices[0].Id && vertex.Id != vertices[^1].Id;
        Button(c, new(x, y, w, 28), L.Get("editor.command.toggleBoundary"), () =>
        {
            if (Edit(L.Get("editor.command.toggleBoundary"), () => SliderControlEditing.ToggleBoundary(track, vertex.Id))) SelectAnchors(track, [vertex.Id]);
        }, false, interior && draftTrack == Guid.Empty); y += 34;
        Button(c, new(x, y, w, 28), L.Get("editor.command.deletePoint"), DeleteLegacyPoints, false, draftTrack == Guid.Empty); y += 34;

        void Move(double time, double position)
        {
            if (time < 0 || time > EditableDurationMs) throw new ArgumentException(L.Get("ui.timeRangeError"));
            var current = Document.Tracks.Single(t => t.Id == track.Id);
            if (!SliderControlEditing.TryMove(current, [vertex.Id], new MapPoint(time, position) - vertex.Point, out var error)) throw new ArgumentException(error);
            Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(current));
        }
    }
}
