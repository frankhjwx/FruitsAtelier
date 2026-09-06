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
    private bool boxAdds, boxAnchors;
    private sealed record SelectionSnapshot(Guid[] Objects, Guid[] Anchors, Guid Primary, Guid Track, DragKind Part);
    public IReadOnlyCollection<Guid> SelectedObjectIds => objectSelection.ToArray();
    public IReadOnlyCollection<Guid> SelectedAnchorIds => anchorSelection.ToArray();
    private bool IsObjectSelected(Guid id) => objectSelection.Contains(id) || tool == Tool.Slider && selectedTrack == id;

    private void SelectObjects(IEnumerable<Guid> ids, Guid primary = default)
    {
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
        var selected = ids.Where(id => track.Nodes.Any(n => n.Id == id)).Distinct().ToArray();
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
        selectionBeforeBox = new(objectSelection.ToArray(), anchorSelection.ToArray(), selection, selectedTrack, selectedPart);
        boxAdds = additive; boxAnchors = anchors;
        selectionBox = new(x, y, 0, 0);
        BeginPointerDrag(x, y);
        drag = DragKind.Marquee;
    }

    private void MoveBox(float x, float y)
    {
        x = Math.Clamp(x, plot.X, plot.Right); y = Math.Clamp(y, plot.Y, plot.Bottom);
        if (Math.Abs(x - dragStartX) >= 3 || Math.Abs(y - dragStartY) >= 3) dragMoved = true;
        if (!dragMoved || selectionBeforeBox is null) return;
        selectionBox = new(Math.Min(x, dragStartX), Math.Min(y, dragStartY), Math.Abs(x - dragStartX), Math.Abs(y - dragStartY));
        if (boxAnchors)
        {
            var track = Document.Tracks.FirstOrDefault(t => t.Id == selectionBeforeBox.Track);
            if (track is null) return;
            var ids = boxAdds ? selectionBeforeBox.Anchors.ToHashSet() : [];
            if (showTargets)
                foreach (var node in track.Nodes)
                {
                    var point = Screen(Point(node));
                    if (selectionBox.Contains(point.X, point.Y)) ids.Add(node.Id);
                }
            SelectAnchors(track, ids);
        }
        else
        {
            var ids = boxAdds ? selectionBeforeBox.Objects.ToHashSet() : [];
            EnsureConversion();
            foreach (var item in conversion!.Objects)
            {
                var bounds = CatchHitBounds(item);
                if (Intersects(bounds, plot) && Intersects(bounds, selectionBox)) ids.Add(item.SourceId);
            }
            SelectObjects(ids);
        }
        StatusMessage = boxAnchors ? L.Get("editor.status.boxAnchors", anchorSelection.Count) : L.Get("editor.status.boxObjects", objectSelection.Count);
    }

    private void FinishBox(float x, float y)
    {
        bool moved = dragMoved;
        drag = DragKind.None;
        selectionBeforeBox = null;
        if (!moved && !boxAdds)
        {
            if (boxAnchors)
            {
                tool = Tool.Select;
                Select(Guid.Empty);
                SeekTo(MapAt(x, y, true).TimeMs);
            }
            else if (tool == Tool.Fruit)
            {
                var point = MapAt(x, y, true);
                var fruit = new Fruit { TimeMs = point.TimeMs, X = point.X };
                if (Edit(L.Get("editor.command.addFruit"), () =>
                    {
                        Document.Fruits.Add(fruit);
                        Document.DurationMs = Math.Max(Document.DurationMs, fruit.TimeMs);
                    })) Select(fruit.Id);
            }
            else { Select(Guid.Empty); SeekTo(MapAt(x, y, true).TimeMs); }
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
        drag = DragKind.None;
        if (AudioPlaying || pinPlayhead) FollowPlayhead();
    }

    private void DrawSelectionBox(ICanvas c)
    {
        if (drag != DragKind.Marquee || !dragMoved) return;
        c.Clip(plot);
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
        StatusMessage = L.Get("editor.help.newSlider");
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
