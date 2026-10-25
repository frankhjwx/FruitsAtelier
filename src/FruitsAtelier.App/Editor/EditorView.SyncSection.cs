using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private int syncSectionPage;
    private (string Key, int Page)? syncSectionLoaded;
    private string syncSectionLocal = "", syncSectionExternal = "";
    private string syncBreakLocal = "", syncBreakExternal = "", syncEventLocal = "", syncEventExternal = "";
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
        string localText = conflict.Local, externalText = conflict.External;
        bool timing = conflict.Key == "TimingPoints/";
        bool events = conflict.Key == "Events/";
        if (!timing && conflict.TimingShift is { } shift)
        {
            localText = L.Get("sync.timingOffsetSummary", shift.Count, 0);
            externalText = L.Get("sync.timingOffsetSummary", shift.Count, shift.OffsetMs);
            if (shift.LocalRemainder.Length > 0) localText += "\n\n" + shift.LocalRemainder;
            if (shift.ExternalRemainder.Length > 0) externalText += "\n\n" + shift.ExternalRemainder;
        }
        if (!ReferenceEquals(syncVisualMerge, merge) || syncVisualKey != key)
        {
            syncVisualMerge = merge; syncVisualKey = key;
            syncSectionPage = 0; syncSectionLoaded = null;
            if (events)
            {
                var local = SplitSyncEvents(localText); var externalEvents = SplitSyncEvents(externalText);
                (syncBreakLocal, syncBreakExternal) = AlignSyncTiming(local.Breaks, externalEvents.Breaks);
                syncEventLocal = local.Other; syncEventExternal = externalEvents.Other;
            }
        }
        if (events) { localText = syncEventLocal; externalText = syncEventExternal; }
        int pages = timing ? 1 : Math.Max(1, (Math.Max(localText.Length, externalText.Length) + SyncSectionPageSize - 1) / SyncSectionPageSize);
        syncSectionPage = Math.Clamp(syncSectionPage, 0, pages - 1);
        if (syncSectionLoaded != (key, syncSectionPage))
        {
            syncSectionLocal = timing ? localText : SyncSectionSlice(localText, syncSectionPage);
            syncSectionExternal = timing ? externalText : SyncSectionSlice(externalText, syncSectionPage);
            if (timing) (syncSectionLocal, syncSectionExternal) = AlignSyncTiming(localText, externalText);
            syncSectionDiff = MetadataTextDiff.Compare(syncSectionLocal, syncSectionExternal);
            syncSectionLoaded = (key, syncSectionPage);
            syncTextScroll = 0; syncTextLayouts.Clear();
        }
        float x = 16, y = 16, w = width - 32, h = height - 32;
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get(merge.NativeDifficulty ? "fad.mergeTitle" : "sync.title"), x + 20, y + 16, 20, Foreground, w - 40, true);
        bool hasConflict = merge.Conflicts.Any(item => item.Key == conflict.Key);
        c.Text(SyncMapHeading(merge), x + 20, y + 46, 12, Muted, w - 40);
        uint colour = !hasConflict ? Muted : syncRoundChoices.Contains(conflict.Key) ? SyncResolvedThisRound
            : merge.PreviouslyResolved.Contains(conflict.Key) ? SyncPreviouslyResolved : SyncUnresolved;
        c.Text(timing ? L.Get("timing.title") : events && (syncBreakLocal.Length > 0 || syncBreakExternal.Length > 0) ? L.Get("sync.breaks") : conflict.Key == "$audio" ? L.Get("sync.audio") : conflict.Key.TrimEnd('/'), x + 20, y + 106, 15, colour, w - 400);
        if (!timing && (!events || pages > 1))
        {
        TimingButton(c, new(x + w - 336, y + 102, 32, 28), "«", () => syncSectionPage = 0, enabled: syncSectionPage > 0, flatArrow: true);
        TimingButton(c, new(x + w - 300, y + 102, 32, 28), "‹", () => syncSectionPage--, enabled: syncSectionPage > 0, flatArrow: true);
        c.Text(L.Get("sync.textPage", syncSectionPage + 1, pages), x + w - 260, y + 108, 12, Muted, 164);
        TimingButton(c, new(x + w - 88, y + 102, 32, 28), "›", () => syncSectionPage++, enabled: syncSectionPage + 1 < pages, flatArrow: true);
        TimingButton(c, new(x + w - 52, y + 102, 32, 28), "»", () => syncSectionPage = pages - 1, enabled: syncSectionPage + 1 < pages, flatArrow: true);
        }
        float paneWidth = (w - 52) / 2;
        bool dirty = difficulties.FirstOrDefault(d => d.Id == syncDifficulty)?.History.IsDirty == true;
        c.Text(L.Get("sync.localHeading"), x + 20, y + 138, 14, Foreground, paneWidth, true);
        c.Text(L.Get(merge.NativeDifficulty ? "fad.externalHeading" : "sync.externalHeading"), x + 32 + paneWidth, y + 138, 14, Foreground, paneWidth, true);
        c.Text(SavedLabel(comparison.LocalSaved, comparison.ExternalSaved, dirty), x + 20, y + 160, 11, Muted, paneWidth);
        c.Text(SavedLabel(comparison.ExternalSaved, comparison.LocalSaved, false), x + 32 + paneWidth, y + 160, 11, Muted, paneWidth);
        syncCanvasBounds = new(x + 20, y + 186, w - 40, Math.Max(23, h - 368));
        int lines = timing ? Math.Max(syncSectionLocal.Split('\n').Length, syncSectionExternal.Split('\n').Length) + 2
            : Math.Max(WrapSyncText(c, syncSectionLocal, paneWidth - 24).Length, WrapSyncText(c, syncSectionExternal, paneWidth - 24).Length);
        int breakRows = events && (syncBreakLocal.Length > 0 || syncBreakExternal.Length > 0) ? syncBreakLocal.Split('\n').Length : 0;
        float eventOffset = events ? 32 + Math.Max(1, breakRows) * 23 + (localText.Length > 0 || externalText.Length > 0 ? 38 : 0) : 0;
        float contentHeight = Math.Max(syncCanvasBounds.Height, eventOffset + 16 + lines * 23);
        syncTextMaxScroll = Math.Max(0, (int)Math.Ceiling(contentHeight - syncCanvasBounds.Height));
        syncTextScroll = Math.Clamp(syncTextScroll, 0, syncTextMaxScroll);
        var left = new Rect(x + 20, syncCanvasBounds.Y - syncTextScroll, paneWidth, contentHeight);
        var right = left with { X = left.Right + 12 };
        c.Clip(syncCanvasBounds);
        if (timing)
        {
            uint cellColour = !hasConflict ? Muted : syncChoices.ContainsKey(conflict.Key) || merge.PreviouslyResolved.Contains(conflict.Key)
                ? SyncResolvedThisRound : SyncUnresolved;
            DrawSyncTimingTable(c, left, syncSectionLocal, syncSectionExternal, cellColour);
            DrawSyncTimingTable(c, right, syncSectionExternal, syncSectionLocal, cellColour);
        }
        else
        {
            if (events)
            {
                DrawSyncBreakTable(c, left, syncBreakLocal, syncBreakExternal, colour);
                DrawSyncBreakTable(c, right, syncBreakExternal, syncBreakLocal, colour);
                if (localText.Length > 0 || externalText.Length > 0)
                {
                    c.Text(L.Get("sync.otherEvents"), left.X + 8, left.Y + eventOffset - 28, 12, Muted, paneWidth - 16);
                    c.Text(L.Get("sync.otherEvents"), right.X + 8, right.Y + eventOffset - 28, 12, Muted, paneWidth - 16);
                }
                left = left with { Y = left.Y + eventOffset, Height = contentHeight - eventOffset };
                right = right with { Y = right.Y + eventOffset, Height = contentHeight - eventOffset };
            }
            if (!events || localText.Length > 0 || externalText.Length > 0)
            {
                DrawSyncText(c, left, syncSectionLocal, colour, hasConflict ? syncSectionDiff.Local : []);
                DrawSyncText(c, right, syncSectionExternal, colour, hasConflict ? syncSectionDiff.External : []);
            }
        }
        c.Unclip();
        bool chosen = syncChoices.TryGetValue(conflict.Key, out bool external);
        if (chosen) c.Stroke(new(external ? right.X : left.X, syncCanvasBounds.Y, paneWidth, syncCanvasBounds.Height), Accent, 3);
        SyncReviewButton(c, new(left.X, y + h - 164, paneWidth, 36), L.Get("sync.chooseLocal"), () => ChooseSyncItem(merge, conflict.Key, false), chosen && !external, enabled: hasConflict);
        SyncReviewButton(c, new(right.X, y + h - 164, paneWidth, 36), L.Get(merge.NativeDifficulty ? "fad.chooseExternal" : "sync.chooseExternal"), () => ChooseSyncItem(merge, conflict.Key, true), chosen && external, enabled: hasConflict);
        DrawSyncReviewFooter(c, merge, x, y, w, h, showPreview: false);
    }

    internal static (string Breaks, string Other) SplitSyncEvents(string text)
    {
        var breaks = new List<string>(); var other = new List<string>();
        foreach (string line in text.Replace("\r", "").Split('\n'))
        {
            var fields = line.Trim().Split(',');
            if (fields.Length == 3 && fields[0].Trim() is "2" or "Break"
                && int.TryParse(fields[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int start)
                && int.TryParse(fields[2].Split("//", 2)[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int end))
                breaks.Add(FormattableString.Invariant($"{start},{end}"));
            else if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("//")) other.Add(line);
        }
        return (string.Join('\n', breaks), string.Join('\n', other));
    }

    private void DrawSyncBreakTable(ICanvas c, Rect bounds, string text, string other, uint colour)
    {
        c.Fill(bounds, Background);
        string[] headings = [L.Get("sync.breakStart"), L.Get("sync.breakEnd"), L.Get("sync.breakDuration")];
        float column = (bounds.Width - 16) / 3;
        for (int i = 0; i < 3; i++) c.Text(headings[i], bounds.X + 8 + i * column, bounds.Y + 6, 11, Muted, column - 4);
        var rows = text.Split('\n'); var opposite = other.Split('\n');
        if (text.Length == 0 && other.Length == 0) c.Text("—", bounds.X + 8, bounds.Y + 32, 12, Muted, column);
        for (int row = 0; row < rows.Length; row++)
        {
            var values = Values(rows[row]); var previous = Values(row < opposite.Length ? opposite[row] : "");
            float y = bounds.Y + 32 + row * 23;
            for (int i = 0; i < 3; i++)
            {
                bool changed = values[i] != previous[i];
                if (changed) c.Fill(new(bounds.X + 6 + i * column, y, column - 2, 21), colour, opacity: .22f);
                c.Text(values[i], bounds.X + 8 + i * column, y, 12, changed ? colour : Foreground, column - 4);
            }
        }

        static string[] Values(string line)
        {
            var fields = line.Split(',');
            if (fields.Length != 2) return ["—", "—", "—"];
            double start = double.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture);
            double end = double.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            return [SyncObjectDifferences.FormatTime(start), SyncObjectDifferences.FormatTime(end),
                (end - start).ToString("0", System.Globalization.CultureInfo.InvariantCulture)];
        }
    }

    private static (string, string) AlignSyncTiming(string local, string external)
    {
        static Dictionary<double, string[]> Rows(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .GroupBy(line => double.Parse(line.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var left = Rows(local); var right = Rows(external);
        var a = new List<string>(); var b = new List<string>();
        foreach (double time in left.Keys.Union(right.Keys).Order())
        {
            var ours = left.GetValueOrDefault(time) ?? []; var theirs = right.GetValueOrDefault(time) ?? [];
            for (int i = 0; i < Math.Max(ours.Length, theirs.Length); i++)
            { a.Add(i < ours.Length ? ours[i] : ""); b.Add(i < theirs.Length ? theirs[i] : ""); }
        }
        return (string.Join('\n', a), string.Join('\n', b));
    }

    private void DrawSyncTimingTable(ICanvas c, Rect bounds, string text, string other, uint conflictColour)
    {
        string[] headings = [L.Get("timing.offset"), L.Get("timing.bpm"), L.Get("timing.meter"),
            L.Get("timing.volumeShort"), L.Get("timing.sample"), L.Get("sync.timingIndex"), "Kiai", L.Get("sync.timingOmit")];
        c.Fill(bounds, Background);
        float column = (bounds.Width - 16) / headings.Length;
        for (int i = 0; i < headings.Length; i++) c.Text(headings[i], bounds.X + 8 + column * i, bounds.Y + 6, 11, Muted, column - 4);
        int row = 0;
        var otherRows = other.Split('\n');
        foreach (string line in text.Split('\n'))
        {
            float y = bounds.Y + 32 + row * 23;
            var opposite = row < otherRows.Length ? otherRows[row].Split(',') : [];
            row++;
            var fields = line.Split(',');
            if (fields.Length != 8) { c.Text("—", bounds.X + 8, y, 12, opposite.Length == 8 ? conflictColour : Muted, column); continue; }
            string bpm = fields[6] == "1" && double.TryParse(fields[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double beat) ? (60000 / beat).ToString("0.######") : "—";
            string[] values = [fields[0], bpm, fields[2], fields[5] + "%", fields[3] switch { "1" => L.Get("timing.normal"), "2" => L.Get("timing.soft"), "3" => L.Get("timing.drum"), _ => L.Get("timing.default") },
                fields[4], L.Get(int.TryParse(fields[7], out int effects) && (effects & 1) != 0 ? "inspector.on" : "inspector.off"), L.Get((effects & 8) != 0 ? "inspector.on" : "inspector.off")];
            int[] indices = [0, 1, 2, 5, 3, 4, 7, 7];
            for (int i = 0; i < values.Length; i++)
            {
                bool changed = opposite.Length != 8 || (i >= 6
                    ? (effects & (i == 6 ? 1 : 8)) != (int.Parse(opposite[7]) & (i == 6 ? 1 : 8))
                    : fields[indices[i]] != opposite[indices[i]]);
                if (changed) c.Fill(new(bounds.X + 6 + column * i, y, column - 2, 21), conflictColour, opacity: .22f);
                c.Text(values[i], bounds.X + 8 + column * i, y, 12, changed ? conflictColour : Foreground, column - 4);
            }
        }
    }
}
