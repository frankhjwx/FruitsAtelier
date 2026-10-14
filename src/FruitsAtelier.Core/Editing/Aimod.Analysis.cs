using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public enum AimodCategory { Compose, Design, Timing, Meta, Mapset }
public enum AimodSeverity { Information, Warning, Error }
public sealed record AimodIssue(AimodCategory Category, AimodSeverity Severity, string Code,
    double? TimeMs = null, Guid[]? ObjectIds = null, params object[] Arguments)
{
    public string Message => L.Get("aimod." + Code, Arguments);
}

public static partial class Aimod
{
    // Analysis is explicit: callers may pass their committed export to avoid rebuilding geometry.
    public static IReadOnlyList<AimodIssue> Analyze(MapDocument document, OsuWriteResult? export = null,
        bool checkDistanceSnap = false, IEnumerable<MapDocument>? otherDifficulties = null)
    {
        export ??= OsuBeatmapWriter.Serialize(document);
        var map = export.ReadBack;
        var timing = new TimingMap.Lookup(map);
        var issues = new List<AimodIssue>();
        var sources = map.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder))
            .Concat(map.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
            .Concat(map.BananaShowers.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
            .OrderBy(x => x.TimeMs).ThenBy(x => x.SourceOrder).ToArray();
        var sourceIds = sources.Select((s, i) => (s.Id, Source: i < export.ObjectSources.Count ? export.ObjectSources[i] : s.Id))
            .ToDictionary(x => x.Id, x => x.Source);
        Guid[] Ids(Guid id) => [sourceIds.GetValueOrDefault(id, id)];
        void Add(AimodCategory category, AimodSeverity severity, string code, double? time = null, Guid[]? ids = null, params object[] args)
            => issues.Add(new(category, severity, code, time, ids, args));
        void Snap(double time, Guid id, bool end)
        {
            int divisor = SnapDivisor(timing.At((int)time), time, end ? 2 : 1);
            if (divisor == 0) Add(AimodCategory.Compose, AimodSeverity.Warning, end ? "unsnappedEnd" : "unsnapped", time, Ids(id));
            else if (divisor is not (1 or 2 or 3 or 4 or 6 or 8))
                Add(AimodCategory.Compose, AimodSeverity.Information, end ? "rareEnd" : "rare", time, Ids(id), divisor);
        }
        foreach (var overlap in FindOverlaps(map))
            Add(AimodCategory.Compose, AimodSeverity.Error, overlap.FirstTimeMs == overlap.SecondTimeMs ? "simultaneous" : "overlap",
                overlap.FirstTimeMs, [.. Ids(overlap.FirstId), .. Ids(overlap.SecondId)]);
        foreach (var fruit in map.Fruits) Snap(fruit.TimeMs, fruit.Id, false);
        foreach (var slider in map.ImportedSliders)
        {
            Snap(slider.TimeMs, slider.Id, false);
            var state = timing.At(slider.TimeMs);
            double end = slider.TimeMs + slider.PixelLength * slider.SpanCount * state.BeatLengthMs
                / (100 * map.EffectiveSliderMultiplier * state.SliderVelocityMultiplier);
            Snap(end, slider.Id, true);

        }
        foreach (var shower in map.BananaShowers)
        {
            Snap(shower.TimeMs, shower.Id, false); Snap(shower.EndTimeMs, shower.Id, true);
        }
        // Stable's Catch Compose pass does not apply standard-mode geometry or distance checks.
        _ = checkDistanceSnap;
        CheckTiming(document, map, timing, sources.Select(s => s.TimeMs).ToArray(), issues);
        if (OsuTimeline.BackgroundFilename(document) is null)
            Add(AimodCategory.Design, AimodSeverity.Error, "noBackground");
        if (sources.Length > 0)
        {
            double drain = sources[^1].TimeMs - sources[0].TimeMs;
            foreach (var period in OsuTimeline.Breaks(map))
                drain -= Math.Max(0, Math.Min(sources[^1].TimeMs, period.EndMs) - Math.Max(sources[0].TimeMs, period.StartMs));
            if (drain < 30000) Add(AimodCategory.Meta, AimodSeverity.Error, "shortDrain");
        }
        double stack = Number(SongSetup.Get(map, "General", "StackLeniency", "0.7"));
        if (stack is < .3 or > .9) Add(AimodCategory.Meta, AimodSeverity.Warning, "stackLeniency");
        foreach (string key in new[] { "Artist", "Title" })
            if (SongSetup.Get(map, "Metadata", key).Any(c => c > 127))
                Add(AimodCategory.Meta, AimodSeverity.Warning, "unicode" + key);
        if (otherDifficulties is not null)
            foreach (var other in otherDifficulties.Where(d => !ReferenceEquals(d, document)))
            {
                string name = SongSetup.Get(other, "Metadata", "Version", other.Name);
                foreach (var (section, key) in new[] { ("Metadata", "Artist"), ("Metadata", "ArtistUnicode"), ("Metadata", "Title"),
                    ("Metadata", "TitleUnicode"), ("Metadata", "Source"), ("Metadata", "Tags"), ("General", "AudioFilename"),
                    ("General", "AudioLeadIn"), ("General", "Countdown"), ("General", "LetterboxInBreaks"), ("General", "PreviewTime") })
                    if (SongSetup.Get(document, section, key) != SongSetup.Get(other, section, key))
                        Add(AimodCategory.Mapset, AimodSeverity.Warning, "conflict" + key, args: [name]);
                if (!document.TimingPoints.Where(p => p.Uninherited).Select(p => (p.TimeMs, p.BeatLengthMs, p.Meter))
                    .SequenceEqual(other.TimingPoints.Where(p => p.Uninherited).Select(p => (p.TimeMs, p.BeatLengthMs, p.Meter))))
                    Add(AimodCategory.Mapset, AimodSeverity.Warning, "conflictTiming", args: [name]);
            }
        return issues.OrderBy(i => i.Category).ThenBy(i => i.TimeMs ?? (i.Code == "previewUnset" ? double.MaxValue : double.MinValue)).ToArray();
    }

    internal static int SnapDivisor(TimingState state, double time, int tolerance = 1)
    {
        int integerTime = (int)time;
        if (Math.Abs(integerTime - SnappedTime(state, integerTime, 8)) <= tolerance
            || Math.Abs(integerTime - SnappedTime(state, integerTime, 6)) <= tolerance) return 1;
        foreach (int divisor in new[] { 5, 7, 9, 12, 16 })
            if (Math.Abs(integerTime - SnappedTime(state, integerTime, divisor)) <= 1) return divisor;
        return 0;
    }

    private static int SnappedTime(TimingState state, double time, int divisor)
    {
        double step = state.BeatLengthMs / divisor;
        double index = Math.Floor((time - state.OffsetMs) / step);
        int before = (int)(state.OffsetMs + index * step);
        int after = (int)(state.OffsetMs + (index + 1) * step);
        return time - before < after - time ? before : after;
    }
    private static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : 0;

    private static void CheckTiming(MapDocument original, MapDocument map, TimingMap.Lookup lookup, double[] times, List<AimodIssue> issues)
    {
        void Add(string code, double? time = null, AimodSeverity severity = AimodSeverity.Warning, params object[] args)
            => issues.Add(new(AimodCategory.Timing, severity, code, time, null, args));
        if (times.Length == 0) return;
        double length = times[^1] - times[0];
        if (length > 360000) Add("longMap");
        else if (length < 45000) Add("shortMap");
        var points = map.TimingPoints.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        int quiet = points.Count(p => p.Volume < 5);
        if (quiet > 0) Add(quiet == points.Length ? "allQuiet" : "someQuiet", severity: quiet == points.Length ? AimodSeverity.Error : AimodSeverity.Warning, args: [quiet, points.Length]);
        for (int i = 1; i < points.Length; i++)
            if (points[i].TimeMs == points[i - 1].TimeMs && points[i].Uninherited == points[i - 1].Uninherited) Add("duplicateTiming", points[i].TimeMs);
        bool active = false; double start = 0, total = 0, previousEnd = 0;
        foreach (var p in points)
        {
            bool on = (p.Effects & 1) != 0;
            if (on == active) continue;
            var state = lookup.At(p.TimeMs);
            bool snapped = SnappedTime(state, p.TimeMs, 8) == (int)p.TimeMs || SnappedTime(state, p.TimeMs, 6) == (int)p.TimeMs;
            if (!snapped) Add(on ? "kiaiStartUnsnapped" : "kiaiEndUnsnapped", p.TimeMs);
            if (on)
            {
                start = p.TimeMs;
                // Stable's short-Kiai message tests the gap since the preceding Kiai end.
                if (start - previousEnd is > 10 and < 15000) Add("shortKiai", start);
            }
            else
            {
                total += p.TimeMs - start;
                previousEnd = p.TimeMs;
            }
            active = on;
        }
        if (active) { Add("openKiai", start); total += Math.Max(0, times[^1] - start); }
        bool tv = SongSetup.Get(map, "Metadata", "Title").Contains("TV Size", StringComparison.OrdinalIgnoreCase);
        if (total > length * (tv ? .5 : 1d / 3)) Add(tv ? "muchKiaiTv" : "muchKiai");
        if (Number(SongSetup.Get(original, "General", "PreviewTime", "-1")) < 0) Add("previewUnset");
    }
}
