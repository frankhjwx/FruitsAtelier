using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int syncSectionPage;
    private (string Key, int Page)? syncSectionLoaded;
    private string syncSectionLocal = "", syncSectionExternal = "";
    private MetadataTextDiff syncSectionDiff = new([], []);
    private const int SyncSectionPageSize = 4096;

    internal static string SyncSectionSlice(string value, int page)
    {
        int start = Math.Min(value.Length, page * SyncSectionPageSize);
        int end = Math.Min(value.Length, start + SyncSectionPageSize);
        // Keep UTF-16 pairs intact across page boundaries without omitting text.
        if (start > 0 && start < value.Length && char.IsLowSurrogate(value[start]) && char.IsHighSurrogate(value[start - 1])) start--;
        if (end > 0 && end < value.Length && char.IsLowSurrogate(value[end]) && char.IsHighSurrogate(value[end - 1])) end--;
        return value[start..end].Replace("\r", "");
    }

    private void DrawSyncSection(ICanvas c, WorkspaceMerge merge, SyncComparison comparison, WorkspaceSyncConflict conflict)
    {
        string key = "$text:" + conflict.Key;
        if (!ReferenceEquals(syncVisualMerge, merge) || syncVisualKey != key)
        {
            syncVisualMerge = merge; syncVisualKey = key;
            syncSectionPage = 0; syncSectionLoaded = null;
        }
        int pages = Math.Max(1, (Math.Max(conflict.Local.Length, conflict.External.Length) + SyncSectionPageSize - 1) / SyncSectionPageSize);
        syncSectionPage = Math.Clamp(syncSectionPage, 0, pages - 1);
        if (syncSectionLoaded != (key, syncSectionPage))
        {
            syncSectionLocal = SyncSectionSlice(conflict.Local, syncSectionPage);
            syncSectionExternal = SyncSectionSlice(conflict.External, syncSectionPage);
            syncSectionDiff = MetadataTextDiff.Compare(syncSectionLocal, syncSectionExternal);
            syncSectionLoaded = (key, syncSectionPage);
            syncTextScroll = 0; syncTextLayouts.Clear();
        }
        float x = 16, y = 16, w = width - 32, h = height - 32;
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("sync.title"), x + 20, y + 16, 20, Foreground, w - 40, true);
        c.Text(L.Get("sync.sectionHelp"), x + 20, y + 46, 12, Muted, w - 40);
        uint colour = syncRoundChoices.Contains(conflict.Key) ? SyncResolvedThisRound
            : merge.PreviouslyResolved.Contains(conflict.Key) ? SyncPreviouslyResolved : SyncUnresolved;
        c.Text(conflict.Key == "$audio" ? L.Get("sync.audio") : conflict.Key, x + 20, y + 70, 15, colour, w - 400);
        TimingButton(c, new(x + w - 336, y + 66, 32, 28), "«", () => syncSectionPage = 0, enabled: syncSectionPage > 0, flatArrow: true);
        TimingButton(c, new(x + w - 300, y + 66, 32, 28), "‹", () => syncSectionPage--, enabled: syncSectionPage > 0, flatArrow: true);
        c.Text(L.Get("sync.textPage", syncSectionPage + 1, pages), x + w - 260, y + 72, 12, Muted, 164);
        TimingButton(c, new(x + w - 88, y + 66, 32, 28), "›", () => syncSectionPage++, enabled: syncSectionPage + 1 < pages, flatArrow: true);
        TimingButton(c, new(x + w - 52, y + 66, 32, 28), "»", () => syncSectionPage = pages - 1, enabled: syncSectionPage + 1 < pages, flatArrow: true);
        float paneWidth = (w - 52) / 2;
        bool dirty = difficulties.FirstOrDefault(d => d.Id == syncDifficulty)?.History.IsDirty == true;
        c.Text(L.Get("sync.localHeading"), x + 20, y + 102, 14, Foreground, paneWidth, true);
        c.Text(L.Get("sync.externalHeading"), x + 32 + paneWidth, y + 102, 14, Foreground, paneWidth, true);
        c.Text(SavedLabel(comparison.LocalSaved, comparison.ExternalSaved, dirty), x + 20, y + 124, 11, Muted, paneWidth);
        c.Text(SavedLabel(comparison.ExternalSaved, comparison.LocalSaved, false), x + 32 + paneWidth, y + 124, 11, Muted, paneWidth);
        syncCanvasBounds = new(x + 20, y + 150, w - 40, Math.Max(23, h - 332));
        int lines = Math.Max(WrapSyncText(c, syncSectionLocal, paneWidth - 24).Length, WrapSyncText(c, syncSectionExternal, paneWidth - 24).Length);
        float contentHeight = Math.Max(syncCanvasBounds.Height, 16 + lines * 23);
        syncTextMaxScroll = Math.Max(0, (int)Math.Ceiling(contentHeight - syncCanvasBounds.Height));
        syncTextScroll = Math.Clamp(syncTextScroll, 0, syncTextMaxScroll);
        var left = new Rect(x + 20, syncCanvasBounds.Y - syncTextScroll, paneWidth, contentHeight);
        var right = left with { X = left.Right + 12 };
        c.Clip(syncCanvasBounds);
        DrawSyncText(c, left, syncSectionLocal, colour, syncSectionDiff.Local);
        DrawSyncText(c, right, syncSectionExternal, colour, syncSectionDiff.External);
        c.Unclip();
        bool chosen = syncChoices.TryGetValue(conflict.Key, out bool external);
        if (chosen) c.Stroke(new(external ? right.X : left.X, syncCanvasBounds.Y, paneWidth, syncCanvasBounds.Height), Accent, 3);
        SyncReviewButton(c, new(left.X, y + h - 164, paneWidth, 36), L.Get("sync.chooseLocal"), () => ChooseSyncItem(merge, conflict.Key, false), chosen && !external);
        SyncReviewButton(c, new(right.X, y + h - 164, paneWidth, 36), L.Get("sync.chooseExternal"), () => ChooseSyncItem(merge, conflict.Key, true), chosen && external);
        DrawSyncReviewFooter(c, merge, x, y, w, h, showPreview: false);
    }
}
