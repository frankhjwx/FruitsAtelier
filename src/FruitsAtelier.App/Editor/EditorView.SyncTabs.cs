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

    private void DrawSyncObjectDetails(ICanvas c, IReadOnlyList<SyncObjectDifference> differences, Rect bounds)
    {
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
            c.Text(lines[syncObjectDetailScroll + i].Text, bounds.X + 8, bounds.Y + 3 + i * 23, 15, Foreground, bounds.Width - 20);
        c.Unclip();
        if (lines.Length > visible) c.Text("↕", bounds.Right - 14, bounds.Y + 4, 12, Muted, 14);
    }
    private static readonly string[] SyncTabOrder = ["General", "Editor", "Metadata", "Difficulty", "Events", "Timing", "Colours", "Objects"];
    private static readonly Dictionary<string, string[]> SyncFieldOrder = new()
    {
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

    private static SyncReviewData PrepareSyncReview(WorkspaceMerge merge)
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
        return new(local, external, fields, merge.Conflicts.Where(c => c.Key.StartsWith("$objects:"))
            .ToDictionary(c => c.Key, c => SyncObjectDifferences.Compare(c.Local, c.External)), tabs);

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
