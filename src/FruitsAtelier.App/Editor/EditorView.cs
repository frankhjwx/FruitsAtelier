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
    private readonly EditorHistory history = new(DemoMap.Create());
    private readonly List<HitArea> hits = [];
    private readonly List<NumericField> fields = [];
    private readonly List<(Rect Bounds, Guid Id, Guid Track)> rows = [];
    private float width, height, mouseX = -1, mouseY = -1, listScroll;
    private Rect canvas, plot, leftPanel, rightPanel, overview, listBounds, snapSlider, zoomSlider;
    private double viewStart, pixelsPerMs = 0.09, playhead = 1500;
    private bool useArScale = true;
    private CatchSkin? skin;
    private bool compensateTinyDroplets = true;
    private MapDocument? convertedSnapshot;
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
    private static readonly int[] SnapDivisors = [4, 5, 6, 7, 8, 9, 12, 16];
    // Keep edge room stable while CS is edited; 54.4 is the CS=0 fruit radius.
    private const float PlayfieldPadding = 54.4f;
    private int divisor = 4, menu = -1, editField = -1;
    private string editBuffer = "", fieldError = "";
    private bool replaceText = true, showTargets = true, showPreviewCurves;

    public Action? RequestClose { get; set; }
    public Action? RequestResetDemo { get; set; }
    public Action? RequestLoadSkin { get; set; }
    public bool IsDirty => history.IsDirty;
    public bool IsEditingText => editField >= 0;
    public string RendererStatusKey { get; set; } = "ui.renderStatus";
    public bool WantsCapture => drag != DragKind.None;
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
        if (CatchSkin.TryLoad(folder, out var loaded, out string message)) skin = loaded;
        StatusMessage = message;
    }

    private void EnsureConversion()
    {
        if (convertedSnapshot is not null && convertedSnapshot.ContentEquals(Document)
            && convertedWithCompensation == compensateTinyDroplets) return;
        convertedSnapshot = Document.DeepClone();
        convertedWithCompensation = compensateTinyDroplets;
        var input = convertedSnapshot.DeepClone();
        input.Tracks.RemoveAll(t => t.Nodes.Count < 2);
        // Nested slider fruits inherit their parent's full-map visual index.
        skinIndices = input.Fruits.Select(f => (f.Id, Time: f.TimeMs, f.SourceOrder))
            .Concat(input.Tracks.Select(t => (t.Id, Time: t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(input.ImportedSliders.Select(t => (t.Id, Time: t.TimeMs, t.SourceOrder)))
            .Concat(input.BananaShowers.Select(t => (t.Id, Time: t.TimeMs, t.SourceOrder)))
            .OrderBy(source => source.Time).ThenBy(source => source.SourceOrder)
            .Select((source, index) => (source.Id, Index: index))
            .ToDictionary(source => source.Id, source => source.Index);
        conversion = CatchStreamConverter.Convert(input, compensateTinyDroplets);
        hyperdashObjects = HyperDashCalculator.GetHyperDashStarts(conversion.Objects, Document.CircleSize);
    }

    private enum Tool { Select, Fruit, Slider, Banana }
    private enum DragKind { None, Objects, Anchor, HandleIn, HandleOut, DraftHandle, BananaStart, BananaEnd, Pan, Timeline, Marquee, SnapDivisor, TimeZoom }
    private sealed record HitArea(Rect Bounds, Action Action, bool Enabled);
    private sealed record NumericField(Rect Bounds, string Label, double Value, Action<double> Apply);
    private float PlayfieldScale => plot.Width / (512 + PlayfieldPadding * 2);
    private Rect Playfield
    {
        get
        {
            float margin = PlayfieldPadding * PlayfieldScale;
            return new(plot.X + margin, plot.Y, 512 * PlayfieldScale, plot.Height);
        }
    }
    private TimelineTransform Transform => new(Playfield.X, plot.Bottom, Playfield.Width, viewStart, pixelsPerMs);
    private Fruit? SelectedFruit => Document.Fruits.FirstOrDefault(f => f.Id == selection);
    private CurveTrack? SelectedTrack => Document.Tracks.FirstOrDefault(t => t.Id == selectedTrack || t.Id == selection);
    private Anchor? SelectedAnchor => SelectedTrack?.Nodes.FirstOrDefault(n => n.Id == selection);
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Time(double value) => $"{(int)value / 60000:00}:{value / 1000 % 60:00.000}";
    private static MapPoint Point(Anchor node) => new(node.TimeMs, node.X);

    public void ResetDemo()
    {
        CancelInteraction();
        history.Reset(DemoMap.Create());
        Select(Guid.Empty);
        tool = Tool.Select;
        ResetView();
        playhead = 1500;
        listScroll = 0;
        StatusMessage = L.Get("editor.status.demoReset");
    }

    private void ResetView()
    {
        pinPlayhead = true;
        useArScale = true;
        viewStart = 0;
        if (Playfield.Width > 0) pixelsPerMs = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, Playfield.Width);
        if (AudioPlaying) FollowPlayhead();
    }

    private void ClampView()
    {
        if (drag is DragKind.Marquee or DragKind.Objects or DragKind.BananaStart or DragKind.BananaEnd) return;
        if (AudioPlaying || pinPlayhead) { FollowPlayhead(); return; }
        // Blank time before the start and after the end keeps the playback line fixed at both endpoints.
        double padding = plot.Height * playbackLineFromBottom / pixelsPerMs;
        viewStart = Math.Clamp(viewStart, -padding, Math.Max(-padding, TimelineDurationMs - padding));
    }

    private void ZoomTimeAt(float y, double factor)
    {
        useArScale = false;
        var transform = Transform;
        transform.ZoomAt(Math.Clamp(y, plot.Y, plot.Bottom), factor);
        viewStart = transform.ViewStartMs;
        pixelsPerMs = transform.PixelsPerMs;
        ClampView();
    }

    private double DisplayApproachRate
    {
        get
        {
            double preempt = CatchScrollTiming.FallDistance / pixelsPerMs * (Playfield.Width / 512);
            return preempt >= 1200 ? 5 - (preempt - 1200) / 120 : 5 + (1200 - preempt) / 150;
        }
    }

    private void SetTimeZoom(float x)
    {
        double ar = Math.Round(Math.Clamp((x - zoomSlider.X) / zoomSlider.Width, 0, 1) * 10, 1);
        double scale = CatchScrollTiming.PixelsPerMs(ar, Playfield.Width);
        // The slider has no canvas pointer anchor: keep the viewport centre stable while paused.
        ZoomTimeAt(plot.Y + plot.Height / 2, scale / pixelsPerMs);
        StatusMessage = L.Get("editor.status.timeZoom", DisplayApproachRate);
    }

    private void RestoreArScale()
    {
        useArScale = true;
        pixelsPerMs = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, Playfield.Width);
        FollowPlayhead();
        StatusMessage = L.Get("editor.status.arScale", Number(Document.ApproachRate), Number(CatchScrollTiming.PreemptMs(Document.ApproachRate)));
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
        return new(Math.Clamp(time, 0, EditableDurationMs), Math.Clamp(p.X, 0, 512));
    }

    private (float X, float Y) Screen(MapPoint p)
    {
        var s = Transform.ToScreen(p);
        return ((float)s.X, (float)s.Y);
    }

    private void Select(Guid id, Guid track = default)
    {
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
        if (next == Tool.Slider)
        {
            if (SelectedImportedSlider is not null) EditImportedSlider();
            if (SelectedTrack is { } track) SelectAnchors(track, anchorSelection.ToArray());
            else Select(Guid.Empty);
        }
        else if (anchorSelection.Count > 0 && SelectedTrack is { } parent) SelectObjects([parent.Id]);
        menu = -1;
        contextItems.Clear();
        StatusMessage = next switch
        {
            Tool.Fruit => L.Get("editor.help.fruit"),
            Tool.Slider => SelectedTrack is null ? L.Get("editor.help.slider") : L.Get("editor.help.anchors"),
            Tool.Banana => L.Get("editor.help.banana"),
            _ => L.Get("editor.help.select")
        };
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
        try { change(); history.Commit(); return true; }
        catch (ArgumentException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        catch (InvalidOperationException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        catch (InvalidDataException ex) { history.Cancel(); StatusMessage = fieldError = ex.Message; }
        return false;
    }

    private void DeleteSelection()
    {
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
        if (track.Nodes.Count < 2) { StatusMessage = L.Get("editor.status.needTwoAnchors"); return; }
        Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
        history.Commit();
        draftTrack = Guid.Empty;
        drag = DragKind.None;
        tool = Tool.Select;
        SelectObjects([track.Id]);
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
