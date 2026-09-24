using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ConvertedCatchObject? sliderObjectDragTarget;
    private double sliderObjectDragX;
    private MapDocument? sliderObjectDragSource, sliderObjectDragShape;
    private int sliderObjectTrackIndex, sliderObjectImportIndex;

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
        distanceObject = (target.SourceId, target.EventIndex);
        drag = DragKind.SliderObject;
        BeginPointerDrag(x, y);
    }

    private void MoveSliderObject(float x)
    {
        if (sliderObjectDragTarget is not { } target) return;
        if (Math.Abs(x - dragStartX) < .001 && Math.Abs(sliderObjectDragX - target.X) < .00001) return;
        double wantedX = Math.Clamp(SnapX(target.X + (x - dragStartX) / Playfield.Width * 512), 0, 512);
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
            if (sliderObjectDragShape is null)
            {
                RestoreSliderObjectSource(sliderObjectDragSource!);
                var track = Document.Tracks.FirstOrDefault(t => t.Id == target.SourceId)
                    ?? ConvertImportedSlider(target.SourceId, editorConversionCache).Track;
                sliderObjectDragShape = new MapDocument();
                sliderObjectDragShape.Tracks.Add(track);
            }
            // Every candidate starts from the same fitted shape; only this source needs a copy.
            RestoreSliderObjectSource(sliderObjectDragShape);
            if (target.Kind == CatchObjectKind.Fruit)
                DistanceSpacingEditing.ApplyX(Document, target, x, compensateTinyDroplets, editorConversionCache);
            else DistanceSpacingEditing.ApplyIsolatedX(Document, target, x, compensateTinyDroplets, editorConversionCache);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            RestoreSliderObjectSource(sliderObjectDragSource!);
            return false;
        }
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
