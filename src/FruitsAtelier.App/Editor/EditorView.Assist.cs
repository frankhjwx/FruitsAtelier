using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool altHeld, shiftHeld, distanceSnap, notesLocked, distanceOutside;
    private int nextSounds;
    private (Guid Id, int Edge)? soundEdge;
    private CatchConversionResult? distanceConversion;
    private DistanceSnap.Reference[] distanceReferences = [];
    private readonly List<Rect> assistButtons = [];
    private Rect assistPalette;
    private float assistScroll, assistScrollLimit;
    public IReadOnlyList<Rect> AssistButtonBounds => assistButtons;
    public bool DistanceSnapEnabled => distanceSnap ^ altHeld;
    public bool DistanceSpacingVisible => altHeld || drag == DragKind.DistanceSpacing;
    public bool NotesLocked => notesLocked;
    private bool EffectiveGridSnap => gridSnap ^ (shiftHeld && !altHeld);
    public (double? Previous, double? Next) DistanceReadout { get; private set; }

    public void SetModifiers(bool alt, bool shift)
    {
        altHeld = alt; shiftHeld = shift;
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
        if (!DistanceSnapEnabled) return point;
        EnsureDistanceReferences();
        return DistanceSnap.Snap(point, PreviousReference(point.TimeMs, excluded), Document.DistanceSpacing, out distanceOutside);
    }

    private MapPoint PlacementPoint(float x, float y)
    {
        var point = SnapDistance(MapAt(x, y, true));
        return point with { X = Math.Clamp(SnapX(point.X), 0, 512) };
    }

    private void SetDistanceSpacing(float x)
    {
        double fraction = Math.Clamp((x - snapSlider.X - 7) / (snapSlider.Width - 38), 0, 1);
        Document.DistanceSpacing = Math.Clamp(Math.Round(.1 + fraction * 5.9, shiftHeld ? 2 : 1), .1, 6);
    }

    private void AdjustDistanceSpacing(float delta)
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        double value = Math.Clamp(Math.Round(Document.DistanceSpacing + delta / 120 * (shiftHeld ? .01 : .1), 2), .1, 6);
        Edit(L.Get("assist.spacingChange"), () => Document.DistanceSpacing = value);
    }

    private Guid[] FlagTargets() => objectSelection.Count > 0 ? objectSelection.ToArray()
        : SelectedTrack is { } track && draftTrack == Guid.Empty ? [track.Id] : [];
    private int? SoundEdge(Guid id) => soundEdge is { } edge && edge.Id == id && FlagTargets().Length == 1 ? edge.Edge : null;

    private void PickSoundEdge(ConvertedCatchObject item)
    {
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
        bool enabled = !ids.All(id => ObjectFlags.NewCombo(Document, id));
        Edit(L.Get("assist.comboChange"), () => { foreach (var id in ids) ObjectFlags.SetNewCombo(Document, id, enabled); });
    }

    private void ToggleSound(int flag)
    {
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
        if (nextFruitNewCombo) ObjectFlags.SetNewCombo(Document, id, true);
        foreach (int flag in new[] { 2, 4, 8 }) if ((nextSounds & flag) != 0) ObjectFlags.SetSound(Document, id, flag, true);
        nextFruitNewCombo = false;
    }

    private void ToggleNotesLock()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        notesLocked = !notesLocked;
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
        b.DistanceSpacing = a.DistanceSpacing; b.DurationMs = a.DurationMs;
        return a.ContentEquals(b);
    }

    private void DrawAssistPalette(ICanvas c)
    {
        assistButtons.Clear();
        float size = Math.Clamp(plot.Height / 7, 64, 96);
        assistPalette = new(canvas.Right - 26 - size, plot.Y, size, plot.Height);
        assistScrollLimit = Math.Max(0, size * 7 - plot.Height);
        assistScroll = Math.Clamp(assistScroll, 0, assistScrollLimit);
        float top = plot.Y + Math.Max(0, (plot.Height - size * 7) / 2) - assistScroll;
        string[] keys = ["tools.newCombo", "assist.whistle", "assist.finish", "assist.clap", "assist.grid", "assist.distance", "assist.lock"];
        string[] names = ["new-combo", "whistle", "finish", "clap", "grid-snap", "distance-snap", "lock-notes"];
        Rect[] iconSources = [new(155, 125, 944, 1018), new(78, 302, 1128, 628), new(99, 175, 1057, 811),
            new(240, 188, 743, 902), new(204, 222, 846, 817), new(60, 386, 1134, 487), new(196, 125, 880, 983)];
        string[] hints = ["assist.hint.0", "assist.hint.1", "assist.hint.2", "assist.hint.3", "assist.hint.4", "assist.hint.5", "assist.hint.6"];
        Action[] actions = [ToggleCombo, () => ToggleSound(2), () => ToggleSound(4), () => ToggleSound(8), () => gridSnap = !gridSnap, () => distanceSnap = !distanceSnap, ToggleNotesLock];
        var ids = FlagTargets();
        bool placement = tool == Tool.Fruit || ids.Length == 0;
        var sounds = ids.Where(id => !Document.BananaShowers.Any(b => b.Id == id)).SelectMany(id => ObjectFlags.Sounds(Document, id, SoundEdge(id))).ToArray();
        bool combo = placement ? nextFruitNewCombo : ids.All(id => ObjectFlags.NewCombo(Document, id));
        bool[] active = [combo, false, false, false, EffectiveGridSnap, DistanceSnapEnabled, notesLocked];
        for (int i = 1; i <= 3; i++) active[i] = placement ? (nextSounds & (1 << i)) != 0 : sounds.Length > 0 && sounds.All(s => (s & (1 << i)) != 0);
        int hovered = -1;
        c.Clip(assistPalette);
        for (int i = 0; i < 7; i++)
        {
            var r = new Rect(assistPalette.X, top + i * size, size, size);
            assistButtons.Add(r);
            if (!Intersects(r, assistPalette)) continue;
            bool enabled = i is 4 or 5 || draftTrack == Guid.Empty && draftBanana == Guid.Empty;
            if (i is >= 1 and <= 3 && (tool == Tool.Banana || !placement && sounds.Length == 0)) enabled = false;
            float opacity = !enabled ? .25f : active[i] ? 1 : .45f;
            bool mixed = i is >= 1 and <= 3 && !placement && !active[i] && sounds.Any(s => (s & (1 << i)) != 0);
            float scale = size / 512;
            var frame = new Rect(r.X + 16 * scale, r.Y + 16 * scale, 480 * scale, 480 * scale);
            // These dimensions match the generated left palette's 512px button template.
            c.Fill(frame, Dim(Panel, opacity), 64 * scale);
            c.Stroke(frame, i == 5 && DistanceSnapEnabled && distanceOutside ? Error : Dim(Accent, opacity), 8 * scale, 64 * scale);
            var source = iconSources[i];
            float iconScale = Math.Min(340 / source.Width, 288 / source.Height) * scale;
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "assist", names[i] + ".png"),
                new(r.X + 256 * scale - source.Width * iconScale / 2, r.Y + 204 * scale - source.Height * iconScale / 2,
                    source.Width * iconScale, source.Height * iconScale), source: source, opacity: opacity);
            string label = L.Get(keys[i]);
            float font = size * 52 / 512;
            font = Math.Min(font, (size - 12 * scale) / Math.Max(1, c.MeasureText(label, font, true)) * font);
            c.Text(label, r.X + (size - c.MeasureText(label, font, true)) / 2, r.Y + size * .76f,
                font, active[i] ? Foreground : 0x747B85, size, true);
            if (mixed) c.Line(frame.X + 6, frame.Y + 8, frame.X + 14, frame.Y + 8, Gold, 2);
            var hit = new Rect(r.X, Math.Max(r.Y, assistPalette.Y), r.Width,
                Math.Min(r.Bottom, assistPalette.Bottom) - Math.Max(r.Y, assistPalette.Y));
            hits.Add(new(hit, actions[i], enabled));
            if (hit.Contains(mouseX, mouseY)) hovered = i;
        }
        c.Unclip();
        if (assistScrollLimit > 0)
        {
            float thumbHeight = plot.Height * plot.Height / (7 * size);
            c.Fill(new(assistPalette.Right + 2, plot.Y + (plot.Height - thumbHeight) * assistScroll / assistScrollLimit, 3, thumbHeight), Muted, 1);
        }
        if (hovered >= 0)
        {
            string tip = hovered is >= 1 and <= 3 && !placement && soundEdge is { } edge && ids.Length == 1
                ? L.Get("assist.edge", edge.Edge + 1) : L.Get(hints[hovered]);
            float w = Math.Min(280, c.MeasureText(tip, 11) + 16);
            float y = Math.Clamp(assistButtons[hovered].Y, plot.Y, plot.Bottom - 28);
            c.Fill(new(assistPalette.X - w - 6, y, w, 28), Surface, 4);
            c.Text(tip, assistPalette.X - w + 2, y + 8, 11, Foreground, w - 12);
        }
        static uint Dim(uint color, float opacity)
        {
            uint Blend(int shift) => (uint)(((color >> shift) & 255) * opacity + ((Background >> shift) & 255) * (1 - opacity));
            return Blend(16) << 16 | Blend(8) << 8 | Blend(0);
        }
    }

    private void DrawDistanceReadout(ICanvas c)
    {
        DistanceReadout = (null, null);
        var ids = FlagTargets().ToHashSet();
        EnsureDistanceReferences();
        MapPoint point, end;
        double velocity;
        if (PlacementGhostPoint() is { } ghost)
        {
            ids.Clear();
            point = end = ghost;
            var timing = TimingMap.At(Document, point.TimeMs);
            velocity = 100 * Document.SliderMultiplier / timing.BeatLengthMs;
            if (tool == Tool.Slider) velocity *= timing.SliderVelocityMultiplier;
        }
        else
        {
            if (ids.Count != 1 || draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
            var selected = distanceReferences.FirstOrDefault(r => ids.Contains(r.Id));
            if (selected is null) return;
            point = selected.Start; end = selected.End; velocity = selected.Velocity;
        }
        var previous = PreviousReference(point.TimeMs, ids);
        var next = distanceReferences.FirstOrDefault(r => r.Id != draftTrack && !ids.Contains(r.Id) && r.Start.TimeMs >= end.TimeMs);
        DistanceReadout = (previous is null ? null : DistanceSnap.Ratio(previous.End, point, previous.Velocity),
            next is null ? null : DistanceSnap.Ratio(end, next.Start, velocity));

    }
}
