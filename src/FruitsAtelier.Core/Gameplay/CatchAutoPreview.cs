// Adapted from ppy/osu 48c4800e3ae4ee752452cdff83bd3787ccf3105f,
// osu.Game.Rulesets.Catch/Replays/CatchAutoGenerator.cs.
// Copyright (c) ppy Pty Ltd. MIT Licence; see LICENSE.osu.txt.
namespace FruitsAtelier.Core;

public readonly record struct CatchAutoFrame(double TimeMs, double X, bool Dashing);
public readonly record struct CatchHyperInterval(double Start, double End);

public sealed class CatchAutoPreview
{
    private readonly CatchAutoFrame[] frames;
    private readonly CatchHyperInterval[] hyperIntervals;
    private readonly (double Time, double From, double To)[] tintChanges;
    public CatchAutoPreview(IReadOnlyList<ConvertedCatchObject> objects, double circleSize)
    {
        var result = new List<CatchAutoFrame> { new(0, 256, false) };
        var hyper = HyperDashCalculator.GetHyperDashStarts(objects, circleSize);
        var states = HyperDashCalculator.Calculate(objects, circleSize);
        var intervals = new List<CatchHyperInterval>();
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].TargetIndex is not int target || objects[target].TimeMs <= objects[i].TimeMs) continue;
            var interval = new CatchHyperInterval(objects[i].TimeMs, objects[target].TimeMs);
            if (intervals.Count > 0 && interval.Start <= intervals[^1].End)
                intervals[^1] = intervals[^1] with { End = Math.Max(interval.End, intervals[^1].End) };
            else intervals.Add(interval);
        }
        hyperIntervals = intervals.ToArray();
        var changes = new List<(double Time, double From, double To)>();
        foreach (var interval in hyperIntervals) { Change(interval.Start, 1); Change(interval.End, 0); }
        tintChanges = changes.ToArray();
        void Change(double at, double to)
        {
            double from = changes.Count == 0 ? 0 : Tint(changes[^1], at);
            changes.Add((at, from, to));
        }
        double position = 256, time = 0, halfWidth = CatchSize.CatchWidth(circleSize) / 2;
        foreach (var item in objects)
        {
            double available = item.TimeMs - time;
            if (available < 0) continue;
            double distance = Math.Abs(item.X - position);
            double speed = distance == 0 ? 0 : distance / available;
            if (distance < halfWidth)
            {
                Add(item.TimeMs, position); time = item.TimeMs; continue;
            }
            if (speed > 1) { Add(time, position, true); Add(item.TimeMs, item.X); }
            else if (hyper.Contains((item.SourceId, item.EventIndex)))
            {
                Add(time, position); Add(item.TimeMs, item.X);
            }
            else if (speed > .5)
            {
                double dashTime = (distance / .5 - available) / 2;
                Add(Math.Min(item.TimeMs, time + 1), position, true);
                Add(time + dashTime, position + (item.X - position) * dashTime / available);
                Add(item.TimeMs, item.X);
            }
            else
            {
                Add(item.TimeMs - distance / .5, position); Add(item.TimeMs, item.X);
            }
            position = item.X; time = item.TimeMs;
        }
        frames = result.OrderBy(frame => frame.TimeMs).ToArray();
        void Add(double at, double x, bool dash = false) => result.Add(new(at, Math.Clamp(x, 0, 512), dash));
    }
    public bool HyperDashingAt(double time)
    {
        int index = UpperBound(hyperIntervals.Length, i => hyperIntervals[i].Start, time) - 1;
        return index >= 0 && time < hyperIntervals[index].End;
    }
    public double HyperTintAt(double time)
    {
        int index = UpperBound(tintChanges.Length, i => tintChanges[i].Time, time) - 1;
        return index < 0 ? 0 : Tint(tintChanges[index], time);
    }
    public IEnumerable<double> HyperStarts(double from, double to)
    {
        int index = UpperBound(hyperIntervals.Length, i => hyperIntervals[i].Start, from);
        for (; index < hyperIntervals.Length && hyperIntervals[index].Start <= to; index++) yield return hyperIntervals[index].Start;
    }
    private static double Tint((double Time, double From, double To) change, double time)
        => change.To + (change.From - change.To) * Math.Pow(1 - Math.Clamp((time - change.Time) / 180, 0, 1), 5);
    private static int UpperBound(int count, Func<int, double> timeAt, double time)
    {
        int low = 0, high = count;
        while (low < high) { int mid = low + (high - low) / 2; if (timeAt(mid) <= time) low = mid + 1; else high = mid; }
        return low;
    }
    public CatchAutoFrame At(double time)
    {
        int low = 0, high = frames.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (frames[mid].TimeMs <= time) low = mid + 1; else high = mid;
        }
        if (low == 0) return frames[0];
        var from = frames[low - 1];
        if (low == frames.Length) return from;
        var to = frames[low];
        double progress = (time - from.TimeMs) / (to.TimeMs - from.TimeMs);
        return new(time, from.X + (to.X - from.X) * progress, from.Dashing);
    }
}
