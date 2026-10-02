using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string versionTab = "Objects";
    private int versionObjectRow, versionTextScroll, versionTextMax, versionTextPage;
    private bool versionFocusPending, versionFieldFocusPending;
    private (string Tab, int Page)? versionSectionLoaded;
    private string versionSectionLeft = "", versionSectionRight = "";
    private MetadataTextDiff versionSectionDiff = new([], []);

    private void SelectVersionTab(string tab)
    {
        versionTab = tab; versionObjectRow = 0; versionTextScroll = versionTextPage = 0;
        versionSectionLoaded = null; syncTextLayouts.Clear();
        versionFieldFocusPending = true;
        FocusVersionObject();
    }

    private void FocusVersionObject()
    {
        versionFocusPending = true; versionZoom = 1; syncObjectDetailScroll = 0;
    }

    private void DrawVersionDiff(ICanvas c, Rect bounds)
    {
        var merge = versionMerge!; var comparison = versionComparison!;
        var review = comparison.Review;
        float tabWidth = bounds.Width / review.Tabs.Length;
        for (int i = 0; i < review.Tabs.Length; i++)
        {
            string tab = review.Tabs[i];
            uint colour = merge.Conflicts.Any(d => SyncCategory(d.Key) == tab) ? SyncUnresolved : Muted;
            var area = new Rect(bounds.X + i * tabWidth, bounds.Y, tabWidth - 3, 30);
            c.Fill(area, tab == versionTab ? Surface : Panel, 4);
            c.Stroke(area, colour, tab == versionTab ? 2 : 1, 4);
            c.Text(tab, area.X + 4, area.Y + 7, 11, colour, area.Width - 8, tab == versionTab);
            hits.Add(new(area, () => SelectVersionTab(tab), versionRestoreTask is null));
        }
        var body = new Rect(bounds.X, bounds.Y + 38, bounds.Width, bounds.Height - 38);
        if (versionTab == "Objects") { DrawVersionObjects(c, body, merge, comparison); return; }
        float paneWidth = (body.Width - 12) / 2;
        c.Text(L.Get("history.current"), body.X, body.Y, 14, Foreground, paneWidth, true);
        c.Text(L.Get("history.historical"), body.X + paneWidth + 12, body.Y, 14, Foreground, paneWidth, true);
        syncCanvasBounds = new(body.X, body.Y + 28, body.Width, body.Height - 28);
        if (review.Fields.TryGetValue(versionTab, out var rows))
        {
            if (versionFieldFocusPending)
            {
                float total = 0, firstChange = -1;
                foreach (var row in rows)
                {
                    if (firstChange < 0 && merge.Conflicts.Any(d => d.Key == row.Key)) firstChange = total;
                    string left = row.LocalPresent ? row.Local : L.Get("sync.absent"), right = row.ExternalPresent ? row.External : L.Get("sync.absent");
                    total += 52 + 23 * Math.Max(WrapSyncText(c, left, paneWidth - 24).Length, WrapSyncText(c, right, paneWidth - 24).Length);
                }
                versionTextScroll = (int)Math.Clamp(firstChange, 0, Math.Max(0, total - syncCanvasBounds.Height));
                versionFieldFocusPending = false;
            }
            float y = syncCanvasBounds.Y - versionTextScroll;
            c.Clip(syncCanvasBounds);
            foreach (var row in rows)
            {
                bool changed = merge.Conflicts.Any(d => d.Key == row.Key);
                uint colour = changed ? SyncUnresolved : Muted;
                string left = row.LocalPresent ? row.Local : L.Get("sync.absent"), right = row.ExternalPresent ? row.External : L.Get("sync.absent");
                int lines = Math.Max(WrapSyncText(c, left, paneWidth - 24).Length, WrapSyncText(c, right, paneWidth - 24).Length);
                float height = 52 + lines * 23;
                if (y + height > syncCanvasBounds.Y && y < syncCanvasBounds.Bottom)
                {
                    c.Text(row.Key[(row.Key.IndexOf('/') + 1)..], body.X, y, 13, colour, body.Width);
                    DrawSyncText(c, new(body.X, y + 24, paneWidth, height - 30), left, colour, changed ? row.Diff.Local : []);
                    DrawSyncText(c, new(body.X + paneWidth + 12, y + 24, paneWidth, height - 30), right, colour, changed ? row.Diff.External : []);
                }
                y += height;
            }
            c.Unclip();
            versionTextMax = Math.Max(0, (int)(y + versionTextScroll - syncCanvasBounds.Bottom));
            return;
        }
        string section = versionTab == "Timing" ? "TimingPoints" : versionTab;
        string a = review.LocalSections.GetValueOrDefault(section, ""), b = review.ExternalSections.GetValueOrDefault(section, "");
        if (versionTab == "Audio") { a = comparison.Local.Document.AudioPath ?? ""; b = comparison.External.Document.AudioPath ?? ""; }
        int pages = Math.Max(1, (Math.Max(a.Length, b.Length) + SyncSectionPageSize - 1) / SyncSectionPageSize);
        versionTextPage = Math.Clamp(versionTextPage, 0, pages - 1);
        if (versionSectionLoaded != (versionTab, versionTextPage))
        {
            versionSectionLeft = SyncSectionSlice(a, versionTextPage); versionSectionRight = SyncSectionSlice(b, versionTextPage);
            versionSectionDiff = MetadataTextDiff.Compare(versionSectionLeft, versionSectionRight);
            versionSectionLoaded = (versionTab, versionTextPage); versionTextScroll = 0; syncTextLayouts.Clear();
        }
        if (pages > 1)
        {
            TimingButton(c, new(body.X, body.Bottom - 28, 32, 26), "‹", () => versionTextPage--, enabled: versionTextPage > 0, flatArrow: true);
            c.Text(L.Get("sync.textPage", versionTextPage + 1, pages), body.X + 42, body.Bottom - 23, 12, Muted, body.Width - 84);
            TimingButton(c, new(body.Right - 32, body.Bottom - 28, 32, 26), "›", () => versionTextPage++, enabled: versionTextPage + 1 < pages, flatArrow: true);
            syncCanvasBounds = syncCanvasBounds with { Height = syncCanvasBounds.Height - 34 };
        }
        int lineCount = Math.Max(WrapSyncText(c, versionSectionLeft, paneWidth - 24).Length, WrapSyncText(c, versionSectionRight, paneWidth - 24).Length);
        float contentHeight = Math.Max(syncCanvasBounds.Height, 16 + lineCount * 23);
        versionTextMax = Math.Max(0, (int)(contentHeight - syncCanvasBounds.Height));
        uint textColour = merge.Conflicts.Any(d => SyncCategory(d.Key) == versionTab) ? SyncUnresolved : Muted;
        c.Clip(syncCanvasBounds);
        DrawSyncText(c, new(body.X, syncCanvasBounds.Y - versionTextScroll, paneWidth, contentHeight), versionSectionLeft, textColour, versionSectionDiff.Local);
        DrawSyncText(c, new(body.X + paneWidth + 12, syncCanvasBounds.Y - versionTextScroll, paneWidth, contentHeight), versionSectionRight, textColour, versionSectionDiff.External);
        c.Unclip();
    }

    private void DrawVersionObjects(ICanvas c, Rect bounds, WorkspaceMerge merge, SyncComparison comparison)
    {
        var groups = merge.Conflicts.Where(d => SyncCategory(d.Key) == "Objects").ToArray();
        versionObjectRow = Math.Clamp(versionObjectRow, 0, Math.Max(0, groups.Length - 1));
        var group = groups.ElementAtOrDefault(versionObjectRow);
        c.Text(group is null ? L.Get("ui.unchanged") : L.Get("history.difference", versionObjectRow + 1, groups.Length), bounds.X, bounds.Y + 5, 13, group is null ? Muted : SyncUnresolved, bounds.Width - 80);
        TimingButton(c, new(bounds.Right - 72, bounds.Y, 32, 26), "‹", () => { versionObjectRow--; FocusVersionObject(); }, enabled: versionObjectRow > 0, flatArrow: true);
        TimingButton(c, new(bounds.Right - 34, bounds.Y, 32, 26), "›", () => { versionObjectRow++; FocusVersionObject(); }, enabled: versionObjectRow + 1 < groups.Length, flatArrow: true);
        float detailsHeight = group is null ? 0 : Math.Min(74, Math.Max(28, bounds.Height * .24f));
        if (group is not null) DrawSyncObjectDetails(c, comparison.Review.Objects[group.Key], new(bounds.X, bounds.Y + 32, bounds.Width, detailsHeight));
        else syncObjectDetailBounds = default;
        float top = bounds.Y + 38 + detailsHeight, paneWidth = (bounds.Width - 12) / 2;
        var left = new Rect(bounds.X, top, paneWidth, bounds.Bottom - top);
        var focus = group is null ? null : comparison.Focus.GetValueOrDefault(group.Key);
        var field = SyncField(left);
        syncViewSpan = Math.Max(1, field.Height / (CatchScrollTiming.PixelsPerMs(Document.ApproachRate, field.Width) * versionZoom));
        if (versionFocusPending)
        {
            syncViewStart = Math.Max(0, focus is null ? viewStart : focus.Start - syncViewSpan * .35);
            versionFocusPending = false;
        }
        var ranges = comparison.Focus.Where(p => p.Key.StartsWith("$objects:")).Select(p => new SyncRange(p.Key, p.Value.Start, p.Value.End, SyncUnresolved, L.Get("history.changed"))).ToArray();
        void Select(string key) { versionObjectRow = Array.FindIndex(groups, g => g.Key == key); FocusVersionObject(); }
        syncShowValues = false;
        DrawSyncPane(c, comparison.Local, left, L.Get("history.current"), focus?.Local ?? noVersionSelection, null, L.Get("history.current"), ranges: ranges, currentKey: group?.Key, selectRange: Select);
        DrawSyncPane(c, comparison.External, left with { X = left.Right + 12 }, L.Get("history.historical"), focus?.External ?? noVersionSelection, null,
            versions[versionIndex].TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), ranges: ranges, currentKey: group?.Key, selectRange: Select);
    }
}
