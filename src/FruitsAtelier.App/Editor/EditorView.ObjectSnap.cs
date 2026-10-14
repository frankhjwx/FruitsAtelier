using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private CatchConversionResult? objectSnapConversion;
    private ConvertedCatchObject[] objectSnapFruits = [];
    private ConvertedCatchObject? objectSnapTarget;
    private ConvertedCatchObject[] objectDragSnapFruits = [];
    private DistanceSnap.Reference[] objectDragDistanceReferences = [];
    private MapPoint? objectDragSnapFirst, objectDragSnapLast, objectDragSnapPoint;
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
        RefreshObjectSnapFruits();
        double nearest = 6, nearestTime = double.PositiveInfinity;
        objectSnapTarget = FindObjectSnapTarget(point, rawX, objectSnapFruits, null, 0, 512, ref nearest, ref nearestTime);
        if (objectSnapTarget is not { } target) return false;
        result = point with { X = target.X };
        return true;
    }

    private void RefreshObjectSnapFruits()
    {
        if (!ReferenceEquals(objectSnapConversion, conversion))
        {
            objectSnapFruits = conversion!.Objects.Where(o => o.Kind == CatchObjectKind.Fruit).ToArray();
            objectSnapConversion = conversion;
        }
    }

    private ConvertedCatchObject? FindObjectSnapTarget(MapPoint point, double rawX, ConvertedCatchObject[] fruits,
        ISet<Guid>? excluded, double minX, double maxX, ref double nearest, ref double nearestTime)
    {
        var timing = SnapTiming();
        double start = timing.OffsetBeats(point.TimeMs, -EditingPreferences.ObjectSnapEarlierBeats);
        double end = timing.OffsetBeats(point.TimeMs, EditingPreferences.ObjectSnapLaterBeats);
        int low = 0, high = fruits.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (fruits[middle].TimeMs < start) low = middle + 1;
            else high = middle;
        }
        ConvertedCatchObject? found = null;
        for (int i = low; i < fruits.Length && fruits[i].TimeMs <= end; i++)
        {
            var candidate = fruits[i];
            if (candidate.X < Math.Max(0, minX) || candidate.X > Math.Min(512, maxX)
                || candidate.SourceId == draftTrack || excluded?.Contains(candidate.SourceId) == true) continue;
            double distance = Math.Abs(candidate.X - rawX) * Playfield.Width / 512;
            double timeDistance = Math.Abs(candidate.TimeMs - point.TimeMs);
            if (distance < nearest || distance == nearest && double.IsFinite(nearestTime) && timeDistance < nearestTime)
            {
                nearest = distance; nearestTime = timeDistance; found = candidate;
            }
        }
        return found;
    }

    private void BeginObjectDragSnap()
    {
        RefreshObjectSnapFruits();
        EnsureDistanceReferences();
        objectDragDistanceReferences = distanceReferences;
        objectDragSnapFruits = objectSnapFruits;
        objectDragSnapFirst = objectDragSnapLast = objectDragSnapPoint = null;
        objectSnapTarget = null;
        var authoredFruits = Document.Fruits.Where(f => objectSelection.Contains(f.Id)).ToDictionary(f => f.Id);
        foreach (var fruit in objectDragSnapFruits)
        {
            if (!objectSelection.Contains(fruit.SourceId)) continue;
            // Standalone drag offsets are applied to authoring coordinates, before float conversion.
            var point = authoredFruits.TryGetValue(fruit.SourceId, out var authored)
                ? new MapPoint(authored.TimeMs, authored.X) : new MapPoint(fruit.TimeMs, fruit.X);
            objectDragSnapFirst ??= point;
            objectDragSnapLast = point;
        }
    }

    private bool TrySnapDraggedObjects(double deltaTime, double rawDeltaX, double minDeltaX, double maxDeltaX, out double deltaX)
    {
        deltaX = rawDeltaX;
        objectDragSnapPoint = null; objectSnapTarget = null;
        if (objectDragSnapFirst is not { } first) return false;
        var rawPoint = new MapPoint(first.TimeMs + deltaTime, first.X + rawDeltaX);
        bool distanceSnapped = TryDistanceSnap(rawPoint, objectDragDistanceReferences, objectSelection,
            first.X + minDeltaX, first.X + maxDeltaX, out var distancePoint);
        if (distanceSnapped) deltaX = distancePoint.X - first.X;
        if (!EditingPreferences.ObjectSnap) return distanceSnapped;
        // All active snap positions compete against the raw pointer, so crossing an
        // object alignment cannot switch back to a more distant DS position.
        double nearest = distanceSnapped ? Math.Abs(deltaX - rawDeltaX) * Playfield.Width / 512
            : DistanceSnapEnabled ? double.PositiveInfinity : 6;
        double nearestTime = distanceSnapped ? double.MaxValue : double.PositiveInfinity;
        MapPoint? snappedOrigin = null;
        Consider(objectDragSnapFirst.Value);
        if (objectDragSnapLast is { } last && last != objectDragSnapFirst) Consider(last);
        if (objectSnapTarget is not { } target || snappedOrigin is not { } origin) return distanceSnapped;
        deltaX = target.X - origin.X;
        objectDragSnapPoint = new(origin.TimeMs + deltaTime, target.X);
        distanceOutside = false;
        return true;

        void Consider(MapPoint source)
        {
            var point = new MapPoint(source.TimeMs + deltaTime, source.X + rawDeltaX);
            var target = FindObjectSnapTarget(point, point.X, objectDragSnapFruits, objectSelection,
                source.X + minDeltaX, source.X + maxDeltaX, ref nearest, ref nearestTime);
            if (target is null) return;
            objectSnapTarget = target; snappedOrigin = source;
        }
    }

    private void EndObjectDragSnap()
    {
        objectDragSnapFruits = [];
        objectDragDistanceReferences = [];
        objectDragSnapFirst = objectDragSnapLast = objectDragSnapPoint = null;
        objectSnapTarget = null;
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
