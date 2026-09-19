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
        public string Name { get; } = difficulty.Name;
        public EditorHistory History { get; } = new(difficulty.Document);
        public double Playhead, ViewStart;
        public MapDocument? RatingSnapshot;
        public double? Stars;
        public bool RatingCompensation, RatingFailed;
        public Task<double?>? RatingTask;
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
    private Rect canvas, plot, rightPanel, overview, snapSlider, zoomSlider;
    private double viewStart, pixelsPerMs = 0.09, playhead = 1500;
    private double canvasZoom = 1;
    public const float MinimumPlayfieldWidth = 256;
    public double CanvasZoom => canvasZoom;
    private CatchSkin? skin;
    private bool compensateTinyDroplets = true;
    private MapDocument? convertedSnapshot;
    private CatchConversionCache editorConversionCache = new();
    private CatchConversionResult? conversion;
    private bool convertedWithCompensation;
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
    private bool anchorSnap;
    public bool AnchorSnapEnabled => anchorSnap;
    private static readonly int[] SnapDivisors = [1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16];
    // Keep edge room stable while CS is edited; 54.4 is the CS=0 fruit radius.
    private const float PlayfieldPadding = 54.4f;
    private int divisor = 4, menu = -1, editField = -1;
    private string editBuffer = "", fieldError = "";
    private bool replaceText = true, showTargets = true, showPreviewCurves;

    public Action? RequestClose { get; set; }
    public Action? RequestResetDemo { get; set; }
    public Action? RequestLoadSkin { get; set; }
    public bool IsDirty => projectStructureDirty || difficulties.Any(d => d.History.IsDirty);
    public bool IsEditingText => TimeJumpVisible || editField >= 0 || (LibraryVisible || ExportVisible) && libraryField >= 0;
    public bool WantsCapture => drag != DragKind.None || libraryPointerActive || tabPointer;
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
    public void SetNotice(string notice) => StatusMessage = notice;

    public void LoadSkin(string folder)
    {
        if (CatchSkin.TryLoad(folder, out var loaded, out string message, defaultSkin, allowEmpty: true)) skin = loaded;
        StatusMessage = message;
    }

    private TimingMap.Lookup? renderedTiming;
    private void EnsureConversion()
    {
        if (convertedSnapshot is not null && convertedSnapshot.ContentEquals(Document)
            && convertedWithCompensation == compensateTinyDroplets) return;
        convertedSnapshot = Document.DeepClone();
        renderedTiming = new TimingMap.Lookup(Document);
        convertedWithCompensation = compensateTinyDroplets;
        var input = Document;
        if (input.Tracks.Any(t => t.Nodes.Count < 2))
        {
            input = Document.DeepClone(); input.Tracks.RemoveAll(t => t.Nodes.Count < 2);
        }
        // Nested slider fruits inherit their parent's full-map visual index.
        skinIndices = input.Fruits.Select(f => (f.Id, Time: f.TimeMs, f.SourceOrder))
            .Concat(input.Tracks.Select(t => (t.Id, Time: t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(input.ImportedSliders.Select(t => (t.Id, Time: t.TimeMs, t.SourceOrder)))
            .Concat(input.BananaShowers.Select(t => (t.Id, Time: t.TimeMs, t.SourceOrder)))
            .OrderBy(source => source.Time).ThenBy(source => source.SourceOrder)
            .Select((source, index) => (source.Id, Index: index))
            .ToDictionary(source => source.Id, source => source.Index);
        conversion = CatchStreamConverter.Convert(input, compensateTinyDroplets, editorConversionCache);
        BuildComboColours();
        hyperdashObjects = HyperDashCalculator.GetHyperDashStarts(conversion.Objects, Document.CircleSize);
    }

    private enum Tool { Select, Fruit, Slider, Banana }
    private enum DragKind { None, Objects, Anchor, HandleIn, HandleOut, DraftHandle, BananaStart, BananaEnd, Pan, Timeline, Marquee, SnapDivisor, DistanceSpacing, CanvasZoom, LegacyControl, TimelineTail, PreviewResize }
    private sealed record HitArea(Rect Bounds, Action Action, bool Enabled);
    private sealed record NumericField(Rect Bounds, string Label, double Value, Action<double> Apply, bool Timestamp);
    private float FullPlayfieldWidth => plot.Width * 512 / (512 + PlayfieldPadding * 2);
    private double MinimumCanvasZoom => Math.Min(1, MinimumPlayfieldWidth / Math.Max(1, FullPlayfieldWidth));
    private float PlayfieldScale => FullPlayfieldWidth * (float)canvasZoom / 512;
    private Rect Playfield
    {
        get
        {
            float margin = (plot.Width - 512 * PlayfieldScale) / 2;
            return new(plot.X + margin, plot.Y, 512 * PlayfieldScale, plot.Height);
        }
    }
    private TimelineTransform Transform => new(Playfield.X, plot.Bottom, Playfield.Width, viewStart, pixelsPerMs);
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
        canvasZoom = 1;
        viewStart = 0;
        if (Playfield.Width > 0) pixelsPerMs = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, Playfield.Width);
        if (AudioPlaying) FollowPlayhead();
    }

    private void ClampView()
    {
        if (ViewportFrozenByDrag) return;
        if (AudioPlaying || pinPlayhead) { FollowPlayhead(); return; }
        // Blank time before the start and after the end keeps the playback line fixed at both endpoints.
        double padding = plot.Height * playbackLineFromBottom / pixelsPerMs;
        viewStart = Math.Clamp(viewStart, -padding, Math.Max(-padding, TimelineDurationMs - padding));
        // Float pointer deltas can leave a sub-microsecond remainder when navigating back to zero.
        if (Math.Abs(viewStart) < 0.001) viewStart = 0;
    }

    private void ZoomCanvasAt(float y, double factor)
    {
        y = Math.Clamp(y, plot.Y, plot.Bottom);
        double anchorTime = Transform.ToMap(Playfield.X, y).TimeMs;
        canvasZoom = Math.Clamp(canvasZoom * factor, MinimumCanvasZoom, 1);
        pixelsPerMs = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, Playfield.Width);
        viewStart = anchorTime - (plot.Bottom - y) / pixelsPerMs;
        ClampView();
    }

    private void SetCanvasZoom(float x)
    {
        double scale = MinimumCanvasZoom + Math.Clamp((x - zoomSlider.X) / zoomSlider.Width, 0, 1) * (1 - MinimumCanvasZoom);
        // The slider has no canvas pointer anchor: keep the viewport centre stable while paused.
        if (!AudioPlaying) pinPlayhead = false;
        ZoomCanvasAt(plot.Y + plot.Height / 2, scale / canvasZoom);
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

    private MapPoint MapAt(float x, float y, bool useSnap)
    {
        var p = Transform.ToMap(x, y);
        double time = useSnap && snap ? TimingMap.Snap(Document, p.TimeMs, divisor) : p.TimeMs;
        return new(Math.Clamp(time, 0, EditableDurationMs), Math.Clamp(SnapX(p.X), 0, 512));
    }

    private (float X, float Y) Screen(MapPoint p)
    {
        var s = Transform.ToScreen(p);
        return ((float)s.X, (float)s.Y);
    }

    private void Select(Guid id, Guid track = default)
    {
        soundEdge = null;
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
        if (draftBanana != Guid.Empty)
        {
            history.Cancel();
            draftBanana = Guid.Empty;
            Select(Guid.Empty);
        }
        if (draftTrack != Guid.Empty && next == Tool.Slider) return;
        if (draftTrack != Guid.Empty) FinishCurve();
        if (draftTrack != Guid.Empty) return;
        tool = next;
        legacyDragStart = null;
        if (next == Tool.Slider)
        {
            if (SelectedImportedSlider is not null) EditImportedSlider();
            if (SelectedTrack is { } track) SelectAnchors(track, anchorSelection.ToArray());
            else Select(Guid.Empty);
        }
        else if (anchorSelection.Count > 0 && SelectedTrack is { } parent) SelectObjects([parent.Id]);
        menu = -1;
        contextItems.Clear();
        StatusMessage = "";
    }

    private void Undo()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag is DragKind.Objects or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd or DragKind.Marquee)
        { CancelInteraction(); return; }
        CancelInteraction();
        history.Undo();
        Select(Guid.Empty);
        StatusMessage = L.Get("editor.status.undone");
    }

    private void Redo()
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag is DragKind.Objects or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd or DragKind.Marquee)
        { CancelInteraction(); return; }
        CancelInteraction();
        history.Redo();
        Select(Guid.Empty);
        StatusMessage = L.Get("editor.status.redone");
    }

    private bool Edit(string label, Action change)
    {
        history.Begin(label);
        try
        {
            var before = notesLocked ? Document.DeepClone() : null;
            change();
            if (before is not null && !PositionsEqual(before, Document))
            { history.Cancel(); StatusMessage = L.Get("assist.locked"); return false; }
            history.Commit(); return true;
        }
        catch (ArgumentException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        catch (InvalidOperationException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        catch (InvalidDataException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        return false;
    }

    private void DeleteSelection()
    {
        if (LegacyMode && tool == Tool.Slider) { DeleteLegacyPoints(); return; }
        if (tool == Tool.Slider) DeleteSelectedAnchors();
        else DeleteSelectedObjects();
    }

    private void SplitSelected()
    {
        if (draftTrack != Guid.Empty) { StatusMessage = L.Get("editor.status.finishCurve"); return; }
        if (SelectedTrack is not { } track || track.Nodes.Count < 2) return;
        int segment = SelectedAnchor is { } node ? Math.Min(track.Nodes.IndexOf(node), track.Nodes.Count - 2) : 0;
        if (!Edit(L.Get("editor.command.splitCurve"), () => CurveMath.Split(track, segment, 0.5))) return;
        Select(track.Nodes[segment + 1].Id, track.Id);
        tool = Tool.Slider;
        StatusMessage = L.Get("editor.status.curveSplit");
    }

    private void FinishCurve()
    {
        if (draftTrack == Guid.Empty) return;
        var track = Document.Tracks.First(t => t.Id == draftTrack);
        if (LegacyMode && legacyDraft is not null && !legacyPreviewValid) { StatusMessage = L.Get("editor.status.needTwoAnchors"); return; }
        if (track.Nodes.Count < 2) { StatusMessage = L.Get("editor.status.needTwoAnchors"); return; }
        Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
        history.Commit();
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
