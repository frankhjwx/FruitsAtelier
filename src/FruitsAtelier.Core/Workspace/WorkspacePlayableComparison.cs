namespace FruitsAtelier.Core;

internal sealed class WorkspacePlayableComparison
{
    private readonly Dictionary<int, ConvertedCatchObject[]> events;
    private readonly bool valid;

    internal WorkspacePlayableComparison(MapDocument document)
    {
        var converted = CatchStreamConverter.Convert(document);
        valid = converted.Success;
        var orders = WorkspaceSynchronization.SourceIds(document).ToDictionary(p => p.Id, p => p.Order);
        events = converted.Objects.GroupBy(o => orders[o.SourceId]).ToDictionary(g => g.Key, g => g.ToArray());
    }

    internal bool Equivalent(int order, WorkspacePlayableComparison other, int otherOrder)
    {
        if (!valid || !other.valid || !events.TryGetValue(order, out var left)
            || !other.events.TryGetValue(otherOrder, out var right) || left.Length != right.Length) return false;
        return left.Zip(right).All(p => p.First.Kind == p.Second.Kind
            && Math.Abs(p.First.TimeMs - p.Second.TimeMs) <= .000001
            && Math.Abs(p.First.X - p.Second.X) <= 1.000001);
    }
}
