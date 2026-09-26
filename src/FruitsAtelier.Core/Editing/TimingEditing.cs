using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record TimingApplyOptions(bool Scale = false, bool Snap = false, bool SliderLengths = false, bool Bookmarks = false, int Divisor = 4);

public static class TimingEditing
{
    public static TimingPoint Copy(TimingPoint point) => point.DeepClone();
    public static TimingPoint? Current(MapDocument map, double time, bool redOnly = false)
    {
        IEnumerable<TimingPoint> ordered = map.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder);
        if (redOnly) ordered = ordered.Where(p => p.Uninherited).GroupBy(p => p.TimeMs).Select(g => g.First());
        return ordered.LastOrDefault(p => p.TimeMs <= time) ?? ordered.FirstOrDefault(p => p.Uninherited);
    }

    public static TimingPoint Create(MapDocument map, double time, bool inherited)
    {
        var point = Current(map, time)?.DeepClone() ?? new TimingPoint();
        var state = TimingMap.At(map, time);
        point.TimeMs = time; point.Uninherited = !inherited;
        point.BeatLengthMs = inherited ? -100 / state.SliderVelocityMultiplier : state.BeatLengthMs;
        point.Meter = state.Meter; point.OriginalLine = null;
        point.SourceOrder = map.TimingPoints.Count;
        return point;
    }

    public static void Apply(MapDocument map, IReadOnlyList<TimingPoint> points,
        IReadOnlyList<(TimingPoint Before, TimingPoint After)> changes, TimingApplyOptions options)
    {
        var before = map.DeepClone();
        map.TimingPoints.Clear(); map.TimingPoints.AddRange(points.Select(Copy));
        var first = map.TimingPoints.Where(p => p.Uninherited).OrderBy(p => p.TimeMs).FirstOrDefault();
        if (first is not null) { map.BeatLengthMs = first.BeatLengthMs; map.TimingOffsetMs = first.TimeMs; }
        var oldReds = before.TimingPoints.Where(p => p.Uninherited).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).GroupBy(p => p.TimeMs).Select(g => g.First()).ToArray();
        (double Time, double Scale) Transform(double time)
        {
            var red = oldReds.LastOrDefault(p => p.TimeMs <= time) ?? oldReds.FirstOrDefault();
            if (red is null) return (time, 1);
            var change = changes.LastOrDefault(c => c.Before.Uninherited && c.After.Uninherited
                && c.Before.TimeMs == red.TimeMs && c.Before.SourceOrder == red.SourceOrder);
            if (change.Before is null) return (time, 1);
            double scale = change.After.BeatLengthMs / change.Before.BeatLengthMs;
            return (change.After.TimeMs + (time - change.Before.TimeMs) * scale, scale);
        }
        if (options.Scale) TransformObjects(map, Transform);
        if (options.Bookmarks)
        {
            SongSetup.Set(map, "Editor", "Bookmarks", string.Join(',', OsuTimeline.Bookmarks(before).Select(t =>
                Math.Clamp(Math.Round(Transform(t).Time), 0, int.MaxValue).ToString(CultureInfo.InvariantCulture))));
            if (double.TryParse(SongSetup.Get(before, "General", "PreviewTime"), CultureInfo.InvariantCulture, out double preview) && preview >= 0)
                SongSetup.Set(map, "General", "PreviewTime", Math.Clamp(Math.Round(Transform(preview).Time), 0, int.MaxValue).ToString(CultureInfo.InvariantCulture));
        }
        if (options.Snap) Resnap(map, options.Divisor);
        if (options.SliderLengths) ResnapLengths(map, options.Divisor);
        Validate(map);
    }

    public static void TransformObjects(MapDocument map, Func<double, (double Time, double Scale)> transform,
        double start = double.NegativeInfinity, double end = double.PositiveInfinity)
    {
        bool Contains(double time) => time >= start && time < end;
        foreach (var fruit in map.Fruits.Where(f => Contains(f.TimeMs))) fruit.TimeMs = transform(fruit.TimeMs).Time;
        foreach (var slider in map.ImportedSliders.Where(s => Contains(s.TimeMs)))
        {
            double next = transform(slider.TimeMs).Time;
            if (next == slider.TimeMs) continue;
            slider.TimeMs = next;
            UpdateSliderSource(slider, 2, next);
        }
        foreach (var track in map.Tracks.Where(t => t.Nodes.Count > 0 && Contains(t.Nodes[0].TimeMs)))
        {
            double old = track.Nodes[0].TimeMs;
            var next = transform(old);
            TransformTrack(track, old, next.Time, next.Scale);
        }
        foreach (var shower in map.BananaShowers.Where(s => Contains(s.TimeMs)))
        {
            var next = transform(shower.TimeMs);
            shower.EndTimeMs = next.Time + (shower.EndTimeMs - shower.TimeMs) * next.Scale;
            shower.TimeMs = next.Time;
        }
        map.DurationMs = Math.Max(map.DurationMs, map.Fruits.Select(f => f.TimeMs)
            .Concat(map.Tracks.Where(t => t.Nodes.Count > 0).Select(CurveMath.EndTimeMs))
            .Concat(map.BananaShowers.Select(s => s.EndTimeMs)).DefaultIfEmpty(0).Max());
    }

    private static void TransformTrack(CurveTrack track, double origin, double target, double scale)
    {
        foreach (var node in track.Nodes)
        {
            node.TimeMs = target + (node.TimeMs - origin) * scale;
            node.HandleIn = node.HandleIn with { TimeMs = node.HandleIn.TimeMs * scale };
            node.HandleOut = node.HandleOut with { TimeMs = node.HandleOut.TimeMs * scale };
            if (node.OutgoingCurve is { } curve)
            {
                curve.ReferenceScale /= scale;
                foreach (var control in curve.Controls) control.Offset = control.Offset with { TimeMs = control.Offset.TimeMs * scale };
            }
        }
    }

    public static void Resnap(MapDocument map, int divisor, double start = double.NegativeInfinity, double end = double.PositiveInfinity)
    {
        TransformObjects(map, time => (TimingMap.Snap(map, time, divisor), 1), start, end);
        Validate(map);
    }

    public static void ResnapLengths(MapDocument map, int divisor)
    {
        foreach (var slider in map.ImportedSliders)
        {
            var state = TimingMap.At(map, slider.TimeMs);
            double oldEnd = ImportedSliderConverter.EndTimeMs(map, slider);
            double duration = Math.Max(state.BeatLengthMs / divisor, TimingMap.Snap(map, oldEnd, divisor) - slider.TimeMs);
            slider.PixelLength = duration / slider.SpanCount * 100 * map.SliderMultiplier * state.SliderVelocityMultiplier / state.BeatLengthMs;
            UpdateSliderSource(slider, 7, slider.PixelLength);
        }
        foreach (var track in map.Tracks.Where(t => t.Nodes.Count >= 2))
        {
            double start = track.Nodes[0].TimeMs, end = CurveMath.EndTimeMs(track);
            double duration = Math.Max(TimingMap.At(map, start).BeatLengthMs / divisor, TimingMap.Snap(map, end, divisor) - start);
            TransformTrack(track, start, start, duration / (end - start));
        }
        Validate(map);
    }

    public static void Validate(MapDocument map)
    {
        OsuBeatmapReader.Validate(map);
        var errors = CurveMath.Validate(map);
        if (errors.Count > 0) throw new ArgumentException(errors[0]);
    }

    private static void UpdateSliderSource(ImportedSlider slider, int column, double value)
    {
        if (slider.OriginalLine is null) return;
        var fields = slider.OriginalLine.Split(',');
        if (double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double old) && old == value) return;
        fields[column] = value.ToString("R", CultureInfo.InvariantCulture);
        slider.OriginalLine = string.Join(',', fields);
    }

    public static string Serialize(IEnumerable<TimingPoint> points) => string.Join('\n', points.Select(p =>
        string.Create(CultureInfo.InvariantCulture, $"{p.TimeMs:R},{p.BeatLengthMs:R},{p.Meter},{p.SampleSet},{p.SampleIndex},{p.Volume},{(p.Uninherited ? 1 : 0)},{p.Effects}")));

    public static TimingPoint[] Parse(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length is 0 or > 10000) throw new ArgumentException(L.Get("timing.invalidClipboard"));
        try
        {
            var points = lines.Select((line, index) => OsuBeatmapReader.ParseTiming(line, index)).ToArray();
            var map = new MapDocument(); map.TimingPoints.AddRange(points); OsuBeatmapReader.Validate(map);
            return points;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or OverflowException or IndexOutOfRangeException)
        { throw new ArgumentException(L.Get("timing.invalidClipboard")); }
    }
}
