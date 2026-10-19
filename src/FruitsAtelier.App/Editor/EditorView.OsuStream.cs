using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool osuStreamDialog;
    private void OpenOsuStream()
    {
        if (!CanConvertStream || notesLocked) return;
        streamTargets = ClipboardSelectedParentIds().Where(id => Document.ImportedSliders.Any(s => s.Id == id)
            || Document.Tracks.Any(t => t.Id == id)).ToArray();
        StreamSnapDivisor = divisor; stackMode = false; streamSnapDragging = false;
        StreamSnapBounds = StackPreviewBounds = default;
        osuStreamDialog = StreamDialogVisible = true; streamError = "";
        menu = -1; contextItems.Clear();
    }

    private void ApplyOsuStream()
    {
        EnsureOsuCanvasObjects();
        var timing = SnapTiming();
        var sources = osuCanvasObjects.Where(o => streamTargets.Contains(o.Id)).Select(source =>
        {
            if (Document.ImportedSliders.FirstOrDefault(s => s.Id == source.Id) is not { } slider) return source;
            var drawn = OsuSliderGeometrySnapshot(slider.RandomizationSource ?? slider);
            drawn.TimeMs = slider.TimeMs; drawn.SpanCount = slider.SpanCount;
            drawn.SliderVelocityMultiplier = slider.SliderVelocityMultiplier; drawn.ProcessedSliderVelocityMultiplier = null;
            var path = ImportedSliderConverter.Path(drawn).ToArray();
            var distances = new double[path.Length];
            for (int i = 1; i < path.Length; i++) distances[i] = distances[i - 1]
                + Distance(path[i - 1].X, path[i - 1].GeometryY, path[i].X, path[i].GeometryY);
            return new OsuCanvasObject { Id = source.Id, Start = drawn.TimeMs,
                End = drawn.TimeMs + ImportedSliderConverter.DurationMs(Document, drawn, timing),
                X = drawn.X, Y = drawn.Y, Spans = drawn.SpanCount, Order = source.Order, Path = path, Distances = distances };
        }).ToArray();
        var overrides = Document.HitsoundOverrides.Where(h => streamTargets.Contains(h.SourceId)).ToDictionary(h => (h.SourceId, h.EventIndex));
        var events = conversion!.Objects.Where(o => streamTargets.Contains(o.SourceId) && o.Kind == CatchObjectKind.Fruit)
            .GroupBy(o => o.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
        var selected = new List<Guid>();
        if (EditSliderRepresentation(L.Get("stream.title"), () =>
        {
            foreach (var source in sources)
            {
                string? line = Document.ImportedSliders.FirstOrDefault(s => s.Id == source.Id)?.OriginalLine
                    ?? Document.Tracks.FirstOrDefault(t => t.Id == source.Id)?.OriginalLine;
                var flags = new CurveTrack { OriginalLine = line };
                double step = timing.At(source.Start).BeatLengthMs / StreamSnapDivisor;
                double intervals = (source.End - source.Start) / step;
                if (!double.IsFinite(intervals) || intervals >= SliderFruitStream.MaximumObjects)
                    throw new InvalidOperationException(L.Get("stream.tooMany"));
                for (int i = 0; i <= Math.Floor(intervals + 1e-8); i++)
                {
                    double time = Math.Min(source.End, source.Start + i * step);
                    double spanProgress = source.End > source.Start ? (time - source.Start) / (source.End - source.Start) * source.Spans : 0;
                    int span = Math.Min(source.Spans - 1, (int)spanProgress);
                    double progress = span % 2 == 0 ? spanProgress - span : 1 - (spanProgress - span);
                    var p = OsuPathPosition(source, progress);
                    var fruit = new Fruit { TimeMs = time, X = p.X, Y = p.Y, SourceOrder = source.Order,
                        OriginalLine = SliderFruitStream.FruitLine(flags, i, p.X.ToString(CultureInfo.InvariantCulture), time.ToString(CultureInfo.InvariantCulture)) };
                    Document.Fruits.Add(fruit); selected.Add(fruit.Id);
                    if (events.TryGetValue(source.Id, out var originalEvents))
                    {
                        int low = 0, high = originalEvents.Length;
                        while (low < high) { int middle = low + (high - low) / 2; if (originalEvents[middle].TimeMs < time) low = middle + 1; else high = middle; }
                        int edge = low < originalEvents.Length && Math.Abs(originalEvents[low].TimeMs - time) < .001 ? low : low - 1;
                        if (edge >= 0 && Math.Abs(originalEvents[edge].TimeMs - time) < .001
                            && overrides.TryGetValue((source.Id, originalEvents[edge].EventIndex), out var sample))
                            Document.HitsoundOverrides.Add(sample with { SourceId = fruit.Id, EventIndex = 0, EdgeIndex = 0 });
                    }
                }
            }
            Document.ImportedSliders.RemoveAll(s => streamTargets.Contains(s.Id));
            Document.Tracks.RemoveAll(t => streamTargets.Contains(t.Id));
            Document.HitsoundOverrides.RemoveAll(h => streamTargets.Contains(h.SourceId));
        }))
        { StreamDialogVisible = osuStreamDialog = false; SelectObjects(selected); tool = Tool.Select; }
        else streamError = StatusMessage;
    }

    private void DrawOsuStream(ICanvas c)
    {
        hits.Clear();
        float w = Math.Min(480, width - 32), h = 234, x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Grid, radius: 8);
        c.Text(L.Get("stream.title"), x + 18, y + 18, 17, Foreground, w - 36, true);
        c.Text(L.Get("stream.osuDescription"), x + 18, y + 54, 12, Muted, w - 36);
        Button(c, new(x + 18, y + 96, 46, 32), "‹", () => ChangeOsuStreamSnap(-1));
        c.Text("1/" + StreamSnapDivisor, x + 80, y + 102, 16, Foreground, w - 160);
        Button(c, new(x + w - 64, y + 96, 46, 32), "›", () => ChangeOsuStreamSnap(1));
        if (streamError.Length > 0) c.Text(streamError, x + 18, y + 142, 11, Error, w - 36);
        Button(c, new(x + 18, y + h - 52, w - 138, 34), L.Get("mac.ok"), ApplyOsuStream, true);
        Button(c, new(x + w - 110, y + h - 52, 92, 34), L.Get("mac.cancel"), () => StreamDialogVisible = false);
    }

    private void ChangeOsuStreamSnap(int direction)
    {
        StreamSnapDivisor = SnapDivisors[Math.Clamp(Array.IndexOf(SnapDivisors, StreamSnapDivisor) + direction, 0, SnapDivisors.Length - 1)];
    }
}
