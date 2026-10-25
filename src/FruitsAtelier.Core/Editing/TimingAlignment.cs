namespace FruitsAtelier.Core;

public sealed class TimingAlignmentPlan
{
    internal Dictionary<double, double> ObjectTimes { get; init; } = [];
    internal Dictionary<int, double> TimingTimes { get; init; } = [];
    internal Dictionary<Guid, (double Start, double End)> SpinnerTimes { get; init; } = [];
    public int ObjectCount { get; internal init; }
    public int TimingPointCount => TimingTimes.Count;
    public bool HasChanges => ObjectCount > 0 || TimingPointCount > 0;
}

public static class TimingAlignment
{
    public static TimingAlignmentPlan Analyze(MapDocument document, bool alignTimingPoints = true, bool alignObjects = true)
    {
        var timingTimes = new Dictionary<int, double>();
        var occupiedTiming = document.TimingPoints.Select(p => (Time: OsuBeatmapWriter.QuantizeTime(p.TimeMs), p.Uninherited)).ToHashSet();
        var duplicates = document.TimingPoints.GroupBy(p => (p.TimeMs, p.Uninherited))
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        TimingPoint? previousRed = null;
        double previousRedTime = double.NaN;
        foreach (var item in document.TimingPoints.Select((point, index) => (point, index)).OrderBy(p => p.point.TimeMs).ThenBy(p => p.point.SourceOrder))
        {
            var point = item.point;
            if (!alignTimingPoints || !point.Uninherited) continue;
            if (duplicates.Contains((point.TimeMs, point.Uninherited)))
            {
                if (point.Uninherited && point.TimeMs != previousRedTime)
                { previousRed = point; previousRedTime = point.TimeMs; }
                continue;
            }
            double corrected = previousRed is null ? point.TimeMs : CorrectRed(previousRed, point.TimeMs);
            if (corrected != point.TimeMs && !occupiedTiming.Contains((OsuBeatmapWriter.QuantizeTime(corrected), point.Uninherited)))
            {
                timingTimes[item.index] = corrected;
                occupiedTiming.Add((OsuBeatmapWriter.QuantizeTime(corrected), point.Uninherited));
            }
            if (point.Uninherited)
            {
                previousRed = point.DeepClone();
                previousRed.TimeMs = timingTimes.GetValueOrDefault(item.index, point.TimeMs);
                previousRedTime = point.TimeMs;
            }
        }
        var timingDocument = new MapDocument { BeatLengthMs = document.BeatLengthMs, TimingOffsetMs = document.TimingOffsetMs };
        for (int i = 0; i < document.TimingPoints.Count; i++)
        {
            var point = document.TimingPoints[i].DeepClone();
            point.TimeMs = timingTimes.GetValueOrDefault(i, point.TimeMs);
            timingDocument.TimingPoints.Add(point);
        }
        var alignedTiming = new TimingMap.Lookup(timingDocument);
        for (int i = 0; i < document.TimingPoints.Count; i++)
        {
            var point = document.TimingPoints[i];
            if (!alignTimingPoints || point.Uninherited || duplicates.Contains((point.TimeMs, false))) continue;
            double corrected = ExportTiming.CorrectHead(alignedTiming, point.TimeMs);
            if (corrected == point.TimeMs || occupiedTiming.Contains((OsuBeatmapWriter.QuantizeTime(corrected), false))) continue;
            timingTimes[i] = corrected;
            occupiedTiming.Add((OsuBeatmapWriter.QuantizeTime(corrected), false));
        }
        if (!alignObjects) return new() { TimingTimes = timingTimes };
        var heads = document.Fruits.Select(f => (f.Id, Time: f.TimeMs, Spinner: false))
            .Concat(document.ImportedSliders.Select(s => (s.Id, Time: s.TimeMs, Spinner: false)))
            .Concat(document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => (t.Id, Time: t.Nodes[0].TimeMs, Spinner: false)))
            .Concat(document.BananaShowers.Select(s => (s.Id, Time: s.TimeMs, Spinner: true))).ToArray();
        var occupied = heads.Select(h => OsuBeatmapWriter.QuantizeTime(h.Time)).ToHashSet();
        var corrections = heads.Select(h => (h.Id, h.Spinner, Original: h.Time,
                Corrected: h.Spinner ? ExportTiming.CorrectSpinnerHead(alignedTiming, h.Time) : ExportTiming.CorrectHead(alignedTiming, h.Time)))
            .Where(p => p.Original != p.Corrected && !occupied.Contains(OsuBeatmapWriter.QuantizeTime(p.Corrected)))
            .GroupBy(p => OsuBeatmapWriter.QuantizeTime(p.Corrected))
            .Where(g => g.Select(p => OsuBeatmapWriter.QuantizeTime(p.Original)).Distinct().Count() == 1)
            .SelectMany(g => g).ToArray();
        var spinnerHeads = corrections.Where(p => p.Spinner).ToDictionary(p => p.Id, p => p.Corrected);
        var spinnerTimes = new Dictionary<Guid, (double Start, double End)>();
        foreach (var spinner in document.BananaShowers)
        {
            double start = spinnerHeads.GetValueOrDefault(spinner.Id, spinner.TimeMs);
            double end = ExportTiming.CorrectHead(alignedTiming, spinner.EndTimeMs);
            double duration = OsuBeatmapWriter.QuantizeTime(end) - OsuBeatmapWriter.QuantizeTime(start);
            if (duration < 0 || duration == 0 && spinner.EndTimeMs > spinner.TimeMs)
            { start = spinner.TimeMs; end = spinner.EndTimeMs; }
            if (start != spinner.TimeMs || end != spinner.EndTimeMs) spinnerTimes[spinner.Id] = (start, end);
        }
        return new()
        {
            ObjectCount = corrections.Count(p => !p.Spinner) + spinnerTimes.Count,
            ObjectTimes = corrections.Where(p => !p.Spinner).GroupBy(p => p.Original).ToDictionary(g => g.Key, g => g.First().Corrected),
            TimingTimes = timingTimes,
            SpinnerTimes = spinnerTimes
        };
    }

    public static void Apply(MapDocument document, TimingAlignmentPlan plan)
    {
        TimingEditing.TransformObjects(document, time => (plan.ObjectTimes.GetValueOrDefault(time, time), 1),
            double.NegativeInfinity, double.PositiveInfinity, transformSpinners: false);
        foreach (var spinner in document.BananaShowers)
            if (plan.SpinnerTimes.TryGetValue(spinner.Id, out var times))
            { spinner.TimeMs = times.Start; spinner.EndTimeMs = times.End; }
        document.DurationMs = Math.Max(document.DurationMs, document.BananaShowers.Select(s => s.EndTimeMs).DefaultIfEmpty(0).Max());
        foreach (var (index, time) in plan.TimingTimes) document.TimingPoints[index].TimeMs = time;
        TimingEditing.Validate(document);
    }

    private static double CorrectRed(TimingPoint previous, double time)
    {
        var reference = new MapDocument { TimingOffsetMs = previous.TimeMs, BeatLengthMs = previous.BeatLengthMs };
        return ExportTiming.CorrectHead(new(reference), time);
    }
}
