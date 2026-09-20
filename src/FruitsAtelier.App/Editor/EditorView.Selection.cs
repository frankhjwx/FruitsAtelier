using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly HashSet<Guid> objectSelection = [];
    private readonly HashSet<Guid> anchorSelection = [];
    private SelectionSnapshot? selectionBeforeBox;
    private Rect selectionBox;
    private double boxStartTime;
    private bool boxAdds, boxAnchors;
    private long boxScrollTimestamp;
    private double BoxScrollDirection
    {
        get
        {
            if (drag != DragKind.Marquee || !dragMoved) return 0;
            var area = boxTimeline ? objectTimeline : plot;
            double position = boxTimeline ? mouseX : mouseY;
            double start = boxTimeline ? area.X : area.Y, end = boxTimeline ? area.Right : area.Bottom;
            const double edge = 24;
            double direction = position < start + edge ? -Math.Clamp((start + edge - position) / edge, 0, 1)
                : position > end - edge ? Math.Clamp((position - end + edge) / edge, 0, 1) : 0;
            return boxTimeline ? direction : -direction;
        }
    }
    public bool MarqueeScrollNeedsRedraw
    {
        get
        {
            double direction = BoxScrollDirection;
            return direction > 0 && playhead < TimelineDurationMs || direction < 0 && playhead > 0;
        }
    }
    private bool ViewportFrozenByDrag => drag is DragKind.Objects or DragKind.BananaStart or DragKind.BananaEnd
        || drag == DragKind.Marquee && !AudioPlaying;
    private sealed record SelectionSnapshot(Guid[] Objects, Guid[] Anchors, Guid Primary, Guid Track, DragKind Part);
    public IReadOnlyCollection<Guid> SelectedObjectIds => objectSelection.ToArray();
    public IReadOnlyCollection<Guid> SelectedAnchorIds => anchorSelection.ToArray();
    private bool IsObjectSelected(Guid id) => objectSelection.Contains(id) || tool == Tool.Slider && selectedTrack == id;

    private void SelectObjects(IEnumerable<Guid> ids, Guid primary = default)
    {
        soundEdge = null; distanceObject = null;
        var selected = ids.Distinct().ToArray();
        objectSelection.Clear(); objectSelection.UnionWith(selected);
        anchorSelection.Clear();
        selection = objectSelection.Contains(primary) ? primary : selected.FirstOrDefault();
        selectedTrack = Document.Tracks.Any(t => t.Id == selection) ? selection : Guid.Empty;
        selectedPart = DragKind.None;
        editField = -1; fieldError = "";
    }

    private void SelectAnchors(CurveTrack track, IEnumerable<Guid> ids, Guid primary = default)
    {
        soundEdge = null; distanceObject = null;
        var validIds = LegacyMode ? SliderControlEditing.Vertices(track).Select(v => v.Id).ToHashSet() : track.Nodes.Select(n => n.Id).ToHashSet();
        var selected = ids.Where(validIds.Contains).Distinct().ToArray();
        objectSelection.Clear(); anchorSelection.Clear(); anchorSelection.UnionWith(selected);
        selectedTrack = track.Id;
        selection = anchorSelection.Contains(primary) ? primary : selected.FirstOrDefault(track.Id);
        selectedPart = DragKind.Anchor;
        editField = -1; fieldError = "";
    }

    private void PickObject(Guid id, bool toggle)
    {
        if (toggle)
        {
            var ids = objectSelection.ToHashSet();
            if (!ids.Add(id)) ids.Remove(id);
            SelectObjects(ids, id);
        }
        else if (!objectSelection.Contains(id)) SelectObjects([id], id);
        StatusMessage = L.Get("editor.status.objectsSelected", objectSelection.Count);
    }

    private void PickAnchor(CurveTrack track, Anchor node, bool toggle)
    {
        var ids = selectedTrack == track.Id ? anchorSelection.ToHashSet() : [];
        if (toggle) { if (!ids.Add(node.Id)) ids.Remove(node.Id); }
        else if (!ids.Contains(node.Id)) { ids.Clear(); ids.Add(node.Id); }
        SelectAnchors(track, ids, node.Id);
        StatusMessage = L.Get("editor.status.anchorsSelected", anchorSelection.Count);
    }

    private void BeginBox(float x, float y, bool additive, bool anchors)
    {
        boxTimeline = false;
        selectionBeforeBox = new(objectSelection.ToArray(), anchorSelection.ToArray(), selection, selectedTrack, selectedPart);
        boxAdds = additive; boxAnchors = anchors;
        boxStartTime = Transform.ToMap(x, y).TimeMs;
        selectionBox = new(x, y, 0, 0);
        BeginPointerDrag(x, y);
        boxScrollTimestamp = timeProvider.GetTimestamp();
        drag = DragKind.Marquee;
    }

    private void AdvanceBoxScroll()
    {
        if (drag != DragKind.Marquee) return;
        long now = timeProvider.GetTimestamp();
        // Bound catch-up after a slow frame and keep speed independent of redraw frequency.
        double elapsed = Math.Clamp(timeProvider.GetElapsedTime(boxScrollTimestamp, now).TotalSeconds, 0, .05);
        boxScrollTimestamp = now;
        if (!MarqueeScrollNeedsRedraw || elapsed == 0) return;
        double scale = boxTimeline ? objectTimelineScale : pixelsPerMs;
        ScrollBoxTo(playhead + BoxScrollDirection * 100 * elapsed / scale);
    }

    private void ScrollBoxTo(double time)
    {
        double target = Math.Clamp(time, 0, TimelineDurationMs);
        double nextView = viewStart + target - playhead;
        SeekTo(target);
        double padding = plot.Height * playbackLineFromBottom / pixelsPerMs;
        viewStart = Math.Clamp(nextView, -padding, Math.Max(-padding, TimelineDurationMs - padding));
        pinPlayhead = false;
        dragMoved = true;
    }

    private void MoveBox(float x, float y)
    {
        var area = boxTimeline ? objectTimeline : plot;
        x = Math.Clamp(x, area.X, area.Right); y = Math.Clamp(y, area.Y, area.Bottom);
        if (Math.Abs(x - dragStartX) >= 3 || Math.Abs(y - dragStartY) >= 3) dragMoved = true;
        if (!dragMoved || selectionBeforeBox is null) return;
        float startX = boxTimeline ? objectTimeline.X + (float)((boxStartTime - ObjectTimelineStartMs) * objectTimelineScale) : dragStartX;
        float startY = boxTimeline ? dragStartY : Screen(new(boxStartTime, 0)).Y;
        selectionBox = new(Math.Min(x, startX), Math.Min(y, startY), Math.Abs(x - startX), Math.Abs(y - startY));
        if (boxTimeline)
        {
            var ids = boxAdds ? selectionBeforeBox.Objects.ToHashSet() : [];
            foreach (var item in timelineSources)
                if (Intersects(TimelineObjectBounds(item.Start, item.End), selectionBox)) ids.Add(item.Id);
            SelectObjects(ids);
        }
        else if (boxAnchors)
        {
            var track = Document.Tracks.FirstOrDefault(t => t.Id == selectionBeforeBox.Track);
            if (track is null) return;
            var ids = boxAdds ? selectionBeforeBox.Anchors.ToHashSet() : [];
            if (showTargets)
                foreach (var vertex in LegacyMode ? SliderControlEditing.Vertices(track) : track.Nodes.Select(n => new SliderVertex(n.Id, Point(n), SliderCurveType.Linear)).ToList())
                {
                    var point = Screen(vertex.Point);
                    if (selectionBox.Contains(point.X, point.Y)) ids.Add(vertex.Id);
                }
            SelectAnchors(track, ids);
        }
        else
        {
            var ids = boxAdds ? selectionBeforeBox.Objects.ToHashSet() : [];
            EnsureConversion();
            double padding = CatchSize.FruitDiameter(Document.CircleSize) * Playfield.Width / 512 / pixelsPerMs;
            double start = viewStart + (plot.Bottom - selectionBox.Bottom) / pixelsPerMs - padding;
            double end = viewStart + (plot.Bottom - selectionBox.Y) / pixelsPerMs + padding;
            foreach (var item in ObjectsInTimeRange(start, end))
            {
                var bounds = CatchHitBounds(item);
                if (Intersects(bounds, selectionBox)) ids.Add(item.SourceId);
            }
            SelectObjects(ids);
        }
        StatusMessage = boxAnchors ? L.Get("editor.status.boxAnchors", anchorSelection.Count) : L.Get("editor.status.boxObjects", objectSelection.Count);
    }

    private void FinishBox(float x, float y)
    {
        bool moved = dragMoved;
        bool timelineClick = boxTimeline;
        double clickTime = boxTimeline ? ObjectTimelineStartMs + (x - objectTimeline.X) / objectTimelineScale : MapAt(x, y, true).TimeMs;
        drag = DragKind.None;
        selectionBeforeBox = null;
        boxTimeline = false;
        if (!moved && !boxAdds)
        {
            if (boxAnchors)
            {
                tool = Tool.Select;
                Select(Guid.Empty);
            }
            else { Select(Guid.Empty); if (!timelineClick && tool == Tool.Select && !AudioPlaying) SeekTo(clickTime); }
        }
        if (AudioPlaying || pinPlayhead) FollowPlayhead();
    }

    private void CancelBox()
    {
        if (selectionBeforeBox is { } saved)
        {
            objectSelection.Clear(); objectSelection.UnionWith(saved.Objects);
            anchorSelection.Clear(); anchorSelection.UnionWith(saved.Anchors);
            selection = saved.Primary; selectedTrack = saved.Track; selectedPart = saved.Part;
        }
        selectionBeforeBox = null;
        boxTimeline = false;
        drag = DragKind.None;
        if (AudioPlaying || pinPlayhead) FollowPlayhead();
    }

    private void DrawSelectionBox(ICanvas c)
    {
        if (drag != DragKind.Marquee || !dragMoved) return;
        c.Clip(boxTimeline ? objectTimeline : plot);
        c.Stroke(selectionBox, Accent, 1.5f);
        c.Unclip();
    }

    private Rect CatchHitBounds(ConvertedCatchObject item)
    {
        var point = Screen(new(item.TimeMs, item.X));
        float scale = Playfield.Width / 512;
        var sprite = skin?.Bounds(SkinObjectKind(item.Kind), skinIndices.GetValueOrDefault(item.SourceId),
            point.X, point.Y, CatchSize.FruitDiameter(Document.CircleSize) * scale);
        float halfWidth = Math.Max(7, sprite?.Width / 2 ?? ObjectRadius(item.Kind) * scale);
        float halfHeight = Math.Max(7, sprite?.Height / 2 ?? ObjectRadius(item.Kind) * scale);
        return new(point.X - halfWidth, point.Y - halfHeight, halfWidth * 2, halfHeight * 2);
    }

    private static bool Intersects(Rect a, Rect b) => a.X <= b.Right && a.Right >= b.X && a.Y <= b.Bottom && a.Bottom >= b.Y;

    private void StartNewSlider()
    {
        FinishForSelection();
        Select(Guid.Empty);
        tool = Tool.Slider;
        StatusMessage = "";
    }

    private void DeleteSelectedObjects()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        var ids = objectSelection.ToHashSet();
        if (ids.Count == 0 && SelectedTrack is { } track) ids.Add(track.Id);
        if (ids.Count == 0) return;
        if (!Edit(L.Get("editor.command.deleteObjects"), () =>
        {
            Document.Fruits.RemoveAll(f => ids.Contains(f.Id));
            Document.Tracks.RemoveAll(t => ids.Contains(t.Id));
            Document.ImportedSliders.RemoveAll(s => ids.Contains(s.Id));
            Document.BananaShowers.RemoveAll(s => ids.Contains(s.Id));
        })) return;
        Select(Guid.Empty);
        StatusMessage = L.Get("editor.status.objectsDeleted", ids.Count);
    }

    private void DeleteSelectedAnchors()
    {
        if (SelectedTrack is not { } track || anchorSelection.Count == 0) return;
        var ids = anchorSelection.ToArray();
        bool removeTrack = track.Nodes.Count(n => !anchorSelection.Contains(n.Id)) < 2;
        if (track.Id == draftTrack)
        {
            if (removeTrack) { CancelInteraction(); return; }
            try { CurvePointEditing.RemoveMany(track, ids); }
            catch (ArgumentException ex) { StatusMessage = ex.Message; return; }
        }
        else if (!Edit(L.Get("editor.command.deleteAnchors"), () =>
        {
            if (removeTrack) Document.Tracks.Remove(track);
            else CurvePointEditing.RemoveMany(track, ids);
        })) return;
        if (removeTrack) Select(Guid.Empty);
        else SelectAnchors(track, []);
        StatusMessage = removeTrack ? L.Get("editor.status.sliderDeletedTooFewAnchors") : L.Get("editor.status.anchorsDeleted", ids.Length);
    }
}
