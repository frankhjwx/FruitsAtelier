// Adapted from ppy/osu 48c4800e, CatchHealthProcessor and LegacyDrainingHealthProcessor.
// Copyright (c) ppy Pty Ltd. MIT; see Core/Gameplay/LICENSE.osu.txt.
using System.Text.RegularExpressions;

namespace FruitsAtelier.Core;

public sealed class CatchStoryboardHealth
{
    public double DrainRate { get; }
    public double HitMultiplier { get; }
    public double DrainStart { get; }
    public double DrainEnd { get; }
    private readonly (double Start, double End)[] noDrain;
    private readonly double hp;
    public CatchStoryboardHealth(IReadOnlyList<ConvertedCatchObject> objects, IReadOnlyList<BreakPeriod> breaks, double hp,
        CancellationToken cancellationToken = default)
    {
        this.hp = Math.Clamp(hp, 0, 10);
        var top = objects.Where(o => o.Kind != CatchObjectKind.TinyDroplet).ToArray();
        DrainStart = top.Length == 0 ? 0 : top[0].TimeMs; DrainEnd = top.Length == 0 ? 0 : top[^1].TimeMs;
        var expanded = breaks.Select(b => (
            objects.LastOrDefault(o => o.TimeMs <= b.StartMs)?.TimeMs ?? double.MinValue,
            objects.FirstOrDefault(o => o.TimeMs >= b.EndMs)?.TimeMs ?? double.MaxValue)).OrderBy(p => p.Item1).ToArray();
        var merged = new List<(double Start, double End)>();
        foreach (var interval in expanded)
            if (merged.Count > 0 && interval.Item1 <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.Item2));
            else merged.Add(interval);
        noDrain = merged.ToArray();
        double lowestEver = Range(.975, .8, .3), lowestEnd = Range(.99, .9, .4), recoveryAvailable = Range(.04, .02, 0);
        double drop = .00025, multiplier = 1;
        for (int iteration = 0; ; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double health = 1, uncapped = 1, last = DrainStart; int currentBreak = 0; bool fail = false;
            foreach (var item in top)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (currentBreak < breaks.Count && breaks[currentBreak].EndMs <= item.TimeMs) { last = item.TimeMs; currentBreak++; }
                double drain = drop * (item.TimeMs - last); last = item.TimeMs;
                health = Math.Max(0, health - drain); uncapped = Math.Max(0, uncapped - drain);
                if (health <= lowestEver) { fail = true; drop *= .96; break; }
                double gain = Gain(item.Kind, true) * multiplier;
                health = Math.Clamp(health + gain, 0, 1); uncapped += gain;
            }
            if (top.Length == 0) break;
            if (!fail && health < lowestEnd) { fail = true; drop *= .94; multiplier *= 1.01; }
            if (!fail && (uncapped - 1) / Math.Max(1, top.Length) < recoveryAvailable) { fail = true; drop *= .96; multiplier *= 1.01; }
            if (!double.IsFinite(multiplier)) { drop = 0; multiplier = 1; break; }
            if (!fail) break;
            if (iteration > 100000) throw new InvalidDataException("Catch health calibration did not converge.");
        }
        DrainRate = drop; HitMultiplier = multiplier;
    }

    public double Increase(CatchObjectKind kind, bool hit) => hit ? Gain(kind, true) * HitMultiplier : kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet ? Range(-.03, -.125, -.2) : 0;
    private static double Gain(CatchObjectKind kind, bool hit) => kind switch
    { CatchObjectKind.Fruit => .03, CatchObjectKind.Droplet => .015, CatchObjectKind.TinyDroplet => .0015, CatchObjectKind.Banana => .0025, _ => 0 };
    private double Range(double min, double mid, double max) => hp > 5 ? mid + (max - mid) * (hp - 5) / 5 : mid + (mid - min) * (hp - 5) / 5;
    public double ActiveDuration(double from, double to)
    {
        from = Math.Clamp(from, DrainStart, DrainEnd); to = Math.Clamp(to, DrainStart, DrainEnd);
        double amount = Math.Max(0, to - from);
        foreach (var interval in noDrain) amount -= Math.Max(0, Math.Min(to, interval.End) - Math.Max(from, interval.Start));
        return Math.Max(0, amount);
    }
}

/// <summary>Collects every owning judgement, independent of UI snapshots. Readers can seek within retained history.</summary>
public sealed class CatchStoryboardPlayback
{
    private readonly object gate = new();
    private readonly CatchStoryboardHealth configuration;
    private readonly Dictionary<(Guid SourceId, int EventIndex), string[]> hitNames;
    private readonly List<StoryboardTrigger> triggers = [];
    private readonly List<(double Time, double Health)> healthPoints = [];
    private readonly bool recordObjectHit;
    private double time, health = 1;
    private long version;
    public long Version { get { lock (gate) return version; } }
    private CatchStoryboardPlayback(CatchStoryboardPlayback source, double start, bool samplesOnly = false)
    { configuration = source.configuration; hitNames = samplesOnly ? new() : source.hitNames; recordObjectHit = !samplesOnly && source.recordObjectHit; time = start; healthPoints.Add((start, 1)); }
    public CatchStoryboardPlayback Fork(double start) => new(this, start);
    public CatchStoryboardPlayback ForSamples()
    {
        lock (gate)
        {
            var copy = new CatchStoryboardPlayback(this, time, samplesOnly: true);
            copy.healthPoints.Clear(); copy.healthPoints.AddRange(healthPoints);
            copy.health = health; copy.version = version;
            return copy;
        }
    }

