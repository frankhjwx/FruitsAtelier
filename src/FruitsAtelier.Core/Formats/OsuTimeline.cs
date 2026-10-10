using System.Globalization;

namespace FruitsAtelier.Core;

public readonly record struct BreakPeriod(int StartMs, int EndMs);

public static class OsuTimeline
{
    public static string? BackgroundFilename(MapDocument document)
    {
        foreach (var line in document.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines))
        {
            var parts = WorkspaceProject.Csv(line);
            if (parts.Length >= 3 && parts[0] is "0" or "Background" && !string.IsNullOrWhiteSpace(parts[2]))
            {
                OsuBeatmapReader.ValidateOsuResourceReference(parts[2]);
                return parts[2].Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            }
        }
        return null;
    }

    internal static void ReconcileBreaks(MapDocument before, MapDocument document, ImportedSliderLengthCache sliderLengths)
    {
        var periods = Breaks(document);
        bool removedParents = document.Fruits.Count < before.Fruits.Count || document.Tracks.Count < before.Tracks.Count
            || document.ImportedSliders.Count < before.ImportedSliders.Count || document.BananaShowers.Count < before.BananaShowers.Count;
        if (periods.Count == 0 && !removedParents) return;
        var previous = ObjectIntervals(before, sliderLengths);
        var current = ObjectIntervals(document, sliderLengths);
        var changed = current.Where(pair => !previous.TryGetValue(pair.Key, out var old) || old != pair.Value)
            .Select(pair => pair.Value).ToArray();
        var vacated = previous.Where(pair => !current.TryGetValue(pair.Key, out var now) || now != pair.Value)
            .Select(pair => pair.Value).ToArray();
        bool approachChanged = before.ApproachRate != document.ApproachRate;
        if (changed.Length == 0 && vacated.Length == 0 && !approachChanged) return;
        double preempt = CatchScrollTiming.PreemptMs(document.ApproachRate);
        var occupied = current.Values.OrderBy(interval => interval.Start).ToArray();
        // A break needs objects on both sides, even when its boundaries were manually resized.
        double firstStart = occupied.Length == 0 ? double.PositiveInfinity : occupied[0].Start;
        double lastEnd = occupied.Length == 0 ? double.NegativeInfinity : occupied.Max(item => item.End);
        foreach (var period in periods)
            if (period.EndMs <= firstStart || period.StartMs >= lastEnd)
                RemoveBreak(document, period);
        foreach (var interval in vacated)
        {
            int beforeNote = (int)Math.Clamp(Math.Floor(interval.Start - preempt), 0, int.MaxValue);
            int afterNote = BreakStartAfter(interval.End);
            var adjacent = Breaks(document);
            var left = adjacent.LastOrDefault(period => period.EndMs == beforeNote);
            var right = adjacent.FirstOrDefault(period => period.StartMs == afterNote
                || period.StartMs == Math.Ceiling(interval.End + 200));
            if (left == default && right == default) continue;
            if (left != default && right != default && left != right)
            {
                ReplaceBreak(document, left, new(left.StartMs, right.EndMs));
                RemoveBreak(document, right);
            }
            else if (left != default)
            {
                double? next = occupied.Where(item => item.Start >= interval.End).Select(item => (double?)item.Start).FirstOrDefault();
                if (next is not null)
                    ReplaceBreak(document, left, new(left.StartMs,
                        Math.Max(left.EndMs, (int)Math.Clamp(Math.Floor(next.Value - preempt), 0, int.MaxValue))));
            }
            else
            {
                double? prior = occupied.Where(item => item.End <= interval.Start).Select(item => (double?)item.End).Max();
                if (prior is not null)
                    ReplaceBreak(document, right, new(
                        Math.Min(right.StartMs, BreakStartAfter(prior.Value)), right.EndMs));
            }
        }
        foreach (var period in Breaks(document))
        {
            if (!approachChanged && !changed.Any(interval => interval.Start - preempt < period.EndMs && interval.End + 200 > period.StartMs)
                && !vacated.Any(interval => interval.Start - preempt < period.EndMs && interval.End + 200 > period.StartMs)) continue;
            var remaining = new List<BreakPeriod>();
            int start = period.StartMs;
            foreach (var interval in occupied)
            {
                int beforeNote = (int)Math.Clamp(Math.Floor(interval.Start - preempt), 0, int.MaxValue);
                int afterNote = BreakStartAfter(interval.End);
                if (afterNote <= start) continue;
                if (beforeNote >= period.EndMs) break;
                int end = Math.Min(beforeNote, period.EndMs);
                if ((long)end - start >= 650 && end > firstStart && start < lastEnd) remaining.Add(new(start, end));
                start = Math.Max(start, afterNote);
                if (start >= period.EndMs) break;
            }
            if ((long)period.EndMs - start >= 650 && period.EndMs > firstStart && start < lastEnd)
                remaining.Add(new(start, period.EndMs));
            if (remaining.Count == 1 && remaining[0] == period) continue;
            if (remaining.Count == 0) RemoveBreak(document, period);
            else
            {
                ReplaceBreak(document, period, remaining[0]);
                foreach (var part in remaining.Skip(1)) AddBreak(document, part.StartMs, part.EndMs);
            }
        }
        if (!removedParents || occupied.Length < 2) return;
        var removed = previous.Where(pair => !current.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray();
        var reconciled = Breaks(document).ToList();
        double previousEnd = occupied[0].End;
        for (int i = 1; i < occupied.Length; i++)
        {
            var next = occupied[i];
            int start = BreakStartAfter(previousEnd);
            int end = (int)Math.Clamp(Math.Floor(next.Start - preempt), 0, int.MaxValue);
            // Removing the last object in a section can leave no existing break to extend.
            if ((long)end - start >= 650 && removed.Any(interval => interval.Start > previousEnd && interval.End < next.Start)
                && !reconciled.Any(period => period.StartMs < end && period.EndMs > start))
            {
                AddBreak(document, start, end);
                reconciled.Add(new(start, end));
            }
            previousEnd = Math.Max(previousEnd, next.End);
        }
    }

    // Stable saves truncate fractional object recovery boundaries to integer milliseconds.
    public static int BreakStartAfter(double endTimeMs) => (int)Math.Clamp(Math.Truncate(endTimeMs + 200), 0, int.MaxValue);

    internal static Dictionary<Guid, (double Start, double End)> ObjectIntervals(MapDocument document, ImportedSliderLengthCache sliderLengths)
    {
        var timing = new TimingMap.Lookup(document);
        return document.Fruits.Select(item => (item.Id, Start: item.TimeMs, End: item.TimeMs))
            .Concat(document.Tracks.Where(track => track.Nodes.Count >= 2)
                .Select(track => (track.Id, Start: track.Nodes[0].TimeMs, End: CurveMath.EndTimeMs(track))))
            .Concat(document.ImportedSliders.Select(slider => (slider.Id, Start: slider.TimeMs,
                End: sliderLengths.EndTimeMs(document, slider, timing))))
            .Concat(document.BananaShowers.Select(shower => (shower.Id, Start: shower.TimeMs, End: shower.EndTimeMs)))
            .ToDictionary(item => item.Id, item => (item.Start, item.End));
    }

    public static int? PreviewTime(MapDocument document)
    {
        string? value = OsuBeatmapReader.Setting(document, "General", "PreviewTime");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int time) && time >= 0 ? time : null;
    }

    public static IReadOnlyList<BreakPeriod> Breaks(MapDocument document) => document.OriginalSections
        .Where(s => s.Name == "Events").SelectMany(s => s.Lines)
        .Select(ParseBreak).Where(b => b is not null).Select(b => b!.Value)
        .OrderBy(b => b.StartMs).ToArray();

    public static IReadOnlyList<int> Bookmarks(MapDocument document)
    {
        string? value = OsuBeatmapReader.Setting(document, "Editor", "Bookmarks");
        return value is null ? [] : value.Split(',')
            .Select(s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int time) ? time : -1)
            .Where(time => time >= 0).Distinct().Order().ToArray();
    }

    public static void ToggleBookmark(MapDocument document, int time)
    {
        if (time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        var values = Bookmarks(document).ToList();
        if (!values.Remove(time)) values.Add(time);
        values.Sort();
        SongSetup.Set(document, "Editor", "Bookmarks", values.Count == 0 ? null : string.Join(',', values));
    }

    public static void AddBookmark(MapDocument document, int time)
    {
        if (time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        if (Bookmarks(document).Contains(time)) return;
        ToggleBookmark(document, time);
    }

    public static bool RemoveNearestBookmark(MapDocument document, int time, int maximumDistanceMs = 2000)
    {
        int nearest = Bookmarks(document).Where(t => Math.Abs((long)t - time) < maximumDistanceMs)
            .OrderBy(t => Math.Abs((long)t - time)).DefaultIfEmpty(-1).First();
        if (nearest < 0) return false;
        ToggleBookmark(document, nearest);
        return true;
    }

    public static void ClearBookmarks(MapDocument document)
    {
        if (Bookmarks(document).Count > 0) SongSetup.Set(document, "Editor", "Bookmarks", null);
    }

    public static void AddBreak(MapDocument document, int start, int end)
    {
        if (start < 0 || end <= start) throw new ArgumentOutOfRangeException(nameof(end));
        var section = document.OriginalSections.LastOrDefault(s => s.Name == "Events");
        if (section is null) { section = new OsuSection { Name = "Events" }; document.OriginalSections.Add(section); }
        section.Lines.Add($"2,{start.ToString(CultureInfo.InvariantCulture)},{end.ToString(CultureInfo.InvariantCulture)}");
    }

    public static bool RemoveBreak(MapDocument document, BreakPeriod period)
    {
        foreach (var section in document.OriginalSections.Where(s => s.Name == "Events"))
            for (int i = 0; i < section.Lines.Count; i++)
                if (ParseBreak(section.Lines[i]) == period) { section.Lines.RemoveAt(i); return true; }
        return false;
    }

    public static bool ReplaceBreak(MapDocument document, BreakPeriod period, BreakPeriod replacement)
    {
        if (replacement.StartMs < 0 || replacement.EndMs <= replacement.StartMs)
            throw new ArgumentOutOfRangeException(nameof(replacement));
        foreach (var section in document.OriginalSections.Where(s => s.Name == "Events"))
            for (int i = 0; i < section.Lines.Count; i++)
            {
                string line = section.Lines[i];
                if (ParseBreak(line) != period) continue;
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                string suffix = comment < 0 ? "" : line[comment..];
                var fields = (comment < 0 ? line : line[..comment]).Split(',');
                fields[1] = replacement.StartMs.ToString(CultureInfo.InvariantCulture);
                fields[2] = replacement.EndMs.ToString(CultureInfo.InvariantCulture);
                section.Lines[i] = string.Join(',', fields) + suffix;
                return true;
            }
        return false;
    }

    private static BreakPeriod? ParseBreak(string line)
    {
        string[] parts = line.Trim().Split(',');
        if (parts.Length < 3 || parts[0].Trim() is not ("2" or "Break")
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
            || !int.TryParse(parts[2].Split("//", 2)[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int end)
            || start < 0 || end <= start) return null;
        return new(start, end);
    }
}
