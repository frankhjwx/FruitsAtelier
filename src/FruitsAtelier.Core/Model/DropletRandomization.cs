using System.Text.Json.Serialization;

namespace FruitsAtelier.Core;

public readonly record struct DropletAdjustment(double Progress, double Offset);

public sealed class DropletRandomization
{
    public bool Enabled { get; set; }
    public List<DropletAdjustment> Adjustments { get; set; } = [];
    public DropletRandomization DeepClone() => new() { Enabled = Enabled, Adjustments = [.. Adjustments] };
    public static bool Equal(DropletRandomization? a, DropletRandomization? b) => ReferenceEquals(a, b)
        || a is not null && b is not null && a.Enabled == b.Enabled && a.Adjustments is not null
            && b.Adjustments is not null && a.Adjustments.SequenceEqual(b.Adjustments);
    [JsonIgnore]
    public bool IsValid => Adjustments is not null && Adjustments.Count <= LegacyCatchRules.MaximumNestedObjects
        && Adjustments.All(p => double.IsFinite(p.Progress) && p.Progress is >= 0 and <= 1
            && double.IsFinite(p.Offset) && p.Offset is >= -512 and <= 512)
        && Adjustments.Zip(Adjustments.Skip(1)).All(p => p.First.Progress < p.Second.Progress);

    public double AdjustmentAt(double progress)
    {
        int low = 0, high = Adjustments.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Adjustments[mid].Progress < progress - 1e-8) low = mid + 1; else high = mid;
        }
        return low < Adjustments.Count && Math.Abs(Adjustments[low].Progress - progress) <= 1e-8 ? Adjustments[low].Offset : 0;
    }

    public void SetAdjustment(double progress, double offset)
    {
        Adjustments.RemoveAll(p => Math.Abs(p.Progress - progress) <= 1e-8);
        if (Math.Abs(offset) < 1e-8) return;
        int index = Adjustments.FindIndex(p => p.Progress > progress);
        Adjustments.Insert(index < 0 ? Adjustments.Count : index, new(progress, offset));
    }

    public static double Progress(CurveTrack track, double time) => Math.Clamp(
        (time - track.Nodes[0].TimeMs) / (CurveMath.EndTimeMs(track) - track.Nodes[0].TimeMs), 0, 1);

    internal static Func<double, double> Targets(MapDocument document, CurveTrack track,
        IReadOnlyList<NestedCatchEvent> events, Func<double, double> curve, CatchLegacyRandom random)
    {
        var targets = new Dictionary<double, double>();
        for (int index = 0; index < events.Count; index++)
        {
            var item = events[index];
            if (item.Kind == CatchObjectKind.Droplet) random.Next();
            if (item.Kind != CatchObjectKind.TinyDroplet) continue;
            double progress = Progress(track, item.TimeMs);
            double pathTime = track.Nodes[0].TimeMs + item.Progress * (track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs);
            double x = Math.Clamp(curve(pathTime), 0, 512);
            targets[item.TimeMs] = Math.Clamp(Math.Clamp(x + random.NextTinyOffset()
                * document.RandomizeDropletStrength / 20, 0, 512) + track.DropletRandomization!.AdjustmentAt(progress), 0, 512);
        }
        return time => targets.TryGetValue(time, out double x) ? x : curve(time);
    }

}
