using System.Globalization;

namespace FruitsAtelier.Core;

public sealed class WorkspaceEventReview
{
    private sealed record Part(string Key, string Value, string Raw);
    private readonly string local, external;
    private readonly Dictionary<string, Part> ours, theirs;
    public IReadOnlyList<WorkspaceSyncConflict> Breaks { get; }
    public string LocalOther { get; }
    public string ExternalOther { get; }
    public bool OtherChanged => Data(LocalOther) != Data(ExternalOther);

    public WorkspaceEventReview(string local, string external)
    {
        this.local = local; this.external = external;
        ours = Parts(local); theirs = Parts(external);
        LocalOther = Other(local); ExternalOther = Other(external);
        Breaks = ours.Keys.Union(theirs.Keys).OrderBy(k => int.Parse(k.Split('/')[2], CultureInfo.InvariantCulture))
            .ThenBy(k => int.Parse(k.Split('/')[3], CultureInfo.InvariantCulture))
            .Select(k => new WorkspaceSyncConflict(k, ours.GetValueOrDefault(k)?.Value ?? "", theirs.GetValueOrDefault(k)?.Value ?? "")).ToArray();
    }

    public string Resolve(IReadOnlyDictionary<string, bool> choices)
    {
        var keys = Breaks.Where(b => b.Local != b.External).Select(b => b.Key).Concat(OtherChanged ? ["Events/"] : []).ToArray();
        if (keys.All(k => !choices.GetValueOrDefault(k))) return local;
        if (keys.All(k => choices.GetValueOrDefault(k))) return external;
        string source = choices.GetValueOrDefault("Events/") ? external : local;
        // Breaks may occur amongst storyboard commands; replace them in place to preserve command order and indentation.
        var sourceParts = Parts(source).Values.ToArray(); int index = 0;
        var lines = new List<string>(); var emitted = new HashSet<string>();
        foreach (string line in source.Replace("\r", "").Split('\n'))
        {
            if (!TryBreak(line, out _, out _)) { lines.Add(line); continue; }
            string key = sourceParts[index++].Key;
            var part = (choices.GetValueOrDefault(key) ? theirs : ours).GetValueOrDefault(key);
            if (part is not null) lines.Add(part.Raw);
            emitted.Add(key);
        }
        foreach (var row in Breaks.Where(b => !emitted.Contains(b.Key)))
            if ((choices.GetValueOrDefault(row.Key) ? theirs : ours).GetValueOrDefault(row.Key) is { } part) lines.Add(part.Raw);
        return string.Join('\n', lines);
    }

    private static Dictionary<string, Part> Parts(string text)
    {
        var result = new Dictionary<string, Part>(); var counts = new Dictionary<int, int>();
        foreach (string line in text.Replace("\r", "").Split('\n'))
            if (TryBreak(line, out int start, out int end))
            {
                int occurrence = counts.GetValueOrDefault(start); counts[start] = occurrence + 1;
                string key = FormattableString.Invariant($"Events/break/{start}/{occurrence}");
                result.Add(key, new(key, FormattableString.Invariant($"{start},{end}"), line));
            }
        return result;
    }

    private static string Other(string text) => string.Join('\n', text.Replace("\r", "").Split('\n').Where(l => !TryBreak(l, out _, out _)));
    private static string Data(string text) => string.Join('\n', text.Split('\n').Where(l => OsuBeatmapReader.IsDataLine(l.Trim())).Select(l => l.TrimEnd()));
    private static bool TryBreak(string line, out int start, out int end)
    {
        start = end = 0;
        var fields = line.Trim().Split(',');
        return fields.Length == 3 && fields[0].Trim() is "2" or "Break"
            && int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            && int.TryParse(fields[2].Split("//", 2)[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out end);
    }

    internal static void Prepare(WorkspaceMerge merge, string? previousExternal = null)
    {
        var conflict = merge.Conflicts.FirstOrDefault(c => c.Key == "Events/");
        if (conflict is null) return;
        var review = new WorkspaceEventReview(conflict.Local, conflict.External);
        merge.EventReview = review;
        bool retained = merge.PreviouslyResolved.Remove("Events/");
        merge.Conflicts.Remove(conflict);
        var items = review.Breaks.Where(b => b.Local != b.External).ToList();
        if (review.OtherChanged) items.Add(new("Events/", review.LocalOther, review.ExternalOther));
        var previous = previousExternal is null ? null : new WorkspaceEventReview(
            WorkspaceSynchronization.Fields(merge.Baseline).GetValueOrDefault("Events/") ?? "",
            previousExternal);
        foreach (var item in items)
        {
            merge.Conflicts.Add(item);
            var before = previous?.Breaks.FirstOrDefault(b => b.Key == item.Key);
            if (retained || before is not null && before.Local == item.Local && before.External == item.External
                || item.Key == "Events/" && previous is not null && Data(previous.LocalOther) == Data(review.LocalOther)
                    && Data(previous.ExternalOther) == Data(review.ExternalOther)) merge.PreviouslyResolved.Add(item.Key);
        }
    }
}
