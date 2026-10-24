namespace FruitsAtelier.Core;

public sealed class TimingAlignmentPlan
{
    internal Dictionary<double, double> ObjectTimes { get; init; } = [];
    internal Dictionary<int, double> TimingTimes { get; init; } = [];
    public int ObjectCount { get; internal init; }
    public int TimingPointCount => TimingTimes.Count;
    public bool HasChanges => ObjectCount > 0 || TimingPointCount > 0;
}

public static class TimingAlignment
{
    public static TimingAlignmentPlan Analyze(MapDocument document)
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
            if (!point.Uninherited) continue;
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
            if (point.Uninherited || duplicates.Contains((point.TimeMs, false))) continue;
            double corrected = ExportTiming.CorrectHead(alignedTiming, point.TimeMs);
            if (corrected == point.TimeMs || occupiedTiming.Contains((OsuBeatmapWriter.QuantizeTime(corrected), false))) continue;
            timingTimes[i] = corrected;
            occupiedTiming.Add((OsuBeatmapWriter.QuantizeTime(corrected), false));
        }
        var heads = document.Fruits.Select(f => f.TimeMs)
            .Concat(document.ImportedSliders.Select(s => s.TimeMs))
            .Concat(document.Tracks.Where(t => t.Nodes.Count > 0).Select(t => t.Nodes[0].TimeMs))
            .Concat(document.BananaShowers.Select(s => s.TimeMs)).ToArray();
        var occupied = heads.Select(OsuBeatmapWriter.QuantizeTime).ToHashSet();
        var corrections = heads.Select(time => (Original: time, Corrected: ExportTiming.CorrectHead(alignedTiming, time)))
            .Where(p => p.Original != p.Corrected && !occupied.Contains(OsuBeatmapWriter.QuantizeTime(p.Corrected)))
            .GroupBy(p => OsuBeatmapWriter.QuantizeTime(p.Corrected))
            .Where(g => g.Select(p => OsuBeatmapWriter.QuantizeTime(p.Original)).Distinct().Count() == 1)
            .SelectMany(g => g).ToArray();
        return new()
        {
            ObjectCount = corrections.Length,
            ObjectTimes = corrections.GroupBy(p => p.Original).ToDictionary(g => g.Key, g => g.First().Corrected),
            TimingTimes = timingTimes
        };
    }

    public static void Apply(MapDocument document, TimingAlignmentPlan plan)
    {
        TimingEditing.TransformObjects(document, time => (plan.ObjectTimes.GetValueOrDefault(time, time), 1));
        foreach (var (index, time) in plan.TimingTimes) document.TimingPoints[index].TimeMs = time;
        TimingEditing.Validate(document);
    }

    private static double CorrectRed(TimingPoint previous, double time)
    {
        var reference = new MapDocument { TimingOffsetMs = previous.TimeMs, BeatLengthMs = previous.BeatLengthMs };
        return ExportTiming.CorrectHead(new(reference), time);
    }
}
