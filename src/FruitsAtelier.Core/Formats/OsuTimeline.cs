using System.Globalization;

namespace FruitsAtelier.Core;

public readonly record struct BreakPeriod(int StartMs, int EndMs);

public static class OsuTimeline
{
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
