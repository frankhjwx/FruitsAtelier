using System.Globalization;

namespace FruitsAtelier.App.Editor;

internal sealed record SyncObjectDifference(string Message, params object[] Arguments);

internal static class SyncObjectDifferences
{
    internal static IReadOnlyList<SyncObjectDifference> Compare(string local, string external)
    {
        var left = Lines(local); var right = Lines(external);
        var result = new List<SyncObjectDifference>();
        var common = left.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
        var rightCounts = right.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
        foreach (string key in common.Keys.ToArray()) common[key] = Math.Min(common[key], rightCounts.GetValueOrDefault(key));
        left = UnchangedRemoved(left, new(common)); right = UnchangedRemoved(right, new(common));
        // Pair only unambiguous same-time objects; a remaining singleton pair also
        // permits a time edit. Ambiguous groups retain explicit per-side entries.
        var leftTimes = left.GroupBy(l => Parts(l).ElementAtOrDefault(2) ?? "").ToDictionary(g => g.Key, g => g.ToArray());
        var rightTimes = right.GroupBy(l => Parts(l).ElementAtOrDefault(2) ?? "").ToDictionary(g => g.Key, g => g.ToArray());
        var pairedLeft = new HashSet<string>(); var pairedRight = new HashSet<string>();
        foreach (var group in leftTimes)
            if (group.Value.Length == 1 && rightTimes.TryGetValue(group.Key, out var matches) && matches.Length == 1)
            { Pair(group.Value[0], matches[0]); pairedLeft.Add(group.Value[0]); pairedRight.Add(matches[0]); }
        left.RemoveAll(pairedLeft.Contains); right.RemoveAll(pairedRight.Contains);
        if (left.Count == 1 && right.Count == 1) { Pair(left[0], right[0]); left.Clear(); right.Clear(); }
        foreach (string line in left) result.Add(new("sync.diff.localOnly", Summary(line)));
        foreach (string line in right) result.Add(new("sync.diff.externalOnly", Summary(line)));
        return result;

        void Pair(string a, string b)
        {
            var before = Parts(a); var after = Parts(b);
            string at = Summary(a);
            string[] common = ["x", "y", "time", "type", "sound"];
            for (int i = 0; i < Math.Max(before.Length, after.Length); i++)
            {
                string from = before.ElementAtOrDefault(i) ?? "", to = after.ElementAtOrDefault(i) ?? "";
                if (from == to) continue;
                if (i == 3 && int.TryParse(from, out int oldType) && int.TryParse(to, out int newType))
                {
                    if ((oldType & 4) != (newType & 4)) result.Add(new("sync.diff.combo", at, (oldType & 4) != 0 ? 1 : 0, (newType & 4) != 0 ? 1 : 0));
                    if ((oldType & 112) != (newType & 112)) result.Add(new("sync.diff.colour", at, (oldType >> 4) & 7, (newType >> 4) & 7));
                    if ((oldType & ~116) == (newType & ~116)) continue;
                }
                string field = i < 5 ? common[i] : ExtraField(before, after, i);
                if (field is "time" or "end")
                {
                    if (double.TryParse(from, NumberStyles.Float, CultureInfo.InvariantCulture, out double start)) from = FormatTime(start);
                    if (double.TryParse(to, NumberStyles.Float, CultureInfo.InvariantCulture, out double end)) to = FormatTime(end);
                }
                result.Add(new("sync.diff." + field, at, from, to));
            }
        }
    }

    private static List<string> UnchangedRemoved(List<string> lines, Dictionary<string, int> counts)
    {
        var result = new List<string>();
        foreach (string line in lines)
            if (counts.GetValueOrDefault(line) > 0) counts[line]--; else result.Add(line);
        return result;
    }

    private static List<string> Lines(string text) => text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    private static string[] Parts(string line) => line.Split(',');
    private static string Summary(string line)
    {
        var p = Parts(line);
        string time = p.ElementAtOrDefault(2) ?? "?";
        if (double.TryParse(time, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
            time = FormatTime(ms);
        return time + " · X=" + (p.ElementAtOrDefault(0) ?? "?");
    }
    internal static string FormatTime(double ms)
    {
        long whole = (long)Math.Round(Math.Max(0, ms), MidpointRounding.AwayFromZero);
        return FormattableString.Invariant($"{whole / 60000:00}:{whole / 1000 % 60:00}:{whole % 1000:000}");
    }
    private static string ExtraField(string[] a, string[] b, int index)
    {
        int.TryParse(a.ElementAtOrDefault(3), out int type);
        int.TryParse(b.ElementAtOrDefault(3), out int other);
        if ((type & 11) != (other & 11)) return "parameters";
        if ((type & 2) != 0) return index switch { 5 => "path", 6 => "repeats", 7 => "length", 8 => "edgeSounds", 9 => "edgeSamples", _ => "samples" };
        if ((type & 8) != 0 && index == 5) return "end";
        return "samples";
    }
}
