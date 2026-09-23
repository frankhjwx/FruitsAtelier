using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const float MovementPanelWidth = 300, MovementPanelHeight = 84, MovementPanelFontSize = 11;
    private IReadOnlyList<ConvertedCatchObject>? movementSource;
    private HyperDashState[] movementStates = [];
    private int[] movementIndices = [];
    private double movementCircleSize;
    public Rect? MovementOverlayBounds { get; private set; }
    public (CatchMovementRange? Previous, CatchMovementRange? Next) MovementReadout { get; private set; }

    private bool movementAnalysis;
    public bool MovementAnalysisEnabled => movementAnalysis;

    private void EnsureMovementStates(IReadOnlyList<ConvertedCatchObject> objects)
    {
        if (!ReferenceEquals(movementSource, objects) || movementCircleSize != Document.CircleSize)
        {
            movementSource = objects;
            movementCircleSize = Document.CircleSize;
            movementStates = HyperDashCalculator.Calculate(objects, movementCircleSize);
            movementIndices = Enumerable.Range(0, objects.Count)
                .Where(i => objects[i].Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet)
                .OrderBy(i => objects[i].TimeMs).ToArray();
        }
    }

    private uint MovementColour(CatchMovementMode mode) => mode switch
    {
        CatchMovementMode.Stand => LibrarySettings.StandIndicatorColour,
        CatchMovementMode.Walk => LibrarySettings.WalkIndicatorColour,
        CatchMovementMode.Dash => LibrarySettings.DashIndicatorColour,
        _ => LibrarySettings.HyperDashIndicatorColour
    };

    private int FirstVisibleMovement(IReadOnlyList<ConvertedCatchObject> objects)
    {
        int low = 1, high = movementIndices.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (objects[movementIndices[middle]].TimeMs < viewStart) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private bool KiaiOverlaps(double from, double to)
    {
        double? start = null;
        foreach (var transition in kiaiTransitions)
        {
            if (transition.TimeMs > to) break;
            if (transition.Active) start = transition.TimeMs;
            else if (start is double activeStart)
            {
                if (activeStart <= to && transition.TimeMs >= from) return true;
                start = null;
            }
        }
        return start is double openStart && openStart <= to;
    }

    private void DrawMovementConnections(ICanvas c)
    {
        if (!movementAnalysis) return;
        var objects = placementMovementObjects ?? playableObjects;
        EnsureMovementStates(objects);
        double endTime = viewStart + plot.Height / pixelsPerMs;
        for (int i = FirstVisibleMovement(objects); i < movementIndices.Length; i++)
        {
            int departure = movementIndices[i - 1];
            var from = objects[departure];
            var to = objects[movementIndices[i]];
            if (from.TimeMs > endTime) break;
            if (movementStates[departure].Movement is not { } movement) continue;
            if (Document.BananaShowers.Any(shower => shower.TimeMs <= to.TimeMs && shower.EndTimeMs >= from.TimeMs)
                || KiaiOverlaps(from.TimeMs, to.TimeMs)
                || breakPeriods.Any(period => period.StartMs <= to.TimeMs && period.EndMs >= from.TimeMs)) continue;
            // Clip in map time so long connections keep their slope without oversized screen coordinates.
            double start = Math.Max(viewStart, from.TimeMs), end = Math.Min(endTime, to.TimeMs);
            if (end <= start) continue;
            double XAt(double time) => from.X + (to.X - from.X) * ((time - from.TimeMs) / (to.TimeMs - from.TimeMs));
            Segment(start, end);

            void Segment(double first, double last)
            {
                if (last <= first) return;
                var a = Screen(new(first, XAt(first)));
                var b = Screen(new(last, XAt(last)));
                c.Line(a.X, a.Y, b.X, b.Y, MovementColour(movement.Mode), 4, .65f);
            }
        }
    }

    private void DrawMovementOverlay(ICanvas c)
    {
        MovementOverlayBounds = null;
        MovementReadout = (null, null);
        IReadOnlyList<ConvertedCatchObject> objects = playableObjects;
        Guid source;
        bool placement = PlacementGhostPoint() is not null;
        if (placement)
        {
            if (placementGhost is not { } ghost || placementMovementObjects is null) return;
            objects = placementMovementObjects;
            source = ghost.SourceId;
        }
        else
        {
            var ids = FlagTargets();
            if (ids.Length != 1 || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
            source = ids[0];
        }
        EnsureMovementStates(objects);
        var indices = movementIndices;
        var selectedObject = placement ? placementGhost : SelectedDistanceObject();
        int first = Array.FindIndex(indices, i => objects[i].SourceId == source
            && (selectedObject is null || objects[i].EventIndex == selectedObject.EventIndex));
        if (first < 0 && selectedObject?.Kind == CatchObjectKind.TinyDroplet)
            first = Array.FindIndex(indices, i => objects[i].SourceId == source);
        if (first < 0) return;
        int last = selectedObject is not null ? first : Array.FindLastIndex(indices, i => objects[i].SourceId == source);
        if (selectedObject?.Kind != CatchObjectKind.TinyDroplet)
            MovementReadout = (first > 0 ? movementStates[indices[first - 1]].Movement : null,
                movementStates[indices[last]].Movement);

        float panelWidth = Math.Min(MovementPanelWidth, plot.Width - 12);
        if (panelWidth < 180 || plot.Height < MovementPanelHeight + 20) return;
        var r = new Rect(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, plot.X + 6, plot.Right - panelWidth - 6),
            plot.Bottom - MovementPanelHeight - 8, panelWidth, MovementPanelHeight);
        MovementOverlayBounds = r;
        c.Fill(r, 0x171C24, 6, .82f);
        c.Stroke(r, 0x424D5C, 1, 6);
        c.Text(L.Get("movement.previous", Label(MovementReadout.Previous)), r.X + 10, r.Y + 7, MovementPanelFontSize, Foreground, r.Width / 2 - 14);
        string next = L.Get("movement.next", Label(MovementReadout.Next));
        float nextWidth = Math.Min(c.MeasureText(next, MovementPanelFontSize), r.Width / 2 - 14);
        c.Text(next, r.Right - 10 - nextWidth, r.Y + 7, MovementPanelFontSize, Foreground, nextWidth);
        float left = r.X + 10, length = r.Width - 20, y = r.Y + 27;
        if (MovementReadout.Previous is { } value)
        {
            // A centre-start Stand can remain possible even when prefix context marks a hyperdash.
            double dashLimit = Math.Max(value.StandLimit, value.DashLimit);
            double extent = Math.Max(1, dashLimit * 1.5);
            float stand = (float)(value.StandLimit / extent) * length;
            float walk = (float)(Math.Max(value.StandLimit, value.WalkLimit) / extent) * length;
            float dash = (float)(dashLimit / extent) * length;
            c.Fill(new(left, y, stand, 5), MovementColour(CatchMovementMode.Stand));
            c.Fill(new(left + stand, y, walk - stand, 5), MovementColour(CatchMovementMode.Walk));
            c.Fill(new(left + walk, y, dash - walk, 5), MovementColour(CatchMovementMode.Dash));
            c.Fill(new(left + dash, y, length - dash, 5), MovementColour(CatchMovementMode.HyperDash));
            float x = left + (float)Math.Clamp(value.Distance / extent, 0, 1) * length;
            c.Line(x - 4, y - 6, x, y - 2, Foreground, 2);
            c.Line(x + 4, y - 6, x, y - 2, Foreground, 2);
            c.Line(x, y - 1, x, y + 7, Foreground, 1.5f);
        }
        else c.Fill(new(left, y, length, 5), Grid, 2);

        DrawDistanceFields(c, r);

        static string Label(CatchMovementRange? range) => range is { } value
            ? L.Get(value.Mode switch { CatchMovementMode.Stand => "movement.stand", CatchMovementMode.Walk => "movement.walk", CatchMovementMode.Dash => "movement.dash", _ => "movement.hyperdash" })
            : "—";
    }
}