    public CatchStoryboardPlayback(CatchStoryboardHealth configuration, IReadOnlyList<string> names,
        IReadOnlyList<ConvertedCatchObject> objects, HitsoundResolver resolver, double start)
    {
        this.configuration = configuration; time = start; recordObjectHit = names.Contains("HitObjectHit", StringComparer.OrdinalIgnoreCase);
        var matchers = names.Where(n => n.StartsWith("HitSound", StringComparison.OrdinalIgnoreCase)).Select(n => (Name: n, Match: TriggerRegex.Match(n))).Where(n => n.Match.Success).ToArray();
        hitNames = new();
        if (matchers.Length > 0)
            foreach (var item in objects)
            {
                if (item.Kind == CatchObjectKind.TinyDroplet) continue;
                var sample = resolver.Describe(item);
                hitNames[(item.SourceId, item.EventIndex)] = matchers.Where(m => Matches(m.Match, sample)).Select(m => m.Name).ToArray();
            }
        healthPoints.Add((start, 1));
    }

    private static readonly Regex TriggerRegex = new("^HitSound(?<bank1>All|Normal|Soft|Drum)?(?<bank2>All|Normal|Soft|Drum)?(?<name>Whistle|Clap|Finish)?(?<suffix>\\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static bool Matches(Match match, HitSampleSettings sample)
    {
        string bank1 = match.Groups["bank1"].Value.ToLowerInvariant(), bank2 = match.Groups["bank2"].Value.ToLowerInvariant();
        string addition = match.Groups["name"].Value.ToLowerInvariant();
        if (bank1.Length > 0 && bank2.Length == 0 && addition.Length > 0) { bank2 = bank1; bank1 = ""; }
        int Bank(string name) => name switch { "normal" => 1, "soft" => 2, "drum" => 3, _ => 0 };
        if (Bank(bank1) is > 0 and var normal && normal != sample.NormalSet || Bank(bank2) is > 0 and var extra && extra != sample.AdditionSet) return false;
        int flag = addition switch { "whistle" => 2, "finish" => 4, "clap" => 8, _ => 0 };
        if (flag != 0 && (sample.Additions & flag) == 0) return false;
        return !match.Groups["suffix"].Success || int.TryParse(match.Groups["suffix"].Value, out int suffix) && suffix == sample.Index;
    }

    public void Judge(ConvertedCatchObject item, bool hit)
    {
        lock (gate)
        {
            AdvanceInternal(item.TimeMs);
            bool passing = health >= .5;
            health = Math.Clamp(health + configuration.Increase(item.Kind, hit), 0, 1);
            healthPoints.Add((item.TimeMs, health));
            if (passing != (health >= .5)) triggers.Add(new(health >= .5 ? "Passing" : "Failing", item.TimeMs));
            if (hit)
            {
                if (recordObjectHit) triggers.Add(new("HitObjectHit", item.TimeMs));
                if (hitNames.TryGetValue((item.SourceId, item.EventIndex), out var names)) foreach (string name in names) triggers.Add(new(name, item.TimeMs));
            }
            version++;
        }
    }
    public void Advance(double to) { lock (gate) AdvanceInternal(to); }
    public void RecordSound(ConvertedCatchObject item)
    {
        lock (gate)
        {
            if (!hitNames.TryGetValue((item.SourceId, item.EventIndex), out var names) || names.Length == 0) return;
            int low = 0, high = triggers.Count;
            while (low < high) { int mid = (low + high) / 2; if (triggers[mid].TimeMs <= item.TimeMs) low = mid + 1; else high = mid; }
            foreach (string name in names) triggers.Insert(low++, new(name, item.TimeMs));
            version++;
        }
    }
    public void RecordSounds(IEnumerable<ConvertedCatchObject> items)
    {
        lock (gate)
        {
            foreach (var item in items)
                if (hitNames.TryGetValue((item.SourceId, item.EventIndex), out var names))
                    foreach (string name in names) triggers.Add(new(name, item.TimeMs));
            triggers.Sort(static (a, b) => a.TimeMs.CompareTo(b.TimeMs)); version++;
        }
    }
    public void CopyTriggers(List<StoryboardTrigger> destination, ref long copiedVersion)
    {
        lock (gate)
            if (copiedVersion != version) { destination.Clear(); destination.AddRange(triggers); copiedVersion = version; }
    }
    private void AdvanceInternal(double to)
    {
        if (to <= time) return;
        double next = Math.Max(0, health - configuration.DrainRate * configuration.ActiveDuration(time, to));
        if (health >= .5 && next < .5)
        {
            double low = time, high = to;
            for (int i = 0; i < 40; i++) { double mid = (low + high) / 2; if (health - configuration.DrainRate * configuration.ActiveDuration(time, mid) >= .5) low = mid; else high = mid; }
            triggers.Add(new("Failing", high)); version++;
        }
        health = next; time = to;
    }
    public bool CopyAt(double at, List<StoryboardTrigger> destination, ref long copiedVersion)
    {
        lock (gate)
        {
            if (copiedVersion != version) { destination.Clear(); destination.AddRange(triggers); copiedVersion = version; }
            int low = 0, high = healthPoints.Count;
            while (low < high) { int mid = (low + high) / 2; if (healthPoints[mid].Time <= at) low = mid + 1; else high = mid; }
            if (low == 0) return true;
            var point = healthPoints[low - 1];
            return point.Health - configuration.DrainRate * configuration.ActiveDuration(point.Time, at) >= .5;
        }
    }
}
