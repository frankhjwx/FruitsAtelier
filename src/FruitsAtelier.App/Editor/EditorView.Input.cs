using L = FruitsAtelier.Localization.Strings;
using System.Globalization;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public void PointerDown(float x, float y, int button, bool shift, bool ctrl)
    {
        mouseX = x; mouseY = y;
        if (drag != DragKind.None) return;
        if (button == 2 && draftBanana != Guid.Empty && tool == Tool.Banana && plot.Contains(x, y))
        {
            FinishBanana(x, y);
            return;
        }
        if (button == 2)
        {
            if (editField >= 0 && !CommitField()) return;
            OpenContextMenu(x, y);
            return;
        }
        if (contextItems.Count > 0)
        {
            if (button == 0) ActivateContextMenu(x, y);
            return;
        }
        if (button == 1 && canvas.Contains(x, y))
        {
            if (!AudioPlaying) pinPlayhead = false;
            drag = DragKind.Pan; dragStartY = y; dragStartTime = AudioPlaying ? playhead : viewStart; return;
        }
        if (button != 0) return;
        if (editField >= 0 && !CommitField()) return;
        if (menu >= 0)
        {
            var popup = new Rendering.Rect(109 + menu * 53, 38, 282, menu is 0 or 1 ? 281 : 171);
            if (popup.Contains(x, y))
            {
                for (int i = hits.Count - 1; i >= 0; i--)
                    if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
                return;
            }
            menu = -1;
            return;
        }
        if (zoomSlider.Contains(x, y))
        {
            SetTimeZoom(x);
            drag = DragKind.TimeZoom;
            return;
        }
        if (snapSlider.Contains(x, y))
        {
            SetSnapDivisor(x);
            drag = DragKind.SnapDivisor;
            BeginPointerDrag(x, y);
            return;
        }
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y))
            {
                if (hits[i].Enabled) hits[i].Action();
                else StatusMessage = L.Get("editor.status.operationUnavailable");
                return;
            }
        for (int i = 0; i < fields.Count; i++)
            if (fields[i].Bounds.Contains(x, y))
            {
                if (draftTrack != Guid.Empty) { StatusMessage = L.Get("editor.status.finishBeforeNumericEdit"); return; }
                FocusField(i); return;
            }
        if (listBounds.Contains(x, y))
        {
            foreach (var row in rows)
                if (row.Bounds.Contains(x, y))
                {
                    FinishForSelection();
                    PickObject(row.Track != Guid.Empty ? row.Track : row.Id, ctrl);
                    if (SelectedFruit is null && SelectedTrack is null && SelectedImportedSlider is null && SelectedBananaShower is null)
                        Select(Guid.Empty);
                    double? time = SelectedFruit?.TimeMs ?? SelectedAnchor?.TimeMs ?? SelectedTrack?.Nodes.FirstOrDefault()?.TimeMs
                        ?? SelectedImportedSlider?.TimeMs ?? SelectedBananaShower?.TimeMs;
                    if (time is { } t && (t < viewStart || t > viewStart + plot.Height / pixelsPerMs))
                    { pinPlayhead = false; viewStart = t - plot.Height / pixelsPerMs / 3; ClampView(); }
                    return;
                }
        }
        if (overview.Contains(x, y))
        {
            drag = DragKind.Timeline;
            NavigateTime(x);
            return;
        }
        if (!plot.Contains(x, y)) return;
        if (!ctrl && TryBeginSelectedBananaHandle(x, y)) return;
        if (tool != Tool.Slider && !ctrl && showTargets && objectSelection.Count == 1 && SelectedTrack is { } selectedObject)
        {
            foreach (var node in selectedObject.Nodes)
                if (Near(Point(node), x, y, 9))
                {
                    tool = Tool.Slider;
                    PickAnchor(selectedObject, node, false);
                    BeginNodeDrag(selectedObject, node, DragKind.Anchor, x, y);
                    return;
                }
        }
        if (tool == Tool.Slider && showTargets && SelectedTrack is { } selected)
        {
            foreach (var node in selected.Nodes)
                if (Near(Point(node), x, y, 7))
                {
                    PickAnchor(selected, node, ctrl);
                    if (!ctrl && anchorSelection.Count == 1) BeginNodeDrag(selected, node, DragKind.Anchor, x, y);
                    return;
                }
            foreach (var node in selected.Nodes)
            {
                int index = selected.Nodes.IndexOf(node);
                if (index > 0 && node.HandleIn != default && CurveMath.SegmentKind(selected, index - 1) == CurveKind.Bezier && Near(Point(node) + node.HandleIn, x, y, 7))
                { BeginNodeDrag(selected, node, DragKind.HandleIn, x, y); return; }
                if (node.HandleOut != default && (index < selected.Nodes.Count - 1 && CurveMath.SegmentKind(selected, index) == CurveKind.Bezier || selected.Id == draftTrack) && Near(Point(node) + node.HandleOut, x, y, 7))
                { BeginNodeDrag(selected, node, DragKind.HandleOut, x, y); return; }
            }
        }
        if (tool == Tool.Slider && draftTrack == Guid.Empty && SelectedTrack is { } editedTrack)
        {
            if (HitCatchObject(x, y) is null && HitTrackPath(x, y) == Guid.Empty && HitBananaRectangle(x, y) is null)
            {
                BeginBox(x, y, ctrl, true);
                return;
            }
            tool = Tool.Select;
            SelectObjects([editedTrack.Id], editedTrack.Id);
        }
        if (tool == Tool.Slider)
        {
            if (!ctrl) AddCurveAnchor(x, y);
            return;
        }
        var hitObject = HitCatchObject(x, y);
        bool sliderPathOverBanana = hitObject?.Kind == CatchObjectKind.Banana && showTargets && HitSliderLocation(x, y) is not null;
        if (!sliderPathOverBanana && hitObject is not null)
        {
            PickObject(hitObject.SourceId, ctrl);
            if (ctrl) return;
            if (!hitObject.IsStandalone)
            {
                StatusMessage = L.Get("editor.status.parentSelected", hitObject.Kind == CatchObjectKind.Banana ? L.Get("editor.object.bananaShower") : L.Get("editor.object.sliderWithSpace"), Number(hitObject.TimeMs));
                BeginObjectDrag(x, y);
                return;
            }
            if (Document.Fruits.FirstOrDefault(f => f.Id == hitObject.SourceId) is { } fruit)
            {
                BeginObjectDrag(x, y);
                return;
            }
        }
        if (showTargets)
        {
            foreach (var track in Document.Tracks.AsEnumerable().Reverse())
                foreach (var node in track.Nodes)
                    if (Near(Point(node), x, y, 9))
                    { PickObject(track.Id, ctrl); return; }
            foreach (var track in Document.Tracks)
                for (int s = 0; s < track.Nodes.Count - 1; s++)
                {
                    var last = Screen(CurveMath.Evaluate(track, s, 0));
                    for (int n = 1; n <= 64; n++)
                    {
                        var p = Screen(CurveMath.Evaluate(track, s, n / 64.0));
                        if (SegmentDistance(x, y, last.X, last.Y, p.X, p.Y) < 6)
                        {
                            PickObject(track.Id, ctrl);
                            if (!ctrl) BeginObjectDrag(x, y);
                            return;
                        }
                        last = p;
                    }
                }
            if (HitSliderLocation(x, y) is { } sliderLocation)
            {
                PickObject(sliderLocation.Id, ctrl);
                if (!ctrl) BeginObjectDrag(x, y);
                return;
            }
        }
        if (HitBananaRectangle(x, y) is { } shower)
        {
            PickObject(shower.Id, ctrl);
            if (!ctrl) BeginObjectDrag(x, y);
            return;
        }
        if (tool == Tool.Banana)
        {
            StartBanana(x, y);
            return;
        }
        BeginBox(x, y, ctrl, false);
    }

    public void PointerMove(float x, float y, bool shift, bool ctrl)
    {
        mouseX = x; mouseY = y;
        if (drag == DragKind.None) return;
        if (drag == DragKind.TimeZoom) { SetTimeZoom(x); return; }
        if (drag == DragKind.SnapDivisor) { SetSnapDivisor(x); return; }
        if (drag == DragKind.Marquee) { MoveBox(x, y); return; }
        if (drag == DragKind.Pan)
        {
            double panTime = dragStartTime + (y - dragStartY) / pixelsPerMs;
            if (AudioPlaying) SeekTo(panTime);
            else { viewStart = panTime; ClampView(); }
            return;
        }
        if (drag == DragKind.Timeline) { NavigateTime(x); return; }
        if (!dragMoved)
        {
            if (MathF.Abs(x - dragStartX) < 2 && MathF.Abs(y - dragStartY) < 2) return;
            dragMoved = true;
        }
        if (drag == DragKind.Objects) { MoveSelectedObjects(x, y); return; }
        if (drag is DragKind.BananaStart or DragKind.BananaEnd) { MoveBananaBoundary(x, y); return; }
        var raw = Transform.ToMap(x, y) - dragOffset;
        double time = snap ? TimingMap.Snap(Document, raw.TimeMs, divisor) : raw.TimeMs;
        var p = new MapPoint(Math.Clamp(time, 0, EditableDurationMs), Math.Clamp(raw.X, 0, 512));
        if (SelectedTrack is { } track && SelectedAnchor is { } node)
        {
            if (drag == DragKind.Anchor)
            {
                // The draft's last outgoing handle is visible before its future segment exists.
                if (track.Id == draftTrack && node == track.Nodes[^1])
                    p = new(p.TimeMs, Math.Clamp(p.X, Math.Max(0, -node.HandleOut.X), Math.Min(512, 512 - node.HandleOut.X)));
                var start = Point(node);
                if (!CurveMath.TryMoveAnchor(track, node.Id, p.TimeMs, p.X, out var error))
                {
                    ClampMove(start, p, value => CurveMath.TryMoveAnchor(track, node.Id, value.TimeMs, value.X, out _));
                    StatusMessage = error;
                }
                else
                {
                    Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
                    StatusMessage = L.Get("editor.status.anchorPosition", Number(node.TimeMs), Number(node.X));
                }
            }
            else if (drag == DragKind.DraftHandle)
            {
                var cursor = MapAt(x, y, false);
                double dt = Math.Max(0, cursor.TimeMs - node.TimeMs);
                double dx = Math.Clamp(cursor.X - node.X, -node.X, 512 - node.X);
                node.HandleOut = new(dt, dx);
                selectedPart = DragKind.HandleOut;
                if (track.Nodes.Count > 1)
                {
                    var previous = track.Nodes[^2];
                    double maxIncoming = node.TimeMs - previous.TimeMs - previous.HandleOut.TimeMs;
                    node.HandleIn = new(-Math.Min(dt, maxIncoming), Math.Clamp(-dx, -node.X, 512 - node.X));
                    previous.OutgoingKind = previous.HandleOut != default || node.HandleIn != default ? CurveKind.Bezier : CurveKind.Linear;
                }
                StatusMessage = L.Get("editor.status.definingHandle");
            }
            else
            {
                bool incoming = drag == DragKind.HandleIn;
                var start = incoming ? node.HandleIn : node.HandleOut;
                var cursor = Transform.ToMap(x, y) - dragOffset;
                var desired = cursor - Point(node);
                desired = new(incoming ? Math.Min(0, desired.TimeMs) : Math.Max(0, desired.TimeMs), Math.Clamp(desired.X, -node.X, 512 - node.X));
                if (!CurveMath.TryMoveHandle(track, node.Id, incoming, desired, out var error))
                {
                    ClampMove(start, desired, value => CurveMath.TryMoveHandle(track, node.Id, incoming, value, out _));
                    StatusMessage = error;
                }
                else StatusMessage = L.Get("editor.status.handleAdjusted");
            }
        }
    }

    public void PointerUp(float x, float y, int button)
    {
        if (drag == DragKind.None || button != (drag == DragKind.Pan ? 1 : 0)) return;
        PointerMove(x, y, false, false);
        if (drag == DragKind.Marquee) { FinishBox(x, y); return; }
        if (draftTrack == Guid.Empty && drag is DragKind.Objects or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd) history.Commit();
        if (drag is DragKind.Objects or DragKind.BananaStart or DragKind.BananaEnd)
        {
            objectDragStart = null;
            objectDragPrepared = false;
            if (AudioPlaying || pinPlayhead) FollowPlayhead();
        }
        drag = DragKind.None;
    }

    public void PointerDoubleClick(float x, float y, bool shift, bool ctrl)
    {
        if (drag != DragKind.None || buttonTargetIsUnavailable()) return;
        Guid sourceId = Guid.Empty;
        if (listBounds.Contains(x, y))
        {
            var row = rows.FirstOrDefault(row => row.Bounds.Contains(x, y));
            sourceId = row.Track != Guid.Empty ? row.Track : row.Id;
        }
        if (sourceId == Guid.Empty && plot.Contains(x, y))
        {
            var hit = HitCatchObject(x, y);
            if (hit is not null && !hit.IsStandalone) sourceId = hit.SourceId;
            if (sourceId == Guid.Empty) sourceId = HitTrackPath(x, y);
        }
        if (sourceId == Guid.Empty) return;
        if (Document.ImportedSliders.Any(slider => slider.Id == sourceId))
        {
            SelectObjects([sourceId], sourceId);
            EditImportedSlider();
            return;
        }
        if (Document.Tracks.FirstOrDefault(track => track.Id == sourceId) is not { } track) return;
        tool = Tool.Slider;
        SelectAnchors(track, []);
        StatusMessage = L.Get("editor.help.anchors");

        bool buttonTargetIsUnavailable() => editField >= 0 || draftTrack != Guid.Empty || draftBanana != Guid.Empty || menu >= 0 || contextItems.Count > 0;
    }

    public void Wheel(float x, float y, float delta, bool ctrl)
    {
        if (drag != DragKind.None) return;
        if (contextItems.Count > 0) { contextItems.Clear(); return; }
        if (leftPanel.Contains(x, y)) { listScroll = Math.Max(0, listScroll - delta / 120 * 65); return; }
        if (overview.Contains(x, y))
        {
            SeekTo(playhead + delta / 120 * 78 / pixelsPerMs);
            return;
        }
        if (!canvas.Contains(x, y)) return;
        if (!AudioPlaying) pinPlayhead = false;
        if (ctrl)
        {
            ZoomTimeAt(y, Math.Pow(1.16, delta / 120));
            StatusMessage = L.Get("editor.status.timeZoom", DisplayApproachRate);
        }
        else if (AudioPlaying) SeekTo(playhead + delta / 120 * 78 / pixelsPerMs);
        else viewStart += delta / 120 * 78 / pixelsPerMs;
        ClampView();
    }

    public void KeyDown(int virtualKey, bool ctrl, bool shift)
    {
        if (editField >= 0)
        {
            if (virtualKey == 27) { editField = -1; fieldError = ""; return; }
            if (ctrl && virtualKey == 65) { replaceText = true; return; }
            if (virtualKey is 13 or 9)
            {
                int current = editField;
                if (CommitField() && virtualKey == 9 && fields.Count > 0)
                    FocusField((current + (shift ? fields.Count - 1 : 1)) % fields.Count);
                return;
            }
            if (virtualKey == 8)
            {
                editBuffer = replaceText || editBuffer.Length == 0 ? "" : editBuffer[..^1]; replaceText = false;
            }
            else if (virtualKey == 46) { editBuffer = ""; replaceText = false; }
            return;
        }
        if (virtualKey == 27)
        {
            if (contextItems.Count > 0) { contextItems.Clear(); return; }
            if (drag != DragKind.None || draftTrack != Guid.Empty || draftBanana != Guid.Empty) CancelInteraction();
            else { Select(Guid.Empty); menu = -1; }
            return;
        }
        if (ctrl)
        {
            if (drag != DragKind.None) return;
            contextItems.Clear();
            if (virtualKey == 90) { if (shift) Redo(); else Undo(); }
            else if (virtualKey == 89) Redo();
            else if (virtualKey == 79) RequestOpen?.Invoke();
            else if (virtualKey == 83) { if (shift) RequestSaveAs?.Invoke(); else RequestSave?.Invoke(); }
            else if (virtualKey == 69) RequestExport?.Invoke();
            else if (virtualKey == 67) CopySelection();
            else if (virtualKey == 88) CutSelection();
            else if (virtualKey == 86) PasteSelection();
            return;
        }
        if (drag != DragKind.None) return;
        contextItems.Clear();
        switch (virtualKey)
        {
            case 13: FinishCurve(); break;
            case 46: DeleteSelection(); break;
            case 86: ChangeTool(Tool.Select); break;
            case 70: ChangeTool(Tool.Fruit); break;
            case 66: ChangeTool(Tool.Slider); break;
            case 78: ChangeTool(Tool.Banana); break;
            case 32: TogglePlayback(); break;
            case 36: viewStart = 0; SeekTo(0); break;
        }
    }

    public void TextInput(char value)
    {
        if (editField < 0 || char.IsControl(value)) return;
        if (!(char.IsAsciiDigit(value) || value is '.' or '-' or '+' or 'e' or 'E')) return;
        if (replaceText) { editBuffer = ""; replaceText = false; }
        if (editBuffer.Length < 30) editBuffer += value;
        fieldError = "";
    }

    public void CancelInteraction()
    {
        if (drag == DragKind.Marquee) { CancelBox(); contextItems.Clear(); return; }
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag is DragKind.Objects or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd)
        {
            history.Cancel();
            if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) Select(Guid.Empty);
            StatusMessage = L.Get("editor.status.editCancelled");
        }
        drag = DragKind.None;
        objectDragStart = null;
        objectDragPrepared = false;
        draftTrack = Guid.Empty;
        draftBanana = Guid.Empty;
        editField = -1;
        fieldError = "";
        menu = -1;
        contextItems.Clear();
    }

    private void AddCurveAnchor(float x, float y)
    {
        var p = MapAt(x, y, true);
        CurveTrack track;
        if (draftTrack == Guid.Empty)
        {
            history.Begin(L.Get("editor.command.drawTrack"));
            track = new CurveTrack
            {
                Name = L.Get("editor.track.defaultName", Document.Tracks.Count + 1),
                Kind = CurveKind.Linear,
                CompensateTinyDroplets = true
            };
            Document.Tracks.Add(track);
            draftTrack = track.Id;
        }
        else track = Document.Tracks.First(t => t.Id == draftTrack);
        var node = new Anchor { TimeMs = p.TimeMs, X = p.X };
        if (track.Nodes.Count > 0)
        {
            var previous = track.Nodes[^1];
            double dt = p.TimeMs - previous.TimeMs;
            if (dt < 0.001 || previous.TimeMs + previous.HandleOut.TimeMs > p.TimeMs)
            { StatusMessage = L.Get("editor.error.anchorMustBeLater"); return; }
            previous.OutgoingKind = previous.HandleOut == default ? CurveKind.Linear : CurveKind.Bezier;
        }
        track.Nodes.Add(node);
        Document.DurationMs = Math.Max(Document.DurationMs, p.TimeMs);
        Select(node.Id, track.Id);
        drag = DragKind.DraftHandle;
        BeginPointerDrag(x, y);
        dragOffset = new(0, 0);
        StatusMessage = L.Get("editor.help.drawingSlider");
    }

    private void StartBanana(float x, float y)
    {
        if (draftBanana != Guid.Empty)
        {
            StatusMessage = L.Get("editor.status.bananaNeedsEnd");
            return;
        }
        double time = MapAt(x, y, true).TimeMs;
        history.Begin(L.Get("editor.command.drawBanana"));
        var shower = new BananaShower { TimeMs = time, EndTimeMs = time };
        Document.BananaShowers.Add(shower);
        Document.DurationMs = Math.Max(Document.DurationMs, time);
        draftBanana = shower.Id;
        SelectObjects([shower.Id], shower.Id);
        StatusMessage = L.Get("editor.status.bananaStarted", Number(time));
    }

    private void FinishBanana(float x, float y)
    {
        if (draftBanana == Guid.Empty)
        {
            StatusMessage = L.Get("editor.help.banana");
            return;
        }
        var shower = Document.BananaShowers.First(item => item.Id == draftBanana);
        double end = MapAt(x, y, true).TimeMs;
        if (end <= shower.TimeMs)
        {
            StatusMessage = L.Get("editor.error.bananaEndAfterStart");
            return;
        }
        shower.EndTimeMs = end;
        Document.DurationMs = Math.Max(Document.DurationMs, end);
        history.Commit();
        draftBanana = Guid.Empty;
        SelectObjects([shower.Id], shower.Id);
        StatusMessage = L.Get("editor.status.bananaFinished", Number(shower.TimeMs), Number(end));
    }

    private void BeginNodeDrag(CurveTrack track, Anchor node, DragKind kind, float x, float y)
    {
        Select(node.Id, track.Id);
        selectedPart = kind;
        if (draftTrack == Guid.Empty) history.Begin(kind == DragKind.Anchor ? L.Get("editor.command.moveAnchor") : L.Get("editor.command.adjustHandle"));
        drag = kind;
        BeginPointerDrag(x, y);
        var original = Point(node);
        if (kind == DragKind.HandleIn) original += node.HandleIn;
        else if (kind == DragKind.HandleOut) original += node.HandleOut;
        dragOffset = Transform.ToMap(x, y) - original;
    }

    private void BeginPointerDrag(float x, float y)
    {
        dragStartX = x;
        dragStartY = y;
        dragMoved = false;
    }

    private void SetSnapDivisor(float x)
    {
        float left = snapSlider.X + 7, right = snapSlider.Right - 31;
        int index = (int)MathF.Round(Math.Clamp((x - left) / (right - left), 0, 1) * (SnapDivisors.Length - 1));
        divisor = SnapDivisors[index];
        snap = true;
    }

    private void NavigateTime(float x)
    {
        SeekTo(Math.Clamp((x - overview.X) / overview.Width, 0, 1) * TimelineDurationMs);
    }

    private void FocusField(int index)
    {
        editField = index;
        editBuffer = fields[index].Value.ToString("G17", CultureInfo.InvariantCulture);
        fieldError = "";
        replaceText = true;
    }

    private bool CommitField()
    {
        if (editField < 0 || editField >= fields.Count) { editField = -1; return true; }
        if (!double.TryParse(editBuffer, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        { fieldError = L.Get("editor.error.finiteNumberRequired"); return false; }
        var field = fields[editField];
        history.Begin(L.Get("editor.command.changeField", field.Label));
        try
        {
            field.Apply(value);
            history.Commit();
            editField = -1;
            fieldError = "";
            StatusMessage = L.Get("editor.status.fieldChanged", field.Label);
            return true;
        }
        catch (ArgumentException ex)
        {
            history.Cancel(); fieldError = ex.Message; return false;
        }
    }

    private bool Near(MapPoint p, float x, float y, float distance)
    {
        var s = Screen(p);
        return (s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y) <= distance * distance;
    }

    private float PointerDistance(MapPoint point, float x, float y)
    {
        var p = Screen(point);
        return (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y);
    }

    private static float SegmentDistance(float x, float y, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay, length = dx * dx + dy * dy;
        float t = length > 0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length, 0, 1) : 0;
        dx = x - ax - t * dx; dy = y - ay - t * dy;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void ClampMove(MapPoint start, MapPoint desired, Func<MapPoint, bool> apply)
    {
        double low = 0, high = 1;
        for (int i = 0; i < 20; i++)
        {
            double middle = (low + high) / 2;
            if (apply(start + (desired - start) * middle)) low = middle;
            else high = middle;
        }
        apply(start + (desired - start) * low);
    }
}
