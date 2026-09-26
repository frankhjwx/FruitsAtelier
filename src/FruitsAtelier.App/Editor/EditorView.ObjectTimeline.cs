using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Rect objectTimeline;
    private double objectTimelineScale = .18;
    private readonly List<(Guid Id, double Time, Rect Bounds)> timelineObjects = [];
    private CatchConversionResult? timelineConversion;
    private (Guid Id, double Start, double End, int SourceOrder, bool IsBanana, int Spans)[] timelineSources = [];
    private double[] timelineStarts = [], timelineEnds = [], timelinePrefixEnd = [];
    private Dictionary<Guid, int> timelineNumbers = [];
    private bool boxTimeline;
    private Guid tailId;
    private string? tailOriginalLine;
    private double tailStart, tailEnd, tailSpanDuration;
    private int breakEditIndex;
    private bool breakEditStart;
    private BreakPeriod breakEditOriginal, breakEditPreview;
    private BreakPeriod[] breakPeriods = [];
    private bool IsTimelineTail((Guid Id, double Time, Rect Bounds) item, float x, float y)
        => item.Bounds.Contains(x, y) && Math.Abs(x - (item.Bounds.Right - 19)) <= 10
        && (Document.Tracks.Any(t => t.Id == item.Id && t.Nodes.Count > 1)
            || Document.ImportedSliders.Any(s => s.Id == item.Id));
    public bool TimelineResizeCursor => !TimeJumpVisible && !LibraryVisible && !ExportVisible && !SliderDialogVisible && !DiscardConfirmationVisible && !ErrorVisible
        && (drag is DragKind.TimelineTail or DragKind.BreakEdge || menu < 0 && contextItems.Count == 0 && objectTimeline.Contains(mouseX, mouseY)
            && (BreakEdgeAt(mouseX, mouseY) is not null || timelineObjects.AsEnumerable().Reverse().Where(i => i.Bounds.Contains(mouseX, mouseY)).Take(1)
                .Any(i => IsTimelineTail(i, mouseX, mouseY))));

    private void MoveTimelineTail(float x)
    {
        double end = tailEnd + (x - dragStartX) / objectTimelineScale;
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
        int start = (int)Math.Clamp(Math.Ceiling(previousEnd + 200), 0, int.MaxValue);
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
        PlaybackSpeed = speed;
        RequestPlaybackSpeed?.Invoke(speed);
    }

    private void AdjustPlaybackSpeed(int direction, bool fine)
        => SetPlaybackSpeed(Math.Clamp((Math.Round(PlaybackSpeed * 100) + direction * (fine ? 5 : 25)) / 100, .1, 1.5));

    private Rect TimelineObjectBounds(double start, double end)
        => new(objectTimeline.X + (float)((start - ObjectTimelineStartMs) * objectTimelineScale) - 19,
            objectTimeline.Y + 8, (float)((end - start) * objectTimelineScale) + 38, 38);

    private void RefreshTimelineSources()
    {
        if (ReferenceEquals(timelineConversion, conversion)) return;
        // Slider duration resolves timing and geometry; recompute only when content changes, not on every frame.
        var sliderEnds = conversion!.Sliders.ToDictionary(s => s.SourceId, s => s.StartTimeMs + s.DurationMs);
        timelineSources = Document.Fruits.Select(f => (f.Id, Start: f.TimeMs, End: f.TimeMs, f.SourceOrder, IsBanana: false, Spans: 1))
            .Concat(Document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Start: t.Nodes[0].TimeMs, End: CurveMath.EndTimeMs(t), t.SourceOrder, IsBanana: false, Spans: t.SpanCount)))
            .Concat(Document.ImportedSliders.Select(s => (s.Id, Start: s.TimeMs, End: sliderEnds.TryGetValue(s.Id, out double endTime) ? endTime : ImportedSliderConverter.EndTimeMs(Document, s), s.SourceOrder, IsBanana: false, Spans: s.SpanCount)))
            .Concat(Document.BananaShowers.Select(s => (s.Id, Start: s.TimeMs, End: s.EndTimeMs, s.SourceOrder, IsBanana: true, Spans: 1)))
            .OrderBy(o => o.Start).ThenBy(o => o.SourceOrder).ToArray();
        timelineStarts = timelineSources.Select(item => item.Start).ToArray();
        timelineEnds = timelineSources.Select(item => item.End).Order().ToArray();
        timelinePrefixEnd = new double[timelineSources.Length];
        double latestEnd = double.NegativeInfinity;
        for (int i = 0; i < timelineSources.Length; i++)
            timelinePrefixEnd[i] = latestEnd = Math.Max(latestEnd, timelineSources[i].End);
        timelineNumbers = ComboNumbers();
        timelineConversion = conversion;
    }

    private void DrawObjectTimeline(ICanvas c)
    {
        float top = canvas.Y + 38;
        objectTimeline = new(42, top + 4, Math.Max(30, canvas.Right - 54), 64);
        timelineObjects.Clear();
        c.Fill(new(0, top, canvas.Right, 72), Background);
        Button(c, new(7, top + 5, 29, 28), L.Get("ui.timelineZoomIn"), () => ZoomObjectTimeline(1.25));
        Button(c, new(7, top + 37, 29, 28), L.Get("ui.timelineZoomOut"), () => ZoomObjectTimeline(.8));
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
        foreach (var tick in renderedTiming!.Grid(Math.Max(0, start), Math.Max(0, end), divisor))
        {
            if (!tick.IsBeat && renderedTiming.At(tick.TimeMs).BeatLengthMs / tick.Subdivision * objectTimelineScale < 5) continue;
            var style = GridStyle(tick);
            c.Line(X(tick.TimeMs), objectTimeline.Bottom - style.Height, X(tick.TimeMs), objectTimeline.Bottom,
                style.Color, style.Width);
        }
        var visibleItems = timelineSources.Where(item => item.End >= start - 20 / objectTimelineScale
            && item.Start <= end + 20 / objectTimelineScale)
            .OrderByDescending(item => item.Start).ThenByDescending(item => item.End).ThenByDescending(item => item.SourceOrder).ToArray();
        foreach (var item in visibleItems)
            timelineObjects.Add((item.Id, item.Start, TimelineObjectBounds(item.Start, item.End)));
        // Each object's body, circles and number share its chronological layer and hit order.
        for (int i = 0; i < visibleItems.Length; i++) { DrawBody(i); DrawMarkers(i); }

        void DrawBody(int index)
        {
            var item = visibleItems[index];
            if (item.End <= item.Start) return;
            bool selected = IsObjectSelected(item.Id);
            uint color = item.IsBanana ? Gold : ComboColour(item.Id, useFallbackPalette: true);
            var bounds = timelineObjects[index].Bounds;
            c.Fill(bounds, item.IsBanana ? color : skin?.SliderTrackColour ?? color, 19, .7f);
            c.Stroke(bounds, item.IsBanana ? 0xFFFFFF : skin?.SliderBorderColour ?? 0xFFFFFFu, 1.5f, 19);
            if (selected) c.Stroke(bounds, 0x2866C6, 2, 19);
        }

        void DrawMarkers(int index)
        {
            var item = visibleItems[index];
            bool selected = IsObjectSelected(item.Id);
            uint color = item.IsBanana ? Gold : ComboColour(item.Id, useFallbackPalette: true);
            float left = X(item.Start), right = X(item.End), cy = objectTimeline.Y + 27;
            void Ring(float ringX, int? number = null, string prefix = "hitcircle")
            {
                FruitsAtelier.App.Skinning.CatchSkin.DrawTimelineCircle(c, skin ?? defaultSkin, ringX, cy, 38, color, number, prefix);
                if (selected)
                {
                    c.Circle(ringX, cy, 19, 0xFFA600, false, 3);
                    c.Circle(ringX, cy, 21, 0x2866C6, false, 2);
                }
            }
            if (right > left + 1) Ring(right, prefix: "sliderendcircle");
            if (item.Spans > 1 && item.End > item.Start)
            {
                double spanDuration = (item.End - item.Start) / item.Spans;
                int first = (int)Math.Clamp(Math.Ceiling((start - 20 / objectTimelineScale - item.Start) / spanDuration), 1, item.Spans);
                int last = (int)Math.Clamp(Math.Floor((end + 20 / objectTimelineScale - item.Start) / spanDuration), 0, item.Spans - 1);
                for (int span = last; span >= first; span--)
                {
                    float repeatX = X(item.Start + span * spanDuration);
                    Ring(repeatX);
                    if (skin is null || !skin.DrawReverseArrow(c, repeatX, cy, 38))
                    {
                        c.Line(repeatX - 8, cy, repeatX + 4, cy, Foreground, 5);
                        c.Line(repeatX + 1, cy - 6, repeatX + 8, cy, Foreground, 3);
                        c.Line(repeatX + 8, cy, repeatX + 1, cy + 6, Foreground, 3);
                    }
                }
            }
            Ring(left, timelineNumbers[item.Id], item.End > item.Start ? "sliderstartcircle" : "hitcircle");
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
        c.Unclip();
    }

    private void ZoomObjectTimeline(double factor) => objectTimelineScale = Math.Clamp(objectTimelineScale * factor, .025, 1.5);

    private void BeginObjectTimeline(float x, float y, bool toggle)
    {
        if (draftTrack != Guid.Empty || draftBanana != Guid.Empty) return;
        for (int i = timelineObjects.Count - 1; i >= 0; i--)
        {
            var item = timelineObjects[i];
            if (!item.Bounds.Contains(x, y)) continue;
            tool = Tool.Select;
            PickObject(item.Id, toggle);
            if (!notesLocked && !toggle && IsTimelineTail(item, x, y))
            {
                var source = timelineSources.First(o => o.Id == item.Id);
                int spans = Document.Tracks.FirstOrDefault(t => t.Id == item.Id)?.SpanCount
                    ?? Document.ImportedSliders.First(s => s.Id == item.Id).SpanCount;
                tailOriginalLine = Document.ImportedSliders.FirstOrDefault(s => s.Id == item.Id)?.OriginalLine;
                tailId = item.Id; tailStart = source.Start; tailEnd = source.End;
                tailSpanDuration = (source.End - source.Start) / spans;
                if (tailSpanDuration > 0)
                {
                    history.Begin(L.Get("editor.command.adjustReverse"));
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
