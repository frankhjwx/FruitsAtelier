using L = FruitsAtelier.Localization.Strings;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public void Render(ICanvas c, float width, float height)
    {
        RefreshLanguage();
        if (this.width != width || this.height != height)
        {
            revealDifficultyTabs = true;
            if (drag == DragKind.Marquee) CancelBox();
            else if (drag == DragKind.Timeline) CancelInteraction();
        }
        this.width = width;
        this.height = height;
        hits.Clear(); fields.Clear();
        if (IsTestplaying)
        {
            AdvanceTestplay();
            if (IsTestplaying) { DrawTestplay(c); return; }
        }
        if (ErrorVisible) { DrawError(c); return; }
        PumpSliderBatch();
        PumpLibrary();
        if (LibraryVisible) { DrawLibrary(c); DrawContextMenu(c); DrawLanguageMenu(c); DrawDiscardConfirmation(c); return; }
        float rightWidth = catchPreviewVisible ? Math.Clamp(previewWidth, MinimumPreviewWidth, Math.Max(MinimumPreviewWidth, width * .5f)) : 0;
        float bodyHeight = Math.Max(180, height - 204);
        rightPanel = new(width - (catchPreviewVisible ? rightWidth : 290), 84, catchPreviewVisible ? rightWidth : 290, catchPreviewVisible ? bodyHeight : 38);
        canvas = new(108, 84, Math.Max(120, width - rightWidth - 109), bodyHeight);
        plot = new(canvas.X + 70, canvas.Y + 140, Math.Max(50, canvas.Width - 198), Math.Max(80, canvas.Height - 152));
        overview = new(220, height - 77, Math.Max(100, width - 248), 40);
        canvasZoom = Math.Clamp(canvasZoom, MinimumCanvasZoom, 1);
        pixelsPerMs = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, Playfield.Width);
        ClampView();
        EnsureConversion();
        if (AudioPlaying && drag == DragKind.Marquee && !boxTimeline && dragMoved) MoveBox(mouseX, mouseY);
        c.Fill(new(0, 0, width, height), Background);
        DrawChrome(c);
        DrawCanvas(c);
        DrawInspector(c);
        DrawToolPalette(c);
        DrawDistanceReadout(c);
        DrawAssistPalette(c);
        DrawSelectionBox(c);
        DrawPreviewSidebar(c);
        DrawLegacyConversionButton(c);
        DrawTransport(c);
        DrawStatus(c);
        if (resourceErrors.Count > 0)
        {
            c.Fill(new(0, height - 145, width, 25), 0x48272Du);
            c.Text(L.Get("library.missingResources", string.Join("; ", resourceErrors)), 12, height - 140, 12, Error, width - 140);
            Button(c, new(width - 126, height - 145, 114, 25), L.Get("library.details"), () => { resourcePage = LibraryVisible = true; exportPage = false; libraryScroll = 0; });
        }
        if (menu >= 0) DrawMenu(c);
        DrawContextMenu(c);
        DrawLanguageMenu(c);
        DrawSliderDialog(c);
        DrawExportOverlay(c);
        DrawTimeJump(c);
        DrawStreamDialog(c);
        DrawDiscardConfirmation(c);
        DrawDifficultyTooltip(c);
    }

    private void DrawChrome(ICanvas c)
    {
        DrawHeader(c);
        c.Fill(new(0, 40, width, 44), Panel);
        Button(c, new(109, 6, 50, 28), L.Get("ui.file"), () => menu = menu == 0 ? -1 : 0, menu == 0);
        Button(c, new(162, 6, 50, 28), L.Get("ui.edit"), () => menu = menu == 1 ? -1 : 1, menu == 1);
        Button(c, new(215, 6, 50, 28), L.Get("ui.view"), () => { gridLevelMenuOpen = false; menu = menu == 2 ? -1 : 2; }, menu == 2);
        DrawDifficultyTabs(c);
        DrawLanguageButton(c, HeaderLanguageBounds);
        DrawSkinSelector(c);
        Button(c, HeaderNavigationBounds, L.Get("library.back"), ShowLibrary);
        c.Line(0, 83, width, 83, Grid);
    }

    private void DrawCanvas(ICanvas c)
    {
        float toolbarRight = rightPanel.X;
        c.Fill(new(0, canvas.Y, toolbarRight, 38), 0x1C2129);
        c.Text(L.Get("ui.canvasZoom"), 16, canvas.Y + 13, 11, Muted, 48);
        zoomSlider = new(74, canvas.Y + 4, Math.Max(30, toolbarRight - 590), 29);
        float zoomX = zoomSlider.X + (float)(1 - MinimumCanvasZoom > 0 ? (canvasZoom - MinimumCanvasZoom) / (1 - MinimumCanvasZoom) : 1) * zoomSlider.Width;
        c.Line(zoomSlider.X, canvas.Y + 19, zoomSlider.Right, canvas.Y + 19, Grid, 3);
        c.Line(zoomSlider.X, canvas.Y + 19, zoomX, canvas.Y + 19, Accent, 3);
        c.Circle(zoomX, canvas.Y + 19, 6, Accent);
        c.Text(L.Get("ui.zoomPercent", canvasZoom * 100), zoomSlider.Right + 8, canvas.Y + 13, 11, Foreground, 48);
        Button(c, new(toolbarRight - 390, canvas.Y + 4, 101, 29), showTargets ? L.Get("ui.hideCurves") : L.Get("ui.showCurves"), () => showTargets = !showTargets);
        float snapLeft = toolbarRight - 158;
        c.Text(L.Get(DistanceSpacingVisible ? "assist.spacing" : "ui.snap"), DistanceSpacingVisible ? snapLeft - 76 : snapLeft, canvas.Y + 13, 11, Muted, DistanceSpacingVisible ? 114 : 40);
        snapSlider = new(snapLeft + 40, canvas.Y + 4, 106, 29);
        float sliderStart = snapSlider.X + 7, sliderEnd = snapSlider.Right - 31;
        float snapX = sliderStart + (DistanceSpacingVisible ? (float)((Document.DistanceSpacing - .1) / 5.9) : Array.IndexOf(SnapDivisors, divisor) / (float)(SnapDivisors.Length - 1)) * (sliderEnd - sliderStart);
        c.Line(sliderStart, canvas.Y + 19, sliderEnd, canvas.Y + 19, Accent, 2);
        c.Circle(snapX, canvas.Y + 19, 6, Accent);
        c.Text(DistanceSpacingVisible ? L.Get("assist.ratio", Document.DistanceSpacing) : L.Get("ui.snapDivisor", divisor), snapSlider.Right - 28, canvas.Y + 13, 10, Foreground, 40);
        c.Line(0, canvas.Y + 38, toolbarRight, canvas.Y + 38, Grid);
        c.Text(L.Get("ui.timeAxis"), canvas.X + 11, canvas.Y + 120, 10, Muted, 43);
        DrawObjectTimeline(c);
        var playfield = Playfield;
        for (int x = 0; x <= 512; x += 128)
        {
            float sx = Screen(new(0, x)).X;
            c.Text(x.ToString(), sx - 9, canvas.Y + 120, 10, Muted, 30);
            c.Line(sx, plot.Y, sx, plot.Bottom, x == 256 ? 0x3C4653u : 0x262D37u);
        }
        if (EffectiveGridSnap)
            for (int x = gridSize; x < 512; x += gridSize)
            {
                float sx = Screen(new(0, x)).X;
                c.Line(sx, plot.Y, sx, plot.Bottom, 0x262D37);
            }
        c.Clip(new(canvas.X, plot.Y, canvas.Width, plot.Height));
        foreach (var line in renderedTiming!.Grid(viewStart, viewStart + plot.Height / pixelsPerMs, divisor))
        {
            double time = line.TimeMs;
            var localTiming = renderedTiming.At(time);
            double step = localTiming.BeatLengthMs / divisor;
            float y = Screen(new(time, 0)).Y;
            bool beat = line.IsBeat;
            bool bar = Math.Abs((time - localTiming.OffsetMs) / localTiming.BeatLengthMs / localTiming.Meter - Math.Round((time - localTiming.OffsetMs) / localTiming.BeatLengthMs / localTiming.Meter)) < 0.0001;
            if (!beat && !line.IsTimingBoundary && step * pixelsPerMs < 7) continue;
            c.Line(playfield.X, y, playfield.Right, y, line.IsTimingBoundary ? 0x845460u : beat ? Grid : 0x222933, bar || line.IsTimingBoundary ? 1.5f : 1);
            if (line.IsTimingBoundary || beat && (localTiming.BeatLengthMs * pixelsPerMs >= 25 || bar))
                c.Text(Time(time), canvas.X + 3, Math.Clamp(y - 7, plot.Y, plot.Bottom - 14), 10, line.IsTimingBoundary ? Error : Muted, 64);
        }
        c.Unclip();
        c.Clip(plot);
        foreach (var shower in Document.BananaShowers.Where(item => item.Id != draftBanana))
        {
            var bounds = BananaRectangle(shower);
            if (bounds.Bottom < plot.Y - 8 || bounds.Y > plot.Bottom + 8) continue;
            bool selected = objectSelection.Count == 1 && objectSelection.Contains(shower.Id);
            c.Fill(bounds, selected ? 0x2B291Fu : 0x211F1Bu);
            c.Stroke(bounds, selected ? Gold : 0x8C7445u, selected ? 2 : 1);
            if (selected)
            {
                float centerX = playfield.X + playfield.Width / 2;
                c.Circle(centerX, bounds.Y, 7, Background);
                c.Circle(centerX, bounds.Y, 7, Gold, false, 2);
                c.Circle(centerX, bounds.Bottom, 7, Background);
                c.Circle(centerX, bounds.Bottom, 7, Gold, false, 2);
            }
        }
        if (draftBanana != Guid.Empty && Document.BananaShowers.FirstOrDefault(item => item.Id == draftBanana) is { } draft)
        {
            float startY = Screen(new(draft.TimeMs, 256)).Y;
            double cursorTime = plot.Contains(mouseX, mouseY) ? MapAt(mouseX, mouseY, true).TimeMs : draft.TimeMs;
            float cursorY = Screen(new(Math.Max(draft.TimeMs, cursorTime), 256)).Y;
            c.Fill(new(playfield.X, Math.Min(startY, cursorY), playfield.Width, Math.Abs(startY - cursorY)), 0x29251B);
            c.Line(playfield.X, startY, playfield.Right, startY, Gold, 2);
            c.Line(playfield.X, cursorY, playfield.Right, cursorY, Gold, 1);
        }
        double margin = CatchSize.FruitRadius(Document.CircleSize) * playfield.Width / 512 * 1.5 / pixelsPerMs;
        foreach (var item in ObjectsInTimeRange(viewStart - margin, viewStart + plot.Height / pixelsPerMs + margin))
        {
            var p = Screen(new(item.TimeMs, item.X));
            float radius = (float)(CatchSize.FruitRadius(Document.CircleSize) * playfield.Width / 512);
            if (p.Y < plot.Y - radius * 1.5f || p.Y > plot.Bottom + radius * 1.5f) continue;
            bool previewTail = LegacyMode && legacyPreviewValid && item.SourceId == draftTrack
                && SelectedTrack is { } draftSlider && legacyDraft is { Count: > 0 }
                && draftSlider.Nodes[^1].TimeMs > legacyDraft[^1].Point.TimeMs
                && Math.Abs(item.TimeMs - draftSlider.Nodes[^1].TimeMs) < 1;
            DrawCatchObject(c, item, p.X, p.Y, playfield.Width, previewTail ? .6f : 1);
            if (IsObjectSelected(item.SourceId))
                c.Circle(p.X, p.Y, ObjectRadius(item.Kind) * playfield.Width / 512 + 3, Accent, false, 1.5f);
        }
        if (showTargets)
        {
            DrawImportedCurves(c, playfield.X, playfield.Width, plot.Bottom, viewStart, pixelsPerMs,
                viewStart, viewStart + plot.Height / pixelsPerMs, false);
            foreach (var track in Document.Tracks)
            {
                uint color = track.Kind == CurveKind.Bezier ? Purple : Accent;
                bool selected = IsObjectSelected(track.Id);
                float opacity = selected ? 1 : 0.5f;
                for (int span = 0; span < track.SpanCount; span++)
                {
                    double spanDuration = track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs;
                    double spanStart = track.Nodes[0].TimeMs + span * spanDuration;
                    if (spanStart + spanDuration < viewStart || spanStart > viewStart + plot.Height / pixelsPerMs) continue;
                    double DisplayTime(double time) => track.Nodes[0].TimeMs + span * spanDuration
                        + (span % 2 == 0 ? time - track.Nodes[0].TimeMs : track.Nodes[^1].TimeMs - time);
                    for (int s = 0; s < track.Nodes.Count - 1; s++)
                    {
                        double segmentStart = DisplayTime(track.Nodes[s].TimeMs), segmentEnd = DisplayTime(track.Nodes[s + 1].TimeMs);
                        if (Math.Max(segmentStart, segmentEnd) < viewStart || Math.Min(segmentStart, segmentEnd) > viewStart + plot.Height / pixelsPerMs) continue;
                        var first = CurveMath.Evaluate(track, s, 0);
                        var previous = Screen(new(DisplayTime(first.TimeMs), first.X));
                        uint segmentColour = CurveMath.SegmentKind(track, s) == CurveKind.Bezier ? Purple : Accent;
                        for (int n = 1; n <= 64; n++)
                        {
                            var value = CurveMath.Evaluate(track, s, n / 64.0);
                            var p = Screen(new(DisplayTime(value.TimeMs), value.X));
                            c.Line(previous.X, previous.Y, p.X, p.Y, segmentColour, selected ? 2.6f : 2, opacity);
                            previous = p;
                        }
                    }
                }
                if (selected && LegacyControlsActive)
                {
                    DrawLegacyControls(c, track);
                    continue;
                }
                foreach (var node in track.Nodes)
                {
                    var p = Screen(Point(node));
                    if (selected && tool == Tool.Slider)
                    {
                        int index = track.Nodes.IndexOf(node);
                        if (index > 0 && CurveMath.SegmentKind(track, index - 1) == CurveKind.Bezier) DrawHandle(PenHandle(track, index, true), DragKind.HandleIn);
                        if (index < track.Nodes.Count - 1 && CurveMath.SegmentKind(track, index) == CurveKind.Bezier
                            || track.Id == draftTrack && tool == Tool.Slider) DrawHandle(PenHandle(track, index, false), DragKind.HandleOut);
                    }
                    if (p.Y < plot.Y - 9 || p.Y > plot.Bottom + 9) continue;
                    bool nodeSelected = tool == Tool.Slider && anchorSelection.Contains(node.Id);
                    Diamond(c, p.X, p.Y, nodeSelected ? 8 : 5.5f, nodeSelected ? Error : color, opacity);
                    if (nodeSelected) c.Circle(p.X, p.Y, 3, Foreground);
                    void DrawHandle(MapPoint offset, DragKind part)
                    {
                        if (offset == default) return;
                        bool active = selection == node.Id && selectedPart == part;
                        var h = PenHandleScreen(track, track.Nodes.IndexOf(node), part == DragKind.HandleIn);
                        c.Line(p.X, p.Y, h.X, h.Y, active ? Foreground : 0x625E7C);
                        c.Circle(h.X, h.Y, active ? 6 : 4.5f, active ? Foreground : Background);
                        c.Circle(h.X, h.Y, active ? 6 : 4.5f, active ? Foreground : Purple, false, 1.5f);
                    }
                }
            }
        }
        DrawPlacementGhost(c);
        float headY = Screen(new(playhead, 0)).Y;
        c.Line(plot.X, headY, plot.Right, headY, Gold, 1.5f);
        c.Fill(new(plot.X, headY - 3, 5, 6), Gold);
        c.Unclip();
    }

    private void DrawInspector(ICanvas c)
    {
        c.Fill(rightPanel, Panel);
        c.Line(rightPanel.X, rightPanel.Y, rightPanel.X, rightPanel.Bottom, Grid);
        float x = rightPanel.X + 16, w = rightPanel.Width - 32;
        c.Text(L.Get("ui.properties"), x, rightPanel.Y + 15, 12, Foreground, 48, true);
        c.Text($"{L.Get("ui.ar")} {Number(Document.ApproachRate)}   {L.Get("ui.cs")} {Number(Document.CircleSize)}   {L.Get("ui.sv")} {Number(Document.SliderMultiplier)}",
            x + 56, rightPanel.Y + 15, 12, Muted, w - 56);
    }

    private (CurveTrack Track, Anchor Node) ResolveAnchor(Guid trackId, Guid nodeId)
    {
        var track = Document.Tracks.First(t => t.Id == trackId);
        return (track, track.Nodes.First(n => n.Id == nodeId));
    }

    private void MoveAnchor(Guid trackId, Guid nodeId, double? time, double? x)
    {
        var (track, node) = ResolveAnchor(trackId, nodeId);
        if (!CurveMath.TryMoveAnchor(track, node.Id, time ?? node.TimeMs, x ?? node.X, out var error)) throw new ArgumentException(error);
    }

    private void SetHandle(Guid trackId, Guid nodeId, bool incoming, double? time, double? x)
    {
        var (track, node) = ResolveAnchor(trackId, nodeId);
        var current = PenHandle(track, track.Nodes.IndexOf(node), incoming);
        var offset = new MapPoint(time ?? current.TimeMs, x ?? current.X);
        if (offset == current) return;
        ControlCurveEditing.ConvertToPen(track, track.Nodes.IndexOf(node) - (incoming ? 1 : 0));
        if (!CurveMath.TryMoveHandle(track, node.Id, incoming, offset, out var error)) throw new ArgumentException(error);
    }

    private void DrawPreview(ICanvas c, Rect r)
    {
        c.Line(r.X, r.Y - 12, r.Right, r.Y - 12, Grid);
        c.Text(L.Get("ui.preview"), r.X, r.Y, 13, Foreground, r.Width, true);
        Button(c, new(r.Right - 92, r.Y - 5, 92, 27), L.Get("ui.debugCurves"), () => showPreviewCurves = !showPreviewCurves, showPreviewCurves);
        c.Text(L.Get("ui.previewStats", Number(PreviewApproachRate), Number(PreviewCircleSize), PreviewModName), r.X, r.Y + 23, 10, Foreground, r.Width);
        DrawPreviewMods(c, r);
        DrawPreviewDisplayModes(c, r);
        Rect stage = new(r.X, r.Y + 107, r.Width, Math.Max(12, r.Height - 113));
        if (previewDisplayMode != 2)
        {
            float aspect = previewDisplayMode == 0 ? 4f / 3 : 16f / 9;
            float viewHeight = Math.Min(stage.Height, stage.Width / aspect);
            stage = new(stage.X + (stage.Width - viewHeight * aspect) / 2, stage.Y + (stage.Height - viewHeight) / 2, viewHeight * aspect, viewHeight);
        }
        PreviewViewport = stage;
        c.Fill(stage, 0x151A22);
        if (previewDisplayMode == 2) c.Stroke(stage, Grid);
        c.Clip(stage);
        float referenceWidth = Math.Max(1, stage.Width - 18);
        float referenceHeight = referenceWidth * .75f;
        float referenceTop = stage.Bottom - 6 - referenceHeight;
        float fieldWidth = previewDisplayMode == 2 ? referenceWidth * .8f : stage.Height * (1024f / 768) * .8f;
        float fieldLeft = stage.X + (stage.Width - fieldWidth) / 2;
        // Legacy's playable top is 15% of 768; the plate is at Y=340 in its 512-wide field.
        float catchY = previewDisplayMode == 2 ? referenceTop + referenceHeight * .15f + (float)CatchScrollTiming.CatchY * fieldWidth / 512
            : stage.Y + stage.Height * .15f + (float)CatchScrollTiming.CatchY * fieldWidth / 512;
        // Fit extends visibility above the complete legacy 4:3 reference view, including its catcher area.
        if (previewDisplayMode == 2)
            c.Stroke(new(stage.X + 9, referenceTop, referenceWidth, referenceHeight), 0x2B3442);
        double scrollSpeed = CatchScrollTiming.PixelsPerMs(PreviewApproachRate, fieldWidth);
        double visibleAhead = (catchY - stage.Y + CatchSize.FruitDiameter(PreviewCircleSize) * fieldWidth / 512 * 1.2) / scrollSpeed;
        if (showPreviewCurves)
        {
            DrawImportedCurves(c, fieldLeft, fieldWidth, catchY, playhead, scrollSpeed,
                playhead, playhead + visibleAhead, true);
            foreach (var track in Document.Tracks)
            {
                if (track.Nodes.Count < 2) continue;
                double begin = Math.Max(playhead, track.Nodes[0].TimeMs), end = Math.Min(playhead + visibleAhead, CurveMath.EndTimeMs(track));
                if (end < begin) continue;
                (float X, float Y)? last = null;
                for (int i = 0; i <= 48; i++)
                {
                    double time = begin + (end - begin) * i / 48;
                    float x = fieldLeft + (float)(CurveMath.PositionAtTime(track, time) / 512) * fieldWidth;
                    float y = catchY - (float)((time - playhead) * scrollSpeed);
                    if (last is { } p) c.Line(p.X, p.Y, x, y, track.Kind == CurveKind.Bezier ? Purple : Accent, 2);
                    last = (x, y);
                }
            }
        }
        DrawPreviewCatcher(c, fieldLeft, fieldWidth, catchY);
        DrawPreviewPlate(c, fieldLeft, fieldWidth, catchY);
        foreach (var item in PreviewObjectsInRange(playhead, playhead + visibleAhead))
        {
            double remaining = item.TimeMs - playhead;
            if (remaining < 0) continue;
            float y = catchY - (float)(remaining * scrollSpeed);
            DrawCatchObject(c, item, fieldLeft + (float)(item.X / 512) * fieldWidth, y, fieldWidth, circleSize: PreviewCircleSize, hyperStarts: previewHyperdash, animated: true);
        }
        c.Unclip();
    }

    private void DrawTransport(ICanvas c)
    {
        float top = height - 120;
        c.Fill(new(0, top, width, 92), 0x20252E);
        c.Line(0, top, width, top, Grid);
        var transport = new Rect(16, top + 28, 40, 36);
        c.Fill(transport, AudioPlaying ? 0x344A50u : Surface, 5);
        c.Stroke(transport, AudioReady ? Accent : Muted, 1.5f, 5);
        float cx = transport.X + transport.Width / 2, cy = transport.Y + transport.Height / 2;
        uint icon = AudioReady ? Foreground : Muted;
        if (AudioPlaying)
        {
            c.Fill(new(cx - 7, cy - 8, 5, 16), icon);
            c.Fill(new(cx + 2, cy - 8, 5, 16), icon);
        }
        else for (int i = 0; i < 13; i++)
            c.Line(cx - 5 + i, cy - 8 + i * 8f / 12, cx - 5 + i, cy + 8 - i * 8f / 12, icon);
        hits.Add(new(transport, TogglePlayback, AudioReady));
        TimeDisplayBounds = new(64, top + 22, 150, 48);
        if (TimeDisplayBounds.Contains(mouseX, mouseY)) c.Fill(TimeDisplayBounds, Surface, 4);
        c.Text(Time(playhead), 69, top + 22, 21, Foreground, 145, true);
        c.Text("/ " + Time(TimelineDurationMs), 70, top + 50, 11, Muted, 130);
        hits.Add(new(TimeDisplayBounds, OpenTimeJump, true));
        if (TimeDisplayBounds.Contains(mouseX, mouseY) && !TimeJumpVisible)
            c.Text(L.Get("timeJump.title"), 69, top - 20, 12, Foreground, 200);
        if (!AudioReady) c.Text(AudioNotice, 16, top + 71, 10, Gold, 192);

        float rateX = overview.Right - 360;
        TestplayButtonBounds = new(220, top + 3, 128, 28);
        Button(c, TestplayButtonBounds, L.Get("testplay.start"), StartTestplay, enabled: !AudioLoading);
        c.Text(L.Get("ui.playbackSpeed"), rateX, top + 12, 11, Muted, 104);
        foreach (double rate in new[] { .25, .5, .75, 1 })
        {
            Button(c, new(rateX + 104, top + 3, 48, 28), L.Get("ui.zoomPercent", rate * 100), () => SetPlaybackSpeed(rate), PlaybackSpeed == rate);
            rateX += 50;
        }
        c.Fill(overview, 0x141922, 4);
        for (int i = 0; i <= 6; i++)
        {
            float x = overview.X + overview.Width * i / 6;
            c.Line(x, overview.Y + 2, x, overview.Bottom, 0x2B3442);
        }
        if (showTargets)
            foreach (var track in Document.Tracks)
                if (track.Nodes.Count >= 2)
                {
                    float start = overview.X + (float)(track.Nodes[0].TimeMs / TimelineDurationMs) * overview.Width;
                    float end = overview.X + (float)(CurveMath.EndTimeMs(track) / TimelineDurationMs) * overview.Width;
                    c.Fill(new(start, overview.Y + 9, Math.Max(2, end - start), 6), track.Kind == CurveKind.Bezier ? Purple : Accent, 2);
                }
        // The overview is a pixel-sized summary. Preserve hyperdash markers when events overlap.
        int lastPixel = int.MinValue;
        bool hyper = false;
        foreach (var item in conversion!.Objects)
        {
            if (item.Kind == CatchObjectKind.TinyDroplet) continue;
            int pixel = (int)Math.Round(item.TimeMs / TimelineDurationMs * overview.Width);
            if (pixel != lastPixel)
            {
                FlushMarker(); lastPixel = pixel; hyper = false;
            }
            hyper |= hyperdashObjects.Contains((item.SourceId, item.EventIndex));
        }
        FlushMarker();
        void FlushMarker()
        {
            if (lastPixel == int.MinValue) return;
            float x = overview.X + lastPixel;
            c.Line(x, overview.Y + 23, x, overview.Y + 32, hyper ? Error : Foreground, 2);
        }
        double visibleStart = Math.Clamp(viewStart, 0, TimelineDurationMs);
        double visibleEnd = Math.Clamp(viewStart + plot.Height / pixelsPerMs, visibleStart, TimelineDurationMs);
        float viewX = overview.X + (float)(visibleStart / TimelineDurationMs) * overview.Width;
        float viewWidth = (float)((visibleEnd - visibleStart) / TimelineDurationMs) * overview.Width;
        c.Stroke(new(viewX, overview.Y + 1, viewWidth, overview.Height - 2), 0x71849A, 1, 3);
        float headX = TimelineHeadX;
        c.Line(headX, overview.Y - 3, headX, overview.Bottom + 2, Gold, 2);
        Diamond(c, headX, overview.Y - 2, 4, Gold);
    }

    private void DrawStatus(ICanvas c)
    {
        c.Fill(new(0, height - 28, width, 28), 0x171C23);
        c.Circle(13, height - 14, 3, IsDirty ? Gold : Accent);
        string notice = conversion?.Diagnostics.FirstOrDefault() ?? StatusMessage;
        c.Text(notice, 25, height - 21, 11, conversion?.Diagnostics.Count > 0 ? Error : Muted, Math.Max(60, width - 40));
    }

    private Rect MenuBounds { get; set; }
    private int menuHitStart;
    private bool gridLevelMenuOpen;
    private Rect gridLevelMenuBounds;

    private void DrawMenu(ICanvas c)
    {
        menuHitStart = hits.Count;
        var items = new List<(string Label, Action Action, bool Enabled, bool Active)>();
        if (menu == 0)
        {
            Item(L.Get("project.new"), () => RequestNewProject?.Invoke());
            Item(L.Get("ui.openMenu"), () => RequestOpen?.Invoke());
            Item(L.Get("ui.saveMenu"), () => RequestSave?.Invoke());
            Item(L.Get("ui.saveAsMenu"), () => RequestSaveAs?.Invoke());
            Item(L.Get("ui.exportMenu"), () => RequestExport?.Invoke());
            Item(L.Get("ui.exitMenu"), () => RequestClose?.Invoke());
        }
        else if (menu == 3)
        {
            Item(L.Get("project.add"), () => AddDifficulty());
            Item(L.Get("project.import"), () => RequestImportDifficulty?.Invoke());
        }
        else if (menu == 1)
        {
            Item(L.Get("ui.undoMenu"), Undo, history.CanUndo);
            Item(L.Get("ui.redoMenu"), Redo, history.CanRedo);
            Item(L.Get("ui.deleteMenu"), DeleteSelection, selection != Guid.Empty);
            Item(L.Get("ui.splitMenu"), SplitSelected, SelectedTrack is not null && draftTrack == Guid.Empty);
            Item(L.Get("ui.cutMenu"), () => CutSelection(), CanCopySelection);
            Item(L.Get("ui.copyMenu"), () => CopySelection(), CanCopySelection);
            Item(L.Get("ui.pasteMenu"), () => PasteSelection(), CanPasteSelection);
            Item(L.Get(SelectedStreamsOnly ? "stream.changeSnapMenu" : "stream.menu"), OpenStreamDialog, CanConvertStream && !notesLocked);
            if (SelectedStreamsOnly) Item(L.Get("stream.convertBack"), ConvertStreamsBack, ClipboardInteractionReady && !notesLocked);
            Item(L.Get("sliderBatch.menu"), ConvertAllSliders, Document.ImportedSliders.Count > 0 && !SliderConversionBusy);
        }
        else
        {
            Item(L.Get("ui.gridLevel", L.Get("ui.grid" + gridSize)), () => gridLevelMenuOpen = true);
            Item(L.Get("ui.gridSnap"), () => gridSnap = !gridSnap, active: gridSnap);
            Item(L.Get("ui.anchorSnap"), () => anchorSnap = !anchorSnap, active: anchorSnap);
            Item(L.Get("ui.resetView"), ResetView);
            Item(showTargets ? L.Get("ui.targetsOn") : L.Get("ui.targetsOff"), () => showTargets = !showTargets);
            Item(showPreviewCurves ? L.Get("ui.previewCurvesOn") : L.Get("ui.previewCurvesOff"), () => showPreviewCurves = !showPreviewCurves);
            Item(L.Get("ui.follow"), FollowPlayhead);
        }
        float x = menu == 3 ? Math.Min(difficultyAddButton.X, width - 288) : 109 + menu * 53;
        float top = menu == 3 ? difficultyAddButton.Bottom + 4 : 38;
        MenuBounds = new(x, top, 282, 14 + items.Count * 34 - 3);
        var rect = MenuBounds;
        var gridRow = new Rect(rect.X + 6, rect.Y + 7, rect.Width - 12, 31);
        gridLevelMenuBounds = new(rect.Right, gridRow.Y - 7, 160, 147);
        var gridBridge = new Rect(gridRow.X, gridRow.Y, rect.Right - gridRow.X, gridRow.Height);
        gridLevelMenuOpen = menu == 2 && (gridRow.Contains(mouseX, mouseY)
            || gridLevelMenuOpen && (gridBridge.Contains(mouseX, mouseY) || gridLevelMenuBounds.Contains(mouseX, mouseY)));
        c.Fill(new(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), 0x11151B, 5);
        c.Fill(rect, Surface, 5); c.Stroke(rect, Grid, 1, 5);
        float y = rect.Y + 7;
        foreach (var item in items)
        {
            bool submenu = menu == 2 && y == rect.Y + 7;
            Button(c, new(rect.X + 6, y, rect.Width - 12, 31), item.Label,
                () => { if (!submenu) menu = -1; item.Action(); }, item.Active || submenu && gridLevelMenuOpen, item.Enabled);
            if (submenu)
            {
                c.Line(rect.Right - 19, y + 11, rect.Right - 14, y + 16, Foreground);
                c.Line(rect.Right - 14, y + 16, rect.Right - 19, y + 21, Foreground);
            }
            y += 34;
        }
        if (gridLevelMenuOpen)
        {
            var child = gridLevelMenuBounds;
            c.Fill(new(child.X + 3, child.Y + 4, child.Width, child.Height), 0x11151B, 5);
            c.Fill(child, Surface, 5); c.Stroke(child, Grid, 1, 5);
            float childY = child.Y + 7;
            foreach (int size in new[] { 4, 8, 16, 32 })
            {
                Button(c, new(child.X + 6, childY, child.Width - 12, 31), L.Get("ui.grid" + size),
                    () => { gridSize = size; gridLevelMenuOpen = false; menu = -1; }, size == gridSize);
                if (size == gridSize)
                {
                    c.Line(child.Right - 27, childY + 16, child.Right - 23, childY + 20, Foreground, 2);
                    c.Line(child.Right - 23, childY + 20, child.Right - 16, childY + 11, Foreground, 2);
                }
                childY += 34;
            }
        }
        void Item(string label, Action action, bool enabled = true, bool active = false)
            => items.Add((label, action, enabled, active));
    }

    private void Button(ICanvas c, Rect r, string label, Action action, bool active = false, bool enabled = true)
    {
        bool hover = enabled && r.Contains(mouseX, mouseY);
        if (hover) c.Fill(r, active ? 0x3A6260u : 0x3D495Au, 4);
        else if (active) c.Fill(r, 0x31494B, 4);
        if (hover) c.Stroke(r, 0x71849A, 1, 4);
        if (active) c.Stroke(r, 0x477E7B, 1, 4);
        uint color = !enabled ? 0x5B6777u : active ? Accent : Foreground;
        int shortcut = label.IndexOf("  ", StringComparison.Ordinal);
        if (shortcut >= 0)
        {
            string key = label[(shortcut + 2)..].Trim();
            float keyWidth = c.MeasureText(key, 12, active);
            c.Text(label[..shortcut], r.X + 9, r.Y + (r.Height - 16) / 2, 12, color, Math.Max(0, r.Width - keyWidth - 36), active);
            c.Text(key, r.Right - 9 - keyWidth, r.Y + (r.Height - 16) / 2, 12, color, keyWidth + 1, active);
        }
        else c.Text(label, r.X + 9, r.Y + (r.Height - 16) / 2, 12, color, r.Width - 15, active);
        hits.Add(new(r, action, enabled));
    }

    private static void Badge(ICanvas c, Rect r, string label, uint color)
    {
        c.Fill(r, 0x2B323B, 4);
        c.Text(label, r.X + 8, r.Y + 5, 11, color, r.Width - 12);
    }

    private void Field(ICanvas c, float x, ref float y, float w, string label, double value, Action<double> apply, float labelWidth = 75)
    {
        c.Text(label, x, y + 8, 11, Muted, labelWidth - 6);
        var r = new Rect(x + labelWidth, y, w - labelWidth, 30);
        int index = fields.Count;
        bool focused = editField == index;
        bool timestamp = label == L.Get("ui.timeField") || label == L.Get("ui.startTimeField") || label == L.Get("ui.endTimeField");
        c.Fill(r, focused ? 0x273638u : 0x151B24u, 3);
        c.Stroke(r, focused ? (fieldError.Length > 0 ? Error : Accent) : r.Contains(mouseX, mouseY) ? 0x67758B : Grid, 1, 3);
        DrawInputText(c, new(r.X + 9, r.Y + 7, r.Width - 18, 18), focused ? editBuffer : timestamp ? Time(value) : Number(value), 12, focused, replaceText);
        fields.Add(new(r, label, value, apply, timestamp));
        y += 37;
    }

    private static void Diamond(ICanvas c, float x, float y, float size, uint color, float opacity = 1)
    {
        c.Line(x, y - size, x + size, y, color, 2, opacity);
        c.Line(x + size, y, x, y + size, color, 2, opacity);
        c.Line(x, y + size, x - size, y, color, 2, opacity);
        c.Line(x - size, y, x, y - size, color, 2, opacity);
    }
}
