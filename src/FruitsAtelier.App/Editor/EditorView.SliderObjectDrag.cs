using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ConvertedCatchObject? sliderObjectDragTarget;
    private double sliderObjectDragX;
    private double sliderObjectPointerOriginX;
    private MapDocument? sliderObjectDragSource, sliderObjectDragShape;
    private int sliderObjectTrackIndex, sliderObjectImportIndex;
    private ConvertedCatchObject? sliderObjectDragPrevious;
    private ConvertedCatchObject? pendingStreamChildSelection;
    private bool streamEndpointTimeDrag;

    private bool TryBeginSelectedSliderObjectDrag(float x, float y, bool requireSelectedFruit = false)
    {
        var track = SelectedTrack;
        bool stream = track?.StreamSnapDivisor is not null;
        if (stream && HitSelectedSliderControl(x, y)) return false;
        if (tool != Tool.Select || objectSelection.Count != 1
            || HitCatchObject(x, y) is not { Kind: CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet } target
            || !objectSelection.Contains(target.SourceId)
            || requireSelectedFruit && target.Kind == CatchObjectKind.Fruit && !stream && distanceObject != (target.SourceId, target.EventIndex)
            || target.IsStandalone && (target.Kind != CatchObjectKind.Fruit
                || !Document.Tracks.Any(track => track.Id == target.SourceId && track.StreamSnapDivisor is not null)))
            return false;
        if (target.Kind != CatchObjectKind.Fruit && track is not null && showTargets && distanceObject != (target.SourceId, target.EventIndex))
        {
            double distance = PointerDistance(new(target.TimeMs, target.X), x, y);
            var controls = LegacyMode ? SliderControlEditing.Vertices(track).Select(v => v.Point) : track.Nodes.Select(Point);
            // Nearby fitted controls must not steal a click closer to the visible droplet.
            if (controls.Any(p => Near(p, x, y, 9) && PointerDistance(p, x, y) <= distance + .01)) return false;
        }
        PickSoundEdge(target);
        BeginSliderObjectDrag(target, x, y);
        return true;
    }

    private void BeginSliderObjectDrag(ConvertedCatchObject target, float x, float y)
    {
        sliderObjectPointerOriginX = target.X;
        EnsureConversion();
        // Hit testing uses exported coordinates. Editing needs the authored event's
        // exact time and X, particularly when a fractional tail rounds past its anchor.
        target = conversion!.Objects.FirstOrDefault(item => item.SourceId == target.SourceId
            && item.EventIndex == target.EventIndex && item.Kind == target.Kind) ?? target;
        StatusMessage = L.Get("editor.status.sliderObjectReady", Time(target.TimeMs));
        if (notesLocked) return;
        history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
        sliderObjectDragTarget = target;
        sliderObjectDragX = target.X;
        sliderObjectTrackIndex = Document.Tracks.FindIndex(t => t.Id == target.SourceId);
        sliderObjectImportIndex = Document.ImportedSliders.FindIndex(t => t.Id == target.SourceId);
        sliderObjectDragSource = new MapDocument();
        if (sliderObjectTrackIndex >= 0) sliderObjectDragSource.Tracks.Add(Document.Tracks[sliderObjectTrackIndex]);
        if (sliderObjectImportIndex >= 0) sliderObjectDragSource.ImportedSliders.Add(Document.ImportedSliders[sliderObjectImportIndex]);
        sliderObjectDragShape = null;
        streamEndpointTimeDrag = false;
        EnsureConversion();
        var large = conversion!.Objects.Where(item => item.SourceId == target.SourceId
            && (item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet
                || target.Kind == CatchObjectKind.TinyDroplet && item.Kind == CatchObjectKind.TinyDroplet))
            .OrderBy(item => item.TimeMs).ThenBy(item => item.EventIndex).ToArray();
        int eventPosition = Array.FindIndex(large, item => item.EventIndex == target.EventIndex);
        sliderObjectDragPrevious = eventPosition > 0 ? large[eventPosition - 1] : null;
        distanceObject = (target.SourceId, target.EventIndex);
        drag = DragKind.SliderObject;
        BeginPointerDrag(x, y);
    }

    private bool TryBeginSliderEndpointTimeDrag(float y, bool navigation = false)
    {
        if (!navigation && Math.Abs(y - dragStartY) < 2 || sliderObjectDragTarget is not { Kind: CatchObjectKind.Fruit } target) return false;
        var source = sliderObjectDragSource?.Tracks.FirstOrDefault();
        bool imported = source is null && sliderObjectDragSource?.ImportedSliders.Count > 0;
        if (imported)
        {
            var generated = conversion!.Sliders.FirstOrDefault(s => s.SourceId == target.SourceId);
            if (generated is null || Math.Abs(target.TimeMs - generated.StartTimeMs) >= .001
                && Math.Abs(target.TimeMs - generated.StartTimeMs - generated.DurationMs / generated.SpanCount) >= .001) return false;
            try { PrepareSliderObjectShape(target); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
            { RestoreSliderObjectSource(sliderObjectDragSource!); StatusMessage = error.Message; return false; }
            source = sliderObjectDragShape!.Tracks.Single();
        }
        if (source is not { StreamSnapDivisor: null } || source.Nodes.Count < 2) return false;
        var endpoint = new[] { source.Nodes[0], source.Nodes[^1] }
            .FirstOrDefault(node => Math.Abs(node.TimeMs - target.TimeMs) < .001);
        if (endpoint is null) return false;

        // Restart from the gesture's source so an earlier horizontal move and the
        // endpoint time edit remain a single undo step.
        if (imported) RestoreSliderObjectSource(sliderObjectDragShape!);
        else history.Cancel();
        var track = Document.Tracks.Single(item => item.Id == source.Id);
        var node = track.Nodes.Single(item => item.Id == endpoint.Id);
        SelectAnchors(track, [node.Id]);
        if (LegacyMode) BeginLegacyDrag(track, Point(node), dragStartX, dragStartY, alreadyBegun: imported);
        else if (imported) ContinueNodeDrag(track, node, DragKind.Anchor, dragStartX, dragStartY);
        else BeginNodeDrag(track, node, DragKind.Anchor, dragStartX, dragStartY);
        sliderObjectDragTarget = null;
        sliderObjectDragSource = sliderObjectDragShape = null;
        sliderObjectDragPrevious = null;
        return true;
    }

    private bool MoveStreamEndpoint(float x, float y)
    {
        if (sliderObjectDragTarget is not { Kind: CatchObjectKind.Fruit } target
            || sliderObjectDragSource?.Tracks.FirstOrDefault() is not { StreamSnapDivisor: not null } source
            || source.Nodes.Count < 2) return false;
        bool head = Math.Abs(target.TimeMs - source.Nodes[0].TimeMs) < .001;
        bool tail = Math.Abs(target.TimeMs - CurveMath.EndTimeMs(source)) < .001;
        if ((!head && !tail) || !streamEndpointTimeDrag && Math.Abs(y - dragStartY) < 2) return false;
        streamEndpointTimeDrag = true;
        var pointer = Transform.ToMap(x, y);
        var origin = Transform.ToMap(dragStartX, dragStartY);
        double time = target.TimeMs + pointer.TimeMs - origin.TimeMs;
        if (snap) time = TimingMap.Snap(Document, time, divisor);
        time = Math.Clamp(time, 0, EditableDurationMs);
        double wantedX = Math.Clamp(SnapX(sliderObjectPointerOriginX + pointer.X - origin.X), 0, 512);
        var accepted = new MapDocument(); accepted.Tracks.Add(Document.Tracks.Single(t => t.Id == source.Id));
        RestoreSliderObjectSource(sliderObjectDragSource);
        var track = Document.Tracks.Single(t => t.Id == source.Id);
        // A repeated tail changes the span duration; its X belongs to the final traversal's endpoint.
        var xNode = head || track.SpanCount % 2 == 0 ? track.Nodes[0] : track.Nodes[^1];
        var timeNode = head ? track.Nodes[0] : track.Nodes[^1];
        double nodeTime = head ? time : source.Nodes[0].TimeMs + (time - source.Nodes[0].TimeMs) / source.SpanCount;
        bool moved = CurveMath.TryMoveAnchor(track, xNode.Id, xNode.TimeMs, wantedX, out var error)
            && CurveMath.TryMoveAnchor(track, timeNode.Id, nodeTime, timeNode.X, out error);
        if (!moved || !CatchStreamConverter.Convert(Document, compensateTinyDroplets, editorConversionCache).Success)
        {
            RestoreSliderObjectSource(accepted);
            StatusMessage = moved ? L.Get("coordinate.unreachable") : error;
            return true;
        }
        Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
        StatusMessage = L.Get("editor.status.anchorPosition", Time(time), Number(wantedX));
        return true;
    }

    private void MoveSliderObject(float x)
    {
        if (sliderObjectDragTarget is not { } target) return;
        if (Math.Abs(x - dragStartX) < .001)
        {
            if (Math.Abs(sliderObjectDragX - target.X) >= .00001)
            {
                RestoreSliderObjectSource(sliderObjectDragSource!);
                sliderObjectDragX = target.X;
                EnsureConversion();
                StatusMessage = L.Get("editor.status.sliderObjectPosition", Time(target.TimeMs), Number(target.X));
            }
            return;
        }
        double rawX = Math.Clamp(sliderObjectPointerOriginX + (x - dragStartX) / Playfield.Width * 512, 0, 512);
        bool strict = DistanceSnapEnabled && sliderObjectDragPrevious is not null;
        double wantedX = strict ? rawX : Math.Clamp(SnapX(rawX), 0, 512);
        if (strict)
        {
            var acceptedSource = new MapDocument();
            acceptedSource.Tracks.AddRange(Document.Tracks.Where(track => track.Id == target.SourceId));
            acceptedSource.ImportedSliders.AddRange(Document.ImportedSliders.Where(slider => slider.Id == target.SourceId));
            try
            {
                PrepareSliderObjectShape(target);
                foreach (double position in SliderObjectDistanceCandidates(target, wantedX))
                {
                    if (!TrySliderObjectPosition(target, position)) continue;
                    sliderObjectDragX = position;
                    EnsureConversion();
                    StatusMessage = L.Get("editor.status.sliderObjectPosition", Time(target.TimeMs), Number(position));
                    distanceObject = (target.SourceId, target.EventIndex);
                    return;
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException) { }
            RestoreSliderObjectSource(acceptedSource);
            StatusMessage = L.Get("editor.error.sliderDistanceSnap");
            return;
        }
        if (Math.Abs(wantedX - sliderObjectDragX) < .00001) return;
        double acceptedX = sliderObjectDragX;
        if (!TrySliderObjectPosition(target, wantedX))
        {
            double blockedX = wantedX;
            for (int i = 0; i < 12 && Math.Abs(blockedX - acceptedX) > .001; i++)
            {
                double middle = SnapX((acceptedX + blockedX) / 2);
                if (Math.Abs(middle - acceptedX) < .00001 || Math.Abs(middle - blockedX) < .00001) break;
                if (TrySliderObjectPosition(target, middle)) acceptedX = middle;
                else blockedX = middle;
            }
            TrySliderObjectPosition(target, acceptedX);
        }
        else acceptedX = wantedX;
        sliderObjectDragX = acceptedX;
        StatusMessage = L.Get("editor.status.sliderObjectPosition", Time(target.TimeMs), Number(acceptedX));
        EnsureConversion();
        if (conversion!.Objects.FirstOrDefault(item => item.SourceId == target.SourceId
            && item.EventIndex == target.EventIndex) is { } updated)
            distanceObject = (updated.SourceId, updated.EventIndex);
    }

    private bool TrySliderObjectPosition(ConvertedCatchObject target, double x)
    {
        try
        {
            if (Math.Abs(x - target.X) < .00001)
            {
                RestoreSliderObjectSource(sliderObjectDragSource!);
                return true;
            }
            PrepareSliderObjectShape(target);
            // Every candidate starts from the same fitted shape; only this source needs a copy.
            RestoreSliderObjectSource(sliderObjectDragShape!);
            var track = Document.Tracks.First(t => t.Id == target.SourceId);
            double sampleTime = CurveMath.FirstSpanTime(track, target.TimeMs);
            bool streamInterior = track.StreamSnapDivisor is not null
                && sampleTime > track.Nodes[0].TimeMs + .001 && sampleTime < track.Nodes[^1].TimeMs - .001;
            if (target.Kind == CatchObjectKind.Fruit && !streamInterior)
                DistanceSpacingEditing.ApplyX(Document, target, x, compensateTinyDroplets, editorConversionCache);
            else DistanceSpacingEditing.ApplyIsolatedX(Document, target, x, compensateTinyDroplets, editorConversionCache);
            if (DistanceSnapEnabled)
            {
                var result = CatchStreamConverter.Convert(Document, compensateTinyDroplets, editorConversionCache);
                if (!StrictSliderObjectResult(target, result))
                    throw new InvalidOperationException(L.Get("editor.error.sliderDistanceSnap"));
            }
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            RestoreSliderObjectSource(sliderObjectDragSource!);
            return false;
        }
    }

    private void PrepareSliderObjectShape(ConvertedCatchObject target)
    {
        if (sliderObjectDragShape is not null) return;
        RestoreSliderObjectSource(sliderObjectDragSource!);
        var track = Document.Tracks.FirstOrDefault(t => t.Id == target.SourceId)
            ?? ConvertImportedSlider(target.SourceId, editorConversionCache).Track;
        sliderObjectDragShape = new MapDocument();
        sliderObjectDragShape.Tracks.Add(track.DeepClone());
    }

    private IEnumerable<MapPoint> StraightSliderCandidates(MapPoint target, MapPoint fixedPoint)
    {
        double velocity = DistanceSnap.BaseVelocity(Document, Math.Min(target.TimeMs, fixedPoint.TimeMs));
        double dt = Math.Abs(target.TimeMs - fixedPoint.TimeMs);
        return new[] { 0d }.Concat(Document.DistanceSnapRatios.Take(DistanceSnap.MaximumPresets)
                .Where(ratio => double.IsFinite(ratio) && ratio > 0))
            .SelectMany(ratio => ratio == 0 ? new[] { fixedPoint.X }
                : new[] { fixedPoint.X - dt * velocity * ratio, fixedPoint.X + dt * velocity * ratio })
            .Where(position => position is >= 0 and <= 512)
            .Distinct()
            .OrderBy(position => Math.Abs(position - target.X))
            .Select(position => target with { X = position });
    }

    private IEnumerable<double> SliderObjectDistanceCandidates(ConvertedCatchObject target, double wantedX)
    {
        var reference = sliderObjectDragPrevious;
        if (reference is null) return [];
        if (target.Kind is CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet
            || sliderObjectDragShape!.Tracks[0].StreamSnapDivisor is not null)
            return StraightSliderCandidates(new(target.TimeMs, wantedX), new(reference.TimeMs, reference.X))
                .Select(point => point.X);

        // Endpoint edits move the neighbouring droplets. Solve against the reshaped
        // authoring curve, then validate the actual generated events before accepting.
        var shape = sliderObjectDragShape!.Tracks[0].DeepClone();
        double time = CurveMath.FirstSpanTime(shape, target.TimeMs);
        var node = shape.Nodes.FirstOrDefault(item => Math.Abs(item.TimeMs - time) < .001);
        if (node is null) return [];
        double departure = Math.Min(reference.TimeMs, target.TimeMs);
        double scale = Math.Abs(reference.TimeMs - target.TimeMs) * DistanceSnap.BaseVelocity(Document, departure);
        if (scale <= 0) return [];
        double Difference(double x)
        {
            node.X = x;
            return x - CurveMath.PositionAtTime(shape, reference.TimeMs);
        }
        return SliderDistanceSnap.EndpointCandidates(Document, wantedX, scale, Difference);
    }

    private bool StrictSliderObjectResult(ConvertedCatchObject target, CatchConversionResult result)
    {
        var current = result.Objects.FirstOrDefault(item => item.SourceId == target.SourceId && item.EventIndex == target.EventIndex);
        if (current is null) return false;
        bool Pair(ConvertedCatchObject? original)
        {
            if (original is null) return true;
            var adjacent = result.Objects.FirstOrDefault(item => item.SourceId == target.SourceId && item.EventIndex == original.EventIndex);
            if (adjacent is null) return false;
            var from = current.TimeMs < adjacent.TimeMs ? current : adjacent;
            var to = current.TimeMs < adjacent.TimeMs ? adjacent : current;
            double? ratio = DistanceSnap.Ratio(new(from.TimeMs, from.X), new(to.TimeMs, to.X),
                DistanceSnap.BaseVelocity(Document, from.TimeMs));
            return ratio is null || SliderDistanceSnap.MatchesPreset(Document, ratio.Value);
        }
        return Pair(sliderObjectDragPrevious);
    }

    private void RestoreSliderObjectSource(MapDocument source)
    {
        Guid id = sliderObjectDragTarget!.SourceId;
        Document.Tracks.RemoveAll(t => t.Id == id);
        Document.ImportedSliders.RemoveAll(t => t.Id == id);
        var copy = source.DeepClone();
        if (copy.Tracks.Count > 0)
            Document.Tracks.Insert(sliderObjectTrackIndex >= 0 ? sliderObjectTrackIndex : Document.Tracks.Count, copy.Tracks[0]);
        if (copy.ImportedSliders.Count > 0)
            Document.ImportedSliders.Insert(sliderObjectImportIndex, copy.ImportedSliders[0]);
    }
}
