using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ConvertedCatchObject? dropletDragTarget;
    private double dropletDragX;

    private void BeginDropletDrag(ConvertedCatchObject target, float x, float y)
    {
        StatusMessage = L.Get("editor.status.dropletReady", Time(target.TimeMs));
        if (notesLocked) return;
        dropletDragTarget = target;
        dropletDragX = target.X;
        history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
        drag = DragKind.Droplet;
        BeginPointerDrag(x, y);
    }

    private void MoveDroplet(float x)
    {
        if (dropletDragTarget is not { } target) return;
        double wantedX = Math.Clamp(target.X + (x - dragStartX) / Playfield.Width * 512, 0, 512);
        if (Math.Abs(wantedX - dropletDragX) < .00001) return;
        history.Cancel();
        history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
        try
        {
            DistanceSpacingEditing.ApplyX(Document, target, wantedX, compensateTinyDroplets);
            dropletDragX = wantedX;
            StatusMessage = L.Get("editor.status.dropletPosition", Time(target.TimeMs), Number(wantedX));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            history.Cancel();
            history.Begin(L.Get("editor.command.changeField", L.Get("coordinate.x")));
            DistanceSpacingEditing.ApplyX(Document, target, dropletDragX, compensateTinyDroplets);
            StatusMessage = error.Message;
        }
        EnsureConversion();
        if (conversion!.Objects.FirstOrDefault(item => item.SourceId == target.SourceId
            && item.Kind == target.Kind && Math.Abs(item.TimeMs - target.TimeMs) < .001) is { } updated)
            distanceObject = (updated.SourceId, updated.EventIndex);
    }
}
