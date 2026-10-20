using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? syncTab;
    private int syncObjectDetailScroll, syncObjectDetailMax;
    private Rect syncObjectDetailBounds;
    private (IReadOnlyList<SyncObjectDifference> Items, float Width, string Language)? syncObjectDetailLayout;
    private SyncTextLine[] syncObjectDetailLines = [];

    private void DrawSyncPlayableDetails(ICanvas c, IReadOnlyList<SyncObjectDifference> differences, Rect bounds, uint colour)
    {
        syncObjectDetailBounds = bounds;
        int visible = Math.Max(1, (int)((bounds.Height - 26) / 23));
        syncObjectDetailMax = Math.Max(0, differences.Count - visible);
        syncObjectDetailScroll = Math.Clamp(syncObjectDetailScroll, 0, syncObjectDetailMax);
        c.Fill(bounds, Background); c.Stroke(bounds, Grid); c.Clip(bounds);
        float cell = (bounds.Width - 24) / 6;
        string[] headings = [L.Get("sync.localHeading"), L.Get("sync.eventTime"), "X (px)",
            L.Get(fadPending is not null ? "fad.externalHeading" : "sync.externalHeading"), L.Get("sync.eventTime"), "X (px)"];
        for (int i = 0; i < headings.Length; i++) c.Text(headings[i], bounds.X + 8 + i * cell, bounds.Y + 3, 11, Muted, cell - 6);
        for (int row = 0; row < visible && syncObjectDetailScroll + row < differences.Count; row++)
        {
            var difference = differences[syncObjectDetailScroll + row];
            float y = bounds.Y + 25 + row * 23;
            if (difference.Playable is not { } item)
            {
                var arguments = SyncDetailArguments(difference);
                DrawSyncAttributeText(c, L.Get(difference.Message, arguments), arguments.Skip(1), bounds.X + 8, y, 12, bounds.Width - 24, colour);
                continue;
            }
            var values = Values(item.Local).Concat(Values(item.External)).ToArray();
            bool[] changed = [item.KindChanged, item.TimeChanged, item.XChanged];
            for (int i = 0; i < values.Length; i++)
            {
                float x = bounds.X + 8 + i * cell;
                if (changed[i % 3]) c.Fill(new(x - 2, y, cell - 4, 21), colour, opacity: .22f);
                c.Text(values[i], x, y, 12, changed[i % 3] ? colour : Foreground, cell - 6);
            }
        }
        c.Unclip();
        if (syncObjectDetailMax > 0) c.Text("↕", bounds.Right - 14, bounds.Y + 4, 12, Muted, 14);

        static string[] Values(ConvertedCatchObject? item) => item is null ? ["—", "—", "—"] :
            [L.Get(item.Kind switch { CatchObjectKind.Fruit => "tools.fruit", CatchObjectKind.Droplet => "sync.eventDroplet",
                CatchObjectKind.TinyDroplet => "sync.eventTiny", _ => "tools.banana" }),
            item.TimeMs.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            item.X.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)];
    }

    private static object[] SyncDetailArguments(SyncObjectDifference difference) => difference.Message == "sync.diff.combo"
        ? [difference.Arguments[0], L.Get((int)difference.Arguments[1] == 0 ? "sync.diff.off" : "sync.diff.on"),
            L.Get((int)difference.Arguments[2] == 0 ? "sync.diff.off" : "sync.diff.on")] : difference.Arguments;

    private void DrawSyncAttributeText(ICanvas c, string text, IEnumerable<object> values, float x, float y, float size, float width, uint colour)
    {
        c.Text(text, x, y, size, Foreground, width);
        foreach (var value in values)
        {
            string token = value.ToString() ?? "";
            int start = token.Length == 0 ? -1 : text.LastIndexOf(token, StringComparison.Ordinal);
            if (start < 0) continue;
            float at = x + c.MeasureText(text[..start], size), length = c.MeasureText(token, size);
            c.Fill(new(at, y, length, 21), colour, opacity: .22f);
            c.Text(token, at, y, size, colour, length + 2);
        }
    }

    private void DrawSyncObjectDetails(ICanvas c, IReadOnlyList<SyncObjectDifference> differences, Rect bounds, uint colour = SyncUnresolved)
    {
        if (differences.Any(d => d.Playable is not null))
        { DrawSyncPlayableDetails(c, differences, bounds, colour); return; }
        syncObjectDetailBounds = bounds;
        var layout = (differences, bounds.Width, L.Language);
        if (syncObjectDetailLayout != layout)
        {
            syncObjectDetailLines = differences.SelectMany(d =>
            {
                object[] arguments = d.Arguments;
                if (d.Message == "sync.diff.combo") arguments = [arguments[0],
                    L.Get((int)arguments[1] == 0 ? "sync.diff.off" : "sync.diff.on"),
                    L.Get((int)arguments[2] == 0 ? "sync.diff.off" : "sync.diff.on")];
                return WrapSyncText(c, L.Get(d.Message, arguments), bounds.Width - 20);
            }).ToArray();
            syncObjectDetailLayout = layout;
        }
        var lines = syncObjectDetailLines;
        int visible = Math.Max(1, (int)((bounds.Height - 5) / 23));
        syncObjectDetailMax = Math.Max(0, lines.Length - visible);
        syncObjectDetailScroll = Math.Clamp(syncObjectDetailScroll, 0, syncObjectDetailMax);
        c.Fill(bounds, Background); c.Stroke(bounds, Grid);
        c.Clip(bounds);
        for (int i = 0; i < visible && syncObjectDetailScroll + i < lines.Length; i++)
            DrawSyncAttributeText(c, lines[syncObjectDetailScroll + i].Text, differences.SelectMany(d => SyncDetailArguments(d).Skip(1)),
                bounds.X + 8, bounds.Y + 3 + i * 23, 15, bounds.Width - 20, colour);
        c.Unclip();
        if (lines.Length > visible) c.Text("↕", bounds.Right - 14, bounds.Y + 4, 12, Muted, 14);
    }
    private static readonly string[] SyncTabOrder = ["General", "Editor", "Metadata", "Difficulty", "Events", "Timing", "Colours", "Objects"];
    private static readonly Dictionary<string, string[]> SyncFieldOrder = new()
    {
        ["Format"] = ["Version"],
        ["General"] = ["AudioFilename", "AudioLeadIn", "AudioHash", "PreviewTime", "Countdown", "SampleSet", "StackLeniency", "Mode", "LetterboxInBreaks", "StoryFireInFront", "UseSkinSprites", "AlwaysShowPlayfield", "OverlayPosition", "SkinPreference", "EpilepsyWarning", "CountdownOffset", "SpecialStyle", "WidescreenStoryboard", "SamplesMatchPlaybackRate"],
        ["Editor"] = ["Bookmarks", "DistanceSpacing", "BeatDivisor", "GridSize", "TimelineZoom"],
        ["Metadata"] = ["Title", "TitleUnicode", "Artist", "ArtistUnicode", "Creator", "Version", "Source", "Tags", "BeatmapID", "BeatmapSetID"],
        ["Difficulty"] = ["HPDrainRate", "CircleSize", "OverallDifficulty", "ApproachRate", "SliderMultiplier", "SliderTickRate"],
        ["Colours"] = Enumerable.Range(1, 8).Select(i => "Combo" + i).Concat(["SliderTrackOverride", "SliderBorder"]).ToArray()
    };
    private sealed record SyncFieldRow(string Key, string Local, string External, bool LocalPresent, bool ExternalPresent, MetadataTextDiff Diff);
    private sealed record SyncReviewData(Dictionary<string, string> LocalSections, Dictionary<string, string> ExternalSections,
        Dictionary<string, SyncFieldRow[]> Fields, Dictionary<string, IReadOnlyList<SyncObjectDifference>> Objects, string[] Tabs);

    private static string SyncCategory(string key) => key.StartsWith("$objects:") ? "Objects"
        : key == "$audio" ? "Audio" : key.StartsWith("TimingPoints/") ? "Timing" : key.Split('/')[0];

    private static SyncReviewData PrepareSyncReview(WorkspaceMerge merge, Dictionary<string, IReadOnlyList<SyncObjectDifference>> objects)
    {
        var local = Sections(WorkspaceSynchronization.Fields(merge.Local));
        var external = Sections(WorkspaceSynchronization.Fields(merge.External.Document));
        var fields = new Dictionary<string, SyncFieldRow[]>();
        foreach (var section in SyncFieldOrder)
        {
            var left = Values(local.GetValueOrDefault(section.Key, ""));
            var right = Values(external.GetValueOrDefault(section.Key, ""));
            var keys = left.Keys.Union(right.Keys).Where(k => section.Key != "General" || k != "SampleSet")
                .OrderBy(k => Array.IndexOf(section.Value, k) is int index && index >= 0 ? index : int.MaxValue)
                .ThenBy(k => k, StringComparer.Ordinal);
            fields[section.Key] = keys.Select(k =>
            {
                string key = section.Key + "/" + k;
                var conflict = merge.Conflicts.FirstOrDefault(c => c.Key == key);
                string a = conflict?.Local ?? left.GetValueOrDefault(k, ""), b = conflict?.External ?? right.GetValueOrDefault(k, "");
                return new SyncFieldRow(key, a, b, left.ContainsKey(k), right.ContainsKey(k), MetadataTextDiff.Compare(a, b));
            }).ToArray();
        }
        var tabs = SyncTabOrder.Concat(merge.Conflicts.Select(c => SyncCategory(c.Key)))
            .Concat(local.Keys.Concat(external.Keys).Where(k => k is not ("" or "HitObjects" or "TimingPoints")))
            .Distinct().ToArray();
        return new(local, external, fields, objects, tabs);

        static Dictionary<string, string> Sections(Dictionary<string, string?> fields)
        {
            var sections = new Dictionary<string, List<string>>();
            foreach (var field in fields)
            {
                int slash = field.Key.IndexOf('/');
                string name = field.Key[..slash], key = field.Key[(slash + 1)..];
                sections.TryAdd(name, []);
                sections[name].Add(key.Length == 0 ? field.Value ?? "" : key + ":" + field.Value);
            }
            return sections.ToDictionary(s => s.Key, s => string.Join('\n', s.Value).TrimEnd('\n'));
        }
        static Dictionary<string, string> Values(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (string line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//")) continue;
                var pair = line.Split(':', 2);
                if (pair.Length == 2) result[pair[0].Trim()] = pair[1].Trim();
            }
            return result;
        }
    }

    private void DrawSyncTabs(ICanvas c, WorkspaceMerge merge, SyncComparison comparison)
    {
        float tabWidth = (width - 72) / comparison.Review.Tabs.Length;
        for (int i = 0; i < comparison.Review.Tabs.Length; i++)
        {
            string tab = comparison.Review.Tabs[i];
            var conflicts = merge.Conflicts.Where(item => SyncCategory(item.Key) == tab).ToArray();
            uint colour = conflicts.Length == 0 ? Muted : conflicts.All(item => syncChoices.ContainsKey(item.Key)) ? SyncResolvedThisRound
                : conflicts.Any(item => syncChoices.ContainsKey(item.Key)) ? SyncPreviouslyResolved : SyncUnresolved;
            var bounds = new Rect(36 + i * tabWidth, 83, tabWidth - 3, 30);
            c.Fill(bounds, tab == syncTab ? Surface : Panel, 4);
            c.Stroke(bounds, colour, tab == syncTab ? 2 : 1, 4);
            c.Text(tab, bounds.X + 5, bounds.Y + 7, 12, colour, bounds.Width - 10, tab == syncTab);
            hits.Add(new(bounds, () => SelectSyncTab(merge, tab), true));
        }
    }

    private void SelectSyncTab(WorkspaceMerge merge, string tab)
    {
        syncTab = tab; syncVisualKey = null; syncTextScroll = 0; syncSectionLoaded = null;
        int first = merge.Conflicts.FindIndex(c => SyncCategory(c.Key) == tab);
        if (first >= 0) syncRow = first;
    }
}
