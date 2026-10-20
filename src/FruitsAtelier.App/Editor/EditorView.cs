using L = FruitsAtelier.Localization.Strings;
using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using FruitsAtelier.App.Skinning;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const uint Background = 0x171A20, Panel = 0x20252E, Surface = 0x282F3A;
    private const uint Foreground = 0xE7EBF2, Muted = 0x9AA8BC, Grid = 0x343C49;
    private const uint Accent = 0x59D3C3, Gold = 0xF2C66D, Purple = 0xAB9DF2, Error = 0xFF7F8D;
    private sealed class DifficultySession(ProjectDifficulty difficulty)
    {
        public Guid Id { get; } = difficulty.Id;
        public bool FaOnly { get; set; } = difficulty.FaOnly;
        public MapEditingPreferences EditingPreferences { get; set; } = new();
        public string Name => OsuBeatmapReader.Setting(History.Document, "Metadata", "Version") ?? difficulty.Name;
        public EditorHistory History { get; } = new(PrepareDocument(difficulty.Document));
        private static MapDocument PrepareDocument(MapDocument document)
        {
            LegacySliderVelocityEditing.Capture(document);
            return document;
        }
        public MapDocument? RatingSnapshot;
        public double? Stars, EasyStars, HardRockStars;
        public IReadOnlyList<CatchStrainSample>? StrainSamples;
        public bool RatingCompensation, RatingFailed;
        public Task<(CatchDifficultyCurveResult Normal, double Easy, double HardRock)?>? RatingTask;
        public readonly CancellationTokenSource RatingCancellation = new();
    }
    private readonly List<DifficultySession> difficulties;
    public bool HasEditorProject { get; private set; }
    private readonly TimeProvider timeProvider;
    public EditorView(bool loadDemo = true, TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        difficulties = BeatmapProject.FromDocuments([loadDemo ? DemoMap.Create() : new MapDocument { IsDemo = false }])
            .Difficulties.Select(d => new DifficultySession(d)).ToList();
        RestoreDockLayouts();
        HasEditorProject = loadDemo;
        if (!loadDemo) { ProjectName = ""; StatusMessage = ""; playhead = 0; }
    }
    private int activeDifficulty;
    private bool projectStructureDirty;
    private EditorHistory history => difficulties[activeDifficulty].History;
    public string ProjectName { get; private set; } = L.Get("core.names.demo");
    public string CurrentDifficultyName => difficulties[activeDifficulty].Name;
    public int DifficultyCount => difficulties.Count;
    public int ActiveDifficultyIndex => activeDifficulty;
    private readonly List<HitArea> hits = [];
    private readonly List<NumericField> fields = [];
    private float width, height, mouseX = -1, mouseY = -1;
    private Rect canvas, plot, rightPanel, leftPanel, overview, snapSlider, zoomSlider;
    private double viewStart, pixelsPerMs = 0.09, playhead = 1500;
    private double canvasZoom = .6;
    public const float MinimumPlayfieldWidth = 192;
    public double CanvasZoom => canvasZoom;
    private CatchSkin? skin;
    private uint HyperDashFruitColour => skin?.HyperDashFruitColour ?? 0xFF0000;
    private uint HyperDashColour => skin?.HyperDashColour ?? 0xFF0000;
    private uint HyperDashAfterImageColour => skin?.HyperDashAfterImageColour ?? HyperDashColour;
    private bool compensateTinyDroplets = true;
    private MapDocument? convertedSnapshot;
    private CatchConversionCache editorConversionCache = new();
    private CatchConversionResult? conversion;
    private OsuWriteResult? playableExport;
    private IReadOnlyList<ConvertedCatchObject> playableObjects = [];
    private bool convertedWithCompensation;
    private bool contentDragPreview;
    private HashSet<(Guid SourceId, int EventIndex)> hyperdashObjects = [];
    private Dictionary<Guid, int> skinIndices = [];
    private Guid selection, selectedTrack, draftTrack, draftBanana;
    private Tool tool;
    private DragKind drag, selectedPart;
    private double dragStartTime;
    private float dragStartX, dragStartY;
    private bool dragMoved;
    private MapPoint dragOffset;
    private MapDocument? objectDragStart;
    private bool objectDragPrepared;
    private bool snap = true;
    private bool anchorSnap
    {
        get => EditingPreferences.AnchorSnap;
        set { EditingPreferences.AnchorSnap = value; SaveMapEditingPreferences(); }
    }
    public bool AnchorSnapEnabled => anchorSnap;
    private static readonly int[] StandardSnapDivisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16];
    private static readonly int[] ExtendedSnapDivisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 24, 32, 48];
    private int[] SnapDivisors => LibrarySettings.ExperimentalSnapDivisions ? ExtendedSnapDivisors : StandardSnapDivisors;
    // Keep edge room stable while CS is edited; 54.4 is the CS=0 fruit radius.
    private const float PlayfieldPadding = 54.4f;
    private int currentSnapDivisor = 4, menu = -1, editField = -1;
    private int divisor
    {
        get => currentSnapDivisor;
        set
        {
            currentSnapDivisor = value;
            if (temporarySnapSource != Guid.Empty || EditingPreferences.BeatSnapDivisor == value) return;
            EditingPreferences.BeatSnapDivisor = value;
            if (drag != DragKind.SnapDivisor) SaveMapEditingPreferences();
        }
    }
    private string editBuffer = "", fieldError = "";
    private bool replaceText = true;
    private bool showTargets
    {
        get => LibrarySettings.ShowSliderPaths;
        set { LibrarySettings.ShowSliderPaths = value; RequestViewPreference?.Invoke(); }
    }
    private bool showSelectionRect
    {
        get => LibrarySettings.ShowSelectionRect;
        set { LibrarySettings.ShowSelectionRect = value; RequestViewPreference?.Invoke(); }
    }

    public Action? RequestClose { get; set; }
    public Action? RequestLoadSkin { get; set; }
    public bool IsDirty => projectNotes != savedProjectNotes || projectStructureDirty || difficulties.Any(d => d.History.IsDirty);
    public bool IsEditingText => CollaborationFocused || RandomDockFocused || SongDockFocused || NotesFocused || PatternDockEditing && (stackNumericField >= 0 || bananaEditField >= 0) || BananaEditVisible && bananaEditField >= 0 || HitsoundCopierVisible && copierNewDiff && copierNameFocused || StreamDialogVisible && stackMode && stackNumericField >= 0 || timingField.Length > 0 || SongSetupVisible && songField.Length > 0 || DistanceSnapDialogVisible && dsBaseFocused || DistanceEditing || RotationVisible || TimeJumpVisible || editField >= 0 || (LibraryVisible || ExportVisible) && libraryField >= 0;
    public bool WantsCapture => collaborationReportDragging || rotationWindowDragging || rotationDragging || aimodGesture != AimodGesture.None || dockHorizontalDrag is not null || randomDragging || notesSelecting || dockPressed is not null || acknowledgementsScrollDragging || canvasDimDragging || movementPanelPointer || backgroundDimDragging || workspaceScrollDragging || timingScrollDragging || timingSnapDragging || timingVolumeStart is not null || textSelecting || songDrag >= 0 || dsSnapDragging || dsBaseDragging || dsSliderDrag >= 0 || distanceDragging || volumeDrag >= 0 || volumePopoverDrag >= 0 || drag != DragKind.None || libraryPointerActive || tabPointer || streamSnapDragging || stackPointDragging >= 0 || stackFruitDragging >= 0 || SliderHoldNeedsRedraw || sliderHoldConsumed;
    public MapDocument Document => history.Document;
    public string? SkinName => skin?.Name;
    public double PlayheadMs => playhead;
    public double ViewStartMs => viewStart;
    public double PixelsPerMs => pixelsPerMs;
    public int SnapDivisor => divisor;
    public Rect PlayfieldBounds => Playfield;
    public Rect CanvasPlotBounds => plot;
    public Rect SnapSliderBounds => snapSlider;
    public Rect ZoomSliderBounds => zoomSlider;
    public string ActiveTool => tool.ToString();
    public string StatusMessage { get; private set; } = L.Get("editor.status.demoLoaded");
    public void SetNotice(string notice)
    {
        StatusMessage = notice;
        if (LibraryVisible) libraryNotice = notice;
    }

    public void LoadSkin(string folder)
    {
        folder = UpgradeSkinFolder(folder);
        if (CatchSkin.TryLoad(folder, out var loaded, out string message, defaultSkin, allowEmpty: true))
        { skin = loaded; RefreshSkinHitsounds(); }
        StatusMessage = message;
    }

    private TimingMap.Lookup? renderedTiming;
    private TimingMap.Lookup? snapTiming;
    private TimingMap.Lookup SnapTiming()
    {
        if (snapTiming is null || !snapTiming.MatchesTiming(Document)) snapTiming = new(Document);
        return snapTiming;
    }
    private OsuWriteCache editorWriteCache = new();
    private bool IsContentDrag => RotationVisible || drag is DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn
        or DragKind.HandleOut or DragKind.DraftHandle or DragKind.LegacyControl or DragKind.Objects
        or DragKind.BananaStart or DragKind.BananaEnd or DragKind.TimelineTail;

    public EditorPerformanceMetrics Performance { get; } = new();
    public string PerformanceContext => $"library={LibraryVisible}; playing={AudioPlaying}; testplay={IsTestplaying}; drag={drag}; difficulties={DifficultyCount}; fruits={Document.Fruits.Count}; tracks={Document.Tracks.Count}; importedSliders={Document.ImportedSliders.Count}; bananas={Document.BananaShowers.Count}; timing={Document.TimingPoints.Count}";

    private void EnsureConversion()
    {
        PumpLegacyDropletRefresh();
        if (LegacyDropletRefreshBusy) return;
        if (draftTrack != Guid.Empty && draftBaseConversion is not null)
        {
            UpdateDraftConversion();
            return;
        }
        if (deferredConversion)
        {
            PumpDeferredConversion();
            return;
        }
        long checkStart = Performance.Start();
        bool cached = convertedSnapshot is not null && convertedSnapshot.ContentEqualsIgnoringOsuDistanceSpacing(Document)
            && convertedWithCompensation == compensateTinyDroplets
            && (!contentDragPreview || IsContentDrag);
        Performance.End(EditorPerformanceStage.ConversionCheck, checkStart);
        if (cached)
        {
            convertedSnapshot!.OsuDistanceSpacing = Document.OsuDistanceSpacing;
            return;
        }
        long rebuildStart = Performance.Start();
        try { RebuildConversion(); }
        finally { Performance.End(EditorPerformanceStage.ConversionRebuild, rebuildStart); }
    }

    private void RebuildConversion()
    {
        clickedCoordinate = null;
        convertedSnapshot = Document.DeepClone();
        renderedTiming = SnapTiming();
        convertedWithCompensation = compensateTinyDroplets;
        var input = Document;
        if (input.Tracks.Any(t => t.Nodes.Count < 2))
        {
            input = Document.DeepClone(); input.Tracks.RemoveAll(t => t.Nodes.Count < 2);
        }
        conversion = RotationVisible && rotationValidation is not null ? rotationValidation
            : editorWriteCache.Convert(input, compensateTinyDroplets);
        playableExport = null;
        playableObjects = conversion.Objects;
        // While dragging, use the validated conversion; refresh export quantization on release/cancel.
        contentDragPreview = IsContentDrag;
        if (conversion.Success && !contentDragPreview)
        {
            long exportStart = Performance.Start();
            try
            {
                var exported = OsuBeatmapWriter.Serialize(input, compensateTinyDroplets, editorWriteCache);
                if (exported.ObjectSequenceMatches)
                {
                    playableExport = exported;
                    playableObjects = exported.PlayableObjects;
                }
            }
            catch (InvalidDataException) { } // Draft content may be convertible before it is exportable.
            finally { Performance.End(EditorPerformanceStage.ExportReadback, exportStart); }
        }
        RefreshConversionPresentation();
    }

    private void RefreshConversionPresentation()
    {
        // Nested slider fruits inherit their parent's full-map visual index.
        skinIndices = Document.Fruits.Select(f => (f.Id, Time: f.TimeMs, f.SourceOrder))
            .Concat(Document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Time: t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(Document.ImportedSliders.Select(t => (t.Id, Time: t.TimeMs, t.SourceOrder)))
            .Concat(Document.BananaShowers.Select(t => (t.Id, Time: t.TimeMs, t.SourceOrder)))
            .OrderBy(source => source.Time).ThenBy(source => source.SourceOrder)
            .Select((source, index) => (source.Id, Index: index))
            .ToDictionary(source => source.Id, source => source.Index);
        RefreshBananaSegments();
        BuildComboColours();
        RefreshKiaiTransitions();
        RefreshBeatmapBackground();
        breakPeriods = OsuTimeline.Breaks(Document).ToArray();
        hyperdashObjects = HyperDashCalculator.GetHyperDashStarts(playableObjects, Document.CircleSize);
    }

    private enum Tool { Select, Fruit, Slider, Banana }
    private enum DragKind { None, PlaybackLine, Objects, SliderObject, Anchor, HandleIn, HandleOut, DraftHandle, BananaStart, BananaEnd, Pan, Timeline, Break, BreakEdge, Marquee, SnapDivisor, OsuDefaultSv, CanvasZoom, LegacyControl, TimelineTail, PreviewResize, DifficultySeek, TimelinePanelResize }
    private sealed record HitArea(Rect Bounds, Action Action, bool Enabled);
    private sealed record NumericField(Rect Bounds, string Label, double Value, Action<double> Apply, bool Timestamp, bool Preference = false);
    private float FullPlayfieldWidth => plot.Width * 512 / (512 + PlayfieldPadding * 2);
    private double MinimumCanvasZoom => IsLazerEditing ? .25 : Math.Min(1, MinimumPlayfieldWidth / Math.Max(1, FullPlayfieldWidth));
    private float PlayfieldScale => IsLazerEditing ? LazerCanvasScale : FullPlayfieldWidth * (float)canvasZoom / 512;
    private Rect Playfield
    {
        get
        {
            float margin = (plot.Width - 512 * PlayfieldScale) / 2;
            return new(plot.X + margin + (float)(IsLazerEditing ? lazerPanX : canvasPanX), plot.Y, 512 * PlayfieldScale, plot.Height);
        }
    }
    private TimelineTransform Transform => new(Playfield.X, plot.Bottom, Playfield.Width, PresentationViewStart, pixelsPerMs);
    private Fruit? SelectedFruit => Document.Fruits.FirstOrDefault(f => f.Id == selection);
    private CurveTrack? SelectedTrack => Document.Tracks.FirstOrDefault(t => t.Id == selectedTrack || t.Id == selection);
    private Anchor? SelectedAnchor => SelectedTrack?.Nodes.FirstOrDefault(n => n.Id == selection);
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Time(double value)
    {
        long milliseconds = (long)Math.Abs(value);
        return FormattableString.Invariant($"{(value < 0 ? "-" : "")}{milliseconds / 60000:00}:{milliseconds / 1000 % 60:00}:{milliseconds % 1000:000}");
    }
    private static MapPoint Point(Anchor node) => new(node.TimeMs, node.X);

    public void ResetDemo()
    {
        CancelInteraction();
        LoadDocument(DemoMap.Create());
        Select(Guid.Empty);
        tool = Tool.Select;
        ResetView();
        playhead = 1500;
        StatusMessage = L.Get("editor.status.demoReset");
    }

    private void ResetView()
    {
        pinPlayhead = true;
        canvasZoom = LibrarySettings.CanvasZoom;
        canvasTimeZoom = LibrarySettings.CanvasTimeZoom;
        canvasPanX = 0;
        viewStart = 0;
        if (Playfield.Width > 0) pixelsPerMs = CanvasPixelsPerMs;
        if (AudioPlaying) FollowPlayhead();
    }

    private void ClampView()
    {
        if (ViewportFrozenByDrag && !IsLazerEditing) return;
        if (IsLazerEditing || AudioPlaying || pinPlayhead) { FollowPlayhead(); return; }
        // Blank time before the start and after the end keeps the playback line fixed at both endpoints.
        double padding = plot.Height * EffectivePlaybackLineFromBottom / pixelsPerMs;
        viewStart = Math.Clamp(viewStart, -padding, Math.Max(-padding, TimelineDurationMs - padding));
        // Float pointer deltas can leave a sub-microsecond remainder when navigating back to zero.
        if (Math.Abs(viewStart) < 0.001) viewStart = 0;
    }

    private void ZoomCanvasAt(float y, double factor)
    {
        SetCanvasScales(canvasZoom * factor, CanvasTimeZoom * factor, y);
        SaveCanvasScale();
    }

    private void SetCanvasZoom(float x)
    {
        if (IsOsuCanvas)
        {
            double zoom = .25 + Math.Clamp((x - zoomSlider.X) / zoomSlider.Width, 0, 1) * 3.75;
            ZoomOsuCanvasAt(plot.X + plot.Width / 2, plot.Y + plot.Height / 2, zoom / osuCanvasZoom);
            return;
        }
        if (IsLazerEditing)
        {
            SetLazerCanvasZoom(MinimumCanvasZoom + (MaximumCanvasZoom - MinimumCanvasZoom)
                * Math.Clamp((x - zoomSlider.X) / zoomSlider.Width, 0, 1));
            return;
        }
        double scale = MinimumCanvasZoom + Math.Clamp((x - zoomSlider.X) / zoomSlider.Width, 0, 1) * (MaximumCanvasZoom - MinimumCanvasZoom);
        // The slider has no canvas pointer anchor: keep the viewport centre stable while paused.
        if (!AudioPlaying) pinPlayhead = false;
        SetCanvasScales(scale, UniformCanvasScale ? scale : canvasTimeZoom, plot.Y + plot.Height / 2);
        StatusMessage = L.Get("editor.status.canvasZoom", canvasZoom * 100);
    }

    private void CycleTickRate()
    {
        if (draftBanana != Guid.Empty) { StatusMessage = L.Get("editor.status.bananaNeedsEnd"); return; }
        if (draftTrack != Guid.Empty) { StatusMessage = L.Get("editor.status.finishBeforeTickRate"); return; }
        double[] rates = [1, 2, 3, 4, 6, 8];
        int index = Array.IndexOf(rates, Document.SliderTickRate);
        Edit(L.Get("editor.command.changeTickRate"), () => Document.SliderTickRate = rates[(index + 1) % rates.Length]);
        StatusMessage = L.Get("editor.status.tickRate", Number(Document.SliderTickRate), divisor);
    }

    private MapPoint MapAt(float x, float y, bool useSnap, bool clampX = true)
    {
        var p = Transform.ToMap(x, y);
        double time = useSnap && snap ? SnapTiming().Snap(p.TimeMs, divisor) : p.TimeMs;
        return new(Math.Clamp(time, 0, EditableDurationMs), clampX ? Math.Clamp(SnapX(p.X), 0, 512) : SnapX(p.X));
    }

    private (float X, float Y) Screen(MapPoint p)
    {
        var s = Transform.ToScreen(p);
        return ((float)s.X, (float)s.Y);
    }

    private void Select(Guid id, Guid track = default)
    {
        if (!CanSelectSlider(track != Guid.Empty ? track : id)) { id = Guid.Empty; track = Guid.Empty; }
        if (id != temporarySnapSource || track != Guid.Empty) RestoreTemporarySnap();
        soundEdge = null; distanceObject = null; clickedCoordinate = null; timelineFruit = null;
        objectSelection.Clear(); anchorSelection.Clear();
        if (Document.Tracks.FirstOrDefault(t => t.Id == track)?.Nodes.Any(n => n.Id == id) == true)
            anchorSelection.Add(id);
        else if (id != Guid.Empty) objectSelection.Add(id);
        selection = id;
        selectedTrack = track;
        selectedPart = DragKind.Anchor;
        editField = -1;
        fieldError = "";
    }

    private void ChangeTool(Tool next)
    {
        if (osuDraftSlider is not null || osuSpinnerStart is not null) CancelOsuInteraction();
        if (draftBanana != Guid.Empty)
        {
            history.Cancel();
            draftBanana = Guid.Empty;
            Select(Guid.Empty);
        }
        if (draftTrack != Guid.Empty) FinishCurve();
        if (draftTrack != Guid.Empty) CancelInteraction();
        tool = next;
        legacyDragStart = null;
        if (next != Tool.Select) Select(Guid.Empty);
        else if (objectSelection.Count == 0 && SelectedTrack is { } parent) SelectObjects([parent.Id]);
        menu = -1;
        contextItems.Clear();
        StatusMessage = "";
    }

    private void Undo()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag is DragKind.Objects or DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd or DragKind.Marquee)
        { CancelInteraction(); return; }
        if (!history.CanUndo) return;
        CancelInteraction();
        var before = Document;
        history.Undo();
        RestoreSelectionAfterHistory(before);
        StatusMessage = L.Get("editor.status.undone");
    }

    private void Redo()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag is DragKind.Objects or DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd or DragKind.Marquee)
        { CancelInteraction(); return; }
        if (!history.CanRedo) return;
        CancelInteraction();
        var before = Document;
        history.Redo();
        RestoreSelectionAfterHistory(before);
        StatusMessage = L.Get("editor.status.redone");
    }

    private void RestoreSelectionAfterHistory(MapDocument before)
    {
        var unchanged = before.UnchangedObjectIds(Document).ToHashSet();
        RevealHistoryChange(before, unchanged);
        if (selectedTrack != Guid.Empty && unchanged.Contains(selectedTrack) && objectSelection.Count == 0
            && SelectedTrack is { } track)
        {
            SelectAnchors(track, anchorSelection, selection);
            return;
        }
        SelectObjects(objectSelection.Where(unchanged.Contains), selection);
    }

    private bool Edit(string label, Action change, Action<bool, MapDocument, MapDocument>? restoreRelated = null)
    {
        history.Begin(label, restoreRelated);
        try
        {
            var before = notesLocked ? Document.DeepClone() : null;
            var readOnlyIds = (IsOsuCanvas ? Document.Tracks.Select(t => t.Id) : Document.ImportedSliders.Select(s => s.Id)).ToHashSet();
            int importedBefore = Document.ImportedSliders.Count;
            change();
            if (!osuAllowSliderConversion && history.TransactionChangesSliderGeometry(readOnlyIds))
            { history.Cancel(); ShowEditorNotice("osu.readOnlySlider"); return false; }
            if (before is not null && !PositionsEqual(before, Document))
            { history.Cancel(); StatusMessage = L.Get("assist.locked"); return false; }
            if (Document.DerandomizeDroplets is null && Document.ImportedSliders.Count < importedBefore)
                Document.DerandomizeDroplets = Document.DerandomizeFSliderDroplets;
            CommitContentHistory(); return true;
        }
        catch (ArgumentException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        catch (InvalidOperationException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        catch (InvalidDataException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        return false;
    }

    private void DeleteSelection()
    {
        if (LegacyMode && anchorSelection.Count > 0) { DeleteLegacyPoints(); return; }
        if (anchorSelection.Count > 0) DeleteSelectedAnchors();
        else DeleteSelectedObjects();
    }

    private void SplitSelected()
    {
        if (draftTrack != Guid.Empty) { StatusMessage = L.Get("editor.status.finishCurve"); return; }
        if (SelectedTrack is not { } track || track.Nodes.Count < 2) return;
        int segment = SelectedAnchor is { } node ? Math.Min(track.Nodes.IndexOf(node), track.Nodes.Count - 2) : 0;
        if (!Edit(L.Get("editor.command.splitCurve"), () => CurveMath.Split(track, segment, 0.5))) return;
        Select(track.Nodes[segment + 1].Id, track.Id);
        tool = Tool.Select;
        StatusMessage = L.Get("editor.status.curveSplit");
    }

    private void FinishCurve()
    {
        if (draftTrack == Guid.Empty) return;
        var track = Document.Tracks.First(t => t.Id == draftTrack);
        if (LegacyMode && legacyDraft is not null && !legacyPreviewValid) { StatusMessage = L.Get("editor.status.needTwoAnchors"); return; }
        if (track.Nodes.Count < 2) { StatusMessage = L.Get("editor.status.needTwoAnchors"); return; }
        var head = track.Nodes[0];
        if (!LibrarySettings.PreserveSameTimeObjects)
            Document.Fruits.RemoveAll(fruit => fruit.TimeMs == head.TimeMs && fruit.X == head.X);
        Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
        // Completion can append an endpoint after the last render; retain that final draft while validation runs.
        UpdateDraftConversion();
        CommitContentHistory();
        EndDraftConversion(cancelled: false);
        legacyDraft = null; legacyPreviewVertices = null;
        draftTrack = Guid.Empty;
        drag = DragKind.None;
        dragFruits.Clear(); dragTracks.Clear(); dragBananas.Clear();
        tool = Tool.Slider;
        Select(Guid.Empty);
        StatusMessage = L.Get("editor.status.sliderFinished");
    }

    private void FinishForSelection()
    {
        if (draftBanana != Guid.Empty)
        {
            history.Cancel();
            draftBanana = Guid.Empty;
            Select(Guid.Empty);
        }
        if (draftTrack != Guid.Empty)
        {
            if (Document.Tracks.First(t => t.Id == draftTrack).Nodes.Count < 2) CancelInteraction();
            else FinishCurve();
        }
        tool = Tool.Select;
        if (anchorSelection.Count > 0 && SelectedTrack is { } track) SelectObjects([track.Id]);
    }
}
