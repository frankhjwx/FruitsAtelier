using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private CatchConversionResult? objectSnapConversion;
    private ConvertedCatchObject[] objectSnapFruits = [];
    private ConvertedCatchObject? objectSnapTarget;
    internal (bool Enabled, int Earlier, int Later) ObjectSnapSettings =>
        (EditingPreferences.ObjectSnap, EditingPreferences.ObjectSnapEarlierBeats, EditingPreferences.ObjectSnapLaterBeats);
    internal MapPoint? ObjectSnapTargetPoint => objectSnapTarget is { } target ? new(target.TimeMs, target.X) : null;

    private bool TrySnapToObject(MapPoint point, double rawX, out MapPoint result)
    {
        result = point;
        objectSnapTarget = null;
        distanceOutside = false;
        if (!EditingPreferences.ObjectSnap || draftTrack != Guid.Empty) return false;
        EnsureConversion();
        if (!ReferenceEquals(objectSnapConversion, conversion))
        {
            objectSnapFruits = conversion!.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            objectSnapConversion = conversion;
        }
        var timing = SnapTiming();
        double start = timing.OffsetBeats(point.TimeMs, -EditingPreferences.ObjectSnapEarlierBeats);
        double end = timing.OffsetBeats(point.TimeMs, EditingPreferences.ObjectSnapLaterBeats);
        int low = 0, high = objectSnapFruits.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (objectSnapFruits[middle].TimeMs < start) low = middle + 1;
            else high = middle;
        }
        double nearest = 6, nearestTime = double.PositiveInfinity;
        for (int i = low; i < objectSnapFruits.Length && objectSnapFruits[i].TimeMs <= end; i++)
        {
            var candidate = objectSnapFruits[i];
            if (candidate.X is < 0 or > 512 || candidate.SourceId == draftTrack) continue;
            double distance = Math.Abs(candidate.X - rawX) * Playfield.Width / 512;
            double timeDistance = Math.Abs(candidate.TimeMs - point.TimeMs);
            if (distance < nearest || distance == nearest && distance < 6 && timeDistance < nearestTime)
            {
                nearest = distance; nearestTime = timeDistance; objectSnapTarget = candidate;
            }
        }
        if (objectSnapTarget is not { } target) return false;
        result = point with { X = target.X };
        return true;
    }

    private void DrawObjectSnapGuide(FruitsAtelier.App.Rendering.ICanvas c, MapPoint point)
    {
        if (draftTrack != Guid.Empty || objectSnapTarget is not { } target) return;
        var p = Screen(point);
        var reference = Screen(new(target.TimeMs, target.X));
        c.Line(p.X, Math.Clamp(reference.Y, plot.Y, plot.Bottom), p.X, p.Y, Accent, 1, .8f);
        if (reference.Y >= plot.Y && reference.Y <= plot.Bottom)
            c.Circle(reference.X, reference.Y, CatchSize.FruitRadius(Document.CircleSize) * CanvasObjectWidth / 512 + 3,
                Accent, filled: false, width: 2);
    }
}
