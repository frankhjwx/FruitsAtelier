using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Rect objectTimeline;
    private readonly List<BeatGridLine> timelineGrid = [];
    private double objectTimelineScale = .18;
    private readonly List<(Guid Id, double Time, Rect Bounds)> timelineObjects = [];
    private CatchConversionResult? timelineConversion;
    private Guid timelineDraft;
    private const int TimelineStackLimit = 16;
    private (Guid Id, double Start, double End, int SourceOrder, bool IsBanana, int Spans, int StackIndex, float StackOffset)[] timelineSources = [];
    private (Guid Id, double Start, double End, int SourceOrder, bool IsBanana, int Spans, int StackIndex, float StackOffset, int EventIndex)[] timelineMarkers = [];
    private readonly List<(Guid Id, double Start, double End, int SourceOrder, bool IsBanana, int Spans, int StackIndex, float StackOffset, int EventIndex)> timelineVisibleMarkers = [];
    private HashSet<Guid> timelinePatterns = [];
    private Dictionary<(Guid Id, int EventIndex), ConvertedCatchObject> timelinePatternEvents = [];
    private Dictionary<(Guid Id, int EventIndex), int> timelineEventNumbers = [];
    private (Guid Id, int EventIndex)? timelineFruit;
    private ConvertedCatchObject? SelectedTimelineFruit()
    {
        if (timelineFruit is not { } picked || objectSelection.Count != 1 || !objectSelection.Contains(picked.Id)) return null;
        RefreshTimelineSources();
        return timelinePatternEvents.GetValueOrDefault((picked.Id, picked.EventIndex));
    }
    private double[] timelineStarts = [], timelineEnds = [], timelinePrefixEnd = [];
    private bool boxTimeline;
    private Guid tailId;
    private string? tailOriginalLine;
    private double tailStart, tailEnd, tailSpanDuration;
    private double tailPointerOffset;
    private int breakEditIndex;
    private bool breakEditStart;
    private BreakPeriod breakEditOriginal, breakEditPreview;
    private BreakPeriod[] breakPeriods = [];
    private bool IsTimelineTail((Guid Id, double Time, Rect Bounds) item, float x, float y)
        => item.Bounds.Contains(x, y) && Math.Abs(x - (item.Bounds.Right - 19)) <= 10
        && (Document.Tracks.Any(t => t.Id == item.Id && t.Nodes.Count > 1 && t.StreamSnapDivisor is null)
            || Document.ImportedSliders.Any(s => s.Id == item.Id)
            || Document.BananaShowers.Any(s => s.Id == item.Id));
    public bool TimelineResizeCursor => !TimeJumpVisible && !LibraryVisible && !ExportVisible && !SliderDialogVisible && !DiscardConfirmationVisible && !ErrorVisible
        && (drag is DragKind.TimelineTail or DragKind.BreakEdge || menu < 0 && contextItems.Count == 0 && objectTimeline.Contains(mouseX, mouseY)
            && (BreakEdgeAt(mouseX, mouseY) is not null || timelineObjects.AsEnumerable().Reverse().Where(i => i.Bounds.Contains(mouseX, mouseY)).Take(1)
                .Any(i => IsTimelineTail(i, mouseX, mouseY))));

    private void MoveTimelineTail(float x, bool shift)
    {
        double end = ObjectTimelineStartMs + (x - objectTimeline.X) / objectTimelineScale - tailPointerOffset;
        if (Document.BananaShowers.FirstOrDefault(s => s.Id == tailId) is { } shower)
        {
            if (snap && !shift) end = TimingMap.Snap(Document, end, divisor);
            shower.EndTimeMs = Math.Clamp(end, Math.Min(EditableDurationMs, tailStart + .001), EditableDurationMs);
            Document.DurationMs = Math.Max(Document.DurationMs, shower.EndTimeMs);
            return;
        }
        int spans = (int)Math.Clamp(Math.Round((end - tailStart) / tailSpanDuration, MidpointRounding.AwayFromZero),
            1, Math.Max(1, Math.Min(9000, Math.Floor((int.MaxValue - tailStart) / tailSpanDuration))));
        if (Document.Tracks.FirstOrDefault(t => t.Id == tailId) is { } track) track.SpanCount = spans;
        else if (Document.ImportedSliders.FirstOrDefault(s => s.Id == tailId) is { } slider)
        {
            if (slider.SpanCount == spans) return;
            if (tailOriginalLine is { } original)
            {
                var fields = original.Split(',');
                fields[6] = spans.ToString(System.Globalization.CultureInfo.InvariantCulture);
                for (int field = 8; field <= 9 && field < fields.Length; field++)
                {
                    var edges = fields[field].Split('|');
                    fields[field] = string.Join('|', Enumerable.Range(0, spans + 1)
                        .Select(i => i == spans ? edges[^1] : i < edges.Length - 1 ? edges[i] : field == 8 ? "0" : "0:0"));
                }
                slider.OriginalLine = string.Join(',', fields);
            }
            slider.SpanCount = spans;
        }
        if (soundEdge is { } edge && edge.Id == tailId) soundEdge = (tailId, spans);
        Document.DurationMs = Math.Max(Document.DurationMs, tailStart + tailSpanDuration * spans);
    }
    public Rect ObjectTimelineBounds => objectTimeline;
    public double ObjectTimelinePixelsPerMs => objectTimelineScale;
    public double ObjectTimelineStartMs => playhead - objectTimeline.Width / objectTimelineScale / 2;
    private static readonly double[] PlaybackRates = [.1, .25, .5, .75, 1, 1.5];
    public double PlaybackSpeed { get; private set; } = 1;
    public Action<double>? RequestPlaybackSpeed { get; set; }

    private BreakPeriod? InsertBreakCandidate()
    {
        int lastStarted = UpperBound(timelineStarts, playhead) - 1;
        if (lastStarted >= 0 && timelinePrefixEnd[lastStarted] >= playhead) return null;
        int previous = LowerBound(timelineEnds, playhead) - 1;
        int next = UpperBound(timelineStarts, playhead);
        if (previous < 0 || next >= timelineStarts.Length) return null;
        double previousEnd = timelineEnds[previous], nextStart = timelineStarts[next];
        int start = OsuTimeline.BreakStartAfter(previousEnd);
        int end = (int)Math.Clamp(Math.Floor(nextStart - CatchScrollTiming.PreemptMs(Document.ApproachRate)), 0, int.MaxValue);
        if (end - (long)start < 400 || breakPeriods.Any(period => period.StartMs < end && period.EndMs > start)) return null;
        return new(start, end);
    }

    private void InsertBreakAtPlayhead()
    {
        if (InsertBreakCandidate() is not { } period) return;
        Edit(L.Get("timeline.addBreak"), () => OsuTimeline.AddBreak(Document, period.StartMs, period.EndMs));
    }

    private BreakPeriod[] DisplayedBreaks()
    {
        if (drag != DragKind.BreakEdge || breakEditIndex >= breakPeriods.Length) return breakPeriods;
        var periods = (BreakPeriod[])breakPeriods.Clone();
        periods[breakEditIndex] = breakEditPreview;
        return periods;
    }

    private (double? PreviousEnd, double? NextStart) BreakNeighborTimes(BreakPeriod period)
    {
        int previous = UpperBound(timelineEnds, period.StartMs) - 1;
        int next = LowerBound(timelineStarts, period.EndMs);
        return (previous >= 0 ? timelineEnds[previous] : null,
            next < timelineStarts.Length ? timelineStarts[next] : null);
    }

    private static int LowerBound(double[] values, double value)
    {
        int low = 0, high = values.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (values[middle] < value) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static int UpperBound(double[] values, double value)
    {
        int low = 0, high = values.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (values[middle] <= value) low = middle + 1; else high = middle;
        }
        return low;
    }

    private (double Before, double After) BreakTransitionLimits(BreakPeriod[] periods, int index)
    {
        var period = periods[index];
        var neighbors = BreakNeighborTimes(period);
        double before = Math.Max(neighbors.PreviousEnd ?? period.StartMs, index > 0 ? periods[index - 1].EndMs : 0);
        double after = Math.Min(neighbors.NextStart ?? period.EndMs,
            index + 1 < periods.Length ? periods[index + 1].StartMs : TimelineDurationMs);
        return (Math.Min(before, period.StartMs), Math.Max(after, period.EndMs));
    }

    private (int Index, bool Start)? BreakEdgeAt(float x, float y)
    {
        if (!objectTimeline.Contains(x, y)) return null;
        var periods = breakPeriods;
        double first = ObjectTimelineStartMs;
        float best = 7;
        (int, bool)? found = null;
        for (int i = 0; i < periods.Length; i++)
        {
            float startX = objectTimeline.X + (float)((periods[i].StartMs - first) * objectTimelineScale);
            float endX = objectTimeline.X + (float)((periods[i].EndMs - first) * objectTimelineScale);
            float startDistance = Math.Abs(x - startX), endDistance = Math.Abs(x - endX);
            if (startDistance < best) { best = startDistance; found = (i, true); }
            if (endDistance < best) { best = endDistance; found = (i, false); }
        }
        return found;
    }

    private bool BeginBreakEdge(float x, float y)
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty || BreakEdgeAt(x, y) is not { } edge) return false;
        breakEditIndex = edge.Index;
        breakEditStart = edge.Start;
        breakEditOriginal = breakPeriods[edge.Index];
        breakEditPreview = breakEditOriginal;
        drag = DragKind.BreakEdge;
        BeginPointerDrag(x, y);
        return true;
    }

    private void MoveBreakEdge(float x)
    {
        var periods = breakPeriods;
        var neighbors = BreakNeighborTimes(breakEditOriginal);
        double raw = ObjectTimelineStartMs + (x - objectTimeline.X) / objectTimelineScale;
        int target = (int)Math.Clamp(Math.Round(snap ? TimingMap.Snap(Document, raw, divisor) : raw), 0, int.MaxValue);
        if (breakEditStart)
        {
            int minimum = (int)Math.Clamp(Math.Ceiling(neighbors.PreviousEnd ?? 0), 0, breakEditOriginal.EndMs);
            if (breakEditIndex > 0) minimum = Math.Max(minimum, periods[breakEditIndex - 1].EndMs);
            breakEditPreview = breakEditOriginal with { StartMs = Math.Clamp(target, Math.Min(minimum, breakEditOriginal.EndMs), breakEditOriginal.EndMs) };
        }
        else
        {
            int maximum = (int)Math.Clamp(Math.Floor(neighbors.NextStart ?? TimelineDurationMs), breakEditOriginal.StartMs, int.MaxValue);
            if (breakEditIndex + 1 < periods.Length) maximum = Math.Min(maximum, periods[breakEditIndex + 1].StartMs);
            breakEditPreview = breakEditOriginal with { EndMs = Math.Clamp(target, breakEditOriginal.StartMs, Math.Max(maximum, breakEditOriginal.StartMs)) };
        }
    }

    private void FinishBreakEdge()
    {
        if (breakEditPreview != breakEditOriginal)
        {
            if (breakEditPreview.EndMs - (long)breakEditPreview.StartMs < 400)
                Edit(L.Get("timeline.removeBreak"), () => OsuTimeline.RemoveBreak(Document, breakEditOriginal));
            else Edit(L.Get("timeline.addBreak"), () => OsuTimeline.ReplaceBreak(Document, breakEditOriginal, breakEditPreview));
        }
        drag = DragKind.None;
    }

    public void SetPlaybackSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed < .1 || speed > 1.5 || speed == PlaybackSpeed) return;
        testplay?.SetPlaybackSpeed(speed);
        PlaybackSpeed = speed;
        RequestPlaybackSpeed?.Invoke(speed);
    }

    private void AdjustPlaybackSpeed(int direction, bool fine)
        => SetPlaybackSpeed(Math.Clamp((Math.Round(PlaybackSpeed * 100) + direction * (fine ? 5 : 25)) / 100, .1, 1.5));

    private Rect TimelineObjectBounds(double start, double end, float stackOffset)
        => new(objectTimeline.X + (float)((start - ObjectTimelineStartMs) * objectTimelineScale) - 19,
            objectTimeline.Y + 8 + stackOffset, (float)((end - start) * objectTimelineScale) + 38, 38);

    private void RefreshTimelineSources()
    {
        if (ReferenceEquals(timelineConversion, conversion)) return;
        if (draftTrack != Guid.Empty && timelineDraft == draftTrack
            && Document.Tracks.FirstOrDefault(t => t.Id == draftTrack) is { Nodes.Count: > 0 } draft)
        {
            int index = Array.FindIndex(timelineSources, item => item.Id == draftTrack);
            if (index >= 0 && timelineSources[index].Start == draft.Nodes[0].TimeMs)
            {
                // Only this draft's tail changes; preserve the committed parent order and combo numbers.
                timelineSources[index].End = CurveMath.EndTimeMs(draft);
                int marker = Array.FindIndex(timelineMarkers, item => item.Id == draftTrack);
                if (marker >= 0) timelineMarkers[marker].End = timelineSources[index].End;
                for (int i = 0; i < timelineSources.Length; i++)
                {
                    timelineEnds[i] = timelineSources[i].End;
                    timelinePrefixEnd[i] = Math.Max(i == 0 ? double.NegativeInfinity : timelinePrefixEnd[i - 1], timelineSources[i].End);
                }
                Array.Sort(timelineEnds);
                timelineConversion = conversion;
                return;
            }
        }
        // Slider duration resolves timing and geometry; recompute only when content changes, not on every frame.
        var sliderEnds = conversion!.Sliders.ToDictionary(s => s.SourceId, s => s.StartTimeMs + s.DurationMs);
        timelineSources = Document.Fruits.Select(f => (f.Id, Start: f.TimeMs, End: f.TimeMs, f.SourceOrder, IsBanana: false, Spans: 1))
            .Concat(Document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Start: t.Nodes[0].TimeMs, End: CurveMath.EndTimeMs(t), t.SourceOrder, IsBanana: false, Spans: t.SpanCount)))
            .Concat(Document.ImportedSliders.Select(s => (s.Id, Start: s.TimeMs, End: sliderEnds.TryGetValue(s.Id, out double endTime) ? endTime : ImportedSliderConverter.EndTimeMs(Document, s), s.SourceOrder, IsBanana: false, Spans: s.SpanCount)))
            .Concat(Document.BananaShowers.Select(s => (s.Id, Start: s.TimeMs, End: s.EndTimeMs, s.SourceOrder, IsBanana: true, Spans: 1)))
            .OrderBy(o => o.Start).ThenBy(o => o.SourceOrder)
            .Select(o => (o.Id, o.Start, o.End, o.SourceOrder, o.IsBanana, o.Spans, StackIndex: 0, StackOffset: 0f)).ToArray();
        timelinePatterns = Document.Tracks.Where(t => t.StreamSnapDivisor is not null).Select(t => t.Id).ToHashSet();
        var patterns = timelineSources.Where(s => timelinePatterns.Contains(s.Id)).ToDictionary(s => s.Id);
        timelinePatternEvents = conversion.Objects.Where(o => patterns.ContainsKey(o.SourceId)).ToDictionary(o => (o.SourceId, o.EventIndex));
        var ordinaryMarkers = timelineSources.Where(s => !timelinePatterns.Contains(s.Id))
            .Select(s => (s.Id, s.Start, s.End, s.SourceOrder, s.IsBanana, s.Spans, s.StackIndex, s.StackOffset, EventIndex: 0));
        timelineMarkers = timelinePatterns.Count == 0 ? ordinaryMarkers.ToArray() : ordinaryMarkers
            .Concat(timelinePatternEvents.Values.Select(o =>
                (Id: o.SourceId, Start: o.TimeMs, End: o.TimeMs, patterns[o.SourceId].SourceOrder,
                    IsBanana: false, Spans: 1, StackIndex: 0, StackOffset: 0f, o.EventIndex)))
            .OrderBy(s => s.Start).ThenBy(s => s.SourceOrder).ThenBy(s => timelinePatterns.Contains(s.Id) ? patterns[s.Id].Start : s.Start).ToArray();
        for (int first = 0; first < timelineMarkers.Length;)
        {
            int last = first + 1;
            while (last < timelineMarkers.Length && timelineMarkers[last].Start == timelineMarkers[first].Start) last++;
            int layers = Math.Min(TimelineStackLimit, last - first);
            // Fit the circles and selection rings inside the existing 64-DIP timeline.
            float step = layers > 1 ? Math.Min(3, 22f / (layers - 1)) : 0;
            float baseline = Math.Max(0, step * (layers - 1) - 6);
            for (int i = first; i < last; i++)
            {
                timelineMarkers[i].StackIndex = i - first;
                timelineMarkers[i].StackOffset = baseline - Math.Min(i - first, TimelineStackLimit - 1) * step;
            }
            first = last;
        }
        timelineStarts = timelineSources.Select(item => item.Start).ToArray();
        timelineEnds = timelineSources.Select(item => item.End).Order().ToArray();
        timelinePrefixEnd = new double[timelineSources.Length];
        double latestEnd = double.NegativeInfinity;
        for (int i = 0; i < timelineSources.Length; i++)
            timelinePrefixEnd[i] = latestEnd = Math.Max(latestEnd, timelineSources[i].End);
        var heads = timelinePatterns.Count > 0 ? timelineMarkers.Where(s => s.EventIndex == 0).ToDictionary(s => s.Id) : null;
        for (int i = 0; i < timelineSources.Length; i++)
            if (heads is null)
            {
                timelineSources[i].StackIndex = timelineMarkers[i].StackIndex;
                timelineSources[i].StackOffset = timelineMarkers[i].StackOffset;
            }
            else if (heads.TryGetValue(timelineSources[i].Id, out var head))
            {
                timelineSources[i].StackIndex = head.StackIndex;
                timelineSources[i].StackOffset = head.StackOffset;
            }
        timelineEventNumbers = ComboEventNumbers();
        timelineConversion = conversion;
        timelineDraft = draftTrack;
    }

    private (Guid Id, double Start, double End, uint Color)? TimelinePlacementPreview()
    {
        if (!plot.Contains(mouseX, mouseY) || drag != DragKind.None || menu >= 0 || contextItems.Count > 0 || editField >= 0)
            return null;
        if (tool == Tool.Banana)
        {
            double time = MapAt(mouseX, mouseY, true).TimeMs;
            if (Document.BananaShowers.FirstOrDefault(s => s.Id == draftBanana) is { } banana)
                return (banana.Id, banana.TimeMs, Math.Max(banana.TimeMs, time), Gold);
            return (placementId, time, time, Gold);
        }
        if (tool == Tool.Slider && SelectedTrack is { } track && track.Id == draftTrack)
        {
            var candidate = placementDraftTrack ?? track;
            return (track.Id, candidate.Nodes[0].TimeMs, CurveMath.EndTimeMs(candidate), ComboColour(track.Id, useFallbackPalette: true));
        }
        if (PlacementGhostPoint() is { } point)
            return (placementId, point.TimeMs, point.TimeMs, Foreground);
        return null;
    }

    private void DrawObjectTimeline(ICanvas c)
    {
        float top = canvas.Y + 38;
        objectTimeline = new(leftPanel.Right + 42, top + 4, Math.Max(30, canvas.Right - leftPanel.Right - 54), 64);
        timelineObjects.Clear();
        Button(c, new(leftPanel.Right + 7, top + 5, 29, 28), L.Get("ui.timelineZoomIn"), () => ZoomObjectTimeline(1.25));
        Button(c, new(leftPanel.Right + 7, top + 37, 29, 28), L.Get("ui.timelineZoomOut"), () => ZoomObjectTimeline(.8));
        objectTimelineScale = Math.Clamp(objectTimelineScale, .025, MaximumObjectTimelineScale);
        double start = ObjectTimelineStartMs;
        double end = start + objectTimeline.Width / objectTimelineScale;
        float X(double time) => objectTimeline.X + (float)((time - start) * objectTimelineScale);
        RefreshTimelineSources();
        c.Clip(objectTimeline);
        var breaks = DisplayedBreaks();
        for (int i = 0; i < breaks.Length; i++)
        {
            var period = breaks[i];
            var limits = BreakTransitionLimits(breaks, i);
            Band(limits.Before, period.StartMs, 0xF1F1F1, .16f);
            Band(period.EndMs, limits.After, 0xA7CFA1, .20f);
            Band(period.StartMs, period.EndMs,
                drag == DragKind.BreakEdge && i == breakEditIndex && period.EndMs - period.StartMs < 400 ? 0xAB5353u : 0x858585u, .45f);
        }
        void Band(double from, double to, uint color, float opacity)
        {
            double a = Math.Max(start, from), b = Math.Min(end, to);
            if (b > a) c.Fill(new(X(a), objectTimeline.Y, X(b) - X(a), objectTimeline.Height - 1), color, 0, opacity);
        }
        renderedTiming!.FillGrid(Math.Max(0, start), Math.Max(0, end), divisor, timelineGrid);
        foreach (var tick in timelineGrid)
        {
            if (!tick.IsBeat && renderedTiming.At(tick.TimeMs).BeatLengthMs / tick.Subdivision * objectTimelineScale < 5) continue;
            var style = GridStyle(tick);
            float height = tick.IsMeasure ? 30 : style.Height;
            c.Line(X(tick.TimeMs), objectTimeline.Bottom - height, X(tick.TimeMs), objectTimeline.Bottom,
                style.Color, style.Width);
        }
        var placement = TimelinePlacementPreview();
        double visibleStartMs = start - 20 / objectTimelineScale, visibleEndMs = end + 20 / objectTimelineScale;
        var visibleItems = timelineVisibleMarkers;
        visibleItems.Clear();
        // Cached chronological order also defines painting and hit order; reuse the visible buffer each frame.
        for (int i = timelineMarkers.Length - 1; i >= 0; i--)
        {
            var item = timelineMarkers[i];
            if (item.Id != placement?.Id && item.StackIndex < TimelineStackLimit && item.End >= visibleStartMs && item.Start <= visibleEndMs)
                visibleItems.Add(item);
        }
        foreach (var item in visibleItems)
            timelineObjects.Add((item.Id, item.Start, TimelineObjectBounds(item.Start, item.End, item.StackOffset)));
        // Each object's body, circles and number share its chronological layer and hit order.
        for (int i = 0; i < visibleItems.Count; i++) { DrawBody(i); DrawMarkers(i); }
        if (placement is { } ghost && ghost.End >= start - 20 / objectTimelineScale && ghost.Start <= end + 20 / objectTimelineScale)
        {
            var bounds = TimelineObjectBounds(ghost.Start, ghost.End, 0);
            if (ghost.End > ghost.Start) c.Fill(bounds, ghost.Color, 19, .35f);
            FruitsAtelier.App.Skinning.CatchSkin.DrawTimelineCircle(c, skin ?? defaultSkin,
                X(ghost.Start), bounds.Y + 19, 38, ghost.Color, opacity: .45f);
            if (ghost.End > ghost.Start)
                FruitsAtelier.App.Skinning.CatchSkin.DrawTimelineCircle(c, skin ?? defaultSkin,
                    X(ghost.End), bounds.Y + 19, 38, ghost.Color, opacity: .45f);
        }

        void DrawBody(int index)
        {
            var item = visibleItems[index];
            if (item.End <= item.Start) return;
            bool selected = IsObjectSelected(item.Id);
            uint color = item.IsBanana ? Gold : ComboColour(item.Id, useFallbackPalette: true, eventIndex: item.EventIndex);
            var bounds = timelineObjects[index].Bounds;
            if (item.IsBanana && bananaSegments.TryGetValue(item.Id, out var segments))
            {
                foreach (var segment in segments)
                {
                    float from = X(segment.TimeMs), to = X(segment.EndTimeMs);
                    if (to < objectTimeline.X || from > objectTimeline.Right) continue;
                    c.Fill(new(from, bounds.Y + 3, Math.Max(1, to - from), bounds.Height - 6), color, 2, .7f);
                }
            }
            else c.Fill(bounds, color, 19, .7f);
            if (item.IsBanana)
            {
                c.Stroke(bounds, 0xFFFFFF, 1.5f, 19);
                if (selected) c.Stroke(bounds, 0x2866C6, 2, 19);
            }
        }

        void DrawMarkers(int index)
        {
            var item = visibleItems[index];
            bool selected = IsObjectSelected(item.Id);
            uint color = item.IsBanana ? Gold : ComboColour(item.Id, useFallbackPalette: true, eventIndex: item.EventIndex);
            float left = X(item.Start), right = X(item.End), cy = timelineObjects[index].Bounds.Y + 19;
            void Ring(float ringX, int? number = null, int? edge = null)
            {
                // Timeline endpoints use the combo marker, independently of gameplay slider textures.
                FruitsAtelier.App.Skinning.CatchSkin.DrawTimelineCircle(c, skin ?? defaultSkin, ringX, cy, 38, color, number);
                if (selected && (soundEdge is not { } picked || picked.Id != item.Id || picked.Edge == edge))
                {
                    c.Circle(ringX, cy, 19, 0xFFA600, false, 3);
                    c.Circle(ringX, cy, 21, 0x2866C6, false, 2);
                }
                if (timelineFruit == (item.Id, item.EventIndex)) c.Circle(ringX, cy, 21, Gold, false, 2);
            }
            if (right > left + 1) Ring(right, edge: item.Spans);
            if (item.Spans > 1 && item.End > item.Start)
            {
                double spanDuration = (item.End - item.Start) / item.Spans;
                int first = (int)Math.Clamp(Math.Ceiling((start - 20 / objectTimelineScale - item.Start) / spanDuration), 1, item.Spans);
                int last = (int)Math.Clamp(Math.Floor((end + 20 / objectTimelineScale - item.Start) / spanDuration), 0, item.Spans - 1);
                for (int span = last; span >= first; span--)
                {
                    float repeatX = X(item.Start + span * spanDuration);
                    Ring(repeatX, edge: span);
                    if (skin is null || !skin.DrawReverseArrow(c, repeatX, cy, 38))
                    {
                        c.Line(repeatX - 8, cy, repeatX + 4, cy, Foreground, 5);
                        c.Line(repeatX + 1, cy - 6, repeatX + 8, cy, Foreground, 3);
                        c.Line(repeatX + 8, cy, repeatX + 1, cy + 6, Foreground, 3);
                    }
                }
            }
            Ring(left, timelineEventNumbers[(item.Id, item.EventIndex)], edge: timelinePatterns.Contains(item.Id) ? null : 0);
        }
        foreach (var period in breaks)
        {
            double visibleStart = Math.Max(start, period.StartMs), visibleEnd = Math.Min(end, period.EndMs);
            if ((visibleEnd - visibleStart) * objectTimelineScale >= 48)
                c.Text(L.Get("timeline.breakLabel"), X(visibleStart) + 8, objectTimeline.Y + 19, 14,
                    Foreground, X(visibleEnd) - X(visibleStart) - 12);
        }
        (int Index, bool Start)? highlightedEdge = drag == DragKind.BreakEdge
            ? (breakEditIndex, breakEditStart) : BreakEdgeAt(mouseX, mouseY);
        if (highlightedEdge is { } edge)
        {
            float edgeX = X(edge.Start ? breaks[edge.Index].StartMs : breaks[edge.Index].EndMs);
            c.Line(edgeX, objectTimeline.Y + 2, edgeX, objectTimeline.Bottom - 2, 0xE8ECED, 2, .8f);
        }
        DrawTimelinePreviewMarker(c, objectTimeline, start, end);
        foreach (var point in Document.TimingPoints)
        {
            if (point.TimeMs < start || point.TimeMs > end) continue;
            float x = X(point.TimeMs);
            c.Line(x, objectTimeline.Y, x, objectTimeline.Bottom - 1,
                point.Uninherited ? 0xEA2222u : 0x7BC600u, 1, .8f);
        }
        float head = X(playhead);
        c.Line(head, objectTimeline.Y, head, objectTimeline.Bottom, Gold, 2);
        c.Line(objectTimeline.X, objectTimeline.Bottom - 1, objectTimeline.Right, objectTimeline.Bottom - 1, Grid);
        DrawTimelineBookmarks(c, objectTimeline, start, end);
        c.Unclip();
    }

    private void ZoomObjectTimeline(double factor)
    {
        objectTimelineScale = Math.Clamp(objectTimelineScale * factor, .025, MaximumObjectTimelineScale);
        LibrarySettings.ObjectTimelineScale = objectTimelineScale;
        RequestViewPreference?.Invoke();
    }

    private void BeginObjectTimeline(float x, float y, bool toggle)
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        for (int i = timelineObjects.Count - 1; i >= 0; i--)
        {
            var item = timelineObjects[i];
            if (!item.Bounds.Contains(x, y)) continue;
            tool = Tool.Select;
            PickObject(item.Id, toggle);
            soundEdge = null; distanceObject = null; clickedCoordinate = null;
            timelineFruit = timelinePatterns.Contains(item.Id) && objectSelection.Count == 1 && objectSelection.Contains(item.Id)
                ? (item.Id, timelineMarkers.First(m => m.Id == item.Id && m.Start == item.Time).EventIndex) : null;
            var markerSource = timelineSources.First(o => o.Id == item.Id);
            if (!markerSource.IsBanana && !timelinePatterns.Contains(item.Id) && markerSource.End > markerSource.Start && markerSource.Spans > 0
                && objectSelection.Count == 1 && objectSelection.Contains(item.Id) && Math.Abs(y-(item.Bounds.Y+19)) <= 19)
            {
                double spanDuration = (markerSource.End-markerSource.Start)/markerSource.Spans;
                double time = ObjectTimelineStartMs+(x-objectTimeline.X)/objectTimelineScale;
                int edge = (int)Math.Clamp(Math.Round((time-markerSource.Start)/spanDuration),0,markerSource.Spans);
                double markerX = objectTimeline.X+(markerSource.Start+edge*spanDuration-ObjectTimelineStartMs)*objectTimelineScale;
                if (Math.Abs(x-markerX)<=19) soundEdge = (item.Id,edge);
            }
            if (!notesLocked && !toggle && IsTimelineTail(item, x, y))
            {
                var source = timelineSources.First(o => o.Id == item.Id);
                int spans = Document.Tracks.FirstOrDefault(t => t.Id == item.Id)?.SpanCount
                    ?? Document.ImportedSliders.FirstOrDefault(s => s.Id == item.Id)?.SpanCount ?? 1;
                tailOriginalLine = Document.ImportedSliders.FirstOrDefault(s => s.Id == item.Id)?.OriginalLine;
                tailId = item.Id; tailStart = source.Start; tailEnd = source.End;
                tailPointerOffset = ObjectTimelineStartMs + (x - objectTimeline.X) / objectTimelineScale - source.End;
                tailSpanDuration = (source.End - source.Start) / spans;
                if (tailSpanDuration > 0)
                {
                    history.Begin(L.Get(source.IsBanana ? "editor.command.resizeBanana" : "editor.command.adjustReverse"));
                    drag = DragKind.TimelineTail; BeginPointerDrag(x, y);
                }
            }
            else if (!toggle) BeginObjectDrag(x, y, timeline: true);
            return;
        }
        tool = Tool.Select;
        BeginBox(x, y, toggle, false);
        boxStartTime = ObjectTimelineStartMs + (x - objectTimeline.X) / objectTimelineScale;
        boxTimeline = true;
    }
    private void DeleteTimelineObject(float x, float y)
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        for (int i = timelineObjects.Count - 1; i >= 0; i--)
        {
            var item = timelineObjects[i];
            if (!item.Bounds.Contains(x, y)) continue;
            tool = Tool.Select;
            if (!objectSelection.Contains(item.Id)) SelectObjects([item.Id]);
            DeleteSelectedObjects();
            return;
        }
    }

}
