using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static partial class WorkspaceSynchronization
{
    public static WorkspaceMerge CompareNativeDifficulty(MapDocument local, MapDocument incoming, string path, bool compensate)
    {
        var ours = local.DeepClone(); var theirs = incoming.DeepClone();
        var localOutput = OsuBeatmapWriter.Serialize(ours, compensate);
        var externalOutput = OsuBeatmapWriter.Serialize(theirs, compensate);
        var merge = new WorkspaceMerge
        {
            NativeDifficulty = true, Local = ours, Baseline = ours,
            External = new(path, "", externalOutput.Text, theirs), Compensate = compensate
        };
        var fields = Fields(ours); var outside = Fields(theirs);
        foreach (string key in fields.Keys.Union(outside.Keys))
        {
            merge.Fields[key] = fields.GetValueOrDefault(key);
            if (fields.GetValueOrDefault(key) != outside.GetValueOrDefault(key))
                merge.Conflicts.Add(new(key, fields.GetValueOrDefault(key) ?? "", outside.GetValueOrDefault(key) ?? ""));
        }
        var a = ours.DeepClone(); var b = theirs.DeepClone();
        // Object authoring and FA settings stay together so mixed choices retain exact editable curves.
        ApplyFields(a, fields, ours.SourcePath ?? path); ApplyFields(b, fields, ours.SourcePath ?? path);
        a.SourcePath = b.SourcePath = ours.SourcePath; a.AudioPath = b.AudioPath = ours.AudioPath;
        a.DurationMs = b.DurationMs = Math.Max(a.DurationMs, b.DurationMs);
        if (!a.ContentEquals(b))
            merge.Conflicts.Add(new("$objects:native", string.Join('\n', ObjectLines(localOutput.Text)), string.Join('\n', ObjectLines(externalOutput.Text))));
        WorkspaceEventReview.Prepare(merge);
        return merge;
    }

    private static MapDocument ResolveNativeDifficulty(WorkspaceMerge merge, IReadOnlyDictionary<string, bool> choices)
    {
        if (merge.Conflicts.Any(c => !choices.ContainsKey(c.Key))) throw new InvalidOperationException(L.Get("sync.unresolved"));
        if (merge.Conflicts.All(c => !choices[c.Key])) return merge.Local.DeepClone();
        if (merge.Conflicts.All(c => choices[c.Key])) return merge.External.Document.DeepClone();
        var result = (choices.GetValueOrDefault("$objects:native") ? merge.External.Document : merge.Local).DeepClone();
        var fields = new Dictionary<string, string?>(merge.Fields);
        var outside = Fields(merge.External.Document);
        foreach (var conflict in merge.Conflicts.Where(c => !c.Key.StartsWith('$') && !(merge.EventReview is not null && c.Key.StartsWith("Events/"))))
            if (choices.GetValueOrDefault(conflict.Key)) fields[conflict.Key] = outside.GetValueOrDefault(conflict.Key);
        if (merge.EventReview is { } events) fields["Events/"] = events.Resolve(choices);
        ApplyFields(result, fields, merge.Local.SourcePath ?? merge.External.Path);
        var timing = (choices.GetValueOrDefault("TimingPoints/") ? merge.External.Document : merge.Local).DeepClone();
        result.TimingPoints.Clear(); result.TimingPoints.AddRange(timing.TimingPoints);
        result.BeatLengthMs = timing.BeatLengthMs; result.TimingOffsetMs = timing.TimingOffsetMs;
        result.SourcePath = merge.Local.SourcePath; result.AudioPath = merge.Local.AudioPath;
        OsuBeatmapReader.Validate(result);
        return result;
    }
}
