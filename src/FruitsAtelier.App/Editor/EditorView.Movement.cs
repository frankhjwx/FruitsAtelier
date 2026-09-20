using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
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

    private static uint MovementColour(CatchMovementMode mode) => mode switch
    {
        CatchMovementMode.Stand => 0xC0C0C0,
        CatchMovementMode.Walk => 0x63B99D,
        CatchMovementMode.Dash => 0xD6B365,
        _ => 0xCE7683
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

    private void DrawMovementConnections(ICanvas c)
    {
        if (!movementAnalysis) return;
        var objects = placementMovementObjects ?? conversion!.Objects;
        EnsureMovementStates(objects);
        double endTime = viewStart + plot.Height / pixelsPerMs;
        for (int i = FirstVisibleMovement(objects); i < movementIndices.Length; i++)
        {
            int departure = movementIndices[i - 1];
            var from = objects[departure];
            var to = objects[movementIndices[i]];
            if (from.TimeMs > endTime) break;
            if (movementStates[departure].Movement is not { } movement) continue;
            if (Document.BananaShowers.Any(shower => shower.TimeMs <= to.TimeMs && shower.EndTimeMs >= from.TimeMs)) continue;
            // Clip in map time so long connections keep their slope without oversized screen coordinates.
            double start = Math.Max(viewStart, from.TimeMs), end = Math.Min(endTime, to.TimeMs);
            double XAt(double time) => from.X + (to.X - from.X) * ((time - from.TimeMs) / (to.TimeMs - from.TimeMs));
            var a = Screen(new(start, XAt(start)));
            var b = Screen(new(end, XAt(end)));
            c.Line(a.X, a.Y, b.X, b.Y, MovementColour(movement.Mode), 4, .65f);
        }
    }

    private void DrawMovementOverlay(ICanvas c)
    {
        MovementOverlayBounds = null;
        MovementReadout = (null, null);
        IReadOnlyList<ConvertedCatchObject> objects = conversion!.Objects;
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
        if (first < 0) return;
        int last = selectedObject is not null ? first : Array.FindLastIndex(indices, i => objects[i].SourceId == source);
        MovementReadout = (first > 0 ? movementStates[indices[first - 1]].Movement : null,
            movementStates[indices[last]].Movement);

        float panelWidth = Math.Min(340, plot.Width - 12);
        if (panelWidth < 180 || plot.Height < 90) return;
        var r = new Rect(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, plot.X + 6, plot.Right - panelWidth - 6),
            plot.Bottom - 78, panelWidth, 70);
        MovementOverlayBounds = r;
        c.Fill(r, 0x171C24, 8, .39f);
        c.Stroke(r, 0x424D5C, 1, 8);
        c.Text(L.Get("movement.previous", Label(MovementReadout.Previous)), r.X + 12, r.Y + 8, 12, Foreground, (r.Width - 24) / 2);
        string next = L.Get("movement.next", Label(MovementReadout.Next));
        c.Text(next, r.Right - 12 - c.MeasureText(next, 12), r.Y + 8, 12, Muted, (r.Width - 24) / 2);
        float left = r.X + 12, length = r.Width - 24, y = r.Y + 33;
        if (MovementReadout.Previous is { } value)
        {
            // A centre-start Stand can remain possible even when prefix context marks a hyperdash.
            double dashLimit = Math.Max(value.StandLimit, value.DashLimit);
            double extent = Math.Max(1, dashLimit * 1.5);
            float stand = (float)(value.StandLimit / extent) * length;
            float walk = (float)(Math.Max(value.StandLimit, value.WalkLimit) / extent) * length;
            float dash = (float)(dashLimit / extent) * length;
            c.Fill(new(left, y, stand, 7), MovementColour(CatchMovementMode.Stand));
            c.Fill(new(left + stand, y, walk - stand, 7), MovementColour(CatchMovementMode.Walk));
            c.Fill(new(left + walk, y, dash - walk, 7), MovementColour(CatchMovementMode.Dash));
            c.Fill(new(left + dash, y, length - dash, 7), MovementColour(CatchMovementMode.HyperDash));
            float x = left + (float)Math.Clamp(value.Distance / extent, 0, 1) * length;
            c.Line(x - 4, y - 6, x, y - 2, Foreground, 2);
            c.Line(x + 4, y - 6, x, y - 2, Foreground, 2);
            c.Line(x, y - 1, x, y + 9, Foreground, 1.5f);
        }
        else c.Fill(new(left, y, length, 7), Grid, 3);

        DrawDistanceFields(c, r);

        static string Label(CatchMovementRange? range) => range is { } value
            ? L.Get(value.Mode switch { CatchMovementMode.Stand => "movement.stand", CatchMovementMode.Walk => "movement.walk", CatchMovementMode.Dash => "movement.dash", _ => "movement.hyperdash" })
            : "—";
    }
}
