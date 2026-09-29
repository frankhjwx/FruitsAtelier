using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int syncTextScroll, syncTextMaxScroll;
    private readonly record struct SyncTextLine(int Start, string Text);
    private readonly Dictionary<(string Value, float Width), SyncTextLine[]> syncTextLayouts = [];

    private void DrawSyncMetadata(ICanvas c, WorkspaceMerge merge, SyncComparison comparison, WorkspaceSyncConflict conflict)
    {
        if (!ReferenceEquals(syncVisualMerge, merge) || syncVisualKey != "$metadata")
        { syncVisualMerge = merge; syncVisualKey = "$metadata"; syncTextScroll = 0; syncTextLayouts.Clear(); }
        float x = 16, y = 16, w = width - 32, h = height - 32;
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("sync.title"), x + 20, y + 16, 20, Foreground, w - 40, true);
        c.Text(L.Get("sync.metadataHelp"), x + 20, y + 46, 12, Muted, w - 40);
        float paneWidth = (w - 52) / 2, leftX = x + 20, rightX = leftX + paneWidth + 12;
        bool dirty = difficulties.FirstOrDefault(d => d.Id == syncDifficulty)?.History.IsDirty == true;
        c.Text(L.Get("sync.localHeading"), leftX + 12, y + 72, 14, Foreground, paneWidth, true);
        c.Text(L.Get("sync.externalHeading"), rightX + 12, y + 72, 14, Foreground, paneWidth, true);
        c.Text(SavedLabel(comparison.LocalSaved, comparison.ExternalSaved, dirty), leftX + 12, y + 94, 11, Muted, paneWidth - 24);
        c.Text(SavedLabel(comparison.ExternalSaved, comparison.LocalSaved, false), rightX + 12, y + 94, 11, Muted, paneWidth - 24);
        syncCanvasBounds = new(leftX, y + 120, w - 40, h - 236);
        float rowY = syncCanvasBounds.Y - syncTextScroll;
        c.Clip(syncCanvasBounds);
        foreach (var item in merge.Conflicts.Where(item => WorkspaceSynchronization.IsMetadataField(item.Key)))
        {
            uint colour = syncRoundChoices.Contains(item.Key) ? SyncResolvedThisRound
                : merge.PreviouslyResolved.Contains(item.Key) ? SyncPreviouslyResolved : SyncUnresolved;
            string status = syncRoundChoices.Contains(item.Key) ? L.Get("sync.resolvedThisRound")
                : merge.PreviouslyResolved.Contains(item.Key) ? L.Get("sync.alreadyResolved") : L.Get("sync.unresolvedRange");
            int lines = Math.Max(WrapSyncText(c, item.Local.Replace("\r", ""), paneWidth - 24).Length,
                WrapSyncText(c, item.External.Replace("\r", ""), paneWidth - 24).Length);
            float rowHeight = 68 + lines * 23;
            if (rowY + rowHeight > syncCanvasBounds.Y && rowY < syncCanvasBounds.Bottom)
            {
                c.Text(item.Key[9..] + " · " + status, leftX, rowY, 14, colour, w - 40);
                var left = new Rect(leftX, rowY + 26, paneWidth, rowHeight - 34);
                var right = left with { X = rightX };
                var differences = comparison.TextDifferences[item.Key];
                DrawSyncText(c, left, item.Local, colour, differences.Local);
                DrawSyncText(c, right, item.External, colour, differences.External);
                bool chosen = syncChoices.TryGetValue(item.Key, out bool external);
                if (chosen)
                {
                    c.Stroke(external ? left : right, Grid);
                    c.Stroke(external ? right : left, Accent, 3);
                }
                AddChoice(left, false); AddChoice(right, true);
                void AddChoice(Rect bounds, bool useExternal)
                {
                    float top = Math.Max(bounds.Y, syncCanvasBounds.Y), bottom = Math.Min(bounds.Bottom, syncCanvasBounds.Bottom);
                    if (bottom > top) hits.Add(new(bounds with { Y = top, Height = bottom - top }, () =>
                    {
                        syncChoices[item.Key] = useExternal; syncRoundChoices.Add(item.Key);
                        syncPreviewRevision++; syncResultPane = null;
                    }, true));
                }
            }
            rowY += rowHeight;
        }
        c.Unclip();
        syncTextMaxScroll = Math.Max(0, (int)(rowY + syncTextScroll - syncCanvasBounds.Bottom));
        c.Text(L.Get("sync.reviewProgress", syncChoices.Count, merge.Conflicts.Count), x + 20, y + h - 102, 12, Muted, w - 40);
        SyncComparisonNavigation(c, merge, x, y + h - 80);
        Button(c, new(x + w - 240, y + h - 80, 220, 30), L.Get("sync.inspect"), () => InspectSync(merge));
        Button(c, new(x + 20, y + h - 40, 200, 30), L.Get("sync.allLocal"), () => ResolveSync(false));
        Button(c, new(x + 232, y + h - 40, 200, 30), L.Get("sync.allExternal"), () => ResolveSync(true));
        Button(c, new(x + w - 360, y + h - 40, 200, 30), L.Get("sync.applyChoices"), () => ResolveSync(null), enabled: merge.Conflicts.All(k => syncChoices.ContainsKey(k.Key)));
        Button(c, new(x + w - 140, y + h - 40, 120, 30), L.Get("mac.cancel"), CancelSynchronization);
    }

    private SyncTextLine[] WrapSyncText(ICanvas c, string value, float textWidth)
    {
        if (syncTextLayouts.TryGetValue((value, textWidth), out var lines)) return lines;
        var wrapped = new List<SyncTextLine>(); int paragraphStart = 0;
        foreach (string paragraph in value.Split('\n'))
        {
            int offset = 0;
            do
            {
                int low = 1, high = Math.Min(512, paragraph.Length - offset);
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    if (c.MeasureText(paragraph.Substring(offset, mid), 15) <= textWidth) low = mid; else high = mid - 1;
                }
                int count = Math.Min(low, paragraph.Length - offset);
                if (count > 1 && char.IsHighSurrogate(paragraph[offset + count - 1])) count--;
                if (count == 1 && char.IsHighSurrogate(paragraph[offset]) && offset + 1 < paragraph.Length) count = 2;
                wrapped.Add(new(paragraphStart + offset, paragraph.Substring(offset, count))); offset += count;
            } while (offset < paragraph.Length);
            paragraphStart += paragraph.Length + 1;
        }
        lines = wrapped.ToArray();
        if (syncTextLayouts.Count >= 64) syncTextLayouts.Clear();
        syncTextLayouts[(value, textWidth)] = lines;
        return lines;
    }

    private void DrawSyncText(ICanvas c, Rect bounds, string value, uint colour, IReadOnlyList<MetadataTextSpan> differences)
    {
        c.Fill(bounds, Background); c.Stroke(bounds, colour);
        var body = new Rect(bounds.X + 12, bounds.Y + 8, bounds.Width - 24, bounds.Height - 16);
        value = value.Replace("\r", "");
        var lines = WrapSyncText(c, value, body.Width);
        if (value.Length == 0) c.Text(L.Get("sync.emptyValue"), body.X, body.Y, 14, differences.Count > 0 ? colour : Muted, body.Width);
        else for (int line = 0; line < lines.Length; line++)
        {
            var row = lines[line]; float textY = body.Y + line * 23;
            if (textY + 23 < syncCanvasBounds.Y || textY > syncCanvasBounds.Bottom) continue;
            c.Text(row.Text, body.X, textY, 15, Foreground, body.Width);
            foreach (var span in differences)
            {
                int start = Math.Max(span.Start, row.Start), end = Math.Min(span.Start + span.Length, row.Start + row.Text.Length);
                if (start > end || end == start && span.Length > 0) continue;
                if (span.Length == 0 && span.Start == row.Start + row.Text.Length && line + 1 < lines.Length && lines[line + 1].Start == span.Start) continue;
                string prefix = row.Text[..(start - row.Start)], text = row.Text.Substring(start - row.Start, end - start);
                float textX = body.X + c.MeasureText(prefix, 15), highlightWidth = Math.Max(2, c.MeasureText(text, 15));
                c.Fill(new(textX, textY, highlightWidth, 21), colour, opacity: text.Length == 0 ? 1 : .22f);
                if (text.Length > 0) c.Text(text, textX, textY, 15, colour, highlightWidth + 2);
            }
        }
    }

    private void SyncComparisonNavigation(ICanvas c, WorkspaceMerge merge, float x, float y)
    {
        var pages = new List<int>(); bool metadataAdded = false;
        for (int i = 0; i < merge.Conflicts.Count; i++)
        {
            if (WorkspaceSynchronization.IsMetadataField(merge.Conflicts[i].Key))
            { if (metadataAdded) continue; metadataAdded = true; }
            pages.Add(i);
        }
        int current = WorkspaceSynchronization.IsMetadataField(merge.Conflicts[syncRow].Key)
            ? pages.FindIndex(i => WorkspaceSynchronization.IsMetadataField(merge.Conflicts[i].Key)) : pages.IndexOf(syncRow);
        Button(c, new(x + 20, y, 40, 30), "\u2039", () => syncRow = pages[current - 1], enabled: current > 0);
        c.Text(L.Get("sync.page", current + 1, pages.Count), x + 100, y + 8, 12, Muted, 100);
        Button(c, new(x + 220, y, 40, 30), "\u203a", () => syncRow = pages[current + 1], enabled: current + 1 < pages.Count);
    }
}
