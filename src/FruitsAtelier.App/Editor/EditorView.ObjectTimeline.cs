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
    private (Guid Id, double Start, double End, int SourceOrder, uint Color)[] timelineSources = [];
    private bool boxTimeline;
    private double timelineBoxStart;
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
        foreach (var tick in TimingMap.Grid(Document, Math.Max(0, start), Math.Max(0, end), divisor))
        {
            if (!tick.IsBeat && !tick.IsTimingBoundary && TimingMap.At(Document, tick.TimeMs).BeatLengthMs / divisor * objectTimelineScale < 5) continue;
            c.Line(X(tick.TimeMs), objectTimeline.Bottom - (tick.IsBeat ? 12 : 5), X(tick.TimeMs), objectTimeline.Bottom,
                tick.IsTimingBoundary ? Error : tick.IsBeat ? Muted : Grid);
        }
        if (!ReferenceEquals(timelineConversion, conversion))
        {
            // Slider duration resolves timing and geometry; recompute only when content changes, not on every frame.
            var sliderEnds = conversion!.Sliders.ToDictionary(s => s.SourceId, s => s.StartTimeMs + s.DurationMs);
            timelineSources = Document.Fruits.Select(f => (f.Id, Start: f.TimeMs, End: f.TimeMs, f.SourceOrder, Color: Accent))
            .Concat(Document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Start: t.Nodes[0].TimeMs, End: CurveMath.EndTimeMs(t), t.SourceOrder, Color: Purple)))
            .Concat(Document.ImportedSliders.Select(s => (s.Id, Start: s.TimeMs, End: sliderEnds.TryGetValue(s.Id, out double endTime) ? endTime : ImportedSliderConverter.EndTimeMs(Document, s), s.SourceOrder, Color: Purple)))
            .Concat(Document.BananaShowers.Select(s => (s.Id, Start: s.TimeMs, End: s.EndTimeMs, s.SourceOrder, Color: Gold)))
            .OrderBy(o => o.Start).ThenBy(o => o.SourceOrder).ToArray();
            timelineConversion = conversion;
        }
        int number = 0;
        foreach (var item in timelineSources)
        {
            number++;
            if (item.End < start - 20 / objectTimelineScale || item.Start > end + 20 / objectTimelineScale) continue;
            float left = X(item.Start), right = X(item.End), cy = objectTimeline.Y + 27;
            bool selected = IsObjectSelected(item.Id);
            var bounds = new Rect(left - 19, cy - 19, right - left + 38, 38);
            c.Fill(bounds, selected ? 0x40545Du : 0x303744u, 19);
            c.Stroke(bounds, selected ? Foreground : item.Color, selected ? 2 : 1, 19);
            c.Circle(left, cy, 19, item.Color, false);
            if (right > left + 1) c.Circle(right, cy, 19, item.Color, false);
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
            SeekTo(item.Time);
            return;
        }
        if (y < objectTimeline.Bottom - 14)
        {
            timelineBoxStart = ObjectTimelineStartMs;
            tool = Tool.Select;
            BeginBox(x, y, toggle, false);
            boxTimeline = true;
            return;
        }
        double time = ObjectTimelineStartMs + (x - objectTimeline.X) / objectTimelineScale;
        drag = DragKind.Timeline;
        dragStartX = x;
        timelineMsPerDip = 1 / objectTimelineScale;
        SeekTo(time);
        dragStartTime = playhead;
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
