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
    private (Guid Id, double Start, double End, int SourceOrder, uint Color, int Spans)[] timelineSources = [];
    private Dictionary<Guid, int> timelineNumbers = [];
    private bool boxTimeline;
    private double timelineBoxStart;
    private Guid tailId;
    private string? tailOriginalLine;
    private double tailStart, tailEnd, tailSpanDuration;
    private bool IsTimelineTail((Guid Id, double Time, Rect Bounds) item, float x, float y)
        => item.Bounds.Contains(x, y) && Math.Abs(x - (item.Bounds.Right - 19)) <= 10
        && (Document.Tracks.Any(t => t.Id == item.Id && t.Nodes.Count > 1)
            || Document.ImportedSliders.Any(s => s.Id == item.Id));
    public bool TimelineResizeCursor => !TimeJumpVisible && !LibraryVisible && !ExportVisible && !SliderDialogVisible && !DiscardConfirmationVisible && !ErrorVisible
        && (drag == DragKind.TimelineTail || menu < 0 && contextItems.Count == 0 && objectTimeline.Contains(mouseX, mouseY)
            && timelineObjects.AsEnumerable().Reverse().Where(i => i.Bounds.Contains(mouseX, mouseY)).Take(1)
                .Any(i => IsTimelineTail(i, mouseX, mouseY)));

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
    public double ObjectTimelineStartMs => boxTimeline && drag == DragKind.Marquee ? timelineBoxStart : playhead - objectTimeline.Width / objectTimelineScale / 2;
    public double PlaybackSpeed { get; private set; } = 1;
    public Action<double>? RequestPlaybackSpeed { get; set; }

    public void SetPlaybackSpeed(double speed)
    {
        if (speed is not (.25 or .5 or .75 or 1) || speed == PlaybackSpeed) return;
        PlaybackSpeed = speed;
        RequestPlaybackSpeed?.Invoke(speed);
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
        c.Clip(objectTimeline);
        foreach (var tick in renderedTiming!.Grid(Math.Max(0, start), Math.Max(0, end), divisor))
        {
            if (!tick.IsBeat && !tick.IsTimingBoundary && renderedTiming.At(tick.TimeMs).BeatLengthMs / divisor * objectTimelineScale < 5) continue;
            c.Line(X(tick.TimeMs), objectTimeline.Bottom - (tick.IsBeat ? 12 : 5), X(tick.TimeMs), objectTimeline.Bottom,
                tick.IsTimingBoundary ? Error : tick.IsBeat ? Muted : Grid);
        }
        if (!ReferenceEquals(timelineConversion, conversion))
        {
            // Slider duration resolves timing and geometry; recompute only when content changes, not on every frame.
            var sliderEnds = conversion!.Sliders.ToDictionary(s => s.SourceId, s => s.StartTimeMs + s.DurationMs);
            timelineSources = Document.Fruits.Select(f => (f.Id, Start: f.TimeMs, End: f.TimeMs, f.SourceOrder, Color: Accent, Spans: 1))
            .Concat(Document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Start: t.Nodes[0].TimeMs, End: CurveMath.EndTimeMs(t), t.SourceOrder, Color: Purple, Spans: t.SpanCount)))
            .Concat(Document.ImportedSliders.Select(s => (s.Id, Start: s.TimeMs, End: sliderEnds.TryGetValue(s.Id, out double endTime) ? endTime : ImportedSliderConverter.EndTimeMs(Document, s), s.SourceOrder, Color: Purple, Spans: s.SpanCount)))
            .Concat(Document.BananaShowers.Select(s => (s.Id, Start: s.TimeMs, End: s.EndTimeMs, s.SourceOrder, Color: Gold, Spans: 1)))
            .OrderBy(o => o.Start).ThenBy(o => o.SourceOrder).ToArray();
            timelineNumbers = ComboNumbers();
            timelineConversion = conversion;
        }
        foreach (var item in timelineSources)
        {
            int number = timelineNumbers[item.Id];
            if (item.End < start - 20 / objectTimelineScale || item.Start > end + 20 / objectTimelineScale) continue;
            float left = X(item.Start), right = X(item.End), cy = objectTimeline.Y + 27;
            bool selected = IsObjectSelected(item.Id);
            var bounds = new Rect(left - 19, cy - 19, right - left + 38, 38);
            c.Fill(bounds, selected ? 0x40545Du : 0x303744u, 19);
            c.Stroke(bounds, selected ? Foreground : item.Color, selected ? 2 : 1, 19);
            c.Circle(left, cy, 19, item.Color, false);
            if (right > left + 1) c.Circle(right, cy, 19, item.Color, false);
            if (item.Spans > 1 && item.End > item.Start)
            {
                double spanDuration = (item.End - item.Start) / item.Spans;
                int first = (int)Math.Clamp(Math.Ceiling((start - 20 / objectTimelineScale - item.Start) / spanDuration), 1, item.Spans);
                int last = (int)Math.Clamp(Math.Floor((end + 20 / objectTimelineScale - item.Start) / spanDuration), 0, item.Spans - 1);
                for (int span = first; span <= last; span++)
                {
                    float repeatX = X(item.Start + span * spanDuration);
                    c.Circle(repeatX, cy, 19, Foreground, false);
                    if (skin is null || !skin.DrawReverseArrow(c, repeatX, cy, 38))
                    {
                        c.Line(repeatX - 8, cy, repeatX + 4, cy, Foreground, 5);
                        c.Line(repeatX + 1, cy - 6, repeatX + 8, cy, Foreground, 3);
                        c.Line(repeatX + 8, cy, repeatX + 1, cy + 6, Foreground, 3);
                    }
                }
            }
            string label = number.ToString();
            c.Text(label, left - c.MeasureText(label, 13) / 2, cy - 8, 13, Foreground, 38);
            timelineObjects.Add((item.Id, item.Start, bounds));
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
        timelineBoxStart = ObjectTimelineStartMs;
        tool = Tool.Select;
        BeginBox(x, y, toggle, false);
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
