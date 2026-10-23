using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool catchPreviewVisible => PanelVisible(DockPanel.Preview);
    private const float MinimumPreviewWidth = 344;
    private int previewMod { get => DockWorkspaceState.PreviewMod; set => DockWorkspaceState.PreviewMod = value; }
    private int previewDisplayMode { get => DockWorkspaceState.PreviewDisplayMode; set => DockWorkspaceState.PreviewDisplayMode = value; }
    private CatchAutoPreview? previewAutoplay;
    private CatchPlatePreview? previewPlate;
    private HashSet<(Guid SourceId, int EventIndex)> previewComboEnds = [];
    public Rect PreviewViewport { get; private set; }
    public double PreviewCatcherX { get; private set; }
    private CatchConversionResult? previewSource;
    private int cachedPreviewMod = -1;
    private IReadOnlyList<ConvertedCatchObject> previewObjects = [];
    private HashSet<(Guid SourceId, int EventIndex)> previewHyperdash = [];
    private int previewMaximumCombo, previewMaximumDroplets;
    private string previewDrainTime = "0:00 (0s)";
    public bool CatchPreviewVisible => catchPreviewVisible;
    public Rect PreviewToggleBounds { get; private set; }
    public Rect PreviewResizeBounds { get; private set; }
    public bool PreviewResizeCursor => !TimeJumpVisible && !LibraryVisible && !ExportVisible && !ErrorVisible && !DiscardConfirmationVisible && !SliderDialogVisible && !languageMenuOpen && menu < 0
        && !PatternGalleryPopupContains(mouseX, mouseY) && !PatternGalleryWantsCapture
        && !TimingPageVisible && dockPressed is null && (drag == DragKind.PreviewResize || leftDockResizeBounds.Contains(mouseX, mouseY) || rightDockResizeBounds.Contains(mouseX, mouseY));
    public Rect LegacyConversionBounds { get; private set; }
    public double PreviewApproachRate => previewMod switch { 1 => Document.ApproachRate * .5, 2 => Math.Min(10, Document.ApproachRate * 1.4), _ => Document.ApproachRate };
    public double PreviewCircleSize => previewMod switch { 1 => Document.CircleSize * .5, 2 => Math.Min(10, Document.CircleSize * 1.3), _ => Document.CircleSize };
    public double PreviewStarRating
    {
        get
        {
            var session = difficulties[activeDifficulty];
            return (previewMod switch { 1 => session.EasyStars, 2 => session.HardRockStars, _ => session.Stars }) ?? 0;
        }
    }
    private string PreviewModName => previewMod switch { 1 => "EZ", 2 => "HR", _ => "NM" };
    private void DrawPreviewMods(ICanvas c, Rect r)
    {
        string[] keys = ["preview.normal", "preview.easy", "preview.hardRock"];
        bool narrow = r.Width < 260;
        c.Text(L.Get("preview.mode"), r.X, r.Y + (narrow ? 43 : 50), 10, Muted, narrow ? r.Width : 65);
        float start = r.X + (narrow ? 0 : 66), buttonWidth = narrow ? r.Width : (r.Width - 66) / 3;
        for (int i = 0; i < keys.Length; i++)
        {
            int mod = i;
            Button(c, new(start + (narrow ? 0 : i * buttonWidth), r.Y + (narrow ? 60 + i * 28 : 43), buttonWidth - 3, 26), L.Get(keys[i]), () => previewMod = mod, previewMod == mod);
        }
    }
    private IReadOnlyList<ConvertedCatchObject> PreviewObjects()
    {
        if (!ReferenceEquals(previewSource, conversion) || cachedPreviewMod != previewMod)
        {
            previewSource = conversion; cachedPreviewMod = previewMod;
            previewObjects = previewMod == 2
                ? playableExport?.PlayableHardRockObjects ?? CatchPreviewMods.HardRock(Document, conversion!)
                : playableObjects;
            previewMaximumCombo = previewObjects.Count(item => item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet);
            previewMaximumDroplets = previewObjects.Count(item => item.Kind == CatchObjectKind.TinyDroplet);
            previewDrainTime = PreviewDrainTime();
            previewHyperdash = HyperDashCalculator.GetHyperDashStarts(previewObjects, PreviewCircleSize);
            previewAutoplay = new(previewObjects, PreviewCircleSize);
            previewLighting = BuildLighting(previewObjects, previewAutoplay);
            var entries = ComboEntries().ToArray();
            var tails = previewObjects.GroupBy(item => item.SourceId).ToDictionary(group => group.Key, group => group.Last());
            var streamIds = Document.Tracks.Where(t => t.StreamSnapDivisor is not null).Select(t => t.Id).ToHashSet();
            var fruits = previewObjects.Where(o => streamIds.Contains(o.SourceId)).ToDictionary(o => (o.SourceId, o.EventIndex));
            var ends = previewComboEnds = new HashSet<(Guid, int)>();
            for (int i = 0; i < entries.Length; i++)
                if ((i == entries.Length - 1 || (entries[i + 1].Flags & 4) != 0 || entries[i].IsBanana || entries[i + 1].IsBanana)
                    && (fruits.TryGetValue((entries[i].Id, entries[i].EventIndex), out var tail) || tails.TryGetValue(entries[i].Id, out tail)))
                    ends.Add((tail.SourceId, tail.EventIndex));
            previewPlate = new(previewObjects, previewAutoplay, PreviewCircleSize, ends);
        }
        return previewObjects;
    }
    private string PreviewDrainTime()
    {
        RefreshTimelineSources();
        if (previewObjects.Count == 0 || timelineStarts.Length == 0) return "0:00 (0s)";
        double start = timelineStarts[0], end = timelineEnds[^1];
        double duration = end - start, countedUntil = start;
        foreach (var period in breakPeriods)
        {
            double breakStart = Math.Max(countedUntil, period.StartMs);
            double breakEnd = Math.Min(end, period.EndMs);
            if (breakEnd <= breakStart) continue;
            duration -= breakEnd - breakStart;
            countedUntil = breakEnd;
        }
        long seconds = (long)Math.Max(0, Math.Floor(duration / 1000));
        return FormattableString.Invariant($"{seconds / 60}:{seconds % 60:00} ({seconds}s)");
    }
    private IEnumerable<ConvertedCatchObject> PreviewObjectsInRange(double start, double end)
    {
        var objects = PreviewObjects();
        int low = 0, high = objects.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (objects[middle].TimeMs < start) low = middle + 1; else high = middle;
        }
        for (int i = low; i < objects.Count && objects[i].TimeMs <= end; i++) yield return objects[i];
    }
    private void DrawPreviewDisplayModes(ICanvas c, Rect r)
    {
        string[] keys = ["preview.ratio43", "preview.ratio169", "preview.fit"];
        bool narrow = r.Width < 260;
        c.Text(L.Get("preview.resolution"), r.X, r.Y + (narrow ? 150 : 81), 10, Muted, narrow ? r.Width : 65);
        float start = r.X + (narrow ? 0 : 66), buttonWidth = narrow ? r.Width / 3 : (r.Width - 66) / 3;
        for (int i = 0; i < keys.Length; i++)
        {
            int mode = i;
            Button(c, new(start + i * buttonWidth, r.Y + (narrow ? 167 : 74), buttonWidth - 3, 26), L.Get(keys[i]), () => previewDisplayMode = mode, previewDisplayMode == mode);
        }
    }
    private static readonly HashSet<(Guid SourceId, int EventIndex)> NoHyperdash = [];
    private void DrawPreviewPlate(ICanvas c, float fieldLeft, float fieldWidth, float catchY)
    {
        PreviewObjects();
        PreviewCatcherX = previewAutoplay!.At(LazerPresentationTime).X;
        DrawCaughtPlate(c, previewPlate!.At(LazerPresentationTime, PreviewCatcherX), fieldLeft, fieldWidth, catchY);
    }
    private void DrawCaughtPlate(ICanvas c, IEnumerable<CatchPlateSprite> sprites, float fieldLeft, float fieldWidth, float catchY)
    {
        foreach (var sprite in sprites)
            DrawCatchObject(c, sprite.Object, fieldLeft + (float)(sprite.X / 512) * fieldWidth,
                catchY + (float)(sprite.Y / 512) * fieldWidth, fieldWidth * .5f, sprite.Opacity,
                PreviewCircleSize, previewHyperdash, animated: true, caught: true);
    }
    private void DrawPreviewCatcher(ICanvas c, float fieldLeft, float fieldWidth, float catchY)
    {
        PreviewObjects();
        var frame = previewAutoplay!.At(LazerPresentationTime);
        PreviewCatcherX = frame.X;
        float x = fieldLeft + (float)(frame.X / 512) * fieldWidth;
        uint hyperColour = HyperDashColour;
        // Sample map time, not render history, so pause, seek and playback speed produce identical trails.
        for (double at = Math.Max(0, Math.Ceiling((LazerPresentationTime - 800) / 16) * 16); at <= LazerPresentationTime; at += 16)
        {
            var past = previewAutoplay.At(at);
            bool hyper = previewAutoplay.HyperDashingAt(at);
            if (!past.Dashing && !hyper) continue;
            DrawCatcherTrail(c, new(at, past.X, previewAutoplay.FacingLeftAt(at), hyper, false), fieldLeft, fieldWidth, catchY);
        }
        foreach (double start in previewAutoplay.HyperStarts(LazerPresentationTime - 1200, LazerPresentationTime))
            DrawCatcherTrail(c, new(start, previewAutoplay.At(start).X, previewAutoplay.FacingLeftAt(start), true, true), fieldLeft, fieldWidth, catchY);
        DrawCatcherBody(c, x, catchY, fieldWidth, 0xFFFFFF, 1, false, previewAutoplay.FacingLeftAt(LazerPresentationTime));
        float tint = (float)previewAutoplay.HyperTintAt(LazerPresentationTime);
        if (tint > .001f) DrawCatcherBody(c, x, catchY, fieldWidth, hyperColour, tint, false, previewAutoplay.FacingLeftAt(LazerPresentationTime));
        DrawCatchLighting(c, previewLighting, LazerPresentationTime, x, fieldWidth, catchY, PreviewCircleSize);
    }
    private Guid sliderHoldId;
    private float sliderHoldX, sliderHoldY;
    private double sliderHoldStart;
    private bool sliderHoldConsumed;
    private ConvertedCatchObject? noteHoldTarget;
    public bool SliderHoldNeedsRedraw => sliderHoldId != Guid.Empty || noteHoldTarget is not null;
    private void BeginSliderHold(float x, float y, bool modified)
    {
        sliderHoldId = Guid.Empty;
        noteHoldTarget = null;
        if (modified || notesLocked || draftTrack != Guid.Empty || draftBanana != Guid.Empty || tool is not (Tool.Select or Tool.Slider)) return;
        var hit = HitCatchObject(x, y);
        var box = SelectionTransformBounds;
        Guid boxedSlider = tool == Tool.Select && objectSelection.Count == 1 && box.Width > 0
            && x >= box.X && x <= box.Right && y >= box.Y && y <= box.Bottom
            ? objectSelection.First() : Guid.Empty;
        noteHoldTarget = hit is { Kind: CatchObjectKind.Fruit } && (boxedSlider == Guid.Empty || hit.SourceId == boxedSlider) ? hit : null;
        sliderHoldX = x; sliderHoldY = y; sliderHoldStart = TestplayRealtime;
        Guid id = boxedSlider != Guid.Empty ? boxedSlider : hit?.SourceId ?? (showTargets ? HitSliderLocation(x, y)?.Id : null)
            ?? HitBananaRectangle(x, y)?.Id ?? Guid.Empty;
        if (!Document.Tracks.Any(t => t.Id == id) && !Document.ImportedSliders.Any(t => t.Id == id)
            && !Document.Fruits.Any(f => f.Id == id) && !Document.BananaShowers.Any(s => s.Id == id)) return;
        if (tool == Tool.Slider && SelectedTrack?.Id != id) return;
        sliderHoldId = id; sliderHoldX = x; sliderHoldY = y; sliderHoldStart = TestplayRealtime;
    }

    private void OpenSliderActionsAtPointer()
    {
        if (notesLocked || tool is not (Tool.Select or Tool.Slider)) return;
        if (plot.Contains(mouseX, mouseY)) BeginSliderHold(mouseX, mouseY, false);
        noteHoldTarget = null;
        if (sliderHoldId == Guid.Empty)
        {
            RefreshTimelineSources();
            double visibleEnd = viewStart + plot.Height / pixelsPerMs;
            foreach (var source in timelineSources)
            {
                if (!objectSelection.Contains(source.Id) || source.End < viewStart || source.Start > visibleEnd
                    || !Document.Tracks.Any(t => t.Id == source.Id) && !Document.ImportedSliders.Any(t => t.Id == source.Id)
                        && !Document.Fruits.Any(f => f.Id == source.Id) && !Document.BananaShowers.Any(s => s.Id == source.Id)) continue;
                if (tool == Tool.Slider && SelectedTrack?.Id != source.Id) continue;
                sliderHoldId = source.Id;
                var position = Screen(new(Math.Clamp(source.Start, viewStart, visibleEnd), 256));
                sliderHoldX = position.X; sliderHoldY = position.Y;
                break;
            }
        }
        if (sliderHoldId == Guid.Empty && objectSelection.Count > 0)
        {
            sliderHoldId = objectSelection.FirstOrDefault(id => Document.Tracks.Any(t => t.Id == id)
                || Document.ImportedSliders.Any(s => s.Id == id) || Document.Fruits.Any(f => f.Id == id) || Document.BananaShowers.Any(s => s.Id == id));
            sliderHoldX = plot.X + plot.Width / 2; sliderHoldY = plot.Y + plot.Height / 2;
        }
        if (sliderHoldId == Guid.Empty) return;
        contextItems.Clear();
        OpenSliderActions(sliderHoldId);
        sliderHoldId = Guid.Empty;
    }

    private void OpenSliderActions(Guid id)
        => OpenObjectContextMenu(id, sliderHoldX, sliderHoldY);

    private void OpenObjectContextMenu(Guid id, float x, float y)
    {
        if (!objectSelection.Contains(id)) SelectObjects([id]);
        RefreshSliderInspector();
        menu = -1; contextItems.Clear();
        contextItems.Add(new(L.Get("editor.command.cut"), () => CutSelection(), Shortcut: "Ctrl+X"));
        contextItems.Add(new(L.Get("editor.command.copy"), () => CopySelection(), Shortcut: "Ctrl+C"));
        contextItems.Add(new(L.Get("editor.command.delete"), DeleteSelectedObjects, Shortcut: "Delete"));
        if (IsLazerEditing && plot.Contains(x, y) && SelectedTrack is { } selected && selected.Id == id && !notesLocked)
        {
            var target = MapAt(x, y, true, clampX: false);
            if (target.TimeMs >= CurveMath.EndTimeMs(selected) + CurveMath.MinimumAnchorSpacingMs)
                contextItems.Add(new(L.Get("editor.command.extendSlider"), () => ExtendSlider(selected.Id, target)));
        }
        foreach (var action in SliderInspectorActions().Where(a => a.Kind >= 0))
            contextItems.Add(new(action.Label, action.Action, action.Enabled, SliderAction: action.Kind));
        AddGalleryCaptureContextItem();
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 280)),
            Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 280, ContextMenuHeight);
    }

    private void DrawSliderHold(ICanvas c)
    {
        if (!SliderHoldNeedsRedraw) return;
        if (dragMoved || draftTrack != Guid.Empty || draftBanana != Guid.Empty)
        { sliderHoldId = Guid.Empty; noteHoldTarget = null; return; }
        double elapsed = TestplayRealtime - sliderHoldStart;
        if (elapsed < 300) return;
        if (noteHoldTarget is { } note)
        {
            noteHoldTarget = null;
            if (!note.IsStandalone && !IsReadOnlySlider(note.SourceId))
            {
                CommitContentHistory(); drag = DragKind.None;
                if (LegacyDropletRefreshBusy) { sliderHoldId = Guid.Empty; return; }
                tool = Tool.Select;
                if (objectSelection.Count <= 1)
                {
                    SelectObjects([note.SourceId], note.SourceId);
                    PickSoundEdge(note);
                    BeginSliderObjectDrag(note, sliderHoldX, sliderHoldY);
                }
            }
            ShowNoteBeatPosition(note);
        }
        if (sliderHoldId == Guid.Empty) return;
        double progress = Math.Clamp((elapsed - 300) / 700, 0, 1);
        if (progress >= 1)
        {
            CommitContentHistory(); drag = DragKind.None;
            if (LegacyDropletRefreshBusy) { sliderHoldId = Guid.Empty; return; }
            OpenSliderActions(sliderHoldId);
            sliderHoldId = Guid.Empty; sliderHoldConsumed = true;
            return;
        }
        const float radius = 9;
        c.Circle(sliderHoldX, sliderHoldY, radius, Surface, filled: false, width: 2);
        for (int i = 0; i < Math.Ceiling(progress * 64); i++)
        {
            double a = -Math.PI / 2 + i * Math.Tau / 64;
            double b = -Math.PI / 2 + Math.Min((i + 1) / 64d, progress) * Math.Tau;
            c.Line(sliderHoldX + radius * (float)Math.Cos(a), sliderHoldY + radius * (float)Math.Sin(a),
                sliderHoldX + radius * (float)Math.Cos(b), sliderHoldY + radius * (float)Math.Sin(b), Accent, 2);
        }
    }
    public Rect StreamConversionBounds { get; private set; }
}
