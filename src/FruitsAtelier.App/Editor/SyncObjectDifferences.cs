using System.Globalization;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

internal sealed record SyncObjectDifference(string Message, params object[] Arguments)
{
    internal SyncPlayableDifference? Playable { get; init; }
}

internal sealed record SyncPlayableDifference(ConvertedCatchObject? Local, ConvertedCatchObject? External)
{
    internal bool KindChanged => Local?.Kind != External?.Kind;
    internal bool TimeChanged => Local is null || External is null || Math.Abs(Local.TimeMs - External.TimeMs) > .000001;
    internal bool XChanged => Local is null || External is null
        || Math.Abs(Local.X - External.X) > (Local.IsStandalone && External.IsStandalone ? .000001 : 1.000001);
}

internal static class SyncObjectDifferences
{
    internal static IReadOnlyList<SyncObjectDifference> ComparePlayable(string local, string external,
        IReadOnlyList<ConvertedCatchObject> localEvents, IReadOnlyList<ConvertedCatchObject> externalEvents)
    {
        var result = new List<SyncObjectDifference>();
        var left = localEvents.ToList(); var right = externalEvents.ToList();
        var leftTimes = left.GroupBy(o => (o.Kind, Time: Math.Round(o.TimeMs, 6))).ToDictionary(g => g.Key, g => g.ToArray());
        var rightTimes = right.GroupBy(o => (o.Kind, Time: Math.Round(o.TimeMs, 6))).ToDictionary(g => g.Key, g => g.ToArray());
        var matchedLeft = new HashSet<ConvertedCatchObject>(); var matchedRight = new HashSet<ConvertedCatchObject>();
        foreach (var group in leftTimes)
            if (group.Value.Length == 1 && rightTimes.TryGetValue(group.Key, out var matches) && matches.Length == 1)
            { Add(group.Value[0], matches[0]); matchedLeft.Add(group.Value[0]); matchedRight.Add(matches[0]); }
        left.RemoveAll(matchedLeft.Contains); right.RemoveAll(matchedRight.Contains);
        if (left.Count == right.Count)
            for (int i = 0; i < left.Count; i++) Add(left[i], right[i]);
        else
        {
            foreach (var item in left) Add(item, null);
            foreach (var item in right) Add(null, item);
        }
        result.Sort((a, b) => (a.Playable!.Local?.TimeMs ?? a.Playable.External!.TimeMs)
            .CompareTo(b.Playable!.Local?.TimeMs ?? b.Playable.External!.TimeMs));
        // Geometry, count and lifetime differences are represented by playable events.
        // Explicit sound and combo attributes remain separate from those events.
        result.AddRange(Compare(local, external).Where(d => d.Message is "sync.diff.sound" or "sync.diff.combo"
            or "sync.diff.colour" or "sync.diff.edgeSounds" or "sync.diff.edgeSamples" or "sync.diff.samples" or "sync.diff.y"));
        return result;

        void Add(ConvertedCatchObject? a, ConvertedCatchObject? b)
        {
            var difference = new SyncPlayableDifference(a, b);
            if (difference.KindChanged || difference.TimeChanged || difference.XChanged)
                result.Add(new SyncObjectDifference("") { Playable = difference });
        }
    }

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
