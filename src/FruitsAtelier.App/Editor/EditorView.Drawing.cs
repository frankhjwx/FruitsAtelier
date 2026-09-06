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
            if (drag == DragKind.Marquee) CancelBox();
            else if (drag == DragKind.Timeline) CancelInteraction();
        }
        this.width = width;
        this.height = height;
        hits.Clear(); fields.Clear(); rows.Clear();
        float leftWidth = width < 1100 ? 168 : 192;
        float rightWidth = width < 1100 ? 224 : 270;
        float bodyHeight = Math.Max(180, height - 204);
        leftPanel = new(0, 84, leftWidth, bodyHeight);
        rightPanel = new(width - rightWidth, 84, rightWidth, bodyHeight);
        canvas = new(leftWidth + 1, 84, Math.Max(120, width - leftWidth - rightWidth - 2), bodyHeight);
        plot = new(canvas.X + 58, canvas.Y + 68, Math.Max(50, canvas.Width - 82), Math.Max(80, canvas.Height - 80));
        overview = new(220, height - 77, Math.Max(100, width - 248), 40);
        if (useArScale) pixelsPerMs = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, Playfield.Width);
        ClampView();
        EnsureConversion();
        c.Fill(new(0, 0, width, height), Background);
        DrawChrome(c);
        DrawList(c);
        DrawInspector(c);
        DrawCanvas(c);
        DrawSelectionBox(c);
        DrawTransport(c);
        DrawStatus(c);
        if (menu >= 0) DrawMenu(c);
        DrawContextMenu(c);
    }

    private void DrawChrome(ICanvas c)
    {
        c.Fill(new(0, 0, width, 39), 0x1B2028);
        c.Fill(new(0, 40, width, 44), Panel);
        c.Text(L.Get("ui.logoBrand"), 12, 11, 13, Foreground, 94, true);
        Button(c, new(109, 6, 50, 28), L.Get("ui.file"), () => menu = menu == 0 ? -1 : 0, menu == 0);
        Button(c, new(162, 6, 50, 28), L.Get("ui.edit"), () => menu = menu == 1 ? -1 : 1, menu == 1);
        Button(c, new(215, 6, 50, 28), L.Get("ui.view"), () => menu = menu == 2 ? -1 : 2, menu == 2);
        c.Text($"{Document.Name}{(IsDirty ? " *" : "")}", 289, 11, 13, Muted, Math.Max(20, width - 685));
        Button(c, new(width - 369, 6, 100, 28), L.Get("ui.languageButton"), CycleLanguage);
        Badge(c, new(width - 258, 8, 110, 24), Document.IsDemo ? L.Get("ui.demoProject") : L.Get("ui.catchMap"), Accent);
        Badge(c, new(width - 138, 8, 121, 24), L.Get("ui.stablePending"), Gold);
        float x = 12;
        ToolButton(L.Get("ui.selectTool"), Tool.Select, 76);
        ToolButton(L.Get("ui.fruitTool"), Tool.Fruit, 82);
        ToolButton(L.Get("ui.sliderTool"), Tool.Slider, 90);
        ToolButton(L.Get("ui.bananaTool"), Tool.Banana, 94);
        c.Line(x + 7, 49, x + 7, 75, Grid); x += 22;
        c.Text(L.Get("ui.snap"), x, 55, 12, Muted); x += 38;
        snapSlider = new(x, 47, 112, 30);
        float snapLeft = snapSlider.X + 7, snapRight = snapSlider.Right - 31;
        float snapX = snapLeft + Array.IndexOf(SnapDivisors, divisor) / (float)(SnapDivisors.Length - 1) * (snapRight - snapLeft);
        c.Line(snapLeft, 62, snapRight, 62, snap ? Accent : Grid, 2);
        c.Circle(snapX, 62, 6, snap ? Accent : Muted);
        c.Text(L.Get("ui.snapDivisor", divisor), snapSlider.Right - 28, 55, 11, snap ? Foreground : Muted, 30);
        x += 116;
        Button(c, new(x, 47, 46, 30), L.Get("ui.free"), () => snap = !snap, !snap); x += 63;
        Button(c, new(x, 47, 60, 30), L.Get("ui.undo"), Undo, false, history.CanUndo); x += 64;
        Button(c, new(x, 47, 60, 30), L.Get("ui.redo"), Redo, false, history.CanRedo); x += 72;
        Button(c, new(x, 47, 80, 30), L.Get("ui.resetView"), ResetView);
        x += 85;
        if (width >= 1080)
        {
            Button(c, new(x, 47, 70, 30), L.Get("ui.skin"), () => RequestLoadSkin?.Invoke());
            x += 74;
        }
        if (width >= 1200)
        {
            Button(c, new(x, 47, 93, 30), L.Get("ui.tinyAlign"), () => compensateTinyDroplets = !compensateTinyDroplets, compensateTinyDroplets);
            x += 97;
        }
        if (width >= 1320)
            Button(c, new(x, 47, 75, 30), L.Get("ui.tickRate", Number(Document.SliderTickRate)), CycleTickRate);
        var timing = TimingMap.At(Document, playhead);
        if (width > 1320) c.Text(L.Get("ui.timing", 60000 / timing.BeatLengthMs, timing.SliderVelocityMultiplier), width - 220, 56, 12, Muted, 208);
        c.Line(0, 83, width, 83, Grid);
        void ToolButton(string text, Tool mode, float w)
        {
            Button(c, new(x, 47, w, 30), text, () => ChangeTool(mode), tool == mode);
            x += w + 4;
        }
    }

    private void DrawList(ICanvas c)
    {
        c.Fill(leftPanel, Panel);
        c.Text(L.Get("ui.objects"), 16, 99, 14, Foreground, 100, true);
        c.Text($"{Document.Fruits.Count + Document.Tracks.Count + Document.ImportedSliders.Count + Document.BananaShowers.Count}", leftPanel.Right - 48, 100, 12, Muted);
        c.Text(L.Get("ui.timingCount", Document.TimingPoints.Count, 60000 / TimingMap.At(Document, playhead).BeatLengthMs), 16, 127, 10, Muted, leftPanel.Width - 24);
        Rect listRect = listBounds = new(7, 151, leftPanel.Width - 14, Math.Max(30, leftPanel.Bottom - 209));
        int count = Document.Fruits.Count + Document.ImportedSliders.Count + Document.BananaShowers.Count + Document.Tracks.Sum(t => t.Nodes.Count + 1);
        listScroll = Math.Clamp(listScroll, 0, Math.Max(0, count * 30 + 108 - listRect.Height));
        c.Clip(listRect);
        float y = listRect.Y - listScroll;
        c.Text(L.Get("ui.targetTracks"), 16, y, 11, Muted); y += 25;
        foreach (var track in Document.Tracks)
        {
            DrawRow(track.Name, track.Id, track.Id, track.Kind == CurveKind.Bezier ? Purple : Accent, false, true);
            for (int i = 0; i < track.Nodes.Count; i++)
                DrawRow(L.Get("ui.anchorRow", i + 1, Number(track.Nodes[i].TimeMs)), track.Nodes[i].Id, track.Id, Muted, true, true);
        }
        if (Document.ImportedSliders.Count + Document.BananaShowers.Count > 0)
        {
            c.Text(L.Get("ui.importedObjects"), 16, y + 7, 11, Muted); y += 29;
            foreach (var slider in Document.ImportedSliders)
                DrawRow(L.Get("ui.sliderRow", Number(slider.TimeMs)), slider.Id, Guid.Empty, Purple, false, true);
            foreach (var shower in Document.BananaShowers)
                DrawRow(L.Get("ui.bananaRow", Number(shower.TimeMs)), shower.Id, Guid.Empty, Gold, false, true);
        }
        c.Text(L.Get("ui.standaloneFruits"), 16, y + 7, 11, Muted); y += 29;
        int fruitIndex = 0;
        foreach (var fruit in Document.Fruits.OrderBy(f => f.TimeMs))
            DrawRow(L.Get("ui.fruitRow", ++fruitIndex, Number(fruit.TimeMs)), fruit.Id, Guid.Empty, Gold, false, false);
        c.Unclip();
        c.Line(leftPanel.Right, 84, leftPanel.Right, leftPanel.Bottom, Grid);
        c.Fill(new(0, leftPanel.Bottom - 45, leftPanel.Width, 45), Panel);
        c.Text(L.Get("ui.listLegend"), 13, leftPanel.Bottom - 36, 11, Muted, leftPanel.Width - 18);
        c.Text(L.Get("ui.objectCounts", conversion?.Objects.Count ?? 0, hyperdashObjects.Count), 13, leftPanel.Bottom - 19, 10, Gold, leftPanel.Width - 18);

        void DrawRow(string label, Guid id, Guid trackId, uint color, bool indent, bool diamond)
        {
            var rect = new Rect(8, y, leftPanel.Width - 16, 28);
            if (rect.Bottom >= listRect.Y && rect.Y < listRect.Bottom)
            {
                bool selected = objectSelection.Contains(id) || tool == Tool.Slider && anchorSelection.Contains(id);
                if (selected) c.Fill(rect, 0x314347, 4);
                else if (rect.Contains(mouseX, mouseY)) c.Fill(rect, Surface, 4);
                float iconX = indent ? 30 : 20;
                if (diamond) Diamond(c, iconX, y + 14, indent ? 3 : 4, selected ? (indent ? Error : Accent) : color);
                else c.Circle(iconX, y + 14, 4, color);
                c.Text(label, iconX + 12, y + 6, indent ? 11 : 12, selected ? Foreground : Muted, leftPanel.Width - iconX - 25);
                rows.Add((rect, id, trackId));
            }
            y += 30;
        }
    }

    private void DrawCanvas(ICanvas c)
    {
        c.Fill(new(canvas.X, 84, canvas.Width, 38), 0x1C2129);
        c.Text(L.Get("ui.canvas"), canvas.X + 12, 96, 13, Foreground, 104, true);
        c.Text(L.Get("ui.timeZoom"), canvas.X + 124, 97, 11, Muted, 38);
        zoomSlider = new(canvas.X + 170, 88, Math.Max(30, canvas.Width - 478), 29);
        float zoomX = zoomSlider.X + (float)Math.Clamp(DisplayApproachRate / 10, 0, 1) * zoomSlider.Width;
        c.Line(zoomSlider.X, 103, zoomSlider.Right, 103, Grid, 3);
        c.Line(zoomSlider.X, 103, zoomX, 103, Accent, 3);
        c.Circle(zoomX, 103, 6, Accent);
        c.Text(L.Get("ui.zoomAr", DisplayApproachRate), zoomSlider.Right + 8, 97, 11, Foreground, 48);
        Button(c, new(canvas.Right - 246, 88, 98, 29), showTargets ? L.Get("ui.hideCurves") : L.Get("ui.showCurves"), () => showTargets = !showTargets);
        var matchButton = new Rect(canvas.Right - 143, 88, 132, 29);
        Button(c, matchButton, L.Get("ui.restoreAr"), RestoreArScale, useArScale);
        c.Line(canvas.X, 122, canvas.Right, 122, Grid);
        c.Text(L.Get("ui.timeAxis"), canvas.X + 11, 132, 10, Muted, 43);
        var playfield = Playfield;
        for (int x = 0; x <= 512; x += 128)
        {
            float sx = Screen(new(0, x)).X;
            c.Text(x.ToString(), sx - 9, 132, 10, Muted, 30);
            c.Line(sx, plot.Y, sx, plot.Bottom, x == 256 ? 0x3C4653u : 0x262D37u);
        }
        c.Clip(new(canvas.X, plot.Y, canvas.Width, plot.Height));
        foreach (var line in TimingMap.Grid(Document, viewStart, viewStart + plot.Height / pixelsPerMs, divisor))
        {
            double time = line.TimeMs;
            var localTiming = TimingMap.At(Document, time);
            double step = localTiming.BeatLengthMs / divisor;
            float y = Screen(new(time, 0)).Y;
            bool beat = line.IsBeat;
            bool bar = Math.Abs((time - localTiming.OffsetMs) / localTiming.BeatLengthMs / localTiming.Meter - Math.Round((time - localTiming.OffsetMs) / localTiming.BeatLengthMs / localTiming.Meter)) < 0.0001;
            if (!beat && !line.IsTimingBoundary && step * pixelsPerMs < 7) continue;
            c.Line(playfield.X, y, playfield.Right, y, line.IsTimingBoundary ? 0x845460u : beat ? Grid : 0x222933, bar || line.IsTimingBoundary ? 1.5f : 1);
            if (line.IsTimingBoundary || beat && (localTiming.BeatLengthMs * pixelsPerMs >= 25 || bar))
                c.Text(Number(time), canvas.X + 6, Math.Clamp(y - 7, plot.Y, plot.Bottom - 14), 10, line.IsTimingBoundary ? Error : Muted, 46);
        }
        c.Unclip();
        c.Clip(plot);
        foreach (var shower in Document.BananaShowers.Where(item => item.Id != draftBanana))
        {
            var bounds = BananaRectangle(shower);
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
        foreach (var item in conversion!.Objects)
        {
            var p = Screen(new(item.TimeMs, item.X));
            float radius = (float)(CatchSize.FruitRadius(Document.CircleSize) * playfield.Width / 512);
            if (p.Y < plot.Y - radius * 1.5f || p.Y > plot.Bottom + radius * 1.5f) continue;
            DrawCatchObject(c, item, p.X, p.Y, playfield.Width);
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
                foreach (var node in track.Nodes)
                {
                    var p = Screen(Point(node));
                    if (selected && tool == Tool.Slider)
                    {
                        int index = track.Nodes.IndexOf(node);
                        if (index > 0 && CurveMath.SegmentKind(track, index - 1) == CurveKind.Bezier) DrawHandle(node.HandleIn, DragKind.HandleIn);
                        if (index < track.Nodes.Count - 1 && CurveMath.SegmentKind(track, index) == CurveKind.Bezier
                            || track.Id == draftTrack && tool == Tool.Slider) DrawHandle(node.HandleOut, DragKind.HandleOut);
                    }
                    bool nodeSelected = tool == Tool.Slider && anchorSelection.Contains(node.Id);
                    Diamond(c, p.X, p.Y, nodeSelected ? 8 : 5.5f, nodeSelected ? Error : color, opacity);
                    if (nodeSelected) c.Circle(p.X, p.Y, 3, Foreground);
                    void DrawHandle(MapPoint offset, DragKind part)
                    {
                        if (offset == default) return;
                        bool active = selection == node.Id && selectedPart == part;
                        var h = Screen(Point(node) + offset);
                        c.Line(p.X, p.Y, h.X, h.Y, active ? Foreground : 0x625E7C);
                        c.Circle(h.X, h.Y, active ? 6 : 4.5f, active ? Foreground : Background);
                        c.Circle(h.X, h.Y, active ? 6 : 4.5f, active ? Foreground : Purple, false, 1.5f);
                    }
                }
            }
        }
        if (tool == Tool.Fruit && plot.Contains(mouseX, mouseY) && drag == DragKind.None)
        {
            var p = Screen(MapAt(mouseX, mouseY, true));
            c.Circle(p.X, p.Y, (float)(CatchSize.FruitRadius(Document.CircleSize) * playfield.Width / 512), Foreground, false, 1.5f);
            c.Line(plot.X, p.Y, plot.Right, p.Y, 0x61553B);
        }
        float headY = Screen(new(playhead, 0)).Y;
        c.Line(plot.X, headY, plot.Right, headY, Gold, 1.5f);
        c.Fill(new(plot.X, headY - 3, 5, 6), Gold);
        c.Unclip();
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty)
        {
            var r = new Rect(plot.X + 8, plot.Bottom - 37, Math.Min(plot.Width - 16, 380), 29);
            c.Fill(r, 0x343042, 5);
            c.Text(L.Get(draftBanana != Guid.Empty ? "ui.bananaDrawingHint" : "ui.drawingHint"), r.X + 9, r.Y + 7, 11, Foreground, r.Width - 16);
        }
    }

    private void DrawInspector(ICanvas c)
    {
        c.Fill(rightPanel, Panel);
        c.Line(rightPanel.X, 84, rightPanel.X, rightPanel.Bottom, Grid);
        float x = rightPanel.X + 16, w = rightPanel.Width - 32;
        c.Text(L.Get("ui.properties"), x, 99, 12, Foreground, 48, true);
        float arY = 91;
        float settingWidth = (w - 56) / 2;
        Field(c, x + 50, ref arY, settingWidth, L.Get("ui.ar"), Document.ApproachRate, value =>
        {
            if (value < 0 || value > 10) throw new ArgumentException(L.Get("ui.arRange"));
            Document.ApproachRate = value;
        }, 24);
        float csY = 91;
        Field(c, x + 56 + settingWidth, ref csY, settingWidth, L.Get("ui.cs"), Document.CircleSize, value =>
        {
            if (value < 0 || value > 10) throw new ArgumentException(L.Get("ui.csRange"));
            Document.CircleSize = value;
        }, 24);
        float y = 134;
        if (tool == Tool.Slider)
        {
            Button(c, new(x, y, w, 28), L.Get("ui.newSlider"), StartNewSlider); y += 34;
        }
        if (objectSelection.Count > 1 || anchorSelection.Count > 1)
        {
            bool anchors = tool == Tool.Slider;
            c.Text(L.Get("ui.selectedCount", anchors ? anchorSelection.Count : objectSelection.Count, L.Get(anchors ? "ui.anchorNoun" : "ui.objectNoun")), x, y, 15, Accent, w, true); y += 32;
            Button(c, new(x, y, w, 29), L.Get("ui.deleteSelected"), DeleteSelection); y += 37;
            c.Text(anchors ? L.Get("ui.anchorMultiHint") : L.Get("ui.objectMultiHint"), x, y, 11, Muted, w); y += 28;
        }
        else if (SelectedFruit is { } fruit)
        {
            Badge(c, new(x, y, 108, 24), L.Get("ui.fruitBadge"), Gold); y += 39;
            Field(c, x, ref y, w, L.Get("ui.timeField"), fruit.TimeMs, value =>
            {
                if (value < 0 || value > EditableDurationMs) throw new ArgumentException(L.Get("ui.timeRangeError"));
                Document.Fruits.First(f => f.Id == fruit.Id).TimeMs = value;
                Document.DurationMs = Math.Max(Document.DurationMs, value);
            });
            Field(c, x, ref y, w, L.Get("ui.xField"), fruit.X, value =>
            {
                if (value < 0 || value > 512) throw new ArgumentException(L.Get("ui.xRange"));
                Document.Fruits.First(f => f.Id == fruit.Id).X = value;
            });
            var timing = TimingMap.At(Document, fruit.TimeMs);
            c.Text(L.Get("ui.localBeat", (fruit.TimeMs - timing.OffsetMs) / timing.BeatLengthMs + 1), x, y + 5, 12, Muted, w);
            y += 36;
            Button(c, new(x, y, w, 29), L.Get("ui.deleteFruit"), DeleteSelection);
        }
        else if (SelectedTrack is { } track)
        {
            Badge(c, new(x, y, Math.Min(w, 154), 24), L.Get("ui.trackBadge"), Purple); y += 35;
            if (SelectedAnchor is { } node)
            {
                Field(c, x, ref y, w, L.Get("ui.timeField"), node.TimeMs, value =>
                {
                    if (value < 0 || value > EditableDurationMs) throw new ArgumentException(L.Get("ui.timeRangeError"));
                    MoveAnchor(track.Id, node.Id, value, null);
                    Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
                });
                Field(c, x, ref y, w, L.Get("ui.xField"), node.X, value =>
                {
                    MoveAnchor(track.Id, node.Id, null, value);
                });
                int index = track.Nodes.IndexOf(node);
                bool curved = CurvePointEditing.IsCurved(track, node.Id);
                Button(c, new(x, y, w, 28), curved ? L.Get("ui.curvePoint") : L.Get("ui.cornerPoint"),
                    () => SetSelectedPointCurved(!curved), false, draftTrack == Guid.Empty);
                y += 34;
                {
                    if (index > 0 && CurveMath.SegmentKind(track, index - 1) == CurveKind.Bezier)
                    {
                        Field(c, x, ref y, w, L.Get("ui.inTime"), node.HandleIn.TimeMs, value => SetHandle(track.Id, node.Id, true, value, null));
                        Field(c, x, ref y, w, L.Get("ui.inX"), node.HandleIn.X, value => SetHandle(track.Id, node.Id, true, null, value));
                    }
                    if (index < track.Nodes.Count - 1 && CurveMath.SegmentKind(track, index) == CurveKind.Bezier)
                    {
                        Field(c, x, ref y, w, L.Get("ui.outTime"), node.HandleOut.TimeMs, value => SetHandle(track.Id, node.Id, false, value, null));
                        Field(c, x, ref y, w, L.Get("ui.outX"), node.HandleOut.X, value => SetHandle(track.Id, node.Id, false, null, value));
                    }
                }
            }
            else
            {
                if (tool != Tool.Slider)
                {
                    Button(c, new(x, y, w, 28), L.Get("ui.editAnchors"), () => ChangeTool(Tool.Slider)); y += 34;
                }
                c.Text(track.Name, x, y, 14, Foreground, w, true); y += 27;
                var objects = conversion!.Objects.Where(o => o.SourceId == track.Id).ToArray();
                c.Text(L.Get("ui.streamCounts", objects.Count(o => o.Kind == CatchObjectKind.Fruit), objects.Count(o => o.Kind == CatchObjectKind.Droplet), objects.Count(o => o.Kind == CatchObjectKind.TinyDroplet)), x, y, 11, Accent, w); y += 20;
                c.Text(L.Get("ui.anchorCount", track.Nodes.Count), x, y, 12, Muted, w); y += 26;
                Field(c, x, ref y, w, L.Get("ui.spanCount"), track.SpanCount, value =>
                {
                    if (value != Math.Truncate(value) || value is < 1 or > 9000) throw new ArgumentException(L.Get("ui.spanRange"));
                    track.SpanCount = (int)value;
                    Document.DurationMs = Math.Max(Document.DurationMs, CurveMath.EndTimeMs(track));
                });
                c.Text(L.Get("ui.pickAnchorHint"), x, y, 11, Muted, w); y += 40;
            }
            Button(c, new(x, y + 3, w, 28), L.Get("ui.splitPreserving"), SplitSelected, false, draftTrack == Guid.Empty);
            y += 35;
        }
        else if (SelectedImportedSlider is { } imported)
        {
            c.Text(L.Get("ui.importedSlider"), x, y, 16, Purple, w, true); y += 31;
            c.Text(L.Get("ui.importedDetails", Number(imported.TimeMs), imported.PathType, imported.SpanCount), x, y, 12, Foreground, w); y += 25;
            Button(c, new(x, y, w, 30), L.Get("ui.editSlider"), EditImportedSlider); y += 37;
            c.Text(L.Get("ui.promoteHint"), x, y, 11, Muted, w); y += 25;
        }
        else if (SelectedBananaShower is { } shower)
        {
            c.Text(L.Get("ui.bananaBadge"), x, y, 16, Gold, w, true); y += 31;
            if (draftBanana == shower.Id)
            {
                c.Text(L.Get("ui.timeRange", Number(shower.TimeMs), Number(shower.EndTimeMs)), x, y, 12, Foreground, w); y += 25;
                c.Text(L.Get("ui.bananaDrawingHint"), x, y, 12, Muted, w);
            }
            else
            {
                Field(c, x, ref y, w, L.Get("ui.startTimeField"), shower.TimeMs, value =>
                {
                    if (value < 0 || value >= shower.EndTimeMs) throw new ArgumentException(L.Get("ui.bananaTimeRange"));
                    Document.BananaShowers.First(item => item.Id == shower.Id).TimeMs = value;
                });
                Field(c, x, ref y, w, L.Get("ui.endTimeField"), shower.EndTimeMs, value =>
                {
                    if (value <= shower.TimeMs || value > EditableDurationMs) throw new ArgumentException(L.Get("ui.bananaTimeRange"));
                    Document.BananaShowers.First(item => item.Id == shower.Id).EndTimeMs = value;
                    Document.DurationMs = Math.Max(Document.DurationMs, value);
                });
                c.Text(L.Get("ui.bananaHint"), x, y, 12, Muted, w); y += 30;
                Button(c, new(x, y, w, 29), L.Get("ui.deleteBanana"), DeleteSelection);
            }
        }
        else
        {
            c.Text(L.Get("ui.noSelection"), x, y, 16, Foreground, w, true); y += 33;
            c.Text(L.Get("ui.selectHint"), x, y, 12, Muted, w); y += 27;
            c.Text(L.Get("ui.fruitHint"), x, y, 12, Gold, w); y += 25;
            c.Text(L.Get("ui.sliderHint"), x, y, 12, Purple, w); y += 25;
            c.Text(L.Get("ui.selectToolHint"), x, y, 12, Accent, w); y += 35;
            c.Text(L.Get("ui.undoHint"), x, y, 11, Muted, w); y += 24;
            c.Text(L.Get("ui.panHint"), x, y, 11, Muted, w); y += 24;
            c.Text(L.Get("ui.wheelHint"), x, y, 11, Muted, w); y += 24;
        }
        if (fieldError.Length > 0)
        {
            c.Text(fieldError, x, y + 5, 11, Error, w);
            y += 27;
        }
        float previewTop = Math.Max(y + 22, rightPanel.Y + rightPanel.Height * 0.60f);
        if (rightPanel.Bottom - previewTop > 120) DrawPreview(c, new(x, previewTop, w, rightPanel.Bottom - previewTop - 15));
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
        var current = incoming ? node.HandleIn : node.HandleOut;
        var offset = new MapPoint(time ?? current.TimeMs, x ?? current.X);
        if (!CurveMath.TryMoveHandle(track, node.Id, incoming, offset, out var error)) throw new ArgumentException(error);
    }

    private void DrawPreview(ICanvas c, Rect r)
    {
        c.Line(r.X, r.Y - 12, r.Right, r.Y - 12, Grid);
        c.Text(L.Get("ui.preview"), r.X, r.Y, 13, Foreground, r.Width, true);
        Button(c, new(r.Right - 92, r.Y - 5, 92, 27), L.Get("ui.debugCurves"), () => showPreviewCurves = !showPreviewCurves, showPreviewCurves);
        double preempt = CatchScrollTiming.PreemptMs(Document.ApproachRate);
        c.Text(L.Get("ui.previewAr", Number(Document.ApproachRate), Number(preempt)), r.X, r.Y + 23, 10, Foreground, r.Width);
        c.Text(conversion!.Success ? L.Get("ui.generated") : L.Get("ui.partialFailure"), r.X, r.Y + 40, 10, conversion.Success ? Accent : Error, r.Width);
        Rect stage = new(r.X, r.Y + 62, r.Width, r.Height - 82);
        c.Fill(stage, 0x151A22, 5);
        c.Clip(stage);
        float fallAspect = (float)(CatchScrollTiming.FallDistance / CatchScrollTiming.PlayfieldWidth);
        float fieldWidth = MathF.Min(stage.Width - 18, MathF.Max(1, stage.Height - 12) / fallAspect);
        float fieldHeight = fieldWidth * fallAspect;
        float fieldLeft = stage.X + (stage.Width - fieldWidth) / 2;
        float fieldTop = stage.Y + (stage.Height - fieldHeight) / 2;
        float catchY = fieldTop + fieldHeight;
        double scrollSpeed = CatchScrollTiming.PixelsPerMs(Document.ApproachRate, fieldWidth);
        c.Stroke(new(fieldLeft, fieldTop, fieldWidth, fieldHeight), 0x2B3442);
        c.Line(fieldLeft, catchY, fieldLeft + fieldWidth, catchY, 0x677085);
        if (showPreviewCurves)
        {
            DrawImportedCurves(c, fieldLeft, fieldWidth, catchY, playhead, scrollSpeed,
                playhead, playhead + preempt, true);
            foreach (var track in Document.Tracks)
            {
                if (track.Nodes.Count < 2) continue;
                double begin = Math.Max(playhead, track.Nodes[0].TimeMs), end = Math.Min(playhead + preempt, CurveMath.EndTimeMs(track));
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
        foreach (var item in conversion!.Objects)
        {
            double remaining = item.TimeMs - playhead;
            if (remaining < 0 || remaining > preempt) continue;
            float y = catchY - (float)(remaining * scrollSpeed);
            DrawCatchObject(c, item, fieldLeft + (float)(item.X / 512) * fieldWidth, y, fieldWidth);
        }
        c.Unclip();
        c.Text(L.Get("ui.previewSkin", Number(Document.CircleSize), skin?.Name ?? L.Get("ui.basicShapes")), r.X, r.Bottom - 13, 10, Muted, r.Width);
    }

    private void DrawTransport(ICanvas c)
    {
        float top = height - 120;
        c.Fill(new(0, top, width, 92), 0x20252E);
        c.Line(0, top, width, top, Grid);
        Button(c, new(16, top + 14, 40, 36), AudioPlaying ? L.Get("ui.pauseSymbol") : L.Get("ui.playSymbol"), TogglePlayback, AudioPlaying, AudioReady);
        c.Text(Time(playhead), 69, top + 14, 21, Foreground, 143, true);
        c.Text("/ " + Time(TimelineDurationMs), 70, top + 42, 11, Muted, 130);
        c.Text(AudioNotice, 16, top + 71, 10, AudioReady ? Muted : Gold, 192);
        c.Text(L.Get("ui.timeNavigation"), overview.X, top + 12, 11, Foreground, 84);
        c.Text(AudioPlaying ? L.Get("ui.playing") : AudioReady ? L.Get("ui.paused") : L.Get("ui.noAudio"), overview.X + 80, top + 12, 10, Muted, 190);
        c.Text(L.Get("ui.seconds", TimelineDurationMs / 1000), width - 128, top + 12, 10, Muted, 116);
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
        foreach (var item in conversion!.Objects.Where(o => o.Kind != CatchObjectKind.TinyDroplet))
        {
            float x = overview.X + (float)(item.TimeMs / TimelineDurationMs) * overview.Width;
            c.Line(x, overview.Y + 23, x, overview.Y + 32, hyperdashObjects.Contains((item.SourceId, item.EventIndex)) ? Error : Foreground, 2);
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
        c.Text(notice, 25, height - 21, 11, conversion?.Diagnostics.Count > 0 ? Error : Muted, Math.Max(60, width - 292));
        c.Text(L.Get(RendererStatusKey, pixelsPerMs / 0.09 * 100, L.Get(IsDirty ? "ui.unsaved" : Document.IsDemo ? "ui.demoData" : "ui.unchanged")), width - 247, height - 21, 11, IsDirty ? Gold : Muted, 237);
    }

    private void DrawMenu(ICanvas c)
    {
        var rect = new Rect(109 + menu * 53, 38, 282, menu is 0 or 1 ? 281 : 171);
        c.Fill(new(rect.X + 3, rect.Y + 4, rect.Width, rect.Height), 0x11151B, 5);
        c.Fill(rect, Surface, 5); c.Stroke(rect, Grid, 1, 5);
        float y = rect.Y + 7;
        if (menu == 0)
        {
            Item(L.Get("ui.reloadDemo"), () => RequestResetDemo?.Invoke());
            Item(L.Get("ui.openMenu"), () => RequestOpen?.Invoke());
            Item(L.Get("ui.saveMenu"), () => RequestSave?.Invoke());
            Item(L.Get("ui.saveAsMenu"), () => RequestSaveAs?.Invoke());
            Item(L.Get("ui.exportMenu"), () => RequestExport?.Invoke());
            Item(L.Get("ui.audioMenu"), () => RequestAudio?.Invoke());
            Item(L.Get("ui.exitMenu"), () => RequestClose?.Invoke());
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
        }
        else
        {
            Item(L.Get("ui.resetView"), ResetView);
            Item(showTargets ? L.Get("ui.targetsOn") : L.Get("ui.targetsOff"), () => showTargets = !showTargets);
            Item(showPreviewCurves ? L.Get("ui.previewCurvesOn") : L.Get("ui.previewCurvesOff"), () => showPreviewCurves = !showPreviewCurves);
            Item(L.Get("ui.follow"), FollowPlayhead);
        }
        void Item(string label, Action action, bool enabled = true)
        {
            Button(c, new(rect.X + 6, y, rect.Width - 12, 31), label, () => { menu = -1; action(); }, false, enabled);
            y += 34;
        }
    }

    private void Button(ICanvas c, Rect r, string label, Action action, bool active = false, bool enabled = true)
    {
        bool hover = enabled && r.Contains(mouseX, mouseY);
        if (active) c.Fill(r, 0x31494B, 4);
        else if (hover) c.Fill(r, 0x343E4D, 4);
        if (active) c.Stroke(r, 0x477E7B, 1, 4);
        c.Text(label, r.X + 9, r.Y + (r.Height - 16) / 2, 12, !enabled ? 0x5B6777 : active ? Accent : Foreground, r.Width - 15, active);
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
        c.Fill(r, focused ? 0x273638u : 0x151B24u, 3);
        c.Stroke(r, focused ? (fieldError.Length > 0 ? Error : Accent) : r.Contains(mouseX, mouseY) ? 0x67758B : Grid, 1, 3);
        c.Text(focused ? editBuffer + "|" : Number(value), r.X + 9, r.Y + 7, 12, Foreground, r.Width - 14);
        fields.Add(new(r, label, value, apply));
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
