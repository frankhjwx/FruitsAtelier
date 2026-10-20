using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool IsOsuCanvas { get; private set; }
    private readonly HashSet<int> osuToggleKeys = [];
    private ImportedSlider? osuDraftSlider;
    private (double X, double Y)? osuDraftPointer;
    private Guid osuControlSlider;
    private int osuSelectedControl = -1;
    private MapDocument? osuDragStart;
    private bool osuDragMoved;
    private (double X, double Y) osuDragOrigin;
    private bool osuAllowSliderConversion;
    private double? osuSpinnerStart;
    private OsuCanvasObject[]? osuDistanceSource;
    private (double Time, double X, double Y, double BeatLength, double Sv)[] osuDistanceReferences = [];
    private bool osuSpacingDragging, osuDefaultSvDragging;
    private double osuDefaultSvStart;
    private bool osuPanning;
    private (float X, float Y) osuPanPointer;
    private (double X, double Y) osuPanOrigin;
    private bool osuMarquee;
    private bool osuMarqueeAdditive;
    private (float X, float Y) osuMarqueeStart;
    private Rect osuMarqueeBounds;

    private void EnsureOsuDistanceReferences()
    {
        EnsureOsuCanvasObjects();
        if (ReferenceEquals(osuDistanceSource, osuCanvasObjects)) return;
        var timing = SnapTiming();
        var imported = Document.ImportedSliders.ToDictionary(s => s.Id);
        var generated = conversion?.Sliders.ToDictionary(s => s.SourceId);
        osuDistanceReferences = osuCanvasObjects.Where(o => !o.Spinner).Select(o =>
        {
            var state = timing.At(o.Start);
            double x = o.X, y = o.Y;
            if (o.Path.Count > 0 && o.Spans % 2 == 1) { x = o.Path[^1].X; y = o.Path[^1].GeometryY; }
            double sv = state.SliderVelocityMultiplier;
            if (imported.TryGetValue(o.Id, out var slider))
                sv = !osuShowProcessedSliders && slider.SliderVelocityMultiplier is { } authored
                    ? authored * Document.SliderMultiplier / Document.EffectiveSliderMultiplier
                    : ImportedSliderConverter.SliderVelocityAt(Document, slider, timing);
            else if (generated?.TryGetValue(o.Id, out var path) == true) sv = path.SliderVelocityMultiplier;
            o.DistanceVelocity = 100 * Document.EffectiveSliderMultiplier * sv / state.BeatLengthMs;
            return (Time: o.End, X: x, Y: y, BeatLength: state.BeatLengthMs, Sv: sv);
        }).OrderBy(o => o.Time).ToArray();
        osuDistanceSource = osuCanvasObjects;
    }

    private bool IsReadOnlySlider(Guid id) => IsOsuCanvas
        ? Document.Tracks.Any(t => t.Id == id)
        : Document.ImportedSliders.Any(s => s.Id == id);

    private bool RejectReadOnlySelection()
    {
        if (!ClipboardSelectedParentIds().Any(IsReadOnlySlider)) return false;
        ShowEditorNotice("osu.readOnlySlider");
        return true;
    }

    public void ToggleOsuCanvas()
    {
        CancelInteraction();
        IsOsuCanvas = !IsOsuCanvas;
        Select(Guid.Empty);
        StatusMessage = L.Get(IsOsuCanvas ? "osu.canvasActive" : "osu.catchCanvasActive");
    }

    private bool OsuToggleKey(int key, bool ctrl, bool shift)
    {
        if (key != 9 || ctrl || shift || altHeld || TimingPageVisible
            || menu >= 0 || contextItems.Count > 0 || editField >= 0) return false;
        if (osuToggleKeys.Add(key)) ToggleOsuCanvas();
        return true;
    }

    private (double X, double Y) OsuInputPoint(float x, float y, bool useDistance = false, bool? shift = null, bool useObjects = false)
    {
        var p = OsuCanvasPoint(x, y);
        bool useGrid = gridSnap ^ (shift ?? shiftHeld);
        double px = useGrid ? Math.Round(p.X / gridSize, MidpointRounding.AwayFromZero) * gridSize : Math.Round(p.X);
        double py = useGrid ? Math.Round(p.Y / gridSize, MidpointRounding.AwayFromZero) * gridSize : Math.Round(p.Y);
        bool distanceSnapped = false;
        if (useDistance && distanceSnap != altHeld)
        {
            EnsureOsuDistanceReferences();
            int low = 0, high = osuDistanceReferences.Length;
            while (low < high) { int middle = low + (high - low) / 2; if (osuDistanceReferences[middle].Time < OsuPlacementTime) low = middle + 1; else high = middle; }
            if (low > 0)
            {
                var previous = osuDistanceReferences[low - 1];
                double distance = (OsuPlacementTime - previous.Time) / previous.BeatLength * 100
                    * Document.EffectiveSliderMultiplier * previous.Sv * Document.OsuDistanceSpacing;
                double dx = px - previous.X, dy = py - previous.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length > .001) { px = previous.X + dx / length * distance; py = previous.Y + dy / length * distance; distanceSnapped = true; }
            }
        }
        var result = (X: Math.Clamp(Math.Round(px), 0, 512), Y: Math.Clamp(Math.Round(py), 0, 384));
        return useObjects && EditingPreferences.ObjectSnap ? SnapOsuObjectPoint(p, result, OsuObjectSnapTargets(), null, distanceSnapped) : result;
    }

    private double OsuPlacementTime => Math.Max(0, snap ? SnapTiming().Snap(playhead, divisor) : playhead);

    private bool OsuPointerDown(float x, float y, int button, bool shift, bool ctrl)
    {
        if (!IsOsuCanvas || TimingPageVisible || !plot.Contains(x, y) && !OsuSpacingBounds.Contains(x, y) && !OsuDefaultSvBounds.Contains(x, y)) return false;
        if (notesLockFlyout && notesLockFlyoutBounds.Contains(x, y)) return false;
        if (distanceSnapFlyout && distanceSnapFlyoutBounds.Contains(x, y)) return false;
        if (menu >= 0) return false;
        if (contextItems.Count > 0)
        {
            if (button == 2) { contextItems.Clear(); return true; }
            return false;
        }
        if (button == 0 && OsuSliderDefaultSvValueBounds.Contains(x, y))
        {
            int index = fields.FindIndex(f => f.Preference && f.Label == L.Get("osu.sliderDefaultSv"));
            if (index < 0) return true;
            if (editField != index)
            {
                if (editField >= 0 && !CommitField()) return true;
                FocusField(index);
            }
            FocusInput("numeric:" + index, editBuffer, x);
            return true;
        }
        if (editField >= 0 && !CommitField()) return true;
        if (OsuDefaultSvBounds.Contains(x, y))
        {
            if (button == 0)
            {
                osuDefaultSvStart = LibrarySettings.OsuSliderDefaultSv;
                osuDefaultSvDragging = true; drag = DragKind.OsuDefaultSv;
                SetOsuDefaultSv(x, shift);
            }
            return true;
        }
        if (OsuSpacingBounds.Contains(x, y))
        {
            if (button == 0)
            {
                history.Begin(L.Get("osu.distanceSpacing")); osuSpacingDragging = true; drag = DragKind.SnapDivisor;
                SetOsuSpacing(x, shift);
            }
            return true;
        }
        if (!plot.Contains(x, y)) return false;
        if (button == 1)
        {
            osuPanning = true; osuPanPointer = (x, y); osuPanOrigin = (osuCanvasPanX, osuCanvasPanY);
            drag = DragKind.Pan; return true;
        }
        var point = OsuInputPoint(x, y, tool == Tool.Fruit || tool == Tool.Slider && osuDraftSlider is null, shift, useObjects: tool is Tool.Fruit or Tool.Slider || ctrl);
        if (button == 2)
        {
            if (osuDraftSlider is not null) { osuDraftPointer = point; FinishOsuSlider(); return true; }
            if (osuSpinnerStart is { } start)
            {
                double end = OsuPlacementTime;
                if (end > start) Edit(L.Get("editor.command.drawBanana"), () =>
                {
                    RemovePlacementConflicts(Document, start);
                    Document.BananaShowers.Add(new() { TimeMs = start, EndTimeMs = end });
                });
                osuSpinnerStart = null; return true;
            }
            if (SelectedImportedSlider is { } selected && HitOsuControl(selected, x, y) is int index)
            { DeleteOsuControl(selected, index); return true; }
            if (OsuCanvasHit(x, y) is { } id)
            {
                SelectObjects([id], id);
                if (IsReadOnlySlider(id))
                {
                    contextItems.Clear();
                    contextItems.Add(new(L.Get("osu.readOnlySlider"), () => { }, Enabled: false));
                    contextItems.Add(new(L.Get("osu.convertLegacy"), () => ConvertSelectedSliderRepresentation(id)));
                    contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 320)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 320, ContextMenuHeight);
                }
                else DeleteSelectedObjects();
            }
            return true;
        }
        if (button != 0) return true;
        if (notesLocked && tool != Tool.Select) { StatusMessage = L.Get("assist.locked"); return true; }
        if (tool == Tool.Fruit)
        {
            var fruit = new Fruit { TimeMs = OsuPlacementTime, X = point.X, Y = point.Y };
            if (Edit(L.Get("editor.command.addFruit"), () =>
            {
                RemovePlacementConflicts(Document, fruit.TimeMs);
                Document.Fruits.Add(fruit); ApplyPlacementFlags(fruit.Id);
                Document.DurationMs = Math.Max(Document.DurationMs, fruit.TimeMs);
            })) SelectObjects([fruit.Id], fruit.Id);
            return true;
        }
        if (tool == Tool.Banana)
        {
            osuSpinnerStart ??= OsuPlacementTime;
            StatusMessage = L.Get("osu.spinnerEnd"); return true;
        }
        if (tool == Tool.Slider)
        {
            osuDraftSlider ??= new ImportedSlider { TimeMs = OsuPlacementTime, X = point.X, Y = point.Y, PreserveNativeGeometry = true, SliderVelocityMultiplier = LibrarySettings.OsuSliderDefaultSv };
            osuDraftSlider.ControlPoints.Add(new(point.X, point.Y));
            osuDraftPointer = point;
            StatusMessage = L.Get("osu.sliderDraft"); return true;
        }
        if (SelectedImportedSlider is { } slider && HitOsuControl(slider, x, y) is int control)
        {
            osuControlSlider = slider.Id; osuSelectedControl = control;
            if (ctrl)
            {
                MakeOsuControlRed(slider, control);
            }
            else BeginOsuDrag(point);
            return true;
        }
        if (ctrl && SelectedImportedSlider is { } insert)
        {
            int index = NearestOsuSegment(OsuControlGeometry(insert), point);
            if (Edit(L.Get("editor.command.insertPoint"), () =>
            {
                RestoreOsuControlGeometry(insert);
                insert.ControlPoints.Insert(index + 1, new(point.X, point.Y));
                OsuRecalculateLength(insert, controlCountChanged: true); OsuSyncSliderLine(insert);
            })) { osuControlSlider = insert.Id; osuSelectedControl = index + 1; }
            return true;
        }
        osuControlSlider = Guid.Empty; osuSelectedControl = -1;
        if (OsuCanvasHit(x, y) is not { } hit)
        {
            if (!ctrl) Select(Guid.Empty);
            osuMarquee = true; osuMarqueeAdditive = ctrl; osuMarqueeStart = (x, y);
            osuMarqueeBounds = new(x, y, 0, 0); drag = DragKind.Marquee; return true;
        }
        PickObject(hit, ctrl);
        if (IsReadOnlySlider(hit)) ShowEditorNotice("osu.readOnlySlider");
        else if (!ctrl) BeginOsuDrag(point);
        return true;
    }

    private ImportedSlider OsuControlGeometry(ImportedSlider slider) =>
        !osuShowProcessedSliders ? slider.RandomizationSource ?? slider : slider;

    private void RestoreOsuControlGeometry(ImportedSlider slider, ImportedSlider? source = null)
    {
        source ??= OsuControlGeometry(slider);
        if (ReferenceEquals(source, slider)) return;
        // Controls belong to the displayed path; materialized FX may have a different polygon.
        slider.X = source.X; slider.Y = source.Y;
        slider.PathType = source.PathType; slider.PixelLength = source.PixelLength;
        slider.ControlPoints.Clear(); slider.ControlPoints.AddRange(source.ControlPoints);
    }

    private int? HitOsuControl(ImportedSlider slider, float x, float y)
    {
        slider = OsuControlGeometry(slider);
        for (int i = slider.ControlPoints.Count - 1; i >= 0; i--)
        {
            var p = OsuCanvasScreen(slider.ControlPoints[i].X, slider.ControlPoints[i].GeometryY);
            if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) <= 81) return i;
        }
        return null;
    }

    private static int NearestOsuSegment(ImportedSlider slider, (double X, double Y) p)
    {
        int best = Math.Max(0, slider.ControlPoints.Count - 2); double distance = double.PositiveInfinity;
        for (int i = 0; i < slider.ControlPoints.Count - 1; i++)
        {
            var a = slider.ControlPoints[i]; var b = slider.ControlPoints[i + 1];
            double dx = b.X - a.X, dy = b.GeometryY - a.GeometryY;
            double t = dx * dx + dy * dy == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.GeometryY) * dy) / (dx * dx + dy * dy), 0, 1);
            double d = Math.Pow(p.X - a.X - dx * t, 2) + Math.Pow(p.Y - a.GeometryY - dy * t, 2);
            if (d < distance) { distance = d; best = i; }
        }
        return best;
    }

    private void BeginOsuDrag((double X, double Y) point)
    {
        if (notesLocked || RejectReadOnlySelection()) return;
        history.Begin(L.Get("editor.command.moveObjects"));
        BeginOsuDragSnap();
        osuDragStart = Document.DeepClone(); osuDragOrigin = point;
        osuDragMoved = false;
        drag = DragKind.Objects;
    }

    private bool OsuPointerMove(float x, float y, bool shift = false)
    {
        if (!IsOsuCanvas) return false;
        if (osuPanning)
        { osuCanvasPanX = osuPanOrigin.X + x - osuPanPointer.X; osuCanvasPanY = osuPanOrigin.Y + y - osuPanPointer.Y; return true; }
        if (osuMarquee)
        {
            osuMarqueeBounds = new(Math.Min(x, osuMarqueeStart.X), Math.Min(y, osuMarqueeStart.Y), Math.Abs(x - osuMarqueeStart.X), Math.Abs(y - osuMarqueeStart.Y));
            return true;
        }
        if (osuDefaultSvDragging) { SetOsuDefaultSv(x, shift); return true; }
        if (osuSpacingDragging) { SetOsuSpacing(x, shift); return true; }
        if (osuDraftSlider is not null) osuDraftPointer = OsuInputPoint(x, y, shift: shift, useObjects: true);
        if (osuDragStart is null) return false;
        var point = OsuInputPoint(x, y, shift: shift);
        if (point == osuDragOrigin && !osuDragMoved) return true;
        if (point == osuDragOrigin)
        {
            foreach (var source in osuDragStart.Fruits.Where(f => objectSelection.Contains(f.Id)))
            {
                var target = Document.Fruits.First(f => f.Id == source.Id);
                target.X = source.X; target.Y = source.Y;
            }
            foreach (var source in osuDragStart.ImportedSliders.Where(s => objectSelection.Contains(s.Id)))
            {
                var target = Document.ImportedSliders.First(s => s.Id == source.Id);
                target.X = source.X; target.Y = source.Y; target.PixelLength = source.PixelLength; target.PathType = source.PathType;
                target.ControlPoints.Clear(); target.ControlPoints.AddRange(source.ControlPoints);
                target.OriginalLine = source.OriginalLine; target.RandomizationSource = source.RandomizationSource;
                target.RandomizationTrack = source.RandomizationTrack;
                target.RandomizationBaseVelocity = source.RandomizationBaseVelocity;
                target.PreserveNativeGeometry = source.PreserveNativeGeometry;
                target.SliderVelocityMultiplier = source.SliderVelocityMultiplier;
                target.ProcessedSliderVelocityMultiplier = source.ProcessedSliderVelocityMultiplier;
            }
            return true;
        }
        osuDragMoved = true;
        if (osuControlSlider != Guid.Empty && Document.ImportedSliders.FirstOrDefault(s => s.Id == osuControlSlider) is { } controlSlider)
        {
            var source = osuDragStart.ImportedSliders.First(s => s.Id == controlSlider.Id);
            var geometry = OsuControlGeometry(source);
            RestoreOsuControlGeometry(controlSlider, geometry);
            point = SnapOsuDragPoint(point, useDistance: osuSelectedControl == 0);
            var original = geometry.ControlPoints[osuSelectedControl];
            int first = osuSelectedControl, last = osuSelectedControl;
            while (first > 0 && geometry.ControlPoints[first - 1] == original) first--;
            while (last + 1 < geometry.ControlPoints.Count && geometry.ControlPoints[last + 1] == original) last++;
            for (int i = first; i <= last; i++) controlSlider.ControlPoints[i] = new(point.X, point.Y);
            if (first == 0) { controlSlider.X = point.X; controlSlider.Y = point.Y; }
            controlSlider.SliderVelocityMultiplier = source.SliderVelocityMultiplier;
            controlSlider.ProcessedSliderVelocityMultiplier = null;
            OsuRecalculateLength(controlSlider, snapLength: !shift);
            if (shift && geometry.PixelLength > 0)
            {
                double oldSv = (osuShowProcessedSliders ? source.ProcessedSliderVelocityMultiplier ?? source.SliderVelocityMultiplier : source.SliderVelocityMultiplier)
                    ?? ImportedSliderConverter.SliderVelocityAt(osuDragStart, source);
                double ratio = geometry.PixelLength / oldSv;
                controlSlider.PixelLength = Math.Clamp(controlSlider.PixelLength, .1 * ratio, 10 * ratio);
                controlSlider.SliderVelocityMultiplier = controlSlider.PixelLength / ratio;
            }
            OsuSyncSliderLine(controlSlider);
            return true;
        }
        double dx = point.X - osuDragOrigin.X, dy = point.Y - osuDragOrigin.Y;
        (dx, dy) = SnapOsuDragOffset(dx, dy);
        foreach (var source in osuDragStart.Fruits.Where(f => objectSelection.Contains(f.Id)))
        {
            var target = Document.Fruits.First(f => f.Id == source.Id);
            target.X = Math.Clamp(source.X + dx, 0, 512); target.Y = Math.Clamp(source.Y + dy, 0, 384);
        }
        foreach (var source in osuDragStart.ImportedSliders.Where(s => objectSelection.Contains(s.Id)))
        {
            var target = Document.ImportedSliders.First(s => s.Id == source.Id);
            var geometry = source.RandomizationSource ?? source;
            target.X = geometry.X + dx; target.Y = geometry.Y + dy;
            target.PathType = geometry.PathType; target.PixelLength = geometry.PixelLength;
            target.ControlPoints.Clear();
            target.ControlPoints.AddRange(geometry.ControlPoints.Select(p => new SliderPathPoint(p.X + dx, p.GeometryY + dy)));
            OsuSyncSliderLine(target);
        }
        return true;
    }

    private bool OsuPointerUp(float x, float y, int button, bool shift)
    {
        if (osuPanning && button == 1)
        { OsuPointerMove(x, y); osuPanning = false; drag = DragKind.None; return true; }
        if (osuMarquee && button == 0)
        {
            OsuPointerMove(x, y); EnsureOsuCanvasObjects();
            double now = playhead;
            var ids = osuCanvasObjects.Where(o => OsuMarqueeContains(o, now)).Select(o => o.Id);
            if (osuMarqueeAdditive) ids = ids.Concat(objectSelection);
            SelectObjects(ids.ToArray()); osuMarquee = false; drag = DragKind.None; return true;
        }
        if (osuDefaultSvDragging && button == 0)
        {
            SetOsuDefaultSv(x, shift || shiftHeld); osuDefaultSvDragging = false; drag = DragKind.None;
            if (LibrarySettings.OsuSliderDefaultSv != osuDefaultSvStart) RequestViewPreference?.Invoke();
            return true;
        }
        if (osuSpacingDragging && button == 0)
        { SetOsuSpacing(x, shift || shiftHeld); CommitContentHistory(); osuSpacingDragging = false; drag = DragKind.None; return true; }
        if (osuDragStart is null || button != 0) return false;
        OsuPointerMove(x, y, shift);
        try
        {
            CommitContentHistory();
        }
        catch (InvalidOperationException error) { history.Cancel(); StatusMessage = error.Message; }
        osuDragStart = null; EndOsuDragSnap(); drag = DragKind.None;
        return true;
    }

    private void CancelOsuInteraction()
    {
        if (osuDragStart is not null || osuSpacingDragging) history.Cancel();
        osuSpacingDragging = false;
        if (osuDefaultSvDragging) LibrarySettings.OsuSliderDefaultSv = osuDefaultSvStart;
        osuDefaultSvDragging = false;
        osuPanning = false;
        osuMarquee = false;
        osuDragStart = null; EndOsuDragSnap(); osuDraftSlider = null; osuDraftPointer = null; osuSpinnerStart = null;
        osuControlSlider = Guid.Empty; osuSelectedControl = -1;
    }

    private bool OsuMarqueeContains(OsuCanvasObject o, double now)
    {
        if (o.Start - OsuCanvasPreempt > now || o.End + 700 <= now) return false;
        double shift = -o.Stack * OsuCanvasRadius / 10;
        bool Contains(double x, double y)
        {
            var p = OsuCanvasScreen(x + shift, y + shift);
            return osuMarqueeBounds.Contains(p.X, p.Y);
        }
        if (o.Spinner) return now < o.End + 240 && Contains(o.X, o.Y);
        if (now < o.Start + 700 && Contains(o.X, o.Y)) return true;
        if (o.Path.Count == 0) return false;
        if (now < o.End + 240 && Contains(o.X, o.Y)) return true;
        var tail = OsuPathPosition(o, o.Spans % 2 == 0 ? 0 : 1);
        if (Contains(tail.X, tail.Y)) return true;
        double spanDuration = (o.End - o.Start) / o.Spans;
        int first = Math.Max(1, (int)Math.Floor((now - 700 - o.Start) / Math.Max(1, spanDuration)) + 1);
        int last = Math.Min(o.Spans - 1, Math.Max(1, (int)((now - o.Start) / Math.Max(1, spanDuration)) + 1));
        for (int repeat = first; repeat <= last; repeat++)
        {
            var p = OsuPathPosition(o, repeat % 2 == 0 ? 0 : 1);
            if (Contains(p.X, p.Y)) return true;
        }
        return false;
    }

    private void FinishOsuSlider()
    {
        if (osuDraftSlider is not { } slider) return;
        if (osuDraftPointer is { } pointer && slider.ControlPoints[^1] != new SliderPathPoint(pointer.X, pointer.Y))
            slider.ControlPoints.Add(new(pointer.X, pointer.Y));
        if (slider.ControlPoints.Count < 2) return;
        slider.PathType = OsuPlacementPathType(slider.ControlPoints.Count);
        OsuRecalculateLength(slider);
        if (slider.PixelLength < .001) return;
        OsuSyncSliderLine(slider);
        if (!Edit(L.Get("editor.command.drawTrack"), () =>
        {
            RemovePlacementConflicts(Document, slider.TimeMs);
            Document.ImportedSliders.Add(slider); ApplyPlacementFlags(slider.Id);
            Document.DurationMs = Math.Max(Document.DurationMs, ImportedSliderConverter.EndTimeMs(Document, slider));
        })) return;
        osuDraftSlider = null; osuDraftPointer = null;
        SelectObjects([slider.Id], slider.Id);
        tool = Tool.Select;
    }

    private static char OsuPlacementPathType(int count) => count == 3 ? 'P' : 'B';

    private void OsuRecalculateLength(ImportedSlider slider, bool controlCountChanged = false, bool snapLength = true)
    {
        if (controlCountChanged && slider.PathType == 'P' && slider.ControlPoints.Count != 3) slider.PathType = 'B';
        slider.ProcessedSliderVelocityMultiplier = null;
        slider.PixelLength = 0;
        var path = ImportedSliderConverter.Path(slider);
        for (int i = 1; i < path.Count; i++)
            slider.PixelLength += Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].GeometryY - path[i - 1].GeometryY, 2));
        if (snapLength && slider.EndpointSnap is { } own && slider.PixelLength > .001)
        {
            var timing = SnapTiming();
            double velocity = ImportedSliderConverter.VelocityAt(Document, slider, timing);
            double end = own.Snap(timing, slider.TimeMs + slider.PixelLength / velocity * slider.SpanCount, true);
            if (end > slider.TimeMs) slider.PixelLength = (end - slider.TimeMs) / slider.SpanCount * velocity;
        }
        else if (snapLength && snap && slider.PixelLength > .001)
        {
            var timing = SnapTiming();
            double step = ImportedSliderConverter.VelocityAt(Document, slider, timing)
                * timing.At(slider.TimeMs).BeatLengthMs / divisor;
            slider.PixelLength = Math.Max(1, Math.Round(slider.PixelLength / step, MidpointRounding.AwayFromZero)) * step;
        }
    }

    private static void OsuSyncSliderLine(ImportedSlider slider)
    {
        slider.ProcessedSliderVelocityMultiplier = null;
        slider.RandomizationSource = null; slider.RandomizationTrack = null; slider.RandomizationBaseVelocity = null;
        slider.PreserveNativeGeometry = true;
        static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string[] parts = slider.OriginalLine?.Split(',') ?? ["0", "0", "0", "2", "0", "B", "1", "0", "", "", "0:0:0:0:"];
        int oldSpans = parts.Length > 6 && int.TryParse(parts[6], out int parsed) ? parsed : slider.SpanCount;
        if (parts.Length < 11) Array.Resize(ref parts, 11);
        if (oldSpans != slider.SpanCount)
        {
            parts[8] = OsuResizeSliderEdges(parts[8], slider.SpanCount, "0");
            parts[9] = OsuResizeSliderEdges(parts[9], slider.SpanCount, "0:0");
        }
        parts[0] = N(slider.X); parts[1] = N(slider.Y); parts[2] = N(slider.TimeMs);
        parts[5] = slider.PathType + string.Concat(slider.ControlPoints.Skip(1).Select(p => "|" + N(p.X) + ":" + N(p.GeometryY)));
        parts[6] = slider.SpanCount.ToString(CultureInfo.InvariantCulture); parts[7] = N(slider.PixelLength);
        slider.OriginalLine = string.Join(',', parts);
    }

    private static string OsuResizeSliderEdges(string? source, int spans, string fallback)
    {
        var edges = Enumerable.Repeat(fallback, spans + 1).ToArray();
        if (!string.IsNullOrEmpty(source))
        {
            var old = source.Split('|'); edges[0] = old[0];
            if (old.Length > 1) edges[^1] = old[^1];
            for (int i = 1; i < Math.Min(old.Length - 1, edges.Length - 1); i++) edges[i] = old[i];
        }
        return string.Join('|', edges);
    }

    private static ImportedSlider OsuSliderGeometrySnapshot(ImportedSlider slider)
    {
        var copy = new ImportedSlider { Id = slider.Id, EndpointSnap = slider.EndpointSnap, X = slider.X, Y = slider.Y, TimeMs = slider.TimeMs,
            PathType = slider.PathType, PixelLength = slider.PixelLength, SpanCount = slider.SpanCount,
            SourceOrder = slider.SourceOrder, OriginalLine = slider.OriginalLine, RandomizationSource = slider.RandomizationSource,
            RandomizationTrack = slider.RandomizationTrack, RandomizationBaseVelocity = slider.RandomizationBaseVelocity,
            PreserveNativeGeometry = slider.PreserveNativeGeometry, SliderVelocityMultiplier = slider.SliderVelocityMultiplier, ProcessedSliderVelocityMultiplier = slider.ProcessedSliderVelocityMultiplier };
        copy.ControlPoints.AddRange(slider.ControlPoints);
        return copy;
    }

    private void ChangeOsuSpanCount(ImportedSlider slider, int spans)
    {
        if (slider.SpanCount == spans) return;
        var before = OsuSliderGeometrySnapshot(slider);
        RestoreOsuControlGeometry(slider);
        slider.SpanCount = spans; OsuSyncSliderLine(slider);
        RemapOsuRepeatOverrides(before, slider);
    }

    private void RemapOsuRepeatOverrides(ImportedSlider before, ImportedSlider after)
    {
        if (before.SpanCount == after.SpanCount || !Document.HitsoundOverrides.Any(h => h.SourceId == after.Id)) return;
        ConvertedCatchObject[] Events(ImportedSlider source)
        {
            var local = new MapDocument { BeatLengthMs = Document.BeatLengthMs, TimingOffsetMs = Document.TimingOffsetMs,
                SliderMultiplier = Document.EffectiveSliderMultiplier, SliderTickRate = Document.SliderTickRate, LegacySliderTicks = Document.LegacySliderTicks };
            local.TimingPoints.AddRange(Document.TimingPoints); local.ImportedSliders.Add(source);
            var result = CatchStreamConverter.Convert(local);
            if (!result.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), result.Diagnostics));
            return result.Objects.ToArray();
        }
        var oldEvents = Events(before).ToDictionary(e => e.EventIndex);
        var newEvents = Events(after);
        var newEdges = newEvents.Where(e => e.Kind == CatchObjectKind.Fruit).ToArray();
        var preserved = new List<EventHitsound>();
        foreach (var item in Document.HitsoundOverrides.Where(h => h.SourceId == after.Id))
        {
            if (item.EdgeIndex >= 0)
            {
                int edge = item.EdgeIndex == before.SpanCount ? after.SpanCount : item.EdgeIndex;
                if (edge >= after.SpanCount && item.EdgeIndex != before.SpanCount) continue;
                if (edge < newEdges.Length) preserved.Add(item with { EdgeIndex = edge, EventIndex = newEdges[edge].EventIndex });
            }
            else if (oldEvents.TryGetValue(item.EventIndex, out var old)
                && newEvents.FirstOrDefault(e => e.Kind == old.Kind && Math.Abs(e.TimeMs - old.TimeMs) < .001) is { } surviving)
                preserved.Add(item with { EventIndex = surviving.EventIndex });
        }
        Document.HitsoundOverrides.RemoveAll(h => h.SourceId == after.Id);
        Document.HitsoundOverrides.AddRange(preserved);
    }

    private bool OsuEditingKey(int key, bool ctrl, bool shift)
    {
        if (!IsOsuCanvas) return false;
        if (key == 27 && (osuDraftSlider is not null || osuDragStart is not null || osuSpinnerStart is not null))
        { CancelInteraction(); return true; }
        if (key == 13 && osuDraftSlider is not null) { FinishOsuSlider(); return true; }
        if (osuDraftSlider is not null || osuSpinnerStart is not null)
        {
            if (!ctrl && key is >= 49 and <= 52)
            {
                if (osuDraftSlider is not null) FinishOsuSlider();
                CancelOsuInteraction();
                ChangeTool(key switch { 49 => Tool.Select, 50 => Tool.Fruit, 51 => Tool.Slider, _ => Tool.Banana });
                return true;
            }
            return ctrl && key is 83 or 90 or 89 or 88 or 86;
        }
        // These commands start their own transaction; the captured gesture already owns one.
        if (drag != DragKind.None && (key is 46 or 93 || ctrl && key is 37 or 39 or 71 or 72 or 187 or 189)) return true;
        if (!ctrl && key == 93 && SelectedImportedSlider is { } menuSlider
            && osuControlSlider == menuSlider.Id && osuSelectedControl >= 0)
        { ShowOsuSliderMenu(menuSlider, osuSelectedControl, mouseX, mouseY); return true; }
        if (!ctrl && !shift && key == 46 && osuControlSlider != Guid.Empty && SelectedImportedSlider is { } slider)
        { DeleteOsuControl(slider, osuSelectedControl); return true; }
        if (ctrl && key is 37 or 39 && ClipboardSelectedParentIds().Count > 0)
        { MoveOsuSelection((key == 37 ? -1 : 1) * (shift ? gridSize : 1), 0); return true; }
        if (ctrl && !shift && key == 72) { MirrorOsuSelection(); return true; }
        if (ctrl && !shift && key == 71 && SelectedImportedSlider is { } reversed)
        {
            Edit(L.Get("editor.command.reversePath"), () =>
            {
                RestoreOsuControlGeometry(reversed);
                reversed.ControlPoints.Reverse();
                reversed.X = reversed.ControlPoints[0].X; reversed.Y = reversed.ControlPoints[0].GeometryY;
                OsuSyncSliderLine(reversed);
            });
            return true;
        }
        return false;
    }

    private void MirrorOsuSelection()
    {
        if (notesLocked || RejectReadOnlySelection()) return;
        var ids = ClipboardSelectedParentIds();
        Edit(L.Get("shortcut.mirror"), () =>
        {
            foreach (var fruit in Document.Fruits.Where(f => ids.Contains(f.Id))) fruit.X = 512 - fruit.X;
            foreach (var slider in Document.ImportedSliders.Where(s => ids.Contains(s.Id)))
            {
                RestoreOsuControlGeometry(slider);
                slider.X = 512 - slider.X;
                for (int i = 0; i < slider.ControlPoints.Count; i++) slider.ControlPoints[i] = new(512 - slider.ControlPoints[i].X, slider.ControlPoints[i].GeometryY);
                OsuSyncSliderLine(slider);
            }
        });
    }

    private void MoveOsuSelection(double dx, double dy)
    {
        if (notesLocked || RejectReadOnlySelection()) return;
        var ids = ClipboardSelectedParentIds();
        Edit(L.Get("editor.command.moveObjects"), () =>
        {
            foreach (var fruit in Document.Fruits.Where(f => ids.Contains(f.Id))) { fruit.X += dx; fruit.Y += dy; }
            foreach (var slider in Document.ImportedSliders.Where(s => ids.Contains(s.Id)))
            {
                RestoreOsuControlGeometry(slider);
                slider.X += dx; slider.Y += dy;
                for (int i = 0; i < slider.ControlPoints.Count; i++) slider.ControlPoints[i] = new(slider.ControlPoints[i].X + dx, slider.ControlPoints[i].GeometryY + dy);
                OsuSyncSliderLine(slider);
            }
        });
    }

    private void DeleteOsuControl(ImportedSlider slider, int index)
    {
        var geometry = OsuControlGeometry(slider);
        if (index < 0 || index >= geometry.ControlPoints.Count || notesLocked) return;
        var point = geometry.ControlPoints[index];
        int first = index, last = index;
        while (first > 0 && geometry.ControlPoints[first - 1] == point) first--;
        while (last + 1 < geometry.ControlPoints.Count && geometry.ControlPoints[last + 1] == point) last++;
        if (geometry.ControlPoints.Count - (last - first + 1) < 2) { DeleteSelectedObjects(); return; }
        Edit(L.Get("editor.command.deletePoint"), () =>
        {
            RestoreOsuControlGeometry(slider);
            slider.ControlPoints.RemoveRange(first, last - first + 1);
            slider.X = slider.ControlPoints[0].X; slider.Y = slider.ControlPoints[0].GeometryY;
            OsuRecalculateLength(slider, controlCountChanged: true); OsuSyncSliderLine(slider);
        });
        osuSelectedControl = -1; osuControlSlider = Guid.Empty;
    }

    private void MakeOsuControlRed(ImportedSlider slider, int index)
    {
        var geometry = OsuControlGeometry(slider);
        if (index <= 0 || index >= geometry.ControlPoints.Count - 1 || notesLocked) return;
        if (geometry.ControlPoints[index - 1] == geometry.ControlPoints[index]
            || geometry.ControlPoints[index + 1] == geometry.ControlPoints[index]) return;
        if (Edit(L.Get("editor.command.insertPoint"), () =>
        {
            RestoreOsuControlGeometry(slider);
            slider.ControlPoints.Insert(index + 1, slider.ControlPoints[index]);
            OsuRecalculateLength(slider, controlCountChanged: true); OsuSyncSliderLine(slider);
        })) osuSelectedControl = index + 1;
    }

    private void OsuDoubleClick(float x, float y, bool shift)
    {
        if (notesLocked) return;
        var point = OsuInputPoint(x, y, shift: shift);
        if (osuDraftSlider is { } draft)
        {
            var control = new SliderPathPoint(point.X, point.Y);
            if (draft.ControlPoints[^1] != control) draft.ControlPoints.Add(control);
            if (draft.ControlPoints.Count > 1 && draft.ControlPoints[^2] != control) draft.ControlPoints.Add(control);
            osuDraftPointer = point;
        }
        else if (SelectedImportedSlider is { } selected && HitOsuControl(selected, x, y) is { } index)
        { osuControlSlider = selected.Id; osuSelectedControl = index; MakeOsuControlRed(selected, index); }
        else if (OsuCanvasHit(x, y) is { } id) SelectObjects([id], id);
    }

    private void ShowOsuSliderMenu(ImportedSlider slider, int index, float x, float y)
    {
        var geometry = OsuControlGeometry(slider);
        contextItems.Clear();
        contextItems.Add(new(L.Get("editor.command.deletePoint"), () => DeleteOsuControl(slider, index)));
        foreach (char type in new[] { 'L', 'B', 'P', 'C' })
            contextItems.Add(new(L.Get("osu.path." + type), () => Edit(L.Get("editor.command.curveType"), () =>
            { RestoreOsuControlGeometry(slider); slider.PathType = type; OsuRecalculateLength(slider); OsuSyncSliderLine(slider); }),
                Enabled: type != 'P' || geometry.ControlPoints.Count == 3, Active: geometry.PathType == type));
        contextItems.Add(new(L.Get("editor.command.addReverse"), () => Edit(L.Get("editor.command.addReverse"), () =>
        { ChangeOsuSpanCount(slider, Math.Min(9000, slider.SpanCount + 1)); })));
        contextItems.Add(new(L.Get("editor.command.removeReverse"), () => Edit(L.Get("editor.command.removeReverse"), () =>
        { ChangeOsuSpanCount(slider, Math.Max(1, slider.SpanCount - 1)); })));
        contextItems.Add(new(L.Get("osu.lengthIncrease"), () => ChangeOsuSliderLength(slider, 1)));
        contextItems.Add(new(L.Get("osu.lengthDecrease"), () => ChangeOsuSliderLength(slider, -1)));
        contextItems.Add(new(L.Get("preview.convertSlider"), () => ConvertSelectedSliderRepresentation(slider.Id)));
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 250)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 250, ContextMenuHeight);
    }

    private void ChangeOsuSliderLength(ImportedSlider slider, int direction)
    {
        Edit(L.Get("inspector.length"), () =>
        {
            RestoreOsuControlGeometry(slider);
            if (!osuShowProcessedSliders) slider.ProcessedSliderVelocityMultiplier = null;
            var timing = SnapTiming();
            double step = ImportedSliderConverter.VelocityAt(Document, slider, timing)
                * timing.At(slider.TimeMs).BeatLengthMs / divisor;
            double units = direction > 0 ? Math.Floor(slider.PixelLength / step + 1e-7) + 1
                : Math.Ceiling(slider.PixelLength / step - 1e-7) - 1;
            slider.PixelLength = snap ? Math.Max(1, units) * step : Math.Max(.01, slider.PixelLength + direction * step);
            OsuSyncSliderLine(slider);
        });
    }

    internal void ConvertSelectedSliderRepresentation(Guid id)
    {
        if (notesLocked || !ClipboardInteractionReady) return;
        osuAllowSliderConversion = true;
        try
        {
            if (!Edit(L.Get("editor.command.editImportedSlider"), () =>
            {
                var result = Document.Tracks.Any(t => t.Id == id)
                    ? SliderRepresentationEditing.ConvertToLegacy(Document, id, compensateTinyDroplets)
                    : SliderRepresentationEditing.ConvertToTrack(Document, id, Document.DerandomizeDroplets ?? LibrarySettings.DerandomizeDroplets);
                if (!result.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), result.Diagnostics));
            })) return;
            osuControlSlider = Guid.Empty; osuSelectedControl = -1;
            SelectObjects([id], id);
        }
        finally { osuAllowSliderConversion = false; }
    }

    private const float OsuControlLabelWidth = 34;
    private Rect OsuDefaultSvBounds => OsuSpacingBounds with { Y = OsuSpacingBounds.Bottom + 4 };
    internal Rect OsuSliderDefaultSvTrackBounds => new(OsuDefaultSvBounds.X + OsuControlLabelWidth, OsuDefaultSvBounds.Y + 12,
        Math.Max(24, OsuDefaultSvBounds.Width - OsuControlLabelWidth - 62), 8);
    internal Rect OsuSliderDefaultSvValueBounds => new(OsuDefaultSvBounds.Right - 56, OsuDefaultSvBounds.Y + 4, 50, 24);
    private void SetOsuDefaultSv(float x, bool shift)
    {
        var track = OsuSliderDefaultSvTrackBounds;
        double raw = .1 + Math.Clamp((x - track.X) / track.Width, 0, 1) * 3.1;
        double step = shift ? .01 : .1;
        LibrarySettings.OsuSliderDefaultSv = Math.Round(raw / step) * step;
    }
    private void DrawOsuDefaultSv(ICanvas c)
    {
        var bounds = OsuDefaultSvBounds;
        c.Fill(bounds, Panel, 4);
        string label = L.Get("osu.sliderDefaultSv");
        c.Text(label, bounds.X + 8, bounds.Y + 8, 12, Foreground, OsuControlLabelWidth - 10);
        var track = OsuSliderDefaultSvTrackBounds;
        var input = OsuSliderDefaultSvValueBounds;
        int index = fields.Count;
        bool focused = editField == index;
        c.Fill(input, focused ? 0x273638u : 0x151B24u, 3);
        c.Stroke(input, focused ? (fieldError.Length > 0 ? Error : Accent) : Grid, 1, 3);
        DrawInputText(c, new(input.X + 5, input.Y + 4, input.Width - 10, 18),
            focused ? editBuffer : LibrarySettings.OsuSliderDefaultSv.ToString("0.00", CultureInfo.InvariantCulture),
            12, focused, "numeric:" + index);
        fields.Add(new(input, label, LibrarySettings.OsuSliderDefaultSv, value =>
        {
            if (value is < .1 or > 10) throw new ArgumentException(L.Get("legacySv.range"));
            if (LibrarySettings.OsuSliderDefaultSv == value) return;
            LibrarySettings.OsuSliderDefaultSv = value;
            RequestViewPreference?.Invoke();
        }, false, Preference: true));
        c.Line(track.X, track.Y + 4, track.Right, track.Y + 4, Grid, 4);
        c.Circle(track.X + (float)Math.Clamp((LibrarySettings.OsuSliderDefaultSv - .1) / 3.1, 0, 1) * track.Width, track.Y + 4, 6, Accent);
    }

    private Rect OsuSpacingBounds => new(CanvasCornerRight - 200, plot.Y + 8, 200, 32);
    private Rect OsuSpacingTrack => new(OsuSpacingBounds.X + OsuControlLabelWidth, OsuSpacingBounds.Y + 12,
        Math.Max(24, OsuSpacingBounds.Width - OsuControlLabelWidth - 54), 8);
    internal Rect OsuDistanceSpacingTrackBounds => OsuSpacingTrack;
    private void SetOsuSpacing(float x, bool shift)
    {
        double raw = .1 + Math.Clamp((x - OsuSpacingTrack.X) / OsuSpacingTrack.Width, 0, 1) * 3.1;
        double step = shift ? .01 : .1;
        Document.OsuDistanceSpacing = Math.Clamp(Math.Round(raw / step) * step, .1, 3.2);
    }
    private void DrawOsuDistanceSpacing(ICanvas c)
    {
        if (osuMarquee) c.Stroke(osuMarqueeBounds, Accent, 1.5f);
        DrawOsuDefaultSv(c);
        c.Fill(OsuSpacingBounds, Panel, 4);
        string label = L.Get("osu.distanceSpacing");
        c.Text(label, OsuSpacingBounds.X + 8, OsuSpacingBounds.Y + 8, 12, Foreground, OsuControlLabelWidth - 10);
        var track = OsuSpacingTrack;
        c.Text(Document.OsuDistanceSpacing.ToString("0.00", CultureInfo.InvariantCulture) + "x",
            track.Right + 10, OsuSpacingBounds.Y + 8, 12, Foreground, 42);
        c.Line(track.X, track.Y + 4, track.Right, track.Y + 4, Grid, 4);
        c.Circle(track.X + (float)Math.Clamp((Document.OsuDistanceSpacing - .1) / 3.1, 0, 1) * track.Width, track.Y + 4, 6, Accent);
    }
}
