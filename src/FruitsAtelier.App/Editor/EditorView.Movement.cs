using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const float MovementPanelWidth = 300, MovementPanelHeight = 84, MovementPanelFontSize = 11;
    private sealed record MovementSnapshot(IReadOnlyList<ConvertedCatchObject> Source, double CircleSize,
        bool IncludeTiny, HyperDashState[] States, int[] Indices);
    private readonly List<MovementSnapshot> movementSnapshots = [];
    private HyperDashState[] movementStates = [];
    private int[] movementIndices = [];
    public Rect? MovementOverlayBounds { get; private set; }
    public (CatchMovementRange? Previous, CatchMovementRange? Next) MovementReadout { get; private set; }

    private bool movementIncludeTinyDroplets;
    private bool movementAnalysis { get => EditingPreferences.MovementAnalysis; set { EditingPreferences.MovementAnalysis = value; SaveMapEditingPreferences(); } }
    public bool MovementIncludesTinyDroplets => movementIncludeTinyDroplets;
    public bool MovementAnalysisEnabled => movementAnalysis;
    private (float X, float Y)? movementPanelPosition
    { get => DockWorkspaceState.MovementPosition; set => DockWorkspaceState.MovementPosition = value; }
    private bool movementPanelPointer, movementPanelMoved;
    private float movementPanelPointerX, movementPanelPointerY, movementPanelStartX, movementPanelStartY;
    private int movementPanelCorner;
    private float movementPanelStartWidth;
    private float MovementPanelScale => MovementOverlayBounds is { } panel ? panel.Width / MovementPanelWidth : 1;
    public int MovementPanelResizeCursor => movementPanelPointer ? movementPanelCorner
        : DockContentInputAvailable && !DistanceEditing && menu < 0 && contextItems.Count == 0
            ? MovementCorner(mouseX, mouseY) : 0;

    private int MovementCorner(float x, float y)
    {
        if (MovementOverlayBounds is not { } panel || !panel.Contains(x, y)) return 0;
        bool left = x < panel.X + 7, right = x >= panel.Right - 7;
        bool top = y < panel.Y + 7, bottom = y >= panel.Bottom - 7;
        return top ? left ? 1 : right ? 2 : 0 : bottom ? left ? 3 : right ? 4 : 0 : 0;
    }

    private bool MovementPanelPointerDown(float x, float y, int button)
    {
        if (MovementOverlayBounds is not { } panel || !panel.Contains(x, y)) return false;
        if (button == 2)
        {
            contextItems.Clear(); menu = -1;
            contextItems.Add(new(L.Get("movement.resetPosition"), () => movementPanelPosition = null));
            contextItems.Add(new(L.Get("movement.resetScale"), () => DockWorkspaceState.MovementScale = 1));
            contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 240)),
                Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 240, ContextMenuHeight);
            return true;
        }
        if (button != 0 || DistanceEditing) return false;
        movementPanelPointer = true; movementPanelMoved = false;
        movementPanelPointerX = x; movementPanelPointerY = y;
        movementPanelStartX = panel.X; movementPanelStartY = panel.Y;
        movementPanelCorner = MovementCorner(x, y);
        movementPanelStartWidth = panel.Width;
        return true;
    }

    private void MoveMovementPanel(float x, float y)
    {
        if (!movementPanelMoved && Math.Abs(x - movementPanelPointerX) < 3 && Math.Abs(y - movementPanelPointerY) < 3) return;
        movementPanelMoved = true;
        if (MovementOverlayBounds is not { } panel) return;
        if (movementPanelCorner != 0)
        {
            bool left = movementPanelCorner is 1 or 3, top = movementPanelCorner is 1 or 2;
            float ratio = MovementPanelHeight / MovementPanelWidth;
            float anchorX = movementPanelStartX + (left ? movementPanelStartWidth : 0);
            float anchorY = movementPanelStartY + (top ? movementPanelStartWidth * ratio : 0);
            float dx = (x - movementPanelPointerX) * (left ? -1 : 1);
            float dy = (y - movementPanelPointerY) * (top ? -1 : 1);
            float maximum = Math.Min(left ? anchorX - plot.X : plot.Right - anchorX,
                (top ? anchorY - plot.Y : plot.Bottom - anchorY) / ratio);
            maximum = Math.Min(maximum, Math.Min(plot.Width - 12, (plot.Height - 20) / ratio));
            float size = Math.Clamp(movementPanelStartWidth + (dx + dy * ratio) / (1 + ratio * ratio),
                Math.Min(180, maximum), Math.Min(900, maximum));
            DockWorkspaceState.MovementScale = size / MovementPanelWidth;
            movementPanelPosition = (left ? anchorX - size : anchorX, top ? anchorY - size * ratio : anchorY);
            return;
        }
        movementPanelPosition = (Math.Clamp(movementPanelStartX + x - movementPanelPointerX, plot.X, plot.Right - panel.Width),
            Math.Clamp(movementPanelStartY + y - movementPanelPointerY, plot.Y, plot.Bottom - panel.Height));
    }

    private void EnsureMovementStates(IReadOnlyList<ConvertedCatchObject> objects, bool includeTiny = false)
    {
        var snapshot = movementSnapshots.FirstOrDefault(s => ReferenceEquals(s.Source, objects)
            && s.CircleSize == Document.CircleSize && s.IncludeTiny == includeTiny);
        if (snapshot is null)
        {
            // Keep both display policies for the current authoring, playable and placement snapshots.
            movementSnapshots.RemoveAll(s => s.CircleSize != Document.CircleSize
                || !ReferenceEquals(s.Source, playableObjects) && !ReferenceEquals(s.Source, conversion?.Objects)
                    && !ReferenceEquals(s.Source, placementMovementObjects));
            snapshot = new(objects, Document.CircleSize, includeTiny,
                HyperDashCalculator.Calculate(objects, Document.CircleSize, includeTiny),
                Enumerable.Range(0, objects.Count).Where(i => objects[i].Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet
                    || includeTiny && objects[i].Kind == CatchObjectKind.TinyDroplet)
                    .OrderBy(i => objects[i].TimeMs).ToArray());
            movementSnapshots.Add(snapshot);
        }
        movementStates = snapshot.States;
        movementIndices = snapshot.Indices;
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
            if (objects[movementIndices[middle]].TimeMs < PresentationViewStart) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool WithinOsuMovementRange(double from, double to, TimingMap.Lookup timing) => to <= timing.OffsetBeats(from, 2) + 1e-7;

    private void DrawMovementConnections(ICanvas c)
    {
        if (!movementAnalysis) return;
        var objects = placementMovementObjects ?? playableObjects;
        var movementBreaks = MovementBreakPeriods();
        EnsureMovementStates(objects, movementIncludeTinyDroplets);
        double endTime = PresentationViewStart + plot.Height / pixelsPerMs;
        for (int i = FirstVisibleMovement(objects); i < movementIndices.Length; i++)
        {
            int departure = movementIndices[i - 1];
            var from = objects[departure];
            var to = objects[movementIndices[i]];
            if (from.TimeMs > endTime) break;
            if (movementStates[departure].Movement is not { } movement) continue;
            if (Document.BananaShowers.Any(shower => shower.TimeMs <= to.TimeMs && shower.EndTimeMs >= from.TimeMs)
                || movementBreaks.Any(period => period.StartMs <= to.TimeMs && period.EndMs >= from.TimeMs)) continue;
            // Clip in map time so long connections keep their slope without oversized screen coordinates.
            double start = Math.Max(PresentationViewStart, from.TimeMs), end = Math.Min(endTime, to.TimeMs);
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

    private Rect? MovementPanelBounds()
    {
        float panelWidth = Math.Min(MovementPanelWidth * DockWorkspaceState.MovementScale,
            Math.Min(plot.Width - 12, (plot.Height - 20) * MovementPanelWidth / MovementPanelHeight));
        if (panelWidth < 180 || plot.Height < MovementPanelHeight + 20) return null;
        float scale = panelWidth / MovementPanelWidth, panelHeight = MovementPanelHeight * scale;
        float minX = plot.X + 6;
        // A panel filling the available width can reverse these bounds by one float ULP.
        float maxX = Math.Max(minX, plot.Right - panelWidth - 6);
        var r = new Rect(Math.Clamp(Playfield.X + Playfield.Width / 2 - panelWidth / 2, minX, maxX),
            plot.Bottom - panelHeight - 8, panelWidth, panelHeight);
        if (movementPanelPosition is { } position)
            r = r with { X = Math.Clamp(position.X, plot.X, plot.Right - panelWidth),
                Y = Math.Clamp(position.Y, plot.Y, Math.Max(plot.Y, plot.Bottom - panelHeight)) };
        return r;
    }

    private void DrawMovementOverlay(ICanvas c)
    {
        MovementOverlayBounds = null;
        MovementReadout = (null, null);
        IReadOnlyList<ConvertedCatchObject> objects = playableObjects;
        HashSet<Guid> sources;
        bool placement = PlacementGhostPoint() is not null;
        if (placement)
        {
            if (placementGhost is not { } ghost || placementMovementObjects is null) return;
            objects = placementMovementObjects;
            sources = [ghost.SourceId];
        }
        else
        {
            var ids = FlagTargets();
            if (ids.Length == 0 || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
            if (tool is Tool.Fruit or Tool.Slider && !plot.Contains(mouseX, mouseY) && !DistanceEditing) return;
            sources = ids.ToHashSet();
        }
        var selectedObject = placement ? placementGhost : DistanceReadoutObject();
        EnsureMovementStates(objects, selectedObject?.Kind == CatchObjectKind.TinyDroplet);
        var indices = movementIndices;
        int first = Array.FindIndex(indices, i => sources.Contains(objects[i].SourceId)
            && (selectedObject is null || objects[i].EventIndex == selectedObject.EventIndex));
        if (first < 0) return;
        int last = selectedObject is not null ? first : Array.FindLastIndex(indices, i => sources.Contains(objects[i].SourceId));
        MovementReadout = (first > 0 ? movementStates[indices[first - 1]].Movement : null,
                movementStates[indices[last]].Movement);
        if (IsOsuCanvas)
        {
            var timing = SnapTiming();
            bool previousVisible = first > 0 && WithinOsuMovementRange(objects[indices[first - 1]].TimeMs, objects[indices[first]].TimeMs, timing);
            bool nextVisible = last + 1 < indices.Length && WithinOsuMovementRange(objects[indices[last]].TimeMs, objects[indices[last + 1]].TimeMs, timing);
            MovementReadout = (previousVisible ? MovementReadout.Previous : null, nextVisible ? MovementReadout.Next : null);
            DistanceReadout = (previousVisible ? DistanceReadout.Previous : null, nextVisible ? DistanceReadout.Next : null);
        }

        if (MovementPanelBounds() is not { } r) return;
        float scale = r.Width / MovementPanelWidth;
        MovementOverlayBounds = r;
        c.Fill(r, 0x171C24, 6 * scale, .82f);
        c.Stroke(r, 0x424D5C, scale, 6 * scale);
        c.Text(L.Get("movement.previous", Label(MovementReadout.Previous)), r.X + 10 * scale, r.Y + 7 * scale, MovementPanelFontSize * scale, Foreground, r.Width / 2 - 14 * scale);
        string next = L.Get("movement.next", Label(MovementReadout.Next));
        float nextWidth = Math.Min(c.MeasureText(next, MovementPanelFontSize * scale), r.Width / 2 - 14 * scale);
        c.Text(next, r.Right - 10 * scale - nextWidth, r.Y + 7 * scale, MovementPanelFontSize * scale, Foreground, nextWidth);
        float left = r.X + 10 * scale, length = r.Width - 20 * scale, y = r.Y + 27 * scale;
        if (MovementReadout.Previous is { } value)
        {
            // A centre-start Stand can remain possible even when prefix context marks a hyperdash.
            double dashLimit = Math.Max(value.StandLimit, value.DashLimit);
            double extent = Math.Max(1, dashLimit * 1.5);
            float stand = (float)(value.StandLimit / extent) * length;
            float walk = (float)(Math.Max(value.StandLimit, value.WalkLimit) / extent) * length;
            float dash = (float)(dashLimit / extent) * length;
            c.Fill(new(left, y, stand, 5 * scale), MovementColour(CatchMovementMode.Stand));
            c.Fill(new(left + stand, y, walk - stand, 5 * scale), MovementColour(CatchMovementMode.Walk));
            c.Fill(new(left + walk, y, dash - walk, 5 * scale), MovementColour(CatchMovementMode.Dash));
            c.Fill(new(left + dash, y, length - dash, 5 * scale), MovementColour(CatchMovementMode.HyperDash));
            float x = left + (float)Math.Clamp(value.Distance / extent, 0, 1) * length;
            c.Line(x - 4 * scale, y - 6 * scale, x, y - 2 * scale, Foreground, 2 * scale);
            c.Line(x + 4 * scale, y - 6 * scale, x, y - 2 * scale, Foreground, 2 * scale);
            c.Line(x, y - scale, x, y + 7 * scale, Foreground, 1.5f * scale);
        }
        else c.Fill(new(left, y, length, 5 * scale), Grid, 2 * scale);

        DrawDistanceFields(c, r);

        static string Label(CatchMovementRange? range) => range is { } value
            ? L.Get(value.Mode switch { CatchMovementMode.Stand => "movement.stand", CatchMovementMode.Walk => "movement.walk", CatchMovementMode.Dash => "movement.dash", _ => "movement.hyperdash" })
            : "—";
    }
}
