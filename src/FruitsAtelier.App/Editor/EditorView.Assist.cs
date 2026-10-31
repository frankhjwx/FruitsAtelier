using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool altHeld, shiftHeld, notesLocked, distanceOutside;
    private bool distanceSnap { get => EditingPreferences.DistanceSnap; set { EditingPreferences.DistanceSnap = value; SaveMapEditingPreferences(); } }
    private int nextSounds;
    private (Guid Id, int Edge)? soundEdge;
    private CatchConversionResult? distanceConversion;
    private DistanceSnap.Reference[] distanceReferences = [];
    private readonly List<Rect> assistButtons = [];
    private Rect assistPalette;
    private float assistScroll, assistScrollLimit;
    public IReadOnlyList<Rect> AssistButtonBounds => assistButtons;
    public bool DistanceSnapEnabled => distanceSnap ^ altHeld;
    public bool NotesLocked => notesLocked;
    private bool dropletSelectionLocked { get => LibrarySettings.LockDropletSelection; set => LibrarySettings.LockDropletSelection = value; }
    private bool notesLockFlyout;
    private Rect notesLockFlyoutBounds;
    public bool DropletSelectionLocked => dropletSelectionLocked;
    private bool CanSelectSlider(Guid id) => CanSelectCollaborationObject(id)
        && (!LibrarySettings.LockSlidersToCanvas || !IsReadOnlySlider(id));
    private bool EffectiveGridSnap => !UsesSpeedDistanceSnap && (gridSnap ^ (shiftHeld && !altHeld));
    public (double? Previous, double? Next) DistanceReadout { get; private set; }

    public void SetModifiers(bool alt, bool shift)
    {
        if (altHeld && !alt) volumeShortcutKeys.Clear();
        altHeld = alt; shiftHeld = shift;
        if (!ErrorVisible && dsSliderDrag >= 0) UpdateDistanceSnapSlider(dsDragX, shift);
    }

    private void EnsureDistanceReferences()
    {
        EnsureConversion();
        if (ReferenceEquals(distanceConversion, conversion)) return;
        distanceReferences = DistanceSnap.References(Document, conversion!);
        distanceConversion = conversion;
    }

    private DistanceSnap.Reference? PreviousReference(double time, ISet<Guid>? excluded = null)
        => distanceReferences.LastOrDefault(r => r.Id != draftTrack && r.Start.TimeMs <= time && (excluded is null || !excluded.Contains(r.Id)));

    private MapPoint SnapDistance(MapPoint point, ISet<Guid>? excluded = null)
    {
        distanceOutside = false;
        if (UsesSpeedDistanceSnap) return TryLazerDistanceSnap(point, out var lazerPoint, excluded) ? lazerPoint : point;
        if (!DistanceSnapEnabled) return point;
        EnsureDistanceReferences();
        var next = Document.DistanceSnapCollinear ? distanceReferences.FirstOrDefault(r =>
            r.Id != draftTrack && r.Start.TimeMs > point.TimeMs && (excluded is null || !excluded.Contains(r.Id))) : null;
        return DistanceSnap.SnapMultiple(point, PreviousReference(point.TimeMs, excluded), Document.DistanceSnapRatios, out distanceOutside, next);
    }

    private bool TryDistanceSnap(MapPoint point, DistanceSnap.Reference[] references, ISet<Guid>? excluded,
        double minX, double maxX, out MapPoint result)
    {
        result = point; distanceOutside = false;
        if (UsesSpeedDistanceSnap) return TryLazerDistanceSnap(point, out result, excluded, minX, maxX);
        if (!DistanceSnapEnabled) return false;
        var previous = references.LastOrDefault(r => r.Id != draftTrack && r.Start.TimeMs <= point.TimeMs
            && excluded?.Contains(r.Id) != true);
        if (previous is null || point.TimeMs <= previous.End.TimeMs) return false;
        var next = Document.DistanceSnapCollinear ? references.FirstOrDefault(r => r.Id != draftTrack
            && r.Start.TimeMs > point.TimeMs && excluded?.Contains(r.Id) != true) : null;
        result = DistanceSnap.SnapMultiple(point, previous, Document.DistanceSnapRatios, out distanceOutside, next,
            minX, maxX, EffectiveGridSnap ? gridSize : 0);
        return !distanceOutside;
    }

    private MapPoint PlacementPoint(float x, float y)
    {
        if (UsesSpeedDistanceSnap) return LazerPlacementPoint(x, y);
        var point = MapAt(x, y, true);
        if (TrySnapToObject(point, Transform.ToMap(x, y).X, out var aligned)) return aligned;
        point = SnapDistance(point);
        return point with { X = Math.Clamp(SnapX(point.X), 0, 512) };
    }

    private MapPoint PenStartPoint(float x, float y)
    {
        if (UsesSpeedDistanceSnap) return LazerPlacementPoint(x, y);
        var point = MapAt(x, y, true, clampX: false);
        if (point.X is < 0 or > 512) { objectSnapTarget = null; distanceOutside = false; return point; }
        if (TrySnapToObject(point, Transform.ToMap(x, y).X, out var aligned)) return aligned;
        point = SnapDistance(point);
        return point with { X = Math.Clamp(SnapX(point.X), 0, 512) };
    }

    private Guid[] FlagTargets() => objectSelection.Count > 0 ? objectSelection.ToArray()
        : SelectedTrack is { } track && draftTrack == Guid.Empty ? [track.Id] : [];
    private int? SoundEdge(Guid id) => soundEdge is { } edge && edge.Id == id && FlagTargets().Length == 1 ? edge.Edge : null;

    private void PickSoundEdge(ConvertedCatchObject item)
    {
        clickedCoordinate = item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet ? item : null;
        distanceObject = item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet
            && !(item.Kind is CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet
                && Document.ImportedSliders.Any(s => s.Id == item.SourceId))
            ? (item.SourceId, item.EventIndex) : null;
        soundEdge = null;
        if (item.Kind != CatchObjectKind.Fruit || item.IsStandalone) return;
        int edge = conversion!.Objects.Where(o => o.SourceId == item.SourceId && o.Kind == CatchObjectKind.Fruit)
            .TakeWhile(o => o.EventIndex != item.EventIndex).Count();
        soundEdge = (item.SourceId, edge);
    }

    private void ToggleCombo()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        var ids = FlagTargets();
        if (tool == Tool.Fruit || ids.Length == 0) { nextFruitNewCombo = !nextFruitNewCombo; return; }
        var fruit = SelectedTimelineFruit();
        bool enabled = fruit is not null
            ? !SliderFruitStream.IsNewCombo(Document.Tracks.First(t => t.Id == fruit.SourceId), fruit.EventIndex, fruit.TimeMs)
            : !ids.All(id => ObjectFlags.NewCombo(Document, id));
        bool conversionCurrent = conversion is not null && convertedSnapshot is not null
            && convertedSnapshot.ContentEquals(Document) && convertedWithCompensation == compensateTinyDroplets
            && !contentDragPreview;
        if (Edit(L.Get("assist.comboChange"), () =>
        {
            if (fruit is not null) ObjectFlags.SetStreamNewCombo(Document, fruit.SourceId, fruit.EventIndex, fruit.TimeMs, enabled);
            else foreach (var id in ids) ObjectFlags.SetNewCombo(Document, id, enabled);
        })
            && conversionCurrent && !IsOsuCanvas)
        {
            // The flag changes combo grouping but not the converted catch object stream.
            convertedSnapshot = Document.DeepClone();
            BuildComboColours();
            timelineConversion = null;
            previewSource = null;
        }
    }

    private void ToggleSound(int flag)
    {
        if (TimingPageVisible) return;
        if (tool == Tool.Banana) return;
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        var ids = FlagTargets().Where(id => !Document.BananaShowers.Any(b => b.Id == id)).ToArray();
        if (tool == Tool.Fruit || ids.Length == 0 && FlagTargets().Length == 0) { nextSounds ^= flag; return; }
        if (ids.Length == 0) return;
        bool enabled = !ids.SelectMany(id => ObjectFlags.Sounds(Document, id, SoundEdge(id))).All(s => (s & flag) != 0);
        Edit(L.Get("assist.soundChange"), () => { foreach (var id in ids) ObjectFlags.SetSound(Document, id, flag, enabled, SoundEdge(id)); });
    }

    private void ApplyPlacementFlags(Guid id)
    {
        if (nextNormalBank != 0) ObjectFlags.SetSampleBank(Document, id, false, nextNormalBank);
        if (nextAdditionBank != 0) ObjectFlags.SetSampleBank(Document, id, true, nextAdditionBank);
        if (nextFruitNewCombo) ObjectFlags.SetNewCombo(Document, id, true);
        foreach (int flag in new[] { 2, 4, 8 }) if ((nextSounds & flag) != 0) ObjectFlags.SetSound(Document, id, flag, true);
        nextFruitNewCombo = false;
    }

    private void ToggleNotesLock()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        notesLocked = !notesLocked;
    }

    private void ToggleDropletSelectionLock()
    {
        dropletSelectionLocked = !dropletSelectionLocked;
        RequestViewPreference?.Invoke();
        if (dropletSelectionLocked) clickedCoordinate = null;
        if (dropletSelectionLocked && SelectedDistanceObject() is { Kind: CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet })
            distanceObject = null;
    }

    private bool CanSelectCatchObject(ConvertedCatchObject item)
        => CanSelectSlider(item.SourceId)
            && (!dropletSelectionLocked || item.Kind is not (CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet));

    private void ToggleSliderCanvasLock()
    {
        LibrarySettings.LockSlidersToCanvas = !LibrarySettings.LockSlidersToCanvas;
        if (objectSelection.Any(id => !CanSelectSlider(id))) SelectObjects(objectSelection, selection);
        if (SelectedTrack is { } track && !CanSelectSlider(track.Id)) Select(Guid.Empty);
        RequestViewPreference?.Invoke();
    }

    private void DrawNotesLockFlyout(ICanvas c)
    {
        var button = assistButtons[6];
        string label = L.Get("assist.lockDropletSelection");
        string sliderLabel = L.Get("assist.lockSlidersToCanvas");
        float w = Math.Max(180, Math.Max(c.MeasureText(label, 12, true), c.MeasureText(sliderLabel, 12, true)) + 20);
        float y = Math.Clamp(button.Y, plot.Y, Math.Max(plot.Y, assistPalette.Bottom - 68));
        var flyout = new Rect(button.X - w, y, w, 68);
        notesLockFlyoutBounds = flyout;
        var area = new Rect(flyout.X, y, w + button.Width, Math.Max(68, button.Height));
        notesLockFlyout = button.Contains(mouseX, mouseY) && assistPalette.Contains(mouseX, mouseY)
            || notesLockFlyout && area.Contains(mouseX, mouseY);
        if (!notesLockFlyout) return;
        c.Fill(flyout, Surface, 4);
        Button(c, new(flyout.X, flyout.Y, w - 2, 32), label, ToggleDropletSelectionLock, dropletSelectionLocked);
        Button(c, new(flyout.X, flyout.Y + 34, w - 2, 32), sliderLabel, ToggleSliderCanvasLock, LibrarySettings.LockSlidersToCanvas);
    }

    private static bool PositionsEqual(MapDocument before, MapDocument after)
    {
        var a = before.DeepClone(); var b = after.DeepClone();
        b.Fruits.RemoveAll(f => a.Fruits.All(old => old.Id != f.Id));
        b.Tracks.RemoveAll(t => a.Tracks.All(old => old.Id != t.Id));
        b.ImportedSliders.RemoveAll(t => a.ImportedSliders.All(old => old.Id != t.Id));
        b.BananaShowers.RemoveAll(t => a.BananaShowers.All(old => old.Id != t.Id));
        foreach (var map in new[] { a, b })
        {
            foreach (var f in map.Fruits) f.OriginalLine = null;
            foreach (var t in map.Tracks) t.OriginalLine = null;
            foreach (var s in map.ImportedSliders) s.OriginalLine = null;
            foreach (var s in map.BananaShowers) s.OriginalLine = null;
        }
        b.DistanceSnapCollinear = a.DistanceSnapCollinear;
        b.DistanceSnapRatios.Clear(); b.DistanceSnapRatios.AddRange(a.DistanceSnapRatios);
        b.DistanceSpacing = a.DistanceSpacing; b.DurationMs = a.DurationMs;
        return a.ContentEquals(b);
    }

    private void DrawAssistPalette(ICanvas c)
    {
        assistButtons.Clear();
        float availableHeight = Math.Max(0, plot.Bottom - CanvasCornerBottom);
        float size = IsOsuCanvas ? Math.Clamp(availableHeight / 7, 48, 64) : Math.Clamp(MathF.Floor(availableHeight / 7), 64, 96);
        float buttonWidth = IsOsuCanvas ? 76 : size;
        assistPalette = new(CanvasCornerRight - buttonWidth, CanvasCornerBottom, buttonWidth, availableHeight);
        assistScrollLimit = Math.Max(0, size * 7 - availableHeight);
        assistScroll = Math.Clamp(assistScroll, 0, assistScrollLimit);
        float top = assistPalette.Y - assistScroll;
        string[] keys = ["tools.newCombo", "assist.whistle", "assist.finish", "assist.clap", "assist.grid", "assist.distance", "assist.lock"];
        string[] names = ["new-combo", "whistle", "finish", "clap", "grid-snap", "distance-snap", "lock-notes"];
        Rect[] iconSources = [new(155, 125, 944, 1018), new(78, 302, 1128, 628), new(99, 175, 1057, 811),
            new(240, 188, 743, 902), new(204, 222, 846, 817), new(60, 386, 1134, 487), new(196, 125, 880, 983)];
        string[] hints = ["assist.hint.0", "assist.hint.1", "assist.hint.2", "assist.hint.3", "assist.hint.4", "assist.hint.5", "assist.hint.6"];
        Action[] actions = [ToggleCombo, () => ToggleSound(2), () => ToggleSound(4), () => ToggleSound(8), () => gridSnap = !gridSnap, () => distanceSnap = !distanceSnap, ToggleNotesLock];
        var ids = FlagTargets();
        bool placement = tool == Tool.Fruit || ids.Length == 0;
        var sounds = ids.Where(id => !Document.BananaShowers.Any(b => b.Id == id)).SelectMany(id => ObjectFlags.Sounds(Document, id, SoundEdge(id))).ToArray();
        var selectedFruit = SelectedTimelineFruit();
        bool combo = placement ? nextFruitNewCombo : selectedFruit is not null
            ? newComboEvents.Contains((selectedFruit.SourceId, selectedFruit.EventIndex))
            : ids.All(id => ObjectFlags.NewCombo(Document, id));
        bool[] active = [combo, false, false, false, !UsesSpeedDistanceSnap && EffectiveGridSnap, DistanceSnapEnabled, notesLocked];
        for (int i = 1; i <= 3; i++) active[i] = placement ? (nextSounds & (1 << i)) != 0 : sounds.Length > 0 && sounds.All(s => (s & (1 << i)) != 0);
        int hovered = -1;
        c.Clip(assistPalette);
        for (int i = 0; i < 7; i++)
        {
            var r = new Rect(assistPalette.X, top + i * size, assistPalette.Width, size);
            assistButtons.Add(r);
            if (!Intersects(r, assistPalette)) continue;
            bool enabled = i is 4 or 5 || draftTrack == Guid.Empty && draftBanana == Guid.Empty;
            if (i == 4 && UsesSpeedDistanceSnap) enabled = false;
            if (i is >= 1 and <= 3 && (TimingPageVisible || tool == Tool.Banana || !placement && sounds.Length == 0)) enabled = false;
            float opacity = !enabled ? .25f : active[i] ? 1 : .45f;
            bool mixed = i is >= 1 and <= 3 && !placement && !active[i] && sounds.Any(s => (s & (1 << i)) != 0);
            float scale = size / 512;
            float inset = IsOsuCanvas ? 2 : 16 * scale, corner = IsOsuCanvas ? 8 : 64 * scale;
            var frame = new Rect(r.X + inset, r.Y + inset, r.Width - 2 * inset, r.Height - 2 * inset);
            c.Fill(frame, Dim(Panel, opacity), corner);
            c.Stroke(frame, i == 5 && DistanceSnapEnabled && distanceOutside ? Error : Dim(Accent, opacity), IsOsuCanvas ? 1 : 8 * scale, corner);
            var source = iconSources[i];
            float iconScale = IsOsuCanvas ? Math.Min(50 / source.Width, (size - 22) / source.Height)
                : Math.Min(340 / source.Width, 288 / source.Height) * scale;
            float iconCenterY = IsOsuCanvas ? 4 + (size - 22) / 2 : 204 * scale;
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "assist", names[i] + ".png"),
                new(r.X + r.Width / 2 - source.Width * iconScale / 2, r.Y + iconCenterY - source.Height * iconScale / 2,
                    source.Width * iconScale, source.Height * iconScale), source: source, opacity: opacity);
            string label = L.Get(keys[i]);
            float font = IsOsuCanvas ? 8 : 52 * scale;
            font = Math.Min(font, (r.Width - 8) / Math.Max(1, c.MeasureText(label, font, true)) * font);
            c.Text(label, r.X + (r.Width - c.MeasureText(label, font, true)) / 2, IsOsuCanvas ? r.Bottom - 14 : r.Y + size * .76f,
                font, active[i] ? Foreground : Chrome(0x747B85, Muted), r.Width - 8, true);
            if (mixed) c.Line(frame.X + 6, frame.Y + 8, frame.X + 14, frame.Y + 8, Gold, 2);
            var hit = new Rect(r.X, Math.Max(r.Y, assistPalette.Y), r.Width,
                Math.Min(r.Bottom, assistPalette.Bottom) - Math.Max(r.Y, assistPalette.Y));
            hits.Add(new(hit, actions[i], enabled));
            if (hit.Contains(mouseX, mouseY)) hovered = i;
        }
        c.Unclip();
        if (assistScrollLimit > 0)
        {
            float thumbHeight = availableHeight * availableHeight / (7 * size);
            c.Fill(new(assistPalette.Right + 2, assistPalette.Y + (availableHeight - thumbHeight) * assistScroll / assistScrollLimit, 3, thumbHeight), Muted, 1);
        }
        DrawDistanceSnapFlyout(c);
        DrawNotesLockFlyout(c);
        if (hovered >= 0)
        {
            string tip = hovered is >= 1 and <= 3 && !placement && soundEdge is { } edge && ids.Length == 1
                ? L.Get("assist.edge", edge.Edge + 1) : L.Get(hints[hovered]);
            if (UsesSpeedDistanceSnap && hovered is 4 or 5)
                tip = L.Get(hovered == 4 ? "assist.lazerGridHint" : "assist.lazerDistanceHint");
            DrawPaletteTooltip(c, tip, assistButtons[hovered], left: true, above: hovered is 5 or 6);
        }
        uint Dim(uint color, float opacity)
        {
            uint Blend(int shift) => (uint)(((color >> shift) & 255) * opacity + ((Background >> shift) & 255) * (1 - opacity));
            return Blend(16) << 16 | Blend(8) << 8 | Blend(0);
        }
    }

    private void DrawDistanceReadout(ICanvas c)
    {
        DistanceReadout = (null, null);
        bool placement = PlacementGhostPoint() is not null;
        if (placement && placementGhost is not null) { DistanceReadout = placementDistances; return; }
        var target = placement ? null : DistanceReadoutObject();
        if (target is not null)
        {
            var neighbours = DistanceNeighbours(target, ReferenceEquals(target, clickedCoordinate) ? playableObjects : null);
            DistanceReadout = (neighbours.Previous is { } prev ? BaseDistanceRatio(prev, target) : null,
                neighbours.Next is { } nextObject ? BaseDistanceRatio(target, nextObject) : null);
            return;
        }
        var ids = FlagTargets().ToHashSet();
        EnsureDistanceReferences();
        MapPoint point, end;
        double velocity;
        if (PlacementGhostPoint() is { } ghost)
        {
            ids.Clear();
            point = end = ghost;
            velocity = BaseDistanceVelocity(point.TimeMs);
        }
        else
        {
            if (ids.Count == 0 || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
            var selected = distanceReferences.Where(r => ids.Contains(r.Id)).ToArray();
            if (selected.Length == 0) return;
            point = selected.MinBy(r => r.Start.TimeMs)!.Start;
            end = selected.MaxBy(r => r.End.TimeMs)!.End;
            velocity = BaseDistanceVelocity(end.TimeMs);
        }
        var previous = PreviousReference(point.TimeMs, ids);
        var next = distanceReferences.FirstOrDefault(r => r.Id != draftTrack && !ids.Contains(r.Id) && r.Start.TimeMs >= end.TimeMs);
        DistanceReadout = (previous is null ? null : DistanceSnap.Ratio(previous.End, point, BaseDistanceVelocity(previous.End.TimeMs)),
            next is null ? null : DistanceSnap.Ratio(end, next.Start, velocity));

    }
}
