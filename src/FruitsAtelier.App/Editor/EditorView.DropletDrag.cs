using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ConvertedCatchObject? dropletDragTarget;
    private double dropletDragX;

    private bool TryBeginSelectedDropletDrag(float x, float y)
    {
        if (tool != Tool.Select || objectSelection.Count != 1 || SelectedTrack is not { } track
            || HitCatchObject(x, y) is not { Kind: CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet } target
            || target.SourceId != track.Id) return false;
        if (showTargets && distanceObject != (target.SourceId, target.EventIndex))
        {
            double distance = PointerDistance(new(target.TimeMs, target.X), x, y);
            var controls = LegacyMode ? SliderControlEditing.Vertices(track).Select(v => v.Point) : track.Nodes.Select(Point);
            // Nearby fitted controls must not steal a click closer to the visible droplet.
            if (controls.Any(p => Near(p, x, y, 9) && PointerDistance(p, x, y) <= distance + .01)) return false;
        }
        PickSoundEdge(target);
        BeginDropletDrag(target, x, y);
        return true;
    }

    private void BeginDropletDrag(ConvertedCatchObject target, float x, float y)
    {
        StatusMessage = L.Get("editor.status.dropletReady", Time(target.TimeMs));
        if (notesLocked) return;
        history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
        dropletDragTarget = target;
        dropletDragX = target.X;
        distanceObject = (target.SourceId, target.EventIndex);
        drag = DragKind.Droplet;
        BeginPointerDrag(x, y);
    }

    private void MoveDroplet(float x)
    {
        if (dropletDragTarget is not { } target) return;
        double wantedX = Math.Clamp(target.X + (x - dragStartX) / Playfield.Width * 512, 0, 512);
        if (Math.Abs(wantedX - dropletDragX) < .00001) return;
        double acceptedX = dropletDragX;
        if (!TryDropletPosition(target, wantedX))
        {
            double blockedX = wantedX;
            for (int i = 0; i < 12 && Math.Abs(blockedX - acceptedX) > .001; i++)
            {
                double middle = (acceptedX + blockedX) / 2;
                if (TryDropletPosition(target, middle)) acceptedX = middle;
                else blockedX = middle;
            }
            TryDropletPosition(target, acceptedX);
        }
        else acceptedX = wantedX;
        dropletDragX = acceptedX;
        StatusMessage = L.Get("editor.status.dropletPosition", Time(target.TimeMs), Number(acceptedX));
        EnsureConversion();
        if (conversion!.Objects.FirstOrDefault(item => item.SourceId == target.SourceId
            && item.EventIndex == target.EventIndex) is { } updated)
            distanceObject = (updated.SourceId, updated.EventIndex);
    }

    private bool TryDropletPosition(ConvertedCatchObject target, double x)
    {
        history.Cancel();
        history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
        try
        {
            DistanceSpacingEditing.ApplyIsolatedX(Document, target, x, compensateTinyDroplets);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            history.Cancel();
            history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
            return false;
        }
    }
}
