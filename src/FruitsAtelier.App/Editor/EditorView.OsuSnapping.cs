using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly record struct OsuSnapTarget(Guid Id, double X, double Y);
    private OsuCanvasObject[]? osuObjectSnapSource;
    private double osuObjectSnapTime = double.NaN;
    private Rect osuObjectSnapField, osuObjectSnapPlot;
    private OsuSnapTarget[] osuObjectSnapTargets = [], osuDragObjectTargets = [];
    private (double X, double Y)? osuDragSnapHead, osuDragSnapTail;
    private (double X, double Y, double Distance)? osuDragDistanceTarget;

    private OsuSnapTarget[] OsuObjectSnapTargets()
    {
        RefreshOsuCanvasVisible();
        var field = OsuCanvasBounds;
        double now = LazerPresentationTime;
        if (ReferenceEquals(osuObjectSnapSource, osuCanvasObjects) && osuObjectSnapTime == now
            && osuObjectSnapField == field && osuObjectSnapPlot == plot) return osuObjectSnapTargets;
        var targets = new List<OsuSnapTarget>();
        foreach (int index in osuCanvasVisible)
        {
            var o = osuCanvasObjects[index];
            if (o.Spinner || now <= o.Start - OsuCanvasPreempt) continue;
            double shift = -o.Stack * OsuCanvasRadius / 10;
            if (now < o.Start + 700 || o.Path.Count > 0 && now < o.End + 240) Add(o.X, o.Y);
            if (o.Path.Count > 0 && now < o.End + 240) Add(o.Path[^1].X, o.Path[^1].GeometryY);
            void Add(double x, double y)
            {
                x += shift; y += shift;
                var screen = OsuCanvasScreen(x, y);
                if (plot.Contains(screen.X, screen.Y)) targets.Add(new(o.Id, x, y));
            }
        }
        osuObjectSnapSource = osuCanvasObjects; osuObjectSnapTime = now;
        osuObjectSnapField = field; osuObjectSnapPlot = plot;
        return osuObjectSnapTargets = targets.ToArray();
    }

    private (double X, double Y) SnapOsuObjectPoint((double X, double Y) raw, (double X, double Y) candidate,
        OsuSnapTarget[] targets, ISet<Guid>? excluded, bool distanceSnapped)
    {
        if (!EditingPreferences.ObjectSnap) return candidate;
        double scale = OsuCanvasBounds.Width / 512;
        double nearest = distanceSnapped ? Distance(raw.X, raw.Y, candidate.X, candidate.Y) * scale : double.PositiveInfinity;
        foreach (var target in targets)
        {
            if (excluded?.Contains(target.Id) == true) continue;
            double distance = Distance(raw.X, raw.Y, target.X, target.Y) * scale;
            if (distance >= 6 || distance >= nearest) continue;
            nearest = distance; candidate = (target.X, target.Y);
        }
        return candidate;
    }

    private void BeginOsuDragSnap()
    {
        EnsureOsuDistanceReferences();
        osuDragObjectTargets = OsuObjectSnapTargets();
        osuDragSnapHead = osuDragSnapTail = null; osuDragDistanceTarget = null;
        var first = osuCanvasObjects.FirstOrDefault(o => objectSelection.Contains(o.Id) && !o.Spinner);
        if (first is null) return;
        double x = first.X, y = first.Y;
        var authored = Document.ImportedSliders.FirstOrDefault(s => s.Id == first.Id)?.RandomizationSource;
        if (authored is not null) { x = authored.X; y = authored.Y; }
        var fruit = Document.Fruits.FirstOrDefault(f => f.Id == first.Id);
        if (fruit is not null) { x = fruit.X; y = fruit.Y; }
        osuDragSnapHead = (x, y);
        var last = osuCanvasObjects.Last(o => objectSelection.Contains(o.Id) && !o.Spinner);
        var endpoint = last.Path.Count > 0 ? last.Path[^1] : new FruitsAtelier.Core.SliderPathPoint(last.X, last.Y);
        osuDragSnapTail = (endpoint.X, endpoint.GeometryY);
        var previous = osuCanvasObjects.Where(o => !objectSelection.Contains(o.Id) && !o.Spinner && o.End < first.Start)
            .MaxBy(o => o.End);
        if (previous is null) return;
        var tail = OsuPathPosition(previous, previous.Spans % 2 == 1 ? 1 : 0);
        osuDragDistanceTarget = (tail.X, tail.Y, (first.Start - previous.End) * previous.DistanceVelocity * Document.OsuDistanceSpacing);
    }

    private (double X, double Y) SnapOsuDragPoint((double X, double Y) raw, bool useDistance)
    {
        var candidate = raw;
        bool snapped = false;
        if (useDistance && distanceSnap != altHeld && osuDragDistanceTarget is { } previous)
        {
            double length = Distance(raw.X, raw.Y, previous.X, previous.Y);
            if (length > .001)
            {
                candidate = (previous.X + (raw.X - previous.X) / length * previous.Distance,
                    previous.Y + (raw.Y - previous.Y) / length * previous.Distance);
                snapped = true;
            }
        }
        return SnapOsuObjectPoint(raw, candidate, osuDragObjectTargets, objectSelection, snapped);
    }

    private (double X, double Y) SnapOsuDragOffset(double dx, double dy)
    {
        if (osuDragSnapHead is not { } head) return (dx, dy);
        var raw = (X: head.X + dx, Y: head.Y + dy);
        var snapped = SnapOsuDragPoint(raw, useDistance: true);
        var result = (X: snapped.X - head.X, Y: snapped.Y - head.Y);
        if (osuDragSnapTail is not { } tail || tail == head) return result;
        bool ds = distanceSnap != altHeld && osuDragDistanceTarget is not null;
        var rawTail = (X: tail.X + dx, Y: tail.Y + dy);
        var candidate = SnapOsuObjectPoint(rawTail, (tail.X + result.X, tail.Y + result.Y),
            osuDragObjectTargets, objectSelection, ds || result != (dx, dy));
        var offset = (X: candidate.X - tail.X, Y: candidate.Y - tail.Y);
        if (offset == result) return result;
        double nearest = ds || result != (dx, dy) ? Distance(dx, dy, result.X, result.Y) : double.PositiveInfinity;
        return Distance(dx, dy, offset.X, offset.Y) < nearest ? offset : result;
    }

    private void EndOsuDragSnap()
    {
        osuDragObjectTargets = []; osuDragSnapHead = osuDragSnapTail = null; osuDragDistanceTarget = null;
    }
}
