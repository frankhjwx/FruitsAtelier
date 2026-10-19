using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SliderSnapLabels(int StartEvent, int EndEvent, string StartPosition, string EndPosition);
    private readonly Dictionary<Guid, SliderSnapLabels> sliderSnapLabels = [];
    private CatchConversionResult? sliderSnapLabelSource;
    private string sliderSnapLabelLanguage = "";
    private readonly HashSet<Guid> sliderSnapLabelSelection = [];
    private Guid sliderSnapLabelTrack;

    private void RefreshSliderSnapLabels()
    {
        bool sameSelection = sliderSnapLabelTrack == selectedTrack && sliderSnapLabelSelection.Count == objectSelection.Count;
        if (sameSelection)
            foreach (Guid id in objectSelection) if (!sliderSnapLabelSelection.Contains(id)) { sameSelection = false; break; }
        if (ReferenceEquals(sliderSnapLabelSource, conversion) && sliderSnapLabelLanguage == L.Language && sameSelection) return;
        sliderSnapLabelTrack = selectedTrack; sliderSnapLabelSelection.Clear();
        foreach (Guid id in objectSelection) sliderSnapLabelSelection.Add(id);
        sliderSnapLabelSource = conversion; sliderSnapLabelLanguage = L.Language; sliderSnapLabels.Clear();
        if (conversion is null || objectSelection.Count == 0 && selectedTrack == Guid.Empty) return;
        var ids = new HashSet<Guid>();
        foreach (var track in Document.Tracks) if (track.EndpointSnap is not null && IsObjectSelected(track.Id)) ids.Add(track.Id);
        foreach (var slider in Document.ImportedSliders) if (slider.EndpointSnap is not null && IsObjectSelected(slider.Id)) ids.Add(slider.Id);
        if (ids.Count == 0) return;
        var events = new Dictionary<Guid, (int Start, int End)>();
        foreach (var item in conversion.Objects)
        {
            if (item.Kind != CatchObjectKind.Fruit || !ids.Contains(item.SourceId)) continue;
            events[item.SourceId] = events.TryGetValue(item.SourceId, out var first) ? (first.Start, item.EventIndex) : (item.EventIndex, item.EventIndex);
        }
        var timing = SnapTiming();
        foreach (var slider in conversion.Sliders)
        {
            if (!events.TryGetValue(slider.SourceId, out var endpoints)) continue;
            double end = slider.StartTimeMs + slider.DurationMs;
            string startPosition = SnapPosition(slider.StartTimeMs), endPosition = SnapPosition(end);
            sliderSnapLabels[slider.SourceId] = new(endpoints.Start, endpoints.End, startPosition, endPosition);
        }

        string SnapPosition(double time)
        {
            int divisor = 1;
            double nearest = timing.Snap(time, 1), distance = Math.Abs(time - nearest);
            foreach (int candidate in SliderEndpointSnap.Divisors)
            {
                double snapped = timing.Snap(time, candidate), next = Math.Abs(time - snapped);
                if (next < distance - 1e-7) { nearest = snapped; distance = next; divisor = candidate; }
            }
            if (distance > 2) return L.Get("sliderSnap.offGrid");
            var state = timing.At(nearest);
            long step = (long)Math.Round((nearest - state.OffsetMs) / state.BeatLengthMs * divisor);
            int numerator = (int)((step % divisor + divisor) % divisor);
            int a = numerator, b = divisor;
            while (b != 0) (a, b) = (b, a % b);
            return $"{numerator / a}/{divisor / a}";
        }
    }

    private void DrawSliderSnapCaption(ICanvas c, string caption, float x, float y, float radius, Rect bounds, int line = 0)
    {
        float textWidth = c.MeasureText(caption, 11);
        float right = x + radius + 6, left = x - radius - 6 - textWidth;
        float labelX = right + textWidth <= bounds.Right || left < bounds.X ? right : left;
        c.Text(caption, labelX, y - 7 + line * 18, 11, Accent, Math.Max(0, bounds.Right - labelX));
    }
}
