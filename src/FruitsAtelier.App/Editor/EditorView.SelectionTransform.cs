using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const uint SelectionTransformColour = 0x35ABC9;
    private IReadOnlyList<ConvertedCatchObject>? selectionBoundsObjects;
    private double selectionBoundsCircleSize = double.NaN;
    private readonly HashSet<Guid> selectionBoundsIds = [];
    private (double Left, double Right, double Start, double End)? selectionMapBounds;
    private double selectionVisualLeft, selectionVisualRight;
    private double selectionFruitStart, selectionFruitEnd, selectionDropletStart, selectionDropletEnd;
    private double selectionTinyDropletStart, selectionTinyDropletEnd;
    private int selectionScaleSide;
    private double selectionScaleLeft, selectionScaleRight, selectionScalePointerX;
    private double selectionScaleMaximum = double.PositiveInfinity;

    internal Rect SelectionTransformBounds
    {
        get
        {
            if (tool != Tool.Select || draftTrack != Guid.Empty || draftBanana != Guid.Empty || objectSelection.Count == 0)
                return default;
            RefreshSelectionBounds();
            if (selectionMapBounds is not { } bounds) return default;
            var a = Screen(new(bounds.Start, selectionVisualLeft));
            var b = Screen(new(bounds.End, selectionVisualRight));
            float top = Math.Min(a.Y, b.Y), bottom = Math.Max(a.Y, b.Y);
            Include(CatchObjectKind.Fruit, selectionFruitStart, selectionFruitEnd);
            Include(CatchObjectKind.Droplet, selectionDropletStart, selectionDropletEnd);
            Include(CatchObjectKind.TinyDroplet, selectionTinyDropletStart, selectionTinyDropletEnd);
            return new(a.X, top, b.X - a.X, bottom - top);
            void Include(CatchObjectKind kind, double start, double end)
            {
                if (!double.IsFinite(start)) return;
                float radius = ObjectRadius(kind) * Playfield.Width / 512;
                top = Math.Min(top, Screen(new(end, 0)).Y - radius);
                bottom = Math.Max(bottom, Screen(new(start, 0)).Y + radius);
            }
        }
    }

    private void RefreshSelectionBounds()
    {
        if (ReferenceEquals(selectionBoundsObjects, playableObjects) && selectionBoundsCircleSize == Document.CircleSize
            && selectionBoundsIds.SetEquals(objectSelection)) return;
        selectionBoundsObjects = playableObjects;
        selectionBoundsCircleSize = Document.CircleSize;
        selectionBoundsIds.Clear(); selectionBoundsIds.UnionWith(objectSelection);
        selectionMapBounds = null;
        RefreshTimelineSources();
        int first = Array.FindIndex(timelineSources, s => objectSelection.Contains(s.Id));
        int last = Array.FindLastIndex(timelineSources, s => objectSelection.Contains(s.Id));
        if (first < 0 || last - first + 1 != objectSelection.Count) return;
        for (int i = first; i <= last; i++)
            if (timelineSources[i].IsBanana || !objectSelection.Contains(timelineSources[i].Id)) return;
        if (first == last && !Document.Tracks.Any(t => t.Id == timelineSources[first].Id)
            && !Document.ImportedSliders.Any(t => t.Id == timelineSources[first].Id)) return;
        double start = timelineSources[first].Start, end = start;
        for (int i = first; i <= last; i++) end = Math.Max(end, timelineSources[i].End);
        double left = double.PositiveInfinity, right = double.NegativeInfinity;
        selectionVisualLeft = selectionFruitStart = selectionDropletStart = selectionTinyDropletStart = double.PositiveInfinity;
        selectionVisualRight = selectionFruitEnd = selectionDropletEnd = selectionTinyDropletEnd = double.NegativeInfinity;
        // Read-back rounds object times; include the rounded endpoints around the authored interval.
        foreach (var item in ObjectsInTimeRange(start - 1, end + 1))
        {
            if (!objectSelection.Contains(item.SourceId) || item.Kind is not (CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet)) continue;
            left = Math.Min(left, item.X); right = Math.Max(right, item.X);
            double radius = ObjectRadius(item.Kind);
            selectionVisualLeft = Math.Min(selectionVisualLeft, item.X - radius);
            selectionVisualRight = Math.Max(selectionVisualRight, item.X + radius);
            if (item.Kind == CatchObjectKind.Fruit)
            { selectionFruitStart = Math.Min(selectionFruitStart, item.TimeMs); selectionFruitEnd = Math.Max(selectionFruitEnd, item.TimeMs); }
            else if (item.Kind == CatchObjectKind.Droplet)
            { selectionDropletStart = Math.Min(selectionDropletStart, item.TimeMs); selectionDropletEnd = Math.Max(selectionDropletEnd, item.TimeMs); }
            else
            { selectionTinyDropletStart = Math.Min(selectionTinyDropletStart, item.TimeMs); selectionTinyDropletEnd = Math.Max(selectionTinyDropletEnd, item.TimeMs); }
        }
        if (!double.IsFinite(left)) return;
        selectionMapBounds = (left, right, start, end);
    }

    private static Rect SelectionScaleHandle(Rect bounds, int side)
        => new((side < 0 ? bounds.X : bounds.Right) - 9, bounds.Y + bounds.Height / 2 - 12, 18, 24);

    public bool SelectionScaleCursor => !notesLocked && !LibraryVisible && !ExportVisible && !ErrorVisible
        && !DiscardConfirmationVisible && !SliderDialogVisible && !TimeJumpVisible && menu < 0
        && (drag == DragKind.Objects && selectionScaleSide != 0
            || drag == DragKind.None && plot.Contains(mouseX, mouseY) && SelectionTransformBounds is { Width: > 0 } bounds
                && (SelectionScaleHandle(bounds, -1).Contains(mouseX, mouseY) || SelectionScaleHandle(bounds, 1).Contains(mouseX, mouseY)));

    private void DrawSelectionTransform(ICanvas c, bool background)
    {
        var bounds = SelectionTransformBounds;
        if (bounds.Width <= 0) return;
        if (background) { c.Fill(bounds, SelectionTransformColour, opacity: .10f); return; }
        c.Stroke(bounds, SelectionTransformColour, 1.5f);
        for (int side = -1; side <= 1; side += 2)
        {
            var handle = new Rect((side < 0 ? bounds.X : bounds.Right) - 4, bounds.Y + bounds.Height / 2 - 4, 8, 8);
            c.Fill(handle, Background);
            c.Stroke(handle, SelectionTransformColour, 1.5f);
        }
    }

    private bool HitSelectedSliderControl(float x, float y)
    {
        if (!showTargets || objectSelection.Count != 1 || SelectedTrack is not { } track) return false;
        if (LegacyMode) return SliderControlEditing.Vertices(track).Any(v => Near(v.Point, x, y, 9));
        for (int i = 0; i < track.Nodes.Count; i++)
        {
            if (Near(Point(track.Nodes[i]), x, y, 9)) return true;
            if (i > 0 && CurveMath.SegmentKind(track, i - 1) == CurveKind.Bezier && PenHandle(track, i, true) != default
                && HitPenHandle(track, i, true, x, y)) return true;
            if (i < track.Nodes.Count - 1 && CurveMath.SegmentKind(track, i) == CurveKind.Bezier && PenHandle(track, i, false) != default
                && HitPenHandle(track, i, false, x, y)) return true;
        }
        return false;
    }

    private bool TryBeginSelectionTransform(float x, float y)
    {
        if (notesLocked) return false;
        var bounds = SelectionTransformBounds;
        if (bounds.Width <= 0) return false;
        int side = SelectionScaleHandle(bounds, -1).Contains(x, y) ? -1
            : SelectionScaleHandle(bounds, 1).Contains(x, y) ? 1 : 0;
        if (side == 0 && (SelectedTrack?.StreamSnapDivisor is null || !bounds.Contains(x, y))
            && TryBeginSelectedSliderObjectDrag(x, y, requireSelectedFruit: true)) return true;
        if (HitSelectedSliderControl(x, y)) return false;
        if (side == 0 && !bounds.Contains(x, y)) return false;
        if (side != 0 && selectionMapBounds is { } map && map.Right - map.Left < .001) return true;
        BeginObjectDrag(x, y, scaleSelection: side != 0);
        selectionScaleSide = side;
        if (objectSelection.Count == 1 && HitCatchObject(x, y) is { } child && objectSelection.Contains(child.SourceId))
            pendingStreamChildSelection = child;
        if (side != 0 && selectionMapBounds is { } original)
        {
            selectionScaleLeft = original.Left; selectionScaleRight = original.Right;
            selectionScalePointerX = Transform.ToMap(x, y).X;
        }
        return true;
    }

    private void ScaleSelectedObjects(float x)
    {
        if (!PrepareObjectDrag()) return;
        double fixedX = selectionScaleSide < 0 ? selectionScaleRight : selectionScaleLeft;
        double movingX = selectionScaleSide < 0 ? selectionScaleLeft : selectionScaleRight;
        double wanted = SnapX(movingX + Transform.ToMap(x, dragStartY).X - selectionScalePointerX);
        wanted = selectionScaleSide < 0 ? Math.Clamp(wanted, 0, fixedX - .001) : Math.Clamp(wanted, fixedX + .001, 512);
        double factor = (wanted - fixedX) / (movingX - fixedX);
        factor = Math.Min(factor, selectionScaleMaximum);
        double Position(double value) => fixedX + (value - fixedX) * factor;
        MapPoint Offset(MapPoint value) => new(value.TimeMs, value.X * factor);
        foreach (var source in objectDragStart!.Fruits.Where(f => objectSelection.Contains(f.Id)))
            dragFruits[source.Id].X = Position(source.X);
        foreach (var source in objectDragStart.Tracks.Where(t => objectSelection.Contains(t.Id)))
        {
            var target = dragTracks[source.Id];
            for (int i = 0; i < source.Nodes.Count; i++)
            {
                var a = source.Nodes[i]; var b = target.Nodes[i];
                b.X = Position(a.X); b.HandleIn = Offset(a.HandleIn); b.HandleOut = Offset(a.HandleOut);
                if (a.OutgoingCurve is { } curve)
                    for (int j = 0; j < curve.Controls.Count; j++) b.OutgoingCurve!.Controls[j].Offset = Offset(curve.Controls[j].Offset);
            }
            if (source.Stack is { } stack)
            {
                target.Stack!.Points = stack.Points.Select(p => p with { Distance = p.Distance * factor }).ToList();
                target.Stack.FruitAdjustments = stack.FruitAdjustments.Select(p => p with { Offset = p.Offset * factor }).ToList();
            }
            if (source.DropletRandomization is { } random)
                target.DropletRandomization!.Adjustments = random.Adjustments.Select(p => p with { Offset = p.Offset * factor }).ToList();
        }
        StatusMessage = L.Get("editor.status.objectsScaled", objectSelection.Count, Number(factor));
    }
}
