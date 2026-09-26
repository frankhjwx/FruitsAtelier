using L = FruitsAtelier.Localization.Strings;
using System.Globalization;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private double timelineMsPerDip;
    private float snapWheelRemainder, toolWheelRemainder;
    private int breakStartMs;
    private int OverviewTime(float x) => (int)Math.Clamp(Math.Round((x - overview.X) / overview.Width * TimelineDurationMs), 0, int.MaxValue);
    private float TimelineHeadX => overview.X + (float)(playhead / TimelineDurationMs) * overview.Width;
    private bool HitsTimelineHead(float x, float y) => Math.Abs(x - TimelineHeadX) <= 6
        && y >= overview.Y - 7 && y <= overview.Bottom + 2;

    public void PointerDown(float x, float y, int button, bool shift, bool ctrl)
    {
        placementCtrl = ctrl;
        if (IsTestplaying) return;
        if (ErrorVisible || DiscardConfirmationVisible)
        {
            if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
            return;
        }
        ResetTextCaret();
        mouseX = x; mouseY = y;
        timingPointerShift = shift;
        if (librarySettingsOpen)
        {
            if (LanguagePointerDown(x, y, button) || BeginIndicatorColourDrag(x, y, button)) return;
            if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
            return;
        }
        if (TimingModal) { TimingPointerDown(x, y, button); return; }
        if (!LibraryVisible && !SongSetupVisible && panelMenuOpen) { PanelPointerDown(x, y); return; }
        if (TimingPageVisible && !SongSetupVisible && menu < 0 && rightPanel.Contains(x, y)) { TimingPointerDown(x, y, button); return; }
        if (BeginVolumePopoverPointer(x, y, button)) return;
        if (SongSetupVisible) { SongSetupPointerDown(x, y, button, shift); return; }
        if (DistanceSnapDialogVisible)
        {
            dsSliderShift = shift;
            DistanceSnapPointerDown(x, y, button);
            return;
        }
        if (VolumeDialogVisible)
        {
            if (BeginVolumeDrag(x, y, button)) return;
            if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
            return;
        }
        if (legacyButtonSlider != Guid.Empty && !sliderConversionBounds.Contains(x, y)) legacyButtonSlider = Guid.Empty;
        if (updatesPage)
        {
            if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
            return;
        }
        if (TimeJumpVisible || StreamDialogVisible)
        {
            if (StreamDialogVisible && button == 0 && StreamSnapBounds.Contains(x, y))
            { streamSnapDragging = true; SetStreamSnap(x); return; }
            if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
            return;
        }
        if (LanguagePointerDown(x, y, button)) return;
        if (BeginIndicatorColourDrag(x, y, button)) return;
        if (SliderDialogVisible)
        {
            if (button == 0) for (int i = sliderDialogHits.Count - 1; i >= 0; i--)
                if (sliderDialogHits[i].Bounds.Contains(x, y)) { if (sliderDialogHits[i].Enabled) sliderDialogHits[i].Action(); break; }
            return;
        }
        if (LibraryVisible && LibraryPointerDown(x, y, button)) return;
        if (LibraryVisible || ExportVisible) { if (button == 0) for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; } return; }
        if (drag != DragKind.None) return;
        if (TimingPageVisible && menu < 0 && contextItems.Count == 0 && WaveformBounds.Contains(x, y))
        { TimingWaveformPointer(x, y, button); return; }
        if (menu < 0 && contextItems.Count == 0 && DistancePointerDown(x, y, button, shift)) return;
        if (button == 2 && DifficultyTabContext(x, y)) return;
        if (menu >= 0 && button != 0) { menu = -1; return; }
        if (button == 2)
        {
            if (editField >= 0 && !CommitField()) return;
            contextItems.Clear(); menu = -1;
            if (overview.Contains(x, y))
            {
                int time = OverviewTime(x);
                var period = OsuTimeline.Breaks(Document).FirstOrDefault(b => time >= b.StartMs && time <= b.EndMs);
                if (period.EndMs > period.StartMs) Edit(L.Get("timeline.removeBreak"), () => OsuTimeline.RemoveBreak(Document, period));
                return;
            }
            if (objectTimeline.Contains(x, y)) DeleteTimelineObject(x, y);
            else RightClickCanvas(x, y);
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
        if (BeginTabPointer(x, y)) return;
        if (editField >= 0 && !CommitField()) return;
        if (menu >= 0)
        {
            var popup = MenuBounds;
            if (popup.Contains(x, y) || (menu == 2 || menu == 4) && gridLevelMenuOpen && gridLevelMenuBounds.Contains(x, y))
            {
                for (int i = hits.Count - 1; i >= menuHitStart; i--)
                    if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
                return;
            }
            bool sameHeader = menu == 4
                ? new FruitsAtelier.App.Rendering.Rect(268, 6, 70, 28).Contains(x, y)
                : menu < 3 && new FruitsAtelier.App.Rendering.Rect(109 + menu * 53, 6, 50, 28).Contains(x, y);
            menu = -1;
            if (sameHeader) return;
            if (y >= 39) return;
        }
        if (catchPreviewVisible && PreviewResizeBounds.Contains(x, y)) { drag = DragKind.PreviewResize; return; }
        if (BeginPlaybackLineDrag(x, y)) return;
        if (zoomSlider.Contains(x, y))
        {
            SetCanvasZoom(x);
            drag = DragKind.CanvasZoom;
            return;
        }
        if (snapSlider.Contains(x, y))
        {
            SetSnapDivisor(x); drag = DragKind.SnapDivisor;
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
                FocusField(i); FocusInput("numeric:" + i, editBuffer, x); return;
            }
        if (objectTimeline.Contains(x, y))
        {
            if (!ctrl && !shift && BeginBreakEdge(x, y)) return;
            BeginObjectTimeline(x, y, ctrl); return;
        }
        if (!AudioLoading && (overview.Contains(x, y) || HitsTimelineHead(x, y)))
        {
            if (overview.Contains(x, y) && ctrl)
            {
                int time = OverviewTime(x);
                int nearest = OsuTimeline.Bookmarks(Document).Where(t => Math.Abs(t - time) * overview.Width / TimelineDurationMs <= 5)
                    .OrderBy(t => Math.Abs(t - time)).DefaultIfEmpty(-1).First();
                Edit(L.Get("timeline.bookmark"), () => OsuTimeline.ToggleBookmark(Document, nearest >= 0 ? nearest : time));
                return;
            }
            if (overview.Contains(x, y) && shift)
            {
                breakStartMs = OverviewTime(x);
                drag = DragKind.Break;
                return;
            }
            bool grabbedHead = HitsTimelineHead(x, y);
            drag = DragKind.Timeline;
            dragStartX = x;
            timelineMsPerDip = TimelineDurationMs / overview.Width;
            if (!grabbedHead) SeekTo(Math.Clamp(((double)x - overview.X) / overview.Width, 0, 1) * TimelineDurationMs);
            dragStartTime = playhead;
            return;
        }
        if (!plot.Contains(x, y)) return;
        if (!ctrl && tool == Tool.Select && SelectedDistanceObject() is { IsStandalone: false } selectedChild
            && HitCatchObject(x, y)?.SourceId != selectedChild.SourceId && HitTrackPath(x, y) != selectedChild.SourceId
            && HitSliderLocation(x, y)?.Id != selectedChild.SourceId)
        {
            SelectObjects([selectedChild.SourceId], selectedChild.SourceId);
            return;
        }
        BeginSliderHold(x, y, ctrl || shift);
        if (notesLocked && tool != Tool.Fruit && draftTrack == Guid.Empty && draftBanana == Guid.Empty)
        {
            if (HitCatchObject(x, y) is { } lockedHit) { PickObject(lockedHit.SourceId, ctrl); PickSoundEdge(lockedHit); return; }
            else if (HitSliderLocation(x, y) is { } lockedSlider) { PickObject(lockedSlider.Id, ctrl); return; }
            else if (HitBananaRectangle(x, y) is { } lockedBanana) { PickObject(lockedBanana.Id, ctrl); return; }
            else if (tool == Tool.Select || SelectedTrack is not null) { BeginBox(x, y, ctrl, false); return; }
        }
        if (tool == Tool.Fruit) { PlaceFruit(x, y); return; }
        if (tool == Tool.Slider && draftTrack != Guid.Empty && LegacyMode)
        {
            PlaceLegacyPoint(x, y, ctrl);
            return;
        }
        if (ctrl && StraightenHitPoint(x, y)) return;
        if (ctrl && tool == Tool.Select && SelectedTrack is { } parent && HitCatchObject(x, y) is { } other && other.SourceId != parent.Id)
        { PickObject(other.SourceId, true); return; }
        if (!ctrl && TryBeginSelectedSliderObjectDrag(x, y)) return;
        if (LegacyMode && HandleLegacyPointerDown(x, y, button, ctrl)) return;
        if (!LegacyMode && showTargets && ctrl && tool is Tool.Select or Tool.Slider
            && draftTrack == Guid.Empty && objectSelection.Count <= 1 && SelectedTrack is { } insertTrack)
        {
            var point = MapAt(x, y, anchorSnap);
            if (point.TimeMs > insertTrack.Nodes[0].TimeMs && point.TimeMs < CurveMath.EndTimeMs(insertTrack))
                InsertControlPoint(new(insertTrack.Id, CurveMath.FirstSpanTime(insertTrack, point.TimeMs)), point.X);
            return;
        }
        if (showTargets && ctrl && SelectedImportedSlider is { } imported && HitSliderLocation(x, y) is { } importedHit && importedHit.Id == imported.Id)
        { InsertControlPoint(importedHit); return; }
        if (!ctrl && TryBeginSelectedBananaHandle(x, y)) return;
        if (tool != Tool.Slider && !ctrl && showTargets && objectSelection.Count == 1 && SelectedTrack is { } selectedObject)
        {
            foreach (var node in selectedObject.Nodes)
                if (Near(Point(node), x, y, 9))
                {
                    tool = Tool.Slider;
                    PickAnchor(selectedObject, node, false);
                    if (LegacyMode) BeginLegacyDrag(selectedObject, Point(node), x, y);
                    else BeginNodeDrag(selectedObject, node, DragKind.Anchor, x, y);
                    return;
                }
        }
        if (tool == Tool.Slider && !LegacyMode && showTargets && SelectedTrack is { } selected)
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
                if (index > 0 && PenHandle(selected, index, true) != default && CurveMath.SegmentKind(selected, index - 1) == CurveKind.Bezier && HitPenHandle(selected, index, true, x, y))
                { BeginNodeDrag(selected, node, DragKind.HandleIn, x, y); return; }
                if (PenHandle(selected, index, false) != default && (index < selected.Nodes.Count - 1 && CurveMath.SegmentKind(selected, index) == CurveKind.Bezier || selected.Id == draftTrack) && HitPenHandle(selected, index, false, x, y))
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
            if (LegacyMode) PlaceLegacyPoint(x, y, ctrl); else AddCurveAnchor(x, y, ctrl);
            return;
        }
        var hitObject = HitCatchObject(x, y);
        bool sliderPathOverBanana = hitObject?.Kind == CatchObjectKind.Banana && showTargets && HitSliderLocation(x, y) is not null;
        if (!sliderPathOverBanana && hitObject is not null)
        {
            bool parentSelected = objectSelection.Count == 1 && objectSelection.Contains(hitObject.SourceId);
            bool editableChild = Document.Tracks.Any(t => t.Id == hitObject.SourceId);
            bool streamChild = hitObject.Kind == CatchObjectKind.Fruit
                && Document.Tracks.Any(t => t.Id == hitObject.SourceId && t.StreamSnapDivisor is not null);
            bool childSelected = distanceObject == (hitObject.SourceId, hitObject.EventIndex);
            bool sliderChild = !hitObject.IsStandalone || editableChild && hitObject.Kind == CatchObjectKind.Fruit;
            PickObject(hitObject.SourceId, ctrl);
            if (sliderChild && (!parentSelected || streamChild && !childSelected
                || !editableChild && hitObject.Kind != CatchObjectKind.Fruit))
                distanceObject = null;
            else PickSoundEdge(hitObject);
            if (ctrl) return;
            if (sliderChild)
            {
                if (parentSelected && (!streamChild || childSelected)
                    && (hitObject.Kind == CatchObjectKind.Fruit
                        || editableChild && hitObject.Kind is (CatchObjectKind.Droplet or CatchObjectKind.TinyDroplet)))
                {
                    BeginSliderObjectDrag(hitObject, x, y);
                    return;
                }
                StatusMessage = L.Get("editor.status.parentSelected", hitObject.Kind == CatchObjectKind.Banana ? L.Get("editor.object.bananaShower") : L.Get("editor.object.sliderWithSpace"), Time(hitObject.TimeMs));
                BeginObjectDrag(x, y);
                if (parentSelected && streamChild) pendingStreamChildSelection = hitObject;
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
                    if (!SegmentNearPointer(track, s, y)) continue;
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
        MoveVolumePopoverPointer(x, y);
        if (timingSnapDragging) { SetTimingSnap(x); return; }
        if (timingScrollDragging) { UpdateTimingScroll(y); return; }
        if (timingVolumeStart is not null) { UpdateTimingVolume(x); return; }
        if (TimingModal) { mouseX = x; mouseY = y; return; }
        if (volumePopoverDrag >= 0) return;
        if (textSelecting) { MoveInputSelection(x); return; }
        if (dsSnapDragging) { SetDistanceSnapSubdivision(x); return; }
        if (dsBaseDragging) { UpdateDistanceBase(x); return; }
        if (SongSetupVisible) { mouseX = x; mouseY = y; MoveSongSetup(x, y, shift); return; }
        if (dsSliderDrag >= 0) { UpdateDistanceSnapSlider(x, shift); return; }
        if (distanceDragging) { UpdateDistanceSlider(x, shift); return; }
        placementCtrl = ctrl;
        if (volumeDrag >= 0) { UpdateVolumeDrag(x); return; }
        if (IsTestplaying) return;
        mouseX = x; mouseY = y;
        if (settingsColourDrag != 0) { UpdateIndicatorColourDrag(x, y); return; }
        if (updatesPage) return;
        if (sliderHoldConsumed) return;
        if (SliderHoldNeedsRedraw && (Math.Abs(x - sliderHoldX) >= 2 || Math.Abs(y - sliderHoldY) >= 2)) { sliderHoldId = Guid.Empty; noteHoldTarget = null; }
        if (StreamDialogVisible && streamSnapDragging) { SetStreamSnap(x); return; }
        if (TimeJumpVisible || StreamDialogVisible || VolumeDialogVisible || DistanceSnapDialogVisible) return;
        if (ErrorVisible || DiscardConfirmationVisible) return;
        if (SliderDialogVisible) return;
        if (librarySettingsOpen || ExportVisible || languageMenuOpen) return;
        if (LibraryVisible) { MoveLibraryPointer(y); return; }
        if (tabPointer) { MoveTabPointer(x); return; }
        if (drag == DragKind.PreviewResize) { previewWidth = Math.Clamp(width - x, MinimumPreviewWidth, Math.Max(MinimumPreviewWidth, width * .5f)); return; }
        if (drag == DragKind.None)
        {
            if (LegacyMode && draftTrack != Guid.Empty) UpdateLegacyPreview(x, y);
            return;
        }
        if (drag == DragKind.PlaybackLine) { MovePlaybackLine(y); return; }
        if (drag == DragKind.CanvasZoom) { SetCanvasZoom(x); return; }
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
        if (drag == DragKind.BreakEdge) { MoveBreakEdge(x); return; }
        if (!dragMoved)
        {
            if (MathF.Abs(x - dragStartX) < 2 && MathF.Abs(y - dragStartY) < 2) return;
            dragMoved = true;
        }
        if (drag == DragKind.TimelineTail) { MoveTimelineTail(x); return; }
        if (drag == DragKind.LegacyControl) { MoveLegacyPoints(x, y); return; }
        if (drag == DragKind.Objects) { MoveSelectedObjects(x, y, shift); return; }
        if (drag == DragKind.SliderObject) { MoveSliderObject(x); return; }
        if (drag is DragKind.BananaStart or DragKind.BananaEnd) { MoveBananaBoundary(x, y); return; }
        var raw = Transform.ToMap(x, y) - dragOffset;
        var p = new MapPoint(Math.Clamp(raw.TimeMs, 0, EditableDurationMs), Math.Clamp(SnapX(raw.X), 0, 512));
        if (SelectedTrack is { } track && SelectedAnchor is { } node)
        {
            if (drag == DragKind.Anchor)
            {
                bool endpoint = node == track.Nodes[0] || node == track.Nodes[^1];
                bool snapTime = endpoint ? snap : anchorSnap;
                if (snapTime) p = new(Math.Clamp(TimingMap.Snap(Document, raw.TimeMs, divisor), 0, EditableDurationMs), p.X);
                // The draft's last outgoing handle is visible before its future segment exists.
                if (track.Id == draftTrack && node == track.Nodes[^1])
                    p = new(p.TimeMs, Math.Clamp(p.X, Math.Max(0, -node.HandleOut.X), Math.Min(512, 512 - node.HandleOut.X)));
                var start = Point(node);
                if (DistanceSnapEnabled)
                {
                    if (TryMoveDistanceAnchor(track, node, p))
                    {
                        Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
                        StatusMessage = L.Get("editor.status.anchorPosition", Time(SelectedAnchor!.TimeMs), Number(SelectedAnchor.X));
                    }
                    else StatusMessage = L.Get("editor.error.sliderDistanceSnap");
                }
                else if (!CurveMath.TryMoveAnchor(track, node.Id, p.TimeMs, p.X, out var error))
                {
                    if (endpoint && snapTime)
                        CurveMath.TryMoveAnchor(track, node.Id, start.TimeMs, p.X, out _);
                    else
                        ClampMove(start, p, value => CurveMath.TryMoveAnchor(track, node.Id, value.TimeMs, value.X, out _));
                    StatusMessage = error;
                }
                else
                {
                    Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
                    StatusMessage = L.Get("editor.status.anchorPosition", Time(node.TimeMs), Number(node.X));
                }
            }
            else if (drag == DragKind.DraftHandle)
            {
                var cursor = MapAt(x, y, false);
                double dt = cursor.TimeMs - node.TimeMs;
                double dx = Math.Clamp(cursor.X - node.X, -node.X, 512 - node.X);
                bool SetDraftHandle(double offset)
                {
                    return TryDistanceShape(track, () =>
                    {
                        var current = track.Nodes.Single(item => item.Id == node.Id);
                        current.HandleOut = new(dt, offset);
                        if (track.Nodes.Count > 1)
                        {
                            var previous = track.Nodes[^2];
                            current.HandleIn = new(-dt, Math.Clamp(-offset, -current.X, 512 - current.X));
                            previous.OutgoingKind = previous.HandleOut != default || current.HandleIn != default ? CurveKind.Bezier : CurveKind.Linear;
                        }
                        return true;
                    });
                }
                bool handleAccepted = SetDraftHandle(dx);
                if (!handleAccepted && DistanceSnapEnabled)
                    foreach (var candidate in CurvedSliderCandidates(new(0, dx), node.HandleOut.X).Skip(1))
                        if (SetDraftHandle(candidate.X)) { handleAccepted = true; break; }
                selectedPart = DragKind.HandleOut;
                StatusMessage = L.Get(handleAccepted ? "editor.status.definingHandle" : "editor.error.sliderDistanceSnap");
            }
            else
            {
                bool incoming = drag == DragKind.HandleIn;
                int segment = track.Nodes.IndexOf(node) - (incoming ? 1 : 0);
                try
                {
                    ControlCurveEditing.ConvertToPen(track, segment);
                }
                catch (ArgumentException ex) { StatusMessage = ex.Message; return; }
                var start = incoming ? node.HandleIn : node.HandleOut;
                var cursor = Transform.ToMap(x, y) - dragOffset;
                var desired = cursor - Point(node);
                desired = new(desired.TimeMs, Math.Clamp(desired.X, -node.X, 512 - node.X));
                bool TryHandle(MapPoint value) => TryDistanceShape(track, () =>
                    CurveMath.TryMoveHandle(track, node.Id, incoming, value, out _));
                if (DistanceSnapEnabled)
                {
                    bool accepted = TryHandle(desired);
                    if (!accepted)
                    {
                        foreach (var candidate in CurvedSliderCandidates(desired, start.X).Skip(1))
                            if (TryHandle(candidate)) { accepted = true; break; }
                    }
                    StatusMessage = L.Get(accepted ? "editor.status.handleAdjusted" : "editor.error.sliderDistanceSnap");
                }
                else if (!CurveMath.TryMoveHandle(track, node.Id, incoming, desired, out var error))
                {
                    ClampMove(start, desired, value => CurveMath.TryMoveHandle(track, node.Id, incoming, value, out _));
                    StatusMessage = error;
                }
                else StatusMessage = L.Get("editor.status.handleAdjusted");
            }
        }
    }

    public void PointerUp(float x, float y, int button, bool shift = false)
    {
        if (timingSnapDragging) { SetTimingSnap(x); timingSnapDragging = false; return; }
        if (timingScrollDragging && button == 0) { UpdateTimingScroll(y); timingScrollDragging = false; return; }
        if (timingVolumeStart is not null) { UpdateTimingVolume(x); EndTimingVolume(false); return; }
        if (TimingModal) return;
        if (EndVolumePopoverPointer(x, y, button)) return;
        if (settingsColourDrag != 0 && button == 0) { UpdateIndicatorColourDrag(x, y); settingsColourDrag = 0; return; }
        if (textSelecting && button == 0) { MoveInputSelection(x); textSelecting = false; return; }
        if (SongSetupVisible) { if (button == 0) { MoveSongSetup(x, y, shiftHeld); songDrag = -1; } return; }
        if (distanceDragging && button == 0) { UpdateDistanceSlider(x, shiftHeld); distanceDragging = false; return; }
        if (updatesPage) return;
        if (dsSnapDragging && button == 0) { SetDistanceSnapSubdivision(x); dsSnapDragging = false; return; }
        if (dsBaseDragging && button == 0) { UpdateDistanceBase(x); dsBaseDragging = false; return; }
        if (dsSliderDrag >= 0 && button == 0) { UpdateDistanceSnapSlider(x, dsSliderShift); dsSliderDrag = -1; return; }
        if (volumeDrag >= 0 && button == 0) { UpdateVolumeDrag(x); FinishVolumeDrag(); return; }
        if (button == 0)
        {
            sliderHoldId = Guid.Empty; noteHoldTarget = null;
            if (sliderHoldConsumed) { sliderHoldConsumed = false; return; }
        }
        if (IsTestplaying) return;
        if (tabPointer && button == 0)
        {
            MoveTabPointer(x); tabPointer = false;
            if (!tabMoved) SwitchDifficulty(tabPressed);
            return;
        }
        if (StreamDialogVisible && streamSnapDragging && button == 0)
        { SetStreamSnap(x); streamSnapDragging = false; return; }
        if (TimeJumpVisible || StreamDialogVisible || VolumeDialogVisible || DistanceSnapDialogVisible) return;
        if (ErrorVisible || DiscardConfirmationVisible) return;
        if (SliderDialogVisible) return;
        if (LibraryVisible) { if (button == 0) EndLibraryPointer(x, y); return; }
        if (librarySettingsOpen || ExportVisible) return;
        if (drag == DragKind.None || button != (drag == DragKind.Pan ? 1 : 0)) return;
        if (drag == DragKind.Break)
        {
            int end = OverviewTime(x);
            if (Math.Abs(end - breakStartMs) >= 1)
                Edit(L.Get("timeline.addBreak"), () => OsuTimeline.AddBreak(Document, Math.Min(end, breakStartMs), Math.Max(end, breakStartMs)));
            drag = DragKind.None;
            return;
        }
        if (drag == DragKind.BreakEdge)
        {
            MoveBreakEdge(x);
            FinishBreakEdge();
            return;
        }
        PointerMove(x, y, shift, false);
        if (drag == DragKind.PlaybackLine) { FinishPlaybackLineDrag(); return; }
        if (drag == DragKind.Marquee) { FinishBox(x, y); return; }
        if (draftTrack == Guid.Empty && drag is DragKind.Objects or DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd or DragKind.LegacyControl or DragKind.TimelineTail)
        {
            if (Document.DerandomizeDroplets is null && Document.Tracks.Any(track => pendingImplicitSliderConversions.Contains(track.Id)))
                Document.DerandomizeDroplets = LibrarySettings.DerandomizeDroplets;
            history.Commit();
        }
        pendingImplicitSliderConversions.Clear();
        if (drag == DragKind.SliderObject)
        {
            sliderObjectDragTarget = null;
            sliderObjectDragSource = sliderObjectDragShape = null;
            sliderObjectDragPrevious = null;
        }
        if (draftTrack != Guid.Empty && drag == DragKind.Anchor && !dragMoved
            && SelectedTrack is { } draft && SelectedAnchor == draft.Nodes[^1])
        {
            draft.Nodes[^1].HandleOut = default;
            draftStraight = true;
        }
        if (drag is DragKind.Objects or DragKind.BananaStart or DragKind.BananaEnd)
        {
            objectDragStart = null;
            dragFruits.Clear(); dragTracks.Clear(); dragBananas.Clear();
            objectDragPrepared = false;
            if (AudioPlaying || pinPlayhead) FollowPlayhead();
        }
        if (pendingStreamChildSelection is { } streamChildSelection && !dragMoved && objectSelection.Contains(streamChildSelection.SourceId))
            PickSoundEdge(streamChildSelection);
        pendingStreamChildSelection = null;
        drag = DragKind.None;
    }

    public void PointerDoubleClick(float x, float y, bool shift, bool ctrl)
    {
        if (TimingModal || TimingPageVisible && rightPanel.Contains(x, y))
        {
            var field = timingFields.FirstOrDefault(f => f.Bounds.Contains(x, y));
            if (field.Key is not null && CommitTimingField())
            { timingField = field.Key; timingText = field.Value; timingFieldApply = field.Apply; SelectInput("timing:" + field.Key, timingText); }
            return;
        }
        if (SongSetupVisible)
        {
            foreach (var pair in songFieldBounds)
                if (pair.Value.Contains(x, y) && SongFieldEnabled(pair.Key))
                { songField = pair.Key; SelectInput("song:" + pair.Key, songValues[pair.Key]); return; }
            return;
        }
        if (DistanceEditing)
        {
            if (textLayouts.TryGetValue("distance", out var layout) && layout.Content.Contains(x, y))
                SelectInput("distance", editBuffer);
            else PointerDown(x, y, 0, shift, ctrl);
            return;
        }
        if (updatesPage) return;
        if (IsTestplaying) return;
        if (notesLocked && plot.Contains(x, y)) { PointerDown(x, y, 0, shift, ctrl); return; }
        if (StreamDialogVisible || VolumeDialogVisible || DistanceSnapDialogVisible) return;
        if (TimeJumpVisible) { if (TimeJumpInputBounds.Contains(x, y)) SelectInput("time", timeJumpText); return; }
        if (ErrorVisible || DiscardConfirmationVisible) return;
        if (SliderDialogVisible) return;
        if ((librarySettingsOpen || ExportVisible || LibraryVisible) && SelectLibraryInputAt(x, y)) return;
        if (librarySettingsOpen || ExportVisible) return;
        if (LibraryVisible) { if (!libraryPointerMoved && contextItems.Count == 0 && !languageMenuOpen) OpenLibraryCard(x, y); return; }
        for (int i = 0; i < fields.Count; i++)
            if (fields[i].Bounds.Contains(x, y))
            { FocusField(i); SelectInput("numeric:" + i, editBuffer); return; }
        if (ctrl || tool == Tool.Fruit) { PointerDown(x, y, 0, shift, ctrl); return; }
        if (!LegacyMode && draftTrack != Guid.Empty && SelectedTrack is { } draft && Near(Point(draft.Nodes[^1]), x, y, 8))
        { draft.Nodes[^1].HandleOut = default; draftStraight = true; return; }
        if (menu < 0 && contextItems.Count == 0 && editField < 0 && LegacyDoubleClick(x, y)) return;
        if (drag != DragKind.None || buttonTargetIsUnavailable()) return;
        Guid sourceId = Guid.Empty;
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
        StatusMessage = "";

        bool buttonTargetIsUnavailable() => editField >= 0 || draftTrack != Guid.Empty || draftBanana != Guid.Empty || menu >= 0 || contextItems.Count > 0;
    }

    public void Wheel(float x, float y, float delta, bool ctrl, bool shift = false, bool alt = false)
    {
        if (librarySettingsOpen) return;
        if (TimingModal)
        { if (TimingSetupVisible && timingListBounds.Contains(x, y)) timingScroll = Math.Max(0, timingScroll - (int)(delta / 120) * 3); return; }
        if (HandleVolumePopoverWheel(x, y, delta)) return;
        if (TimingPageVisible && WaveformBounds.Contains(x, y) && !SongSetupVisible && !LibraryVisible)
        {
            if ((alt || altHeld) && !ctrl && !shift && !shiftHeld)
                waveformSpanMs = Math.Clamp(waveformSpanMs / Math.Pow(1.25, delta / 120), 100, Math.Max(10000, TimelineDurationMs * 2));
            else if (!alt && !altHeld && !ctrl) SeekByWheel(-delta / 120 * (shift ? 4 : 1), 0);
            return;
        }
        shift |= shiftHeld;
        alt |= altHeld;
        if (SongSetupVisible) return;
        if (DistanceSnapDialogVisible)
        {
            return;
        }
        if (updatesPage) return;
        if (IsTestplaying)
        {
            if (alt && !ctrl && !shift) AdjustVolumeWheel(delta);
            return;
        }
        if (TimeJumpVisible || StreamDialogVisible || VolumeDialogVisible || DistanceSnapDialogVisible) return;
        if (languageMenuOpen) return;
        if (ErrorVisible)
        {
            errorScroll = Math.Clamp(errorScroll - (int)(delta / 120) * 3, 0, Math.Max(0, errorLineCount - errorVisibleLines));
            return;
        }
        if (ErrorVisible || DiscardConfirmationVisible) return;
        if (SliderDialogVisible) return;
        if (librarySettingsOpen || ExportVisible) return;
        if (LibraryVisible)
        {
            if (languageMenuOpen || contextItems.Count > 0 || librarySettingsOpen) return;
            if (x >= width - 330) libraryDiffScroll = Math.Max(0, libraryDiffScroll - (int)(delta / 120));
            else libraryScroll = Math.Clamp(libraryScroll - delta / 120 * 3, 0, LibraryMaxScroll);
            RememberLibraryPosition(); return;
        }
        if (drag == DragKind.Marquee)
        {
            if (!ctrl && (canvas.Contains(x, y) || objectTimeline.Contains(x, y)))
            {
                mouseX = x; mouseY = y;
                SeekByWheel(-delta / 120, boxTimeline ? 1 : 0);
                MoveBox(x, y);
            }
            return;
        }
        if (drag != DragKind.None) return;
        if (contextItems.Count > 0) { contextItems.Clear(); return; }
        if (assistPalette.Contains(x, y)) { assistScroll = Math.Clamp(assistScroll - delta / 120 * 64, 0, assistScrollLimit); return; }
        if (difficultyTabStrip.Contains(x, y))
        {
            tabRemainder = 0;
            firstDifficultyTab = Math.Clamp(firstDifficultyTab + (delta < 0 ? 1 : delta > 0 ? -1 : 0), 0,
                Math.Max(0, difficulties.Count - visibleDifficultyTabs));
            return;
        }
        if (rightPanel.Contains(x, y))
        {
            return;
        }
        bool onTimeline = objectTimeline.Contains(x, y);
        bool onCanvas = canvas.Contains(x, y);
        bool onOverview = overview.Contains(x, y);
        if ((onTimeline || onCanvas) && ctrl && alt && !shift)
        {
            toolWheelRemainder += delta;
            int steps = (int)Math.Truncate(toolWheelRemainder / 120);
            toolWheelRemainder -= steps * 120;
            if (steps != 0) ChangeTool((Tool)(((int)tool + ((steps % 4) + 4) % 4) % 4));
            return;
        }
        if ((onTimeline || onCanvas || onOverview) && ctrl && !alt && !shift)
        {
            snapWheelRemainder += delta;
            int steps = (int)Math.Truncate(snapWheelRemainder / 120);
            snapWheelRemainder -= steps * 120;
            if (steps != 0)
            {
                ForgetTemporarySnap();
                int current = Array.IndexOf(SnapDivisors, divisor);
                divisor = SnapDivisors[((current + steps) % SnapDivisors.Length + SnapDivisors.Length) % SnapDivisors.Length];
            }
            return;
        }
        if (objectTimeline.Contains(x, y))
        {
            if (alt && !ctrl && !shift) ZoomObjectTimeline(Math.Pow(1.25, delta / 120));
            else if (!ctrl && !alt) SeekByWheel(-delta / 120 * (shift ? 4 : 1), 1);
            return;
        }
        if (onOverview)
        {
            if (!AudioLoading && !ctrl && !alt) SeekByWheel(-delta / 120 * (shift ? 4 : 1), 2);
            return;
        }
        if (!onCanvas) return;
        if (!AudioPlaying) pinPlayhead = false;
        if (alt && !ctrl && !shift)
        {
            ZoomCanvasAt(y, Math.Pow(1.16, delta / 120));
            StatusMessage = L.Get("editor.status.canvasZoom", canvasZoom * 100);
        }
        else if (!ctrl && !alt) SeekByWheel(-delta / 120 * (shift ? 4 : 1), 0);
        ClampView();
    }

    private double wheelRemainder, wheelPlayhead = double.NaN;
    private int wheelDivisor, wheelSurface = -1;

    private void SeekByWheel(double delta, int surface)
    {
        int stepDivisor = AudioPlaying ? 1 : divisor;
        if (double.IsNaN(wheelPlayhead) || (!AudioPlaying && wheelPlayhead != playhead)
            || wheelDivisor != stepDivisor || wheelSurface != surface) wheelRemainder = 0;
        double ticks = delta + wheelRemainder;
        double steps = Math.Truncate(ticks);
        wheelRemainder = ticks - steps;
        wheelPlayhead = playhead; wheelDivisor = stepDivisor; wheelSurface = surface;
        if (steps == 0) return;
        var timing = new TimingMap.Lookup(Document);
        var boundaries = Document.TimingPoints.Where(t => t.Uninherited).Select(t => t.TimeMs).Distinct().Order().ToArray();
        double target = playhead;
        int direction = Math.Sign(steps);
        for (double i = 0; i < Math.Abs(steps); i++)
        {
            var state = timing.At(direction < 0 ? Math.BitDecrement(target) : target);
            double step = state.BeatLengthMs / stepDivisor;
            double index = (target - state.OffsetMs) / step;
            double nearest = Math.Round(index);
            if (Math.Abs(state.OffsetMs + nearest * step - target) < 1e-7) index = nearest;
            double next = state.OffsetMs + (direction > 0 ? Math.Floor(index) + 1 : Math.Ceiling(index) - 1) * step;
            // Red timing boundaries are grid lines even when the preceding beat is incomplete.
            next = direction > 0
                ? Math.Min(next, boundaries.FirstOrDefault(t => t > target + 1e-7, double.PositiveInfinity))
                : Math.Max(next, boundaries.LastOrDefault(t => t < target - 1e-7, double.NegativeInfinity));
            target = Math.Clamp(next, 0, TimelineDurationMs);
            if (target == 0 || target == TimelineDurationMs) { wheelRemainder = 0; break; }
        }
        double nextView = viewStart + target - playhead;
        if (drag == DragKind.Marquee) ScrollBoxTo(target);
        else SeekTo(target);
        wheelPlayhead = playhead; wheelDivisor = stepDivisor; wheelSurface = surface;
        if (surface == 0 && drag != DragKind.Marquee) { viewStart = nextView; pinPlayhead = false; }
    }

    public void KeyDown(int virtualKey, bool ctrl, bool shift)
    {
        if (DistanceKeyDown(virtualKey, ctrl, shift)) return;
        placementCtrl = ctrl;
        if (virtualKey == 27 && legacyButtonSlider != Guid.Empty)
        { legacyButtonSlider = Guid.Empty; return; }
        sliderHoldId = legacyButtonSlider = Guid.Empty; noteHoldTarget = null;
        if (virtualKey == 27 && testplayEscapeConsumed) return;
        if (IsTestplaying)
        {
            if (AdjustVolumeShortcut(virtualKey, altHeld && !ctrl && !shift)) return;
            if (virtualKey == 27) { testplayEscapeConsumed = true; StopTestplay(); }
            else if (virtualKey == 112) StopTestplay();
            else if (virtualKey == 113) { AdvanceTestplay(); StopTestplay(atCurrentPosition: true); }
            else if (ctrl && virtualKey == 80)
            {
                if (!testplayPauseHeld) { testplayPauseHeld = true; ToggleTestplayPause(); }
            }
            else if (ctrl && virtualKey == 66)
            {
                if (!testplayBookmarkHeld)
                {
                    testplayBookmarkHeld = true;
                    AdvanceTestplay();
                    if (IsTestplaying)
                    {
                        int time = (int)Math.Clamp(Math.Round(playhead), 0, int.MaxValue);
                        Edit(L.Get("timeline.bookmark"), () =>
                        {
                            if (shift) OsuTimeline.RemoveNearestBookmark(Document, time);
                            else OsuTimeline.AddBookmark(Document, time);
                        });
                    }
                }
            }
            else if (virtualKey == 9)
            {
                if (!testplayTabHeld) { testplayTabHeld = true; testplay!.ToggleAutoplay(); }
                AdvanceTestplay();
            }
            else
            {
                if (testplayDriver is null) testplay!.SetKey(virtualKey, true);
                AdvanceTestplay();
            }
            return;
        }
        if (CapturingTestplayKey && librarySettingsOpen && !ErrorVisible && !DiscardConfirmationVisible)
        { CaptureTestplayKey(virtualKey); return; }
        ResetTextCaret();
        if (ErrorVisible)
        {
            if (virtualKey is 27 or 13) DismissError();
            return;
        }
        if (DiscardConfirmationVisible)
        {
            if (virtualKey is 27 or 13) AnswerDiscard(2);
            return;
        }
        if (updatesPage) { if (virtualKey == 27) updatesPage = false; return; }
        if (SongSetupVisible) { SongSetupKey(virtualKey, ctrl, shift); return; }
        if (DistanceSnapDialogVisible) { DistanceSnapKey(virtualKey, ctrl, shift); return; }
        if (VolumeDialogVisible) { if (virtualKey == 27) CloseVolumeDialog(); return; }
        if (StreamDialogVisible) { StreamKey(virtualKey); return; }
        if (TimeJumpVisible) { TimeJumpKey(virtualKey, ctrl, shift); return; }
        if (SliderDialogVisible)
        {
            if (virtualKey == 27)
            { if (SliderImportPromptVisible) AnswerSliderImport(false); else if (SliderConversionBusy) CancelSliderConversion(); else sliderBatchErrors = []; }
            return;
        }
        if (ExportVisible) { ExportKey(virtualKey, ctrl, shift); return; }
        if (languageMenuOpen)
        {
            if (!ctrl && !shift && !altHeld)
            {
                if (virtualKey == 27) languageMenuOpen = false;
                else if (virtualKey is 38 or 40) languageSelection = (languageSelection + (virtualKey == 38 ? L.AvailableLanguages.Count - 1 : 1)) % L.AvailableLanguages.Count;
                else if (virtualKey == 13) SelectLanguage(L.AvailableLanguages[languageSelection]);
            }
            return;
        }
        if (librarySettingsOpen || LibraryVisible) { LibraryKey(virtualKey, ctrl, shift); return; }
        if (TimingKey(virtualKey, ctrl, shift)) return;
        if (virtualKey == 117 && !ctrl && !shift && !altHeld) { OpenTimingSetup(); return; }
        if (virtualKey == 114 && !ctrl && !shift && !altHeld) { ShowTimingPage(true); return; }
        if (virtualKey == 115 && !ctrl && !shift && !altHeld && drag == DragKind.None)
        { OpenSongSetup(); return; }
        if (virtualKey == 116 && !ctrl && !shift && !altHeld) { StartTestplay(); return; }
        if (ctrl && !shift && !altHeld && virtualKey == 83 && drag == DragKind.None)
        {
            RequestSave?.Invoke();
            return;
        }
        if (ctrl && altHeld && !shift && virtualKey == 69 && drag == DragKind.None)
        { RequestExport?.Invoke(); return; }
        if (ctrl && shift && !altHeld && virtualKey == 70)
        {
            if (!dragMoved && draftTrack == Guid.Empty && draftBanana == Guid.Empty
                && drag is DragKind.Objects or DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.LegacyControl)
            { history.Commit(); drag = DragKind.None; sliderHoldConsumed = false; }
            if (editField >= 0 && !CommitField()) return;
            OpenStreamDialog();
            return;
        }
        if (editField >= 0)
        {
            if (virtualKey == 27) { editField = -1; fieldError = ""; return; }
            if (ctrl && virtualKey == 65) { SelectInput("numeric:" + editField, editBuffer); return; }
            if (virtualKey is 13 or 9)
            {
                int current = editField;
                if (CommitField() && virtualKey == 9 && fields.Count > 0)
                    FocusField((current + (shift ? fields.Count - 1 : 1)) % fields.Count);
                return;
            }
            InputKey("numeric:" + editField, ref editBuffer, virtualKey, ctrl, shift, 30, RequestPasteField);
            replaceText = false;
            return;
        }
        if (virtualKey == 27)
        {
            if (VolumePopoverVisible) { CloseVolumePopover(); return; }
            if (contextItems.Count > 0) { contextItems.Clear(); return; }
            if (drag != DragKind.None || draftTrack != Guid.Empty || draftBanana != Guid.Empty) CancelInteraction();
            else if (menu >= 0) menu = -1;
            else ShowLibrary();
            return;
        }
        if (AdjustVolumeShortcut(virtualKey, altHeld && !ctrl && !shift)) return;
        if (ctrl && !altHeld)
        {
            if (drag != DragKind.None) return;
            contextItems.Clear();
            if (virtualKey == 66)
            {
                int time = (int)Math.Clamp(Math.Round(playhead), 0, int.MaxValue);
                Edit(L.Get("timeline.bookmark"), () =>
                {
                    if (shift) OsuTimeline.RemoveNearestBookmark(Document, time);
                    else OsuTimeline.AddBookmark(Document, time);
                });
                return;
            }
            if (virtualKey is 37 or 39)
            {
                if (shift) NudgeSelection(0, virtualKey == 37 ? -1 : 1);
                else SeekBookmark(virtualKey == 39);
                return;
            }
            if (ClipboardInteractionReady && HandleLegacyShortcut(virtualKey, shift)) return;
            if (virtualKey == 90) { if (shift) Redo(); else Undo(); }
            else if (virtualKey == 89) Redo();
            else if (virtualKey == 9) SwitchDifficulty((activeDifficulty + (shift ? difficulties.Count - 1 : 1)) % difficulties.Count);
            else if (virtualKey == 79) { if (shift) RequestOpen?.Invoke(); else OpenDifficultyChooser(); }
            else if (virtualKey == 83 && !shift) RequestSave?.Invoke();
            else if (virtualKey == 67 && !shift) CopySelection();
            else if (virtualKey == 88 && !shift) CutSelection();
            else if (virtualKey == 86 && !shift) PasteSelection();
            else if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
            else if (virtualKey == 71 && !shift) ReverseSelectedPath();
            else if (virtualKey == 76 && !shift) TogglePointCurve();
            else if (virtualKey == 73 && !shift) DeleteCurrentTiming();
            else if (virtualKey == 73 && shift && plot.Contains(mouseX, mouseY) && HitSliderLocation(mouseX, mouseY) is { } location) InsertControlPoint(location);
            else if (virtualKey == 80) AddTimingPoint(shift);
            else if (virtualKey == 187 && !shift && SelectedTrack is { } addReverse) ChangeReverseCount(addReverse.Id, 1);
            else if (virtualKey == 189 && !shift && SelectedTrack is { } removeReverse) ChangeReverseCount(removeReverse.Id, -1);
            else if (virtualKey == 74 && !shift && plot.Contains(mouseX, mouseY) && SelectedTrack is { } extend) ExtendSlider(extend.Id, MapAt(mouseX, mouseY, true));
            return;
        }
        if (ctrl || altHeld) return;
        if (drag != DragKind.None) return;
        contextItems.Clear();
        if (draftTrack != Guid.Empty && !shift && virtualKey is >= 49 and <= 52)
        {
            ChangeTool(virtualKey switch { 49 => Tool.Select, 50 => Tool.Fruit, 51 => Tool.Slider, _ => Tool.Banana });
            return;
        }
        if ((draftTrack == Guid.Empty && draftBanana == Guid.Empty ||
             draftTrack != Guid.Empty && shift && virtualKey is >= 49 and <= 57)
            && HandleLegacyKey(virtualKey, shift)) return;
        if (shift) return;
        switch (virtualKey)
        {
            case 71: gridSize = gridSize == 32 ? 4 : gridSize * 2; break;
            case 84: gridSnap = !gridSnap; break;
            case 81: ToggleCombo(); break;
            case 87: ToggleSound(2); break;
            case 69: ToggleSound(4); break;
            case 82: ToggleSound(8); break;
            case 89: distanceSnap = !distanceSnap; break;
            case 76: ToggleNotesLock(); break;
            case 13: FinishCurve(); break;
            case 46: DeleteSelection(); break;
            case 70: ChangeTool(Tool.Fruit); break;
            case 66: ChangeTool(Tool.Slider); break;
            case 78: ChangeTool(Tool.Banana); break;
            case 32: TogglePlayback(); break;
            case 36: viewStart = 0; SeekTo(0); break;
        }
    }

    public void TextInput(char value)
    {
        if (TimingModal || TimingPageVisible && timingField.Length > 0)
        { if (!char.IsControl(value)) PasteTimingText(value.ToString(), TimingInputSession); return; }
        if (SongSetupVisible) { if (!char.IsControl(value)) PasteSongSetupText(value.ToString(), SongSetupInputSession); return; }
        if (DistanceSnapDialogVisible)
        {
            if (dsBaseFocused && (char.IsAsciiDigit(value) || value == '.'))
                SetDistanceBaseText(InsertInput("ds:base", dsBaseText, value.ToString(), 16));
            return;
        }
        if (DistanceEditing) { DistanceTextInput(value); return; }
        if (updatesPage) return;
        if (StreamDialogVisible || VolumeDialogVisible || DistanceSnapDialogVisible) return;
        if (IsTestplaying || CapturingTestplayKey) return;
        if (languageMenuOpen) return;
        ResetTextCaret();
        if (ErrorVisible || DiscardConfirmationVisible) return;
        if (TimeJumpVisible)
        {
            if (!char.IsControl(value)) { timeJumpText = InsertInput("time", timeJumpText, value.ToString(), 128); timeJumpError = ""; }
            return;
        }
        if (SliderDialogVisible) return;
        if (librarySettingsOpen || LibraryVisible || ExportVisible) { if (libraryField >= 0 && !char.IsControl(value)) LibraryFieldValue = InsertInput("library:" + libraryField, LibraryFieldValue, value.ToString(), 4096); return; }
        if (editField < 0 || char.IsControl(value)) return;
        if (!(char.IsAsciiDigit(value) || value is '.' or '-' or '+' or 'e' or 'E' || value == ':' && fields[editField].Timestamp)) return;
        editBuffer = InsertInput("numeric:" + editField, editBuffer, value.ToString(), 30);
        replaceText = false;
        fieldError = "";
    }

    public void CancelInteraction(bool preserveTestplay = false)
    {
        placementCtrl = false; timingTapHeld = false; timingSnapDragging = false; panelMenuOpen = false;
        EndTimingVolume(true);
        timingScrollDragging = false;
        pendingStreamChildSelection = null;
        pendingImplicitSliderConversions.Clear();
        textSelecting = false;
        songDrag = -1;
        CancelPlaybackLineDrag();
        CancelDistanceSnapDrag();
        FinishDistanceEdit(true);
        FinishVolumeDrag();
        testplayEscapeConsumed = false;
        streamSnapDragging = false;
        CloseVolumePopover();
        sliderHoldId = legacyButtonSlider = Guid.Empty; noteHoldTarget = null;
        sliderHoldConsumed = false;
        if (preserveTestplay) testplay?.ReleaseKeys();
        else StopTestplay();
        bindingCapture = -1;
        SetModifiers(false, false);
        tabPointer = false;
        if (libraryPointerActive) { libraryPointerActive = false; libraryPressedMap = null; RememberLibraryPosition(); }
        if (drag == DragKind.Marquee) { CancelBox(); contextItems.Clear(); return; }
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || drag is DragKind.Objects or DragKind.SliderObject or DragKind.Anchor or DragKind.HandleIn or DragKind.HandleOut or DragKind.BananaStart or DragKind.BananaEnd or DragKind.LegacyControl or DragKind.TimelineTail)
        {
            history.Cancel();
            if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) Select(Guid.Empty);
            StatusMessage = L.Get("editor.status.editCancelled");
        }
        drag = DragKind.None;
        sliderObjectDragTarget = null;
        sliderObjectDragSource = sliderObjectDragShape = null;
        sliderObjectDragPrevious = null;
        objectDragStart = null;
        dragFruits.Clear(); dragTracks.Clear(); dragBananas.Clear();
        objectDragPrepared = false;
        draftTrack = Guid.Empty;
        legacyDraft = null; legacyPreviewVertices = null; legacyDragStart = null;
        penPreview.Clear();
        draftBanana = Guid.Empty;
        editField = -1;
        fieldError = "";
        menu = -1;
        contextItems.Clear();
    }

    private void AddCurveAnchor(float x, float y, bool straight = false)
    {
        var p = draftTrack != Guid.Empty && DistanceSnapEnabled
            ? MapAt(x, y, true) with { X = Math.Clamp(SnapX(MapAt(x, y, true).X), 0, 512) }
            : PlacementPoint(x, y);
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
        if (track.Nodes.Count > 0 && Near(Point(track.Nodes[^1]), x, y, 8))
        { track.Nodes[^1].HandleOut = default; return; }
        var node = AppendDistanceAnchor(track, p, straight);
        if (node is null)
        {
            StatusMessage = DistanceSnapEnabled && track.Nodes.Count > 0
                && p.TimeMs > track.Nodes[^1].TimeMs + CurveMath.MinimumAnchorSpacingMs
                ? L.Get("editor.error.sliderDistanceSnap") : L.Get("editor.error.anchorMustBeLater");
            return;
        }
        if (track.Nodes.Count == 1) ApplyPlacementFlags(track.Id);
        Document.DurationMs = Math.Max(Document.DurationMs, node.TimeMs);
        Select(node.Id, track.Id);
        draftStraight = straight;
        drag = straight ? DragKind.None : DragKind.DraftHandle;
        BeginPointerDrag(x, y);
        dragOffset = new(0, 0);
        StatusMessage = "";
    }

    private Anchor? AppendDraftAnchor(CurveTrack track, MapPoint p, bool straight)
    {
        var node = new Anchor { TimeMs = p.TimeMs, X = p.X };
        if (track.Nodes.Count > 0)
        {
            var previous = track.Nodes[^1];
            double dt = p.TimeMs - previous.TimeMs;
            if (dt < 0.001)
                return null;
            if (straight) { previous.HandleOut = default; node.HandleIn = default; }
            previous.OutgoingKind = straight || previous.HandleOut == default ? CurveKind.Linear : CurveKind.Bezier;
        }
        track.Nodes.Add(node);
        if (!straight && track.Nodes.Count > 1)
        {
            var previous = track.Nodes[^2];
            if (draftStraight)
            {
                previous.HandleOut = (Point(node) - Point(previous)) * (1.0 / 3);
                previous.OutgoingKind = CurveKind.Bezier;
            }
            else CurvePointEditing.SetCurved(track, previous.Id, true);
        }
        return node;
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
        if (nextFruitNewCombo) ObjectFlags.SetNewCombo(Document, shower.Id, true);
        nextFruitNewCombo = false;
        Document.DurationMs = Math.Max(Document.DurationMs, time);
        draftBanana = shower.Id;
        SelectObjects([shower.Id], shower.Id);
        StatusMessage = L.Get("editor.status.bananaStarted", Time(time));
    }

    private void FinishBanana(float x, float y)
    {
        if (draftBanana == Guid.Empty)
        {
            StatusMessage = "";
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
        StatusMessage = L.Get("editor.status.bananaFinished", Time(shower.TimeMs), Time(end));
    }

    private void BeginNodeDrag(CurveTrack track, Anchor node, DragKind kind, float x, float y)
    {
        Select(node.Id, track.Id);
        if (HitCatchObject(x, y) is { } item) PickSoundEdge(item);
        selectedPart = kind;
        if (draftTrack == Guid.Empty) history.Begin(kind == DragKind.Anchor ? L.Get("editor.command.moveAnchor") : L.Get("editor.command.adjustHandle"));
        drag = kind;
        BeginPointerDrag(x, y);
        var original = Point(node);
        if (kind == DragKind.HandleIn) original += PenHandle(track, track.Nodes.IndexOf(node), true);
        else if (kind == DragKind.HandleOut) original += PenHandle(track, track.Nodes.IndexOf(node), false);
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
        ForgetTemporarySnap();
        float left = snapSlider.X + 7, right = snapSlider.Right - 31;
        int index = (int)MathF.Round(Math.Clamp((x - left) / (right - left), 0, 1) * (SnapDivisors.Length - 1));
        divisor = SnapDivisors[index];
        snap = true;
    }

    private void NavigateTime(float x)
    {
        // Keep the original time authoritative; never reconstruct it from the rounded painted head.
        double time = Math.Clamp(dragStartTime + ((double)x - dragStartX) * timelineMsPerDip, 0, TimelineDurationMs);
        if (time != playhead) SeekTo(time);
    }

    private void FocusField(int index)
    {
        editField = index;
        editBuffer = fields[index].Timestamp ? Time(fields[index].Value) : fields[index].Value.ToString("G17", CultureInfo.InvariantCulture);
        fieldError = "";
        replaceText = true;
        SelectInput("numeric:" + index, editBuffer);
    }

    private bool CommitField()
    {
        if (editField < 0 || editField >= fields.Count) { editField = -1; return true; }
        var field = fields[editField];
        if (field.Timestamp && replaceText && editBuffer == Time(field.Value)) { editField = -1; fieldError = ""; return true; }
        string input = editBuffer;
        if (field.Timestamp && input.Contains(':'))
        {
            var parts = input.Split(':');
            if (parts.Length != 3 || !long.TryParse(parts[0], out long minutes) || minutes < 0
                || !int.TryParse(parts[1], out int seconds) || seconds is < 0 or >= 60
                || !int.TryParse(parts[2], out int milliseconds) || milliseconds is < 0 or >= 1000)
            { fieldError = L.Get("editor.error.timestampRequired"); return false; }
            input = (minutes * 60000d + seconds * 1000 + milliseconds).ToString("R", CultureInfo.InvariantCulture);
        }
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        { fieldError = L.Get("editor.error.finiteNumberRequired"); return false; }
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
