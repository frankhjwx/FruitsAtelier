using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

internal static class SliderBpmTiming
{
    internal static List<TimingPoint> AlignPairs(List<TimingPoint> timing)
    {
        var pairs = timing.GroupBy(p => p.TimeMs).Select(g => (
            Red: g.FirstOrDefault(p => p.Uninherited && p.SourceOrder == int.MaxValue - 1),
            Green: g.LastOrDefault(p => !p.Uninherited))).Where(p => p.Red is not null && p.Green is not null).ToArray();
        if (pairs.Length == 0) return timing;
        var result = timing.Select(p => p.DeepClone()).ToList();
        var byTime = pairs.ToDictionary(p => p.Red!.TimeMs, p => p.Green!);
        foreach (var red in result.Where(p => p.Uninherited && p.SourceOrder == int.MaxValue - 1))
        {
            if (!byTime.TryGetValue(red.TimeMs, out var green)) continue;
            red.Meter = green.Meter; red.SampleSet = green.SampleSet; red.SampleIndex = green.SampleIndex;
            red.Volume = green.Volume; red.Effects = green.Effects; red.OriginalLine = null;
        }
        return result;
    }

    internal static MapDocument Prepare(MapDocument document, IReadOnlyList<GeneratedSlider> sliders)
    {
        if (!document.EnableSliderBpmMultiplier || sliders.Count == 0) return document;
        var source = new TimingMap.Lookup(document);
        var result = document.DeepClone();
        if (result.TimingPoints.Count == 0)
            result.TimingPoints.Add(new() { TimeMs = document.TimingOffsetMs, BeatLengthMs = document.BeatLengthMs });
        var original = result.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.Uninherited ? 0 : 1).ThenBy(p => p.SourceOrder).ToArray();
        var heads = sliders.GroupBy(s => OsuBeatmapWriter.QuantizeTime(s.StartTimeMs)).OrderBy(g => g.Key).ToArray();
        var changes = new SortedDictionary<double, double>();
        for (int i = 0; i < heads.Length; i++)
        {
            var group = heads[i]; var first = group.First();
            if (group.Any(s => Math.Abs(s.BeatLengthMs - first.BeatLengthMs) > 1e-9))
                throw new InvalidDataException(L.Get("aspire.bpmCollision"));
            if (group.Any(s => source.At(s.StartTimeMs).BeatLengthMs != source.At(group.Key).BeatLengthMs))
                throw new InvalidDataException(L.Get("core.writer.bpmBoundary"));
            changes[group.Key] = first.BeatLengthMs;
            double end = Math.Ceiling(group.Max(s => s.StartTimeMs + s.DurationMs));
            if (i + 1 == heads.Length)
                changes[end] = source.At(end).BeatLengthMs;
        }
        var replacements = new Dictionary<double, (TimingPoint Red, TimingPoint Green)>();
        int index = 0;
        double current = source.At(double.MinValue).BeatLengthMs;
        TimingPoint template = original[0];
        foreach (var (time, beatLength) in changes)
        {
            while (index < original.Length && original[index].TimeMs <= time)
            {
                var point = original[index++];
                if (point.Uninherited) current = source.At(point.TimeMs).BeatLengthMs;
                template = point;
            }
            if (Math.Abs(current - beatLength) <= 1e-9) continue;
            var state = source.At(time);
            var red = template.DeepClone();
            red.TimeMs = time; red.Uninherited = true; red.BeatLengthMs = beatLength;
            red.Meter = state.Meter; red.OriginalLine = null; red.SourceOrder = int.MaxValue - 1;
            var green = red.DeepClone();
            green.Uninherited = false; green.BeatLengthMs = state.GenerateTicks ? -100 / state.SliderVelocityMultiplier : double.NaN;
            green.SourceOrder = int.MaxValue;
            replacements[time] = (red, green);
            current = beatLength;
        }
        result.TimingPoints.RemoveAll(p => replacements.ContainsKey(p.TimeMs));
        foreach (var pair in replacements.Values) { result.TimingPoints.Add(pair.Red); result.TimingPoints.Add(pair.Green); }
        return result;
    }
}
