using System.Text.Json.Serialization;

namespace FruitsAtelier.Core;

public readonly record struct StackPoint(double Progress, double Distance);

public readonly record struct StackFruitAdjustment(double Progress, double Offset);

public sealed class StackEnvelope
{
    public bool StartLeft { get; set; } = true;
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<StackPoint> Points { get; set; } = [new(0, 0), new(0.25, 24), new(0.75, 24), new(1, 0)];
    public List<StackFruitAdjustment> FruitAdjustments { get; set; } = [];
    public StackEnvelope DeepClone() => new() { StartLeft = StartLeft, Points = [.. Points], FruitAdjustments = [.. FruitAdjustments] };
    public static bool Equal(StackEnvelope? a, StackEnvelope? b) => ReferenceEquals(a, b)
        || a is not null && b is not null && a.StartLeft == b.StartLeft && a.Points.SequenceEqual(b.Points) && a.FruitAdjustments.SequenceEqual(b.FruitAdjustments);
    [JsonIgnore]
    public bool IsValid => Points is { Count: >= 2 and <= 64 } && Points[0].Progress == 0 && Points[^1].Progress == 1
        && Points.All(p => double.IsFinite(p.Progress) && double.IsFinite(p.Distance) && p.Distance is >= 0 and <= 512)
        && Points.Zip(Points.Skip(1)).All(p => p.First.Progress < p.Second.Progress)
        && FruitAdjustments is not null && FruitAdjustments.Count <= LegacyCatchRules.MaximumNestedObjects
        && FruitAdjustments.All(p => double.IsFinite(p.Progress) && p.Progress is >= 0 and <= 1
            && double.IsFinite(p.Offset) && p.Offset is >= -512 and <= 512)
        && FruitAdjustments.Zip(FruitAdjustments.Skip(1)).All(p => p.First.Progress < p.Second.Progress);
    public double AdjustmentAt(double progress)
    {
        int low = 0, high = FruitAdjustments.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (FruitAdjustments[mid].Progress < progress - 1e-8) low = mid + 1; else high = mid;
        }
        return low < FruitAdjustments.Count && Math.Abs(FruitAdjustments[low].Progress - progress) <= 1e-8
            ? FruitAdjustments[low].Offset : 0;
    }
    public void SetAdjustment(double progress, double offset)
    {
        FruitAdjustments.RemoveAll(p => Math.Abs(p.Progress - progress) <= 1e-8);
        if (Math.Abs(offset) < 1e-8) return;
        int index = FruitAdjustments.FindIndex(p => p.Progress > progress);
        FruitAdjustments.Insert(index < 0 ? FruitAdjustments.Count : index, new(progress, offset));
    }
    public double DistanceAt(double progress)
    {
        for (int i = 1; i < Points.Count; i++)
        {
            if (progress > Points[i].Progress) continue;
            var a = Points[i - 1]; var b = Points[i];
            double u = Math.Clamp((progress - a.Progress) / (b.Progress - a.Progress), 0, 1);
            u = u * u * (3 - 2 * u);
            return a.Distance + (b.Distance - a.Distance) * u;
        }
        return Points[^1].Distance;
    }
}
