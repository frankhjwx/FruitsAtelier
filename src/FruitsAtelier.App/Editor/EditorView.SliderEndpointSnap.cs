using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    internal Rect SliderEndpointToggleBounds { get; private set; }
    internal Rect SliderEndpointStartBounds { get; private set; }
    internal Rect SliderEndpointEndBounds { get; private set; }
    private SliderEndpointSnap? EndpointSnap(Guid id) => Document.Tracks.FirstOrDefault(t => t.Id == id)?.EndpointSnap
        ?? Document.ImportedSliders.FirstOrDefault(s => s.Id == id)?.EndpointSnap;

    private double SnapSliderEndpoint(Guid id, double time, bool end)
        => EndpointSnap(id) is { } own ? own.Snap(SnapTiming(), time, end)
            : snap ? SnapTiming().Snap(time, divisor) : time;

    private double SnapTrackEndpoint(CurveTrack track, double time, bool end)
    {
        if (track.EndpointSnap is null) return snap ? SnapTiming().Snap(time, divisor) : time;
        if (!end) return SnapSliderEndpoint(track.Id, time, false);
        // Nodes describe the first span; the end grid belongs to the final traversal.
        double start = track.Nodes[0].TimeMs;
        return start + (SnapSliderEndpoint(track.Id, start + (time - start) * track.SpanCount, true) - start) / track.SpanCount;
    }

    private void SetEndpointSnap(Guid id, SliderEndpointSnap? value)
    {
        if (Document.Tracks.FirstOrDefault(t => t.Id == id) is { } track) track.EndpointSnap = value;
        else if (Document.ImportedSliders.FirstOrDefault(s => s.Id == id) is { } slider) slider.EndpointSnap = value;
    }

    private void AddEndpointSnapActions(List<(string Label, System.Action Action, bool Enabled, int Kind)> actions, bool enabled)
    {
        if (inspectorTargets.Length != 1) return;
        Guid id = inspectorTargets[0];
        if (!Document.Tracks.Any(t => t.Id == id && t.StreamSnapDivisor is null) && !Document.ImportedSliders.Any(s => s.Id == id)) return;
        enabled &= !IsReadOnlySlider(id);
        var own = EndpointSnap(id);
        actions.Add((L.Get("sliderSnap.toggle", L.Get(own is null ? "settings.romanisedOff" : "settings.romanisedOn")),
            () => Edit(L.Get("sliderSnap.command"), () => SetEndpointSnap(id, own is null ? new() : null)), enabled, 4));
        if (own is null) return;
        foreach (bool end in new[] { false, true })
        {
            int? value = end ? own.EndDivisor : own.StartDivisor;
            actions.Add((L.Get(end ? "sliderSnap.end" : "sliderSnap.start", value is { } d ? "1/" + d : L.Get("sliderSnap.auto")),
                () => OpenEndpointSnapMenu(id, end), enabled, end ? 6 : 5));
        }
    }

    private void OpenEndpointSnapMenu(Guid id, bool end)
    {
        menu = -1; languageMenuOpen = false; contextItems.Clear();
        var own = EndpointSnap(id);
        if (own is null) return;
        Add(null);
        foreach (int value in SliderEndpointSnap.Divisors) Add(value);
        float w = Math.Min(250, width);
        contextBounds = new(Math.Clamp(mouseX, 0, width - w), Math.Clamp(mouseY + 4, 0, Math.Max(0, height - ContextMenuHeight)), w, ContextMenuHeight);
        void Add(int? value) => contextItems.Add(new(value is { } d ? "1/" + d : L.Get("sliderSnap.auto"), () =>
        {
            if (!ClipboardInteractionReady || notesLocked) return;
            Edit(L.Get("sliderSnap.command"), () =>
            {
                var next = end ? own with { EndDivisor = value } : own with { StartDivisor = value };
                SetEndpointSnap(id, next);
                if (value is null) return;
                EnsureConversion();
                var generated = conversion!.Sliders.First(s => s.SourceId == id);
                double time = end ? generated.StartTimeMs + generated.DurationMs : generated.StartTimeMs;
                SetSliderEndpointTime(id, end, next.Snap(SnapTiming(), time, end));
            });
        }, Active: (end ? own.EndDivisor : own.StartDivisor) == value));
    }

    private void SetSliderEndpointTime(Guid id, bool end, double time)
    {
        if (!double.IsFinite(time) || time < 0 || time > EditableDurationMs) throw new ArgumentException(L.Get("ui.timeRangeError"));
        if (Document.Tracks.FirstOrDefault(t => t.Id == id) is { } track)
        {
            double start = track.Nodes[0].TimeMs, finish = CurveMath.EndTimeMs(track);
            double nextStart = end ? start : time, nextEnd = end ? time : finish;
            double scale = (nextEnd - nextStart) / (finish - start);
            if (scale <= 0 || !double.IsFinite(scale)) throw new ArgumentException(L.Get("sliderSnap.range"));
            for (int i = 1; i < track.Nodes.Count; i++)
                if ((track.Nodes[i].TimeMs - track.Nodes[i - 1].TimeMs) * scale < CurveMath.MinimumAnchorSpacingMs)
                    throw new ArgumentException(L.Get("sliderSnap.range"));
            TimingEditing.TransformTrack(track, start, nextStart, scale);
        }
        else if (Document.ImportedSliders.FirstOrDefault(s => s.Id == id) is { } slider)
        {
            double finish = time;
            if (!end)
            {
                EnsureConversion();
                var generated = conversion!.Sliders.First(s => s.SourceId == id);
                finish = generated.StartTimeMs + generated.DurationMs;
            }
            double nextStart = end ? slider.TimeMs : time, nextEnd = end ? time : finish;
            if (nextEnd <= nextStart) throw new ArgumentException(L.Get("sliderSnap.range"));
            slider.TimeMs = nextStart;
            slider.ProcessedSliderVelocityMultiplier = null;
            slider.PixelLength = (nextEnd - nextStart) / slider.SpanCount * ImportedSliderConverter.VelocityAt(Document, slider, SnapTiming());
            OsuSyncSliderLine(slider);
        }
        Document.DurationMs = Math.Max(Document.DurationMs, time);
    }
}
