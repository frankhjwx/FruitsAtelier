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

    public static void SetBackgroundFilename(MapDocument document, string filename)
    {
        OsuBeatmapReader.ValidateOsuResourceReference(filename);
        foreach (var section in document.OriginalSections.Where(s => s.Name == "Events"))
            for (int i = section.Lines.Count - 1; i >= 0; i--)
            {
                var parts = WorkspaceProject.Csv(section.Lines[i]);
                if (parts.Length >= 3 && parts[0] is "0" or "Background") section.Lines.RemoveAt(i);
            }
        var events = document.OriginalSections.FirstOrDefault(s => s.Name == "Events");
        if (events is null) { events = new OsuSection { Name = "Events" }; document.OriginalSections.Add(events); }
        events.Lines.Insert(0, $"0,0,\"{filename}\",0,0");
    }

    internal static void ReconcileBreaks(MapDocument before, MapDocument document, ImportedSliderLengthCache sliderLengths)
    {
        var periods = Breaks(document);
        bool removedParents = document.Fruits.Count < before.Fruits.Count || document.Tracks.Count < before.Tracks.Count
            || document.ImportedSliders.Count < before.ImportedSliders.Count || document.BananaShowers.Count < before.BananaShowers.Count;
        if (periods.Count == 0 && !removedParents) return;
        // Reconciliation owns its period list; callbacks must not merge fragments midway through the pass.
        ReconcileBreaksCore(periods, ObjectIntervals(before, sliderLengths), ObjectIntervals(document, sliderLengths),
            CatchScrollTiming.PreemptMs(document.ApproachRate), before.ApproachRate != document.ApproachRate, removedParents,
            period => RemoveBreak(document, period), (period, replacement) => ReplaceBreakLine(document, period, replacement),
            period => AddBreakLine(document, period.StartMs, period.EndMs));
    }

    /// <summary>Projects breaks from parent intervals without changing the source periods or documents.</summary>
    public static IReadOnlyList<BreakPeriod> ReconcileBreaks(IReadOnlyList<BreakPeriod> periods,
        IReadOnlyDictionary<Guid, (double Start, double End)> previous,
        IReadOnlyDictionary<Guid, (double Start, double End)> current,
        double preempt, bool approachChanged = false, bool removedParents = false)
        => ReconcileBreaksCore(periods, previous, current, preempt, approachChanged, removedParents);

    private static IReadOnlyList<BreakPeriod> ReconcileBreaksCore(IReadOnlyList<BreakPeriod> sourcePeriods,
        IReadOnlyDictionary<Guid, (double Start, double End)> previous,
        IReadOnlyDictionary<Guid, (double Start, double End)> current,
        double preempt, bool approachChanged, bool removedParents,
        Action<BreakPeriod>? remove = null, Action<BreakPeriod, BreakPeriod>? replace = null, Action<BreakPeriod>? add = null)
    {
        var periods = sourcePeriods.ToList();
        if (periods.Count == 0 && !removedParents) return periods;
        var changed = current.Where(pair => !previous.TryGetValue(pair.Key, out var old) || old != pair.Value)
            .Select(pair => pair.Value).ToArray();
        var vacated = previous.Where(pair => !current.TryGetValue(pair.Key, out var now) || now != pair.Value)
            .Select(pair => pair.Value).ToArray();
        if (changed.Length == 0 && vacated.Length == 0 && !approachChanged) return periods;
        var occupied = current.Values.OrderBy(interval => interval.Start).ToArray();
        var occupiedPrefixEnd = vacated.Length == 0 ? [] : new double[occupied.Length];
        for (int i = 0; i < occupiedPrefixEnd.Length; i++)
            occupiedPrefixEnd[i] = i == 0 ? occupied[i].End : Math.Max(occupiedPrefixEnd[i - 1], occupied[i].End);
        // A break needs objects on both sides, even when its boundaries were manually resized.
        double firstStart = occupied.Length == 0 ? double.PositiveInfinity : occupied[0].Start;
        double lastEnd = occupied.Length == 0 ? double.NegativeInfinity
            : occupiedPrefixEnd.Length > 0 ? occupiedPrefixEnd[^1] : occupied.Max(item => item.End);
        foreach (var period in periods.ToArray())
            if (period.EndMs <= firstStart || period.StartMs >= lastEnd)
                Remove(period);
        foreach (var interval in vacated)
        {
            int beforeNote = (int)Math.Clamp(Math.Floor(interval.Start - preempt), 0, int.MaxValue);
            int afterNote = BreakStartAfter(interval.End);
            var adjacent = OrderedPeriods();
            int following = FirstStartingAtOrAfter(interval.Start);
            double? prior = following > 0 ? occupiedPrefixEnd[following - 1] : null;
            double? next = following < occupied.Length ? occupied[following].Start : null;
            bool vacant = (prior is null || prior.Value < interval.Start) && (next is null || next.Value > interval.End);
            // Manual edge edits keep a fragment in its object gap, even after changing its generated boundary.
            var left = adjacent.LastOrDefault(period => period.EndMs == beforeNote
                || vacant && prior is not null && period.StartMs >= prior.Value && period.EndMs <= interval.Start);
            var right = adjacent.FirstOrDefault(period => period.StartMs == afterNote || period.StartMs == Math.Ceiling(interval.End + 200)
                || vacant && next is not null && period.StartMs >= interval.End && period.EndMs <= next.Value);
            if (left == default && right == default) continue;
            if (!vacant)
            {
                prior = occupied.Where(item => item.End <= interval.Start).Select(item => (double?)item.End).Max();
                next = occupied.Where(item => item.Start >= interval.End).Select(item => (double?)item.Start).FirstOrDefault();
            }
            if (left != default && right != default && left != right)
            {
                Replace(left, new(left.StartMs, right.EndMs));
                Remove(right);
            }
            else if (left != default && next is not null)
            {
                Replace(left, new(left.StartMs,
                    Math.Max(left.EndMs, (int)Math.Clamp(Math.Floor(next.Value - preempt), 0, int.MaxValue))));
            }
            else if (right != default && prior is not null)
            {
                Replace(right, new(
                    Math.Min(right.StartMs, BreakStartAfter(prior.Value)), right.EndMs));
            }
        }
        foreach (var period in OrderedPeriods())
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
            if (remaining.Count == 0) Remove(period);
            else
            {
                Replace(period, remaining[0]);
                foreach (var part in remaining.Skip(1)) Add(part);
            }
        }
        if (!removedParents || occupied.Length < 2) return OrderedPeriods();
        var removed = previous.Where(pair => !current.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray();
        var reconciled = periods.ToList();
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
                Add(new(start, end));
                reconciled.Add(new(start, end));
            }
            previousEnd = Math.Max(previousEnd, next.End);
        }
        return OrderedPeriods();

        int FirstStartingAtOrAfter(double time)
        {
            int low = 0, high = occupied.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (occupied[middle].Start < time) low = middle + 1; else high = middle;
            }
            return low;
        }

        BreakPeriod[] OrderedPeriods() => periods.OrderBy(period => period.StartMs).ToArray();
        void Remove(BreakPeriod period)
        {
            if (periods.Remove(period)) remove?.Invoke(period);
        }
        void Replace(BreakPeriod period, BreakPeriod replacement)
        {
            int index = periods.IndexOf(period);
            if (index < 0) return;
            periods[index] = replacement;
            replace?.Invoke(period, replacement);
        }
        void Add(BreakPeriod period)
        {
            periods.Add(period);
            add?.Invoke(period);
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
        if (!Breaks(document).Any(period => period.StartMs <= start && period.EndMs >= end))
            AddBreakLine(document, start, end);
        MergeOverlappingBreaks(document);
    }

    private static void AddBreakLine(MapDocument document, int start, int end)
    {
        var section = document.OriginalSections.LastOrDefault(s => s.Name == "Events");
        if (section is null) { section = new OsuSection { Name = "Events" }; document.OriginalSections.Add(section); }
        section.Lines.Add($"2,{start.ToString(CultureInfo.InvariantCulture)},{end.ToString(CultureInfo.InvariantCulture)}");
    }

    public static bool RemoveBreak(MapDocument document, BreakPeriod period)
        => RemoveBreakLine(document, period, fromEnd: false);

    private static bool RemoveBreakLine(MapDocument document, BreakPeriod period, bool fromEnd)
    {
        for (int offset = 0; offset < document.OriginalSections.Count; offset++)
        {
            var section = document.OriginalSections[fromEnd ? document.OriginalSections.Count - 1 - offset : offset];
            if (section.Name != "Events") continue;
            for (int lineOffset = 0; lineOffset < section.Lines.Count; lineOffset++)
            {
                int i = fromEnd ? section.Lines.Count - 1 - lineOffset : lineOffset;
                if (ParseBreak(section.Lines[i]) == period) { section.Lines.RemoveAt(i); return true; }
            }
        }
        return false;
    }

    public static bool ReplaceBreak(MapDocument document, BreakPeriod period, BreakPeriod replacement)
    {
        if (replacement.StartMs < 0 || replacement.EndMs <= replacement.StartMs)
            throw new ArgumentOutOfRangeException(nameof(replacement));
        if (!ReplaceBreakLine(document, period, replacement)) return false;
        MergeOverlappingBreaks(document);
        return true;
    }

    private static void MergeOverlappingBreaks(MapDocument document)
    {
        var periods = Breaks(document);
        if (periods.Count < 2) return;
        var merged = periods[0];
        for (int i = 1; i < periods.Count; i++)
        {
            var next = periods[i];
            if (next.StartMs >= merged.EndMs) { merged = next; continue; }
            RemoveBreakLine(document, next, fromEnd: true);
            var replacement = new BreakPeriod(merged.StartMs, Math.Max(merged.EndMs, next.EndMs));
            ReplaceBreakLine(document, merged, replacement);
            merged = replacement;
        }
    }

    private static bool ReplaceBreakLine(MapDocument document, BreakPeriod period, BreakPeriod replacement)
    {
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
