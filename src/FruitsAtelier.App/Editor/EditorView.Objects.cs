using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawCatchObject(ICanvas c, ConvertedCatchObject item, float x, float y, float fieldWidth, float opacity = 1, double? circleSize = null, HashSet<(Guid SourceId, int EventIndex)>? hyperStarts = null, bool animated = false, bool caught = false)
    {
        float scale = fieldWidth / 512;
        double cs = circleSize ?? Document.CircleSize;
        float diameter = CatchSize.FruitDiameter(cs) * scale;
        var visual = animated ? CatchObjectVisual.At(item, playhead, PreviewApproachRate, caught) : new CatchObjectVisual(0, 1);
        diameter *= visual.Scale;
        uint colour = ObjectColour(item);
        bool hyper = (hyperStarts ?? hyperdashObjects).Contains((item.SourceId, item.EventIndex));
        uint hyperColour = skin?.HyperDashFruitColour ?? 0xFF3030;
        var kind = SkinObjectKind(item.Kind);
        if (skin is not null)
        {
            int skinIndex = skinIndices.GetValueOrDefault(item.SourceId);
            if (skin.Draw(c, kind, skinIndex, x, y, diameter, colour, opacity, visual.Rotation, hyper ? hyperColour : null)) return;
        }
        float radius = (item.Kind switch { CatchObjectKind.Droplet => CatchSize.DefaultDropletRadius(cs), CatchObjectKind.TinyDroplet => CatchSize.DefaultTinyDropletRadius(cs), CatchObjectKind.Banana => CatchSize.BananaRadius(cs), _ => CatchSize.FruitRadius(cs) }) * scale;
        radius *= visual.Scale;
        if (hyper) c.Circle(x, y, radius * 1.2f, hyperColour, opacity: opacity * .7f);
        c.Circle(x, y, radius, colour, opacity: opacity);
    }

    private static CatchSkinObject SkinObjectKind(CatchObjectKind kind) => kind switch
    {
        CatchObjectKind.Droplet => CatchSkinObject.Droplet,
        CatchObjectKind.TinyDroplet => CatchSkinObject.TinyDroplet,
        CatchObjectKind.Banana => CatchSkinObject.Banana,
        _ => CatchSkinObject.Fruit
    };

    private float ObjectRadius(CatchObjectKind kind) => kind switch
    {
        CatchObjectKind.Droplet => CatchSize.DefaultDropletRadius(Document.CircleSize),
        CatchObjectKind.TinyDroplet => CatchSize.DefaultTinyDropletRadius(Document.CircleSize),
        CatchObjectKind.Banana => CatchSize.BananaRadius(Document.CircleSize),
        _ => CatchSize.FruitRadius(Document.CircleSize)
    };

    private ConvertedCatchObject? HitCatchObject(float x, float y, Guid? sourceId = null)
    {
        EnsureConversion();
        ConvertedCatchObject? closest = null;
        double distance = double.PositiveInfinity;
        double pointerTime = viewStart + (plot.Bottom - y) / pixelsPerMs;
        double timeRadius = Math.Max(7, CatchSize.FruitDiameter(Document.CircleSize) * Playfield.Width / 512) / pixelsPerMs;
        foreach (var item in ObjectsInTimeRange(pointerTime - timeRadius, pointerTime + timeRadius))
        {
            if (sourceId is { } id && item.SourceId != id) continue;
            var point = new MapPoint(item.TimeMs, item.X);
            double candidateDistance = PointerDistance(point, x, y);
            if (candidateDistance >= distance) continue;
            var p = Screen(point);
            float scale = Playfield.Width / 512;
            var bounds = skin?.Bounds(SkinObjectKind(item.Kind), skinIndices.GetValueOrDefault(item.SourceId),
                p.X, p.Y, CatchSize.FruitDiameter(Document.CircleSize) * scale);
            bool hit = bounds is { } b
                ? Math.Abs(x - p.X) <= Math.Max(7, b.Width / 2) && Math.Abs(y - p.Y) <= Math.Max(7, b.Height / 2)
                : Near(point, x, y, Math.Max(7, ObjectRadius(item.Kind) * scale));
            if (hit) { closest = item; distance = candidateDistance; }
        }
        return closest;
    }

    // Conversion output is time-sorted; work scales with the visible time window.
    private IEnumerable<ConvertedCatchObject> ObjectsInTimeRange(double start, double end)
    {
        var objects = playableObjects;
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (objects[middle].TimeMs < start) low = middle + 1;
            else high = middle;
        }
        for (int i = low; i < objects.Count && objects[i].TimeMs <= end; i++) yield return objects[i];
    }

    private bool SegmentNearPointer(CurveTrack track, int segment, float y)
    {
        float a = Screen(Point(track.Nodes[segment])).Y, b = Screen(Point(track.Nodes[segment + 1])).Y;
        return y >= Math.Min(a, b) - 6 && y <= Math.Max(a, b) + 6;
    }

    private Guid HitTrackPath(float x, float y)
    {
        if (!showTargets) return Guid.Empty;
        foreach (var track in Document.Tracks.AsEnumerable().Reverse())
        {
            foreach (var node in track.Nodes)
                if (Near(Point(node), x, y, 9)) return track.Id;
            for (int segment = 0; segment < track.Nodes.Count - 1; segment++)
            {
                if (!SegmentNearPointer(track, segment, y)) continue;
                var previous = Screen(CurveMath.Evaluate(track, segment, 0));
                for (int sample = 1; sample <= 64; sample++)
                {
                    var point = Screen(CurveMath.Evaluate(track, segment, sample / 64.0));
                    if (SegmentDistance(x, y, previous.X, previous.Y, point.X, point.Y) < 6) return track.Id;
                    previous = point;
                }
            }
        }
        return Guid.Empty;
    }

    private Rect BananaRectangle(BananaShower shower)
    {
        float startY = Screen(new(shower.TimeMs, 256)).Y;
        float endY = Screen(new(shower.EndTimeMs, 256)).Y;
        return new(Playfield.X, Math.Min(startY, endY), Playfield.Width, Math.Max(1, Math.Abs(startY - endY)));
    }

    private BananaShower? HitBananaRectangle(float x, float y)
        => Document.BananaShowers.AsEnumerable().Reverse()
            .FirstOrDefault(shower => shower.Id != draftBanana && BananaRectangle(shower).Contains(x, y));

    private bool TryBeginSelectedBananaHandle(float x, float y)
    {
        if (objectSelection.Count != 1 || SelectedBananaShower is not { } shower || shower.Id == draftBanana) return false;
        float centerX = Playfield.X + Playfield.Width / 2;
        float startY = Screen(new(shower.TimeMs, 256)).Y;
        float endY = Screen(new(shower.EndTimeMs, 256)).Y;
        DragKind kind;
        double boundary;
        if (MathF.Abs(x - centerX) <= 10 && MathF.Abs(y - endY) <= 10)
        { kind = DragKind.BananaEnd; boundary = shower.EndTimeMs; }
        else if (MathF.Abs(x - centerX) <= 10 && MathF.Abs(y - startY) <= 10)
        { kind = DragKind.BananaStart; boundary = shower.TimeMs; }
        else return false;

        history.Begin(L.Get("editor.command.resizeBanana"));
        objectDragStart = Document.DeepClone();
        objectDragPrepared = true;
        drag = kind;
        BeginPointerDrag(x, y);
        dragOffset = Transform.ToMap(x, y) - new MapPoint(boundary, 256);
        return true;
    }

    private void MoveBananaBoundary(float x, float y)
    {
        if (objectDragStart is null || SelectedBananaShower is not { } target) return;
        var source = objectDragStart.BananaShowers.Single(item => item.Id == target.Id);
        double rawTime = (Transform.ToMap(x, y) - dragOffset).TimeMs;
        double time = snap ? TimingMap.Snap(Document, rawTime, divisor) : rawTime;
        if (drag == DragKind.BananaStart)
            target.TimeMs = Math.Clamp(time, 0, Math.Max(0, source.EndTimeMs - 0.001));
        else
        {
            target.EndTimeMs = Math.Clamp(time, Math.Min(EditableDurationMs, source.TimeMs + 0.001), EditableDurationMs);
            Document.DurationMs = Math.Max(Document.DurationMs, target.EndTimeMs);
        }
        StatusMessage = L.Get("editor.status.bananaRange", Time(target.TimeMs), Time(target.EndTimeMs));
    }

    private bool objectDragTimeline;
    private double objectDragTimelineScale;
    private void BeginObjectDrag(float x, float y, bool timeline = false)
    {
        if (notesLocked) return;
        if (objectSelection.Count == 0) return;
        bool movesOneFruit = objectSelection.Count == 1 && Document.Fruits.Any(item => objectSelection.Contains(item.Id));
        history.Begin(L.Get(movesOneFruit ? "editor.command.moveFruit" : "editor.command.moveObjects"));
        objectDragStart = Document.DeepClone();
        objectDragPrepared = false;
        objectDragTimeline = timeline;
        objectDragTimelineScale = objectTimelineScale;
        drag = DragKind.Objects;
        BeginPointerDrag(x, y);
    }

    private Dictionary<Guid, Fruit> dragFruits = [];
    private Dictionary<Guid, CurveTrack> dragTracks = [];
    private Dictionary<Guid, BananaShower> dragBananas = [];
    private void MoveSelectedObjects(float x, float y)
    {
        if (objectDragStart is null) return;
        if (!objectDragPrepared)
        {
            try
            {
                foreach (Guid id in objectSelection.Where(id => !objectDragTimeline && Document.ImportedSliders.Any(slider => slider.Id == id)).ToArray())
                    ImportedSliderEditing.ConvertToTrack(Document, id);
                objectDragStart = Document.DeepClone();
                dragFruits = Document.Fruits.ToDictionary(item => item.Id);
                dragTracks = Document.Tracks.ToDictionary(item => item.Id);
                dragBananas = Document.BananaShowers.ToDictionary(item => item.Id);
                objectDragPrepared = true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                history.Cancel();
                objectDragStart = null;
                drag = DragKind.None;
                StatusMessage = error.Message;
                return;
            }
        }
        var startPointer = Transform.ToMap(dragStartX, dragStartY);
        var pointer = Transform.ToMap(x, y);
        double deltaTime = objectDragTimeline ? (x - dragStartX) / objectDragTimelineScale : pointer.TimeMs - startPointer.TimeMs;
        double deltaX = objectDragTimeline ? 0 : pointer.X - startPointer.X;
        double minTime = double.PositiveInfinity, maxTime = double.NegativeInfinity;
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;

        foreach (var fruit in objectDragStart.Fruits.Where(item => objectSelection.Contains(item.Id)))
        {
            IncludeTime(fruit.TimeMs);
            IncludeX(fruit.X);
        }
        foreach (var track in objectDragStart.Tracks.Where(item => objectSelection.Contains(item.Id)))
        {
            foreach (var node in track.Nodes)
            {
                IncludeTime(node.TimeMs);
                IncludeX(node.X);
                IncludeX(node.X + node.HandleIn.X);
                IncludeX(node.X + node.HandleOut.X);
                if (node.OutgoingCurve is { } curve) foreach (var point in curve.Controls) IncludeX(node.X + point.Offset.X);
            }
            if (track.Nodes.Count >= 2)
                IncludeTime(track.Nodes[0].TimeMs + (track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs) * track.SpanCount);
        }
        foreach (var shower in objectDragStart.BananaShowers.Where(item => objectSelection.Contains(item.Id)))
        {
            IncludeTime(shower.TimeMs);
            IncludeTime(shower.EndTimeMs);
        }

        foreach (var slider in objectDragStart.ImportedSliders.Where(item => objectSelection.Contains(item.Id)))
        {
            IncludeTime(slider.TimeMs);
            IncludeTime(ImportedSliderConverter.EndTimeMs(objectDragStart, slider));
        }

        if (snap && double.IsFinite(minTime))
            deltaTime = TimingMap.Snap(Document, minTime + deltaTime, divisor) - minTime;
        if (double.IsFinite(minTime)) deltaTime = Math.Clamp(deltaTime, -minTime, EditableDurationMs - maxTime);
        else deltaTime = 0;
        if (!objectDragTimeline && double.IsFinite(minX)) deltaX = Math.Clamp(SnapX(minX + deltaX) - minX, -minX, 512 - maxX);
        else deltaX = 0;

        if (!objectDragTimeline && DistanceSnapEnabled && double.IsFinite(minTime))
        {
            var first = objectDragStart.Fruits.Where(f => objectSelection.Contains(f.Id)).Select(f => new MapPoint(f.TimeMs, f.X))
                .Concat(objectDragStart.Tracks.Where(t => objectSelection.Contains(t.Id)).Select(t => Point(t.Nodes[0])))
                .OrderBy(p => p.TimeMs).Cast<MapPoint?>().FirstOrDefault();
            if (first is { } origin)
            {
                var target = SnapDistance(origin + new MapPoint(deltaTime, deltaX), objectSelection);
                deltaX = Math.Clamp(SnapX(target.X) - origin.X, -minX, 512 - maxX);
            }
        }

        foreach (var source in objectDragStart.Fruits.Where(item => objectSelection.Contains(item.Id)))
        {
            var target = dragFruits[source.Id];
            target.TimeMs = source.TimeMs + deltaTime;
            target.X = source.X + deltaX;
        }
        foreach (var source in objectDragStart.Tracks.Where(item => objectSelection.Contains(item.Id)))
        {
            var target = dragTracks[source.Id];
            for (int i = 0; i < source.Nodes.Count; i++)
            {
                var sourceNode = source.Nodes[i];
                var targetNode = target.Nodes[i];
                targetNode.TimeMs = sourceNode.TimeMs + deltaTime;
                targetNode.X = sourceNode.X + deltaX;
            }
        }
        foreach (var source in objectDragStart.BananaShowers.Where(item => objectSelection.Contains(item.Id)))
        {
            var target = dragBananas[source.Id];
            target.TimeMs = source.TimeMs + deltaTime;
            target.EndTimeMs = source.EndTimeMs + deltaTime;
        }
        foreach (var source in objectDragStart.ImportedSliders.Where(item => objectSelection.Contains(item.Id)))
        {
            var target = Document.ImportedSliders.First(s => s.Id == source.Id);
            target.TimeMs = source.TimeMs + deltaTime;
            if (source.OriginalLine is { } original)
            {
                var fields = original.Split(',');
                fields[2] = target.TimeMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                target.OriginalLine = string.Join(',', fields);
            }
        }
        if (double.IsFinite(maxTime)) Document.DurationMs = Math.Max(Document.DurationMs, maxTime + deltaTime);
        StatusMessage = L.Get("editor.status.objectsMoved", objectSelection.Count, Number(deltaTime), Number(deltaX));

        void IncludeTime(double value) { minTime = Math.Min(minTime, value); maxTime = Math.Max(maxTime, value); }
        void IncludeX(double value) { minX = Math.Min(minX, value); maxX = Math.Max(maxX, value); }
    }
}
