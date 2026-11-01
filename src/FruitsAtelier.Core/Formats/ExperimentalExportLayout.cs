using System.Globalization;
using System.Runtime.CompilerServices;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

/// <summary>Projects local time-space groups without changing their Catch movement.</summary>
public static class ExperimentalExportLayout
{
    private static readonly ConditionalWeakTable<OsuWriteResult, OsuWriteResult> results = new();

    public static OsuWriteResult Apply(OsuWriteResult source)
        => results.GetValue(source, Build);

    private sealed record Head(int Order, double Time, double X, double Y, int Flags, ImportedSlider? Slider);

    private static OsuWriteResult Build(OsuWriteResult source)
    {
        var map = source.ReadBack;
        var heads = map.Fruits.Select(f => new Head(f.SourceOrder, f.TimeMs, f.X, f.Y, Flags(f.OriginalLine), null))
            .Concat(map.ImportedSliders.Select(s => new Head(s.SourceOrder, s.TimeMs, s.X, s.Y, Flags(s.OriginalLine), s)))
            .OrderBy(h => h.Order).ToArray();
        if (heads.Length == 0) return source;
        var baseline = CatchStreamConverter.Convert(map);
        var hyper = HyperDashCalculator.Calculate(baseline.Objects, map.CircleSize);
        var orders = map.Fruits.Select(f => (f.Id, f.SourceOrder))
            .Concat(map.ImportedSliders.Select(s => (s.Id, s.SourceOrder)))
            .ToDictionary(p => p.Id, p => p.SourceOrder);
        var hyperArrivals = new HashSet<int>();
        for (int i = 0; i < hyper.Length; i++)
            if (hyper[i].TargetIndex is { } target && baseline.Objects[i].SourceId != baseline.Objects[target].SourceId
                && orders.TryGetValue(baseline.Objects[target].SourceId, out int order)) hyperArrivals.Add(order);

        string difficulty = OsuBeatmapReader.Setting(map, "Metadata", "Version") ?? "";
        bool ncFirst = new[] { "cup", "salad", "platter", "easy", "normal" }
            .Any(name => difficulty.Contains(name, StringComparison.OrdinalIgnoreCase));
        double scale = CatchScrollTiming.PixelsPerMs(map.ApproachRate, 512) * .6;
        var timing = new TimingMap.Lookup(map);
        var offsets = new Dictionary<int, int>();
        int start = 0, group = 0;
        for (int i = 1; i <= heads.Length; i++)
        {
            bool split = i == heads.Length;
            if (!split)
            {
                var current = heads[i]; var previous = heads[i - 1];
                double gap = current.Time - previous.Time;
                double beat = timing.At(current.Time).BeatLengthMs;
                double speed = Speed(previous, current);
                bool separatesCompactRun = i - start >= 2 && Speed(heads[i - 2], previous) < speed * .6
                    || i + 1 < heads.Length && Speed(current, heads[i + 1]) < speed * .6;
                split = current.Order != previous.Order + 1
                    || gap > beat * 1.5 || (current.Time - heads[start].Time) * scale > 288
                    || (current.Flags & 4) != 0 && (ncFirst || gap >= beat * .5)
                    || !ncFirst && hyperArrivals.Contains(current.Order) && separatesCompactRun;
            }
            if (!split) continue;
            // A single translation preserves the group's time-space proportions and upward time direction.
            double height = (heads[i - 1].Time - heads[start].Time) * scale;
            double centre = 192 + (group % 3 - 1) * Math.Min(56, (336 - height) / 2);
            for (int j = start; j < i; j++)
            {
                var head = heads[j];
                double desired = centre + height / 2 - (head.Time - heads[start].Time) * scale;
                int delta = (int)Math.Round(desired - head.Y, MidpointRounding.AwayFromZero);
                if (head.Slider is { } slider)
                {
                    var path = ImportedSliderConverter.Path(slider);
                    double min = Math.Min(head.Y, path.Min(p => p.GeometryY));
                    double max = Math.Max(head.Y, path.Max(p => p.GeometryY));
                    int low = (int)Math.Ceiling(1 - min), high = (int)Math.Floor(383 - max);
                    // Oversized native paths cannot fit by translation; retain their original placement.
                    delta = low <= high ? Math.Clamp(delta, low, high) : 0;
                }
                offsets[head.Order] = delta;
            }
            start = i; group++;
        }

        var lines = source.Text.Split('\n');
        bool inObjects = false; int objectOrder = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.StartsWith('[')) { inObjects = line == "[HitObjects]"; continue; }
            if (!inObjects || !OsuBeatmapReader.IsDataLine(line)) continue;
            int delta = offsets.GetValueOrDefault(objectOrder++);
            if (delta == 0) continue;
            string[] fields = line.Split(',');
            fields[1] = Shift(fields[1], delta);
            if ((int.Parse(fields[3], CultureInfo.InvariantCulture) & 2) != 0)
            {
                var points = fields[5].Split('|');
                for (int p = 1; p < points.Length; p++)
                {
                    var xy = points[p].Split(':');
                    xy[1] = Shift(xy[1], delta); points[p] = string.Join(':', xy);
                }
                fields[5] = string.Join('|', points);
            }
            lines[i] = string.Join(',', fields) + (lines[i].EndsWith('\r') ? "\r" : "");
        }
        string text = string.Join('\n', lines);
        var readBack = OsuBeatmapReader.Read(text, map.SourcePath, inferDuration: false);
        var converted = CatchStreamConverter.Convert(readBack);
        if (!converted.Success || !SameEvents(baseline.Objects, converted.Objects)
            || !SameEvents(CatchPreviewMods.HardRock(map, baseline), CatchPreviewMods.HardRock(readBack, converted)))
            throw new InvalidDataException(L.Get("core.exportStyle.changedPlayback"));
        OsuBeatmapReader.SetDuration(readBack, converted.Sliders.Select(s => s.StartTimeMs + s.DurationMs));
        // The ordinary export owns source identities and diagnostics; this pass changes geometric Y only.
        foreach (var slider in readBack.ImportedSliders) slider.Id = source.ObjectSources[slider.SourceOrder];
        foreach (var shower in readBack.BananaShowers) shower.Id = source.ObjectSources[shower.SourceOrder];
        return new()
        {
            Text = text, ReadBack = readBack, ObjectSources = source.ObjectSources, Diagnostics = source.Diagnostics,
            MaxTimeQuantizationMs = source.MaxTimeQuantizationMs, MaxCoordinateQuantization = source.MaxCoordinateQuantization,
            MaxConvertedTimeErrorMs = source.MaxConvertedTimeErrorMs, MaxConvertedXError = source.MaxConvertedXError,
            ObjectSequenceMatches = source.ObjectSequenceMatches, PlayableObjects = source.PlayableObjects,
            PlayableHardRockObjects = source.PlayableHardRockObjects, PlayableEndTimes = source.PlayableEndTimes
        };
    }

    private static bool SameEvents(IReadOnlyList<ConvertedCatchObject> before, IReadOnlyList<ConvertedCatchObject> after)
        => before.Count == after.Count && before.Zip(after).All(p => p.First.Kind == p.Second.Kind
            && p.First.TimeMs == p.Second.TimeMs && p.First.X == p.Second.X);
    private static double Speed(Head from, Head to) => Math.Abs(to.X - from.X) / Math.Max(1, to.Time - from.Time);
    private static int Flags(string? line) => line is null ? 0 : int.Parse(line.Split(',')[3], CultureInfo.InvariantCulture);
    private static string Shift(string value, int delta)
        => (double.Parse(value, CultureInfo.InvariantCulture) + delta).ToString("G17", CultureInfo.InvariantCulture);
}
