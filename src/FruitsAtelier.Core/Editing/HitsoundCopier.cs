using System.Globalization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record HitSampleSettings(int NormalSet = 1, int AdditionSet = 1, int Index = 0,
    int Volume = 100, int Additions = 0, string FileName = "");
public sealed record EventHitsound(Guid SourceId, int EventIndex, HitSampleSettings Sample, int EdgeIndex = 0);
public sealed record HitsoundCopyResult(MapDocument Document, int MatchedEvents);

public static class HitsoundCopier
{
    public static MapDocument ReadSource(string path)
    {
        if (new FileInfo(path).Length > OsuBeatmapReader.MaximumFileBytes)
            throw new InvalidDataException(L.Get("core.reader.fileLimit"));
        string text = File.ReadAllText(path);
        var lines = new List<string>(); string section = ""; bool mode = false;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (section == "General" && !mode) lines.Add("Mode:2");
                section = trimmed[1..^1];
            }
            if (section == "General")
            {
                var setting = trimmed.Split(':',2);
                if (setting.Length == 2 && setting[0].Trim() == "Mode")
                { mode = true; if (setting[1].Trim() == "0") line = "Mode:2"; }
            }
            lines.Add(line);
        }
        if (section == "General" && !mode) lines.Add("Mode:2");
        var result = OsuBeatmapReader.Read(string.Join('\n',lines),path);
        result.ImportedContentHash = null;
        return result;
    }

    public static bool TimingMatches(MapDocument source, MapDocument target)
    {
        static (double, double, int)[] Reds(MapDocument d) => d.TimingPoints.Any(p => p.Uninherited)
            ? d.TimingPoints.Where(p => p.Uninherited).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder)
                .Select(p => (p.TimeMs, p.BeatLengthMs, p.Meter)).ToArray()
            : [(d.TimingOffsetMs, d.BeatLengthMs, 4)];
        var a = Reds(source); var b = Reds(target);
        return a.Length == b.Length && a.Zip(b).All(p =>
            Math.Abs(p.First.Item1 - p.Second.Item1) <= 1e-7
            && Math.Abs(p.First.Item2 - p.Second.Item2) <= 1e-7 && p.First.Item3 == p.Second.Item3);
    }

    public static HitsoundCopyResult Copy(MapDocument source, MapDocument target, bool compensateTinyDroplets = true)
    {
        if (!TimingMatches(source, target)) throw new InvalidDataException(L.Get("copier.timingMismatch"));
        var sourceEvents = OsuBeatmapWriter.Serialize(source, compensateTinyDroplets).PlayableObjects;
        var targetEvents = OsuBeatmapWriter.Serialize(target, compensateTinyDroplets).PlayableObjects;
        var sourceResolver = new HitsoundResolver(source, sourceEvents);
        var samples = sourceEvents.Where(Audible).GroupBy(o => o.TimeMs)
            .OrderBy(g => g.Key).Select(g => (Time: g.Key, Sample: sourceResolver.Describe(g.First()))).ToArray();
        var times = samples.Select(s => s.Time).ToArray();
        var result = target.DeepClone();
        CopySampleTiming(source, target, result);
        var targetResolver = new HitsoundResolver(target, targetEvents);
        var updates = result.HitsoundOverrides.ToDictionary(o => (o.SourceId, o.EventIndex));
        var edgeIndices = targetEvents.Where(o => o.Kind == CatchObjectKind.Fruit).GroupBy(o => o.SourceId)
            .SelectMany(g => g.Select((o, i) => (o.SourceId, o.EventIndex, Edge: o.IsStandalone ? 0 : i)))
            .ToDictionary(o => (o.SourceId,o.EventIndex), o => o.Edge);
        int count = 0;
        foreach (var item in targetEvents.Where(Audible))
            if (NearestEvent(times, item.TimeMs) is var index && index >= 0)
            {
                updates[(item.SourceId, item.EventIndex)] = new(item.SourceId, item.EventIndex, samples[index].Sample, edgeIndices.GetValueOrDefault((item.SourceId,item.EventIndex), -1));
                count++;
            }
            else
                updates[(item.SourceId, item.EventIndex)] = new(item.SourceId, item.EventIndex,
                    targetResolver.Describe(item), edgeIndices.GetValueOrDefault((item.SourceId,item.EventIndex), -1));
        result.HitsoundOverrides.Clear(); result.HitsoundOverrides.AddRange(updates.Values);
        // Validate the actual legacy representation before exposing a successful copy.
        _ = OsuBeatmapWriter.Serialize(result, compensateTinyDroplets);
        return new(result, count);
    }

    private static void CopySampleTiming(MapDocument source, MapDocument target, MapDocument result)
    {
        int defaultBank = SongSetup.Get(source,"General","SampleSet").ToLowerInvariant() switch { "soft"=>2,"drum"=>3,_=>1 };
        var changes = new List<(double Time, int Bank, int Index, int Volume)>();
        if (!result.TimingPoints.Any(p=>p.Uninherited))
            result.TimingPoints.Add(new TimingPoint { TimeMs=target.TimingOffsetMs, BeatLengthMs=target.BeatLengthMs, Uninherited=true });
        foreach (var point in source.TimingPoints.OrderBy(p=>p.TimeMs).ThenBy(p=>p.SourceOrder).GroupBy(p=>p.TimeMs).Select(g=>g.Last()))
        {
            var sample = (Bank: point.SampleSet is >=1 and <=3 ? point.SampleSet : defaultBank,
                Index: point.SampleIndex, Volume: point.Volume);
            if (changes.Count == 0 || (changes[^1].Bank,changes[^1].Index,changes[^1].Volume) != sample)
                changes.Add((point.TimeMs,sample.Bank,sample.Index,sample.Volume));
        }
        if (changes.Count == 0) changes.Add((source.TimingOffsetMs,defaultBank,0,100));
        var times = changes.Select(p=>p.Time).ToArray();
        foreach (var point in result.TimingPoints)
        {
            int index = Array.BinarySearch(times,point.TimeMs);
            if (index < 0) index = ~index-1;
            var sample = changes[Math.Max(0,index)];
            point.SampleSet=sample.Bank; point.SampleIndex=sample.Index; point.Volume=sample.Volume; point.OriginalLine=null;
        }
        var lookup = new TimingMap.Lookup(target);
        var existing = result.TimingPoints.Select(p=>p.TimeMs).ToHashSet();
        var original = target.TimingPoints.OrderBy(p=>p.TimeMs).ThenBy(p=>p.SourceOrder).ToArray();
        int prior=-1, order=result.TimingPoints.Select(p=>p.SourceOrder).DefaultIfEmpty(-1).Max()+1;
        foreach (var sample in changes)
        {
            while(prior+1<original.Length && original[prior+1].TimeMs<=sample.Time) prior++;
            if (!existing.Add(sample.Time)) continue;
            var state=lookup.At(sample.Time);
            result.TimingPoints.Add(new TimingPoint { TimeMs=sample.Time,Uninherited=false,
                BeatLengthMs=state.GenerateTicks ? -100/state.SliderVelocityMultiplier : double.NaN,
                Meter=state.Meter, Effects=prior>=0 ? original[prior].Effects : 0,
                SampleSet=sample.Bank,SampleIndex=sample.Index,Volume=sample.Volume,SourceOrder=order++ });
        }
        var ordered=result.TimingPoints.OrderBy(p=>p.TimeMs).ThenBy(p=>p.SourceOrder).ToArray();
        result.TimingPoints.Clear(); result.TimingPoints.AddRange(ordered);
    }

    // Equal-distance candidates use the earlier event; exact matches always win.
    internal static int NearestEvent(double[] times, double time)
    {
        int index = Array.BinarySearch(times, time);
        if (index >= 0) return index;
        int right = ~index, left = right - 1;
        if (left < 0) index = right;
        else if (right >= times.Length) index = left;
        else index = time - times[left] <= times[right] - time ? left : right;
        return index < times.Length && Math.Abs(times[index] - time) <= 2 ? index : -1;
    }

    public static MapDocument Clear(MapDocument target)
    {
        var result = target.DeepClone();
        result.HitsoundOverrides.Clear();
        SongSetup.Set(result, "General", "SampleSet", "Normal");
        foreach (var point in result.TimingPoints)
        { point.SampleSet = point.SampleIndex = 0; point.Volume = 100; point.OriginalLine = null; }
        foreach (var fruit in result.Fruits) fruit.OriginalLine = ClearLine(fruit.OriginalLine);
        foreach (var track in result.Tracks) track.OriginalLine = ClearLine(track.OriginalLine);
        foreach (var slider in result.ImportedSliders) slider.OriginalLine = ClearLine(slider.OriginalLine);
        foreach (var shower in result.BananaShowers) shower.OriginalLine = ClearLine(shower.OriginalLine);
        return result;
    }

    private static string? ClearLine(string? line)
    {
        if (line is null) return null;
        var p = line.Split(','); p[4] = "0";
        int type = int.Parse(p[3], CultureInfo.InvariantCulture);
        if ((type & 2) != 0)
        {
            Array.Resize(ref p, Math.Max(p.Length, 11));
            int edges = int.Parse(p[6], CultureInfo.InvariantCulture) + 1;
            p[8] = string.Join('|', Enumerable.Repeat("0", edges));
            p[9] = string.Join('|', Enumerable.Repeat("0:0", edges)); p[10] = "0:0:0:0:";
        }
        else
        {
            int index = (type & 8) != 0 ? 6 : 5;
            Array.Resize(ref p, Math.Max(p.Length, index + 1)); p[index] = "0:0:0:0:";
        }
        return string.Join(',', p);
    }

    internal static bool Audible(ConvertedCatchObject item) => item.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet;

    internal static void ApplyToExport(MapDocument document, IReadOnlyList<ConvertedCatchObject> events,
        List<(double Time, int Order, Guid SourceId, string Text)> lines, List<TimingPoint> timing)
    {
        if (document.HitsoundOverrides.Count == 0) return;
        var resolver = new HitsoundResolver(document, events);
        var parents = events.Where(Audible).GroupBy(o => o.SourceId).ToDictionary(g => g.Key, g => g.ToArray());
        var standalone = events.Where(o => o.IsStandalone && o.Kind == CatchObjectKind.Fruit).GroupBy(o => o.SourceId)
            .ToDictionary(g => g.Key, g => g.GroupBy(o => Math.Truncate(o.TimeMs)).ToDictionary(t => t.Key,t => t.First()));
        var sliderEvents = new List<(ConvertedCatchObject Event, HitSampleSettings Sample)>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i]; var p = line.Text.Split(',');
            if (!parents.TryGetValue(line.SourceId, out var items)) continue;
            bool slider = (int.Parse(p[3], CultureInfo.InvariantCulture) & 2) != 0;
            if (!slider)
            {

                var item = standalone.GetValueOrDefault(line.SourceId)?.GetValueOrDefault(Math.Truncate(line.Time)) ?? items[0];
                var sample = resolver.Describe(item);
                Array.Resize(ref p, Math.Max(p.Length, 6));
                p[4] = sample.Additions.ToString(CultureInfo.InvariantCulture); p[5] = SampleText(sample);
                sliderEvents.Add((item, sample));
            }
            else
            {
                // Slider samples use timing for index/volume; every slider event supplies
                // its own effective state so unmatched events retain their original sound.
                var samples = items.Select(o => (Event: o, Sample: resolver.Describe(o))).ToArray();
                var edges = samples.Where(o => o.Event.Kind == CatchObjectKind.Fruit).ToArray();
                if (samples.Any(o => o.Sample.FileName.Length > 0))
                    throw new InvalidDataException(L.Get("copier.customSlider"));
                Array.Resize(ref p, Math.Max(p.Length, 11));
                var ticks = samples.Where(o => o.Event.Kind == CatchObjectKind.Droplet).Select(o => o.Sample).ToArray();
                var extra = ticks.Where(s => s.Additions != 0 && s.AdditionSet != s.NormalSet).Select(s => s.AdditionSet).Distinct().ToArray();
                if (extra.Length > 1 || extra.Length == 1 && ticks.Any(s => (s.Additions != 0 && s.AdditionSet != s.NormalSet) != (s.NormalSet != extra[0])))
                    throw new InvalidDataException(L.Get("copier.tickBanks"));
                p[4] = extra.Length == 0 ? "0" : "2";
                p[10] = extra.Length == 0 ? "0:0:0:0:" : $"0:{extra[0]}:0:0:";
                p[8] = string.Join('|', edges.Select(o => o.Sample.Additions));
                p[9] = string.Join('|', edges.Select(o => $"{o.Sample.NormalSet}:{o.Sample.AdditionSet}"));
                sliderEvents.AddRange(samples);
            }
            lines[i] = (line.Time, line.Order, line.SourceId, string.Join(',', p));
        }
        var emitted = new MapDocument(); emitted.TimingPoints.AddRange(timing);
        var lookup = new TimingMap.Lookup(emitted);
        var original = timing.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        int priorIndex = -1;
        int defaultBank = SongSetup.Get(document,"General","SampleSet").ToLowerInvariant() switch { "soft"=>2,"drum"=>3,_=>1 };
        (int Bank,int Index,int Volume) active = (defaultBank,0,100);
        foreach (var (item, sample) in sliderEvents.OrderBy(o => o.Event.TimeMs))
        {
            double time = Math.Floor(item.TimeMs);
            var state = lookup.At(time);
            while (priorIndex+1 < original.Length && original[priorIndex+1].TimeMs <= time)
            {
                var point = original[++priorIndex];
                active = (point.SampleSet is >=1 and <=3 ? point.SampleSet : defaultBank,point.SampleIndex,point.Volume);
            }
            var desired = (sample.NormalSet,sample.Index,sample.Volume);
            if (active == desired) continue;
            active = desired;
            var prior = priorIndex >= 0 ? original[priorIndex] : null;
            if (prior is not null && prior.TimeMs == time)
            {
                prior.SampleSet = sample.NormalSet; prior.SampleIndex = sample.Index; prior.Volume = sample.Volume;
                prior.OriginalLine = null;
                continue;
            }
            timing.Add(new TimingPoint { TimeMs = time, Uninherited = false,
                BeatLengthMs = state.GenerateTicks ? -100 / state.SliderVelocityMultiplier : double.NaN,
                Meter = state.Meter, Effects = prior?.Effects ?? 0,
                SampleSet = sample.NormalSet, SampleIndex = sample.Index, Volume = sample.Volume, SourceOrder = int.MaxValue });
        }
        var ordered = timing.OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        timing.Clear(); timing.AddRange(ordered);
    }

    internal static string SampleText(HitSampleSettings s) => string.Create(CultureInfo.InvariantCulture,
        $"{s.NormalSet}:{s.AdditionSet}:{s.Index}:{s.Volume}:{s.FileName}");
}
