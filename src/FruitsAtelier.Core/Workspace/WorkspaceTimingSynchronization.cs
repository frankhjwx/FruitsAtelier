using System.Globalization;

namespace FruitsAtelier.Core;

internal static class WorkspaceTimingSynchronization
{
    private static string Key(TimingPoint point) => point.TimeMs.ToString("G15", CultureInfo.InvariantCulture);

    internal static string Text(IEnumerable<TimingPoint> points, bool comparison = false) => string.Join('\n', points.Select(point =>
        string.Join(',', point.TimeMs.ToString(comparison ? "G15" : "R", CultureInfo.InvariantCulture),
            point.BeatLengthMs.ToString(comparison ? "G15" : "R", CultureInfo.InvariantCulture), point.Meter,
            point.SampleSet, point.SampleIndex, point.Volume, point.Uninherited ? 1 : 0, point.Effects)));

    private static Dictionary<string, TimingPoint[]> Groups(IEnumerable<TimingPoint> points) => points.GroupBy(Key)
        .ToDictionary(group => group.Key, group => group.ToArray());

    private static HashSet<string> Changed(Dictionary<string, TimingPoint[]> before, Dictionary<string, TimingPoint[]> after)
        => before.Keys.Union(after.Keys).Where(key => Text(before.GetValueOrDefault(key) ?? [], true)
            != Text(after.GetValueOrDefault(key) ?? [], true)).ToHashSet();

    internal static List<TimingPoint> ProjectChanges(IEnumerable<TimingPoint> target, IEnumerable<TimingPoint> before, IEnumerable<TimingPoint> after)
    {
        var previous = Groups(before); var next = Groups(after);
        var authored = Groups(target);
        var changed = Changed(previous, next);
        var result = new List<TimingPoint>();
        var replaced = new HashSet<string>();
        foreach (var point in target)
        {
            string key = Key(point);
            if (!changed.Contains(key)) result.Add(point.DeepClone());
            else if (replaced.Add(key))
            {
                var old = previous.GetValueOrDefault(key) ?? []; var updated = next.GetValueOrDefault(key) ?? [];
                var current = authored[key];
                bool aligned = current.Length == old.Length && old.Length == updated.Length
                    && current.Zip(old).All(pair => pair.First.Uninherited == pair.Second.Uninherited)
                    && old.Zip(updated).All(pair => pair.First.Uninherited == pair.Second.Uninherited);
                result.AddRange(aligned ? current.Select((point, i) => Patch(point, old[i], updated[i])) : updated.Select(p => p.DeepClone()));
            }
        }
        foreach (var group in next.Where(group => changed.Contains(group.Key) && !replaced.Contains(group.Key))
            .OrderBy(group => group.Value[0].TimeMs))
        {
            int index = result.FindIndex(point => point.TimeMs > group.Value[0].TimeMs);
            result.InsertRange(index < 0 ? result.Count : index, group.Value.Select(point => point.DeepClone()));
        }
        return result;
    }

    private static TimingPoint Patch(TimingPoint target, TimingPoint before, TimingPoint after)
    {
        var result = target.DeepClone();
        // An emitted SV can replace an authored SV at the same timestamp. A sample
        // or effect edit at that point must not copy the unchanged generated SV back.
        if (before.BeatLengthMs.ToString("G15", CultureInfo.InvariantCulture) != after.BeatLengthMs.ToString("G15", CultureInfo.InvariantCulture)) result.BeatLengthMs = after.BeatLengthMs;
        if (before.Meter != after.Meter) result.Meter = after.Meter;
        if (before.SampleSet != after.SampleSet) result.SampleSet = after.SampleSet;
        if (before.SampleIndex != after.SampleIndex) result.SampleIndex = after.SampleIndex;
        if (before.Volume != after.Volume) result.Volume = after.Volume;
        if (before.Effects != after.Effects) result.Effects = after.Effects;
        return result;
    }

    internal static WorkspaceSyncConflict? Conflict(IEnumerable<TimingPoint> local, IEnumerable<TimingPoint> external)
    {
        var ours = Groups(local); var theirs = Groups(external);
        var changed = Changed(ours, theirs);
        if (changed.Count == 0) return null;
        var ordered = changed.OrderBy(key => (ours.GetValueOrDefault(key) ?? theirs[key])[0].TimeMs).ToArray();
        return new("TimingPoints/", Text(ordered.SelectMany(key => ours.GetValueOrDefault(key) ?? []), true),
            Text(ordered.SelectMany(key => theirs.GetValueOrDefault(key) ?? []), true));
    }
}
