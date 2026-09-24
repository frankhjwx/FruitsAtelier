using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ConvertedCatchObject? sliderObjectDragTarget;
    private double sliderObjectDragX;
    private MapDocument? sliderObjectDragSource, sliderObjectDragShape;
    private int sliderObjectTrackIndex, sliderObjectImportIndex;
    private IReadOnlyDictionary<(int From, int To), double>? sliderObjectDragBaseline;
    private IReadOnlyDictionary<(int From, int To), double>? sliderObjectDragStrictBaseline;
    private ConvertedCatchObject? sliderObjectDragPrevious, sliderObjectDragNext;

    private bool TryBeginSelectedSliderObjectDrag(float x, float y)
    {
        if (tool != Tool.Select || objectSelection.Count != 1
            || HitCatchObject(x, y) is not { IsStandalone: false, Kind: CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet } target
            || !objectSelection.Contains(target.SourceId)) return false;
        var track = SelectedTrack;
        if (target.Kind != CatchObjectKind.Fruit && track is null) return false;
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
        EnsureConversion();
        sliderObjectDragBaseline = SliderDistanceSnap.Excesses(Document, conversion!.Objects, target.SourceId);
        sliderObjectDragStrictBaseline = sliderObjectTrackIndex < 0 ? null
            : SliderDistanceSnap.StrictErrors(Document, Document.Tracks[sliderObjectTrackIndex], conversion.Objects);
        var large = conversion.Objects.Where(item => item.SourceId == target.SourceId
            && (item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet
                || target.Kind == CatchObjectKind.TinyDroplet && item.Kind == CatchObjectKind.TinyDroplet))
            .OrderBy(item => item.TimeMs).ThenBy(item => item.EventIndex).ToArray();
        int eventPosition = Array.FindIndex(large, item => item.EventIndex == target.EventIndex);
        sliderObjectDragPrevious = eventPosition > 0 ? large[eventPosition - 1] : null;
        sliderObjectDragNext = eventPosition >= 0 && eventPosition + 1 < large.Length ? large[eventPosition + 1] : null;
        distanceObject = (target.SourceId, target.EventIndex);
        drag = DragKind.SliderObject;
        BeginPointerDrag(x, y);
    }

    private void MoveSliderObject(float x)
    {
        if (sliderObjectDragTarget is not { } target) return;
        if (Math.Abs(x - dragStartX) < .001 && Math.Abs(sliderObjectDragX - target.X) < .00001) return;
        double rawX = Math.Clamp(target.X + (x - dragStartX) / Playfield.Width * 512, 0, 512);
        bool strict = DistanceSnapEnabled;
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
            if (target.Kind == CatchObjectKind.Fruit)
                DistanceSpacingEditing.ApplyX(Document, target, x, compensateTinyDroplets, editorConversionCache);
            else DistanceSpacingEditing.ApplyIsolatedX(Document, target, x, compensateTinyDroplets, editorConversionCache);
            if (DistanceSnapEnabled)
            {
                var result = CatchStreamConverter.Convert(Document, compensateTinyDroplets, editorConversionCache);
                if (!StrictSliderObjectResult(target, result)
                    || !SliderDistanceSnap.Allows(sliderObjectDragBaseline!,
                        SliderDistanceSnap.Excesses(Document, result.Objects, target.SourceId))
                    || sliderObjectDragStrictBaseline is not null
                        && Document.Tracks.FirstOrDefault(track => track.Id == target.SourceId) is { } currentTrack
                        && !SliderDistanceSnap.Allows(sliderObjectDragStrictBaseline,
                            SliderDistanceSnap.StrictErrors(Document, currentTrack, result.Objects)))
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

    private IEnumerable<double> SliderObjectDistanceCandidates(ConvertedCatchObject target, double wantedX)
    {
        var reference = sliderObjectDragPrevious ?? sliderObjectDragNext;
        if (reference is null) return [];
        if (target.Kind is CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet)
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
        return target.Kind == CatchObjectKind.TinyDroplet
            ? Pair(sliderObjectDragPrevious ?? sliderObjectDragNext)
            : Pair(sliderObjectDragPrevious) && Pair(sliderObjectDragNext);
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
