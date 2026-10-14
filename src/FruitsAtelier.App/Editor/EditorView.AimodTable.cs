using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum AimodColumn { Severity, Information, Time }
    private enum AimodGesture { None, Column, ResizeColumn, Scroll, VerticalScroll }
    private readonly List<AimodColumn> aimodColumns = [AimodColumn.Time, AimodColumn.Severity, AimodColumn.Information];
    private readonly Dictionary<AimodColumn, float> aimodColumnWidths = new()
    { [AimodColumn.Severity] = 70, [AimodColumn.Information] = 540, [AimodColumn.Time] = 94 };
    private bool aimodWidthsInitialized;
    private readonly List<(AimodColumn Column, Rect Bounds)> aimodHeaders = [];
    private readonly List<(int Index, Rect Bounds)> aimodRowBounds = [];
    private readonly HashSet<int> aimodSelected = [];
    private int aimodSelectionAnchor = -1;
    private AimodGesture aimodGesture;
    private AimodColumn aimodDragColumn;
    private float aimodDragX, aimodOriginalWidth, aimodScrollX;
    private Rect aimodScrollTrack, aimodVerticalTrack, aimodVerticalThumb;
    private float aimodScrollY, aimodVerticalGrab;
    private float[] aimodRowOffsets = [0];
    private SyncTextLine[][] aimodRowLayouts = [];
    private IReadOnlyList<AimodIssue>? aimodLayoutRows;
    private string aimodLayoutLanguage = "";
    private float aimodLayoutWidth;
    internal Rect AimodVerticalTrack => aimodVerticalTrack;
    internal Rect AimodHorizontalTrack => aimodScrollTrack;
    internal float AimodVerticalOffset => aimodScrollY;
    public bool AimodColumnResizeCursor => AimodDocked && DockContentInputAvailable && menu < 0 && contextItems.Count == 0
        && (aimodGesture == AimodGesture.ResizeColumn || aimodPanelBounds.Contains(mouseX, mouseY)
            && aimodHeaders.Any(h => mouseY >= h.Bounds.Y && mouseY < h.Bounds.Bottom && Math.Abs(mouseX - h.Bounds.Right) <= 5));

    private void LayoutAimodRows(ICanvas c)
    {
        float textWidth = aimodColumnWidths[AimodColumn.Information] - 16;
        if (ReferenceEquals(aimodLayoutRows, aimodRows) && aimodLayoutLanguage == L.Language && aimodLayoutWidth == textWidth) return;
        aimodLayoutRows = aimodRows; aimodLayoutLanguage = L.Language; aimodLayoutWidth = textWidth;
        aimodRowLayouts = new SyncTextLine[aimodRows.Count][];
        aimodRowOffsets = new float[aimodRows.Count + 1];
        for (int i = 0; i < aimodRows.Count; i++)
        {
            aimodRowLayouts[i] = WrapSyncText(c, aimodRows[i].Message, textWidth);
            aimodRowOffsets[i + 1] = aimodRowOffsets[i] + Math.Max(30, aimodRowLayouts[i].Length * 19 + 10);
        }
    }

    private void ScrollAimod(float delta) => aimodScrollY = Math.Clamp(aimodScrollY + delta, 0, Math.Max(0, aimodRowOffsets[^1] - aimodListBounds.Height));
    internal Rect AimodHeaderBounds(int index) => aimodHeaders[index].Bounds;
    internal Rect AimodRowBounds(int index) => aimodRowBounds[index].Bounds;

    private string AimodCell(AimodIssue issue, AimodColumn column) => column switch
    {
        AimodColumn.Severity => L.Get("aimod.severity" + issue.Severity),
        AimodColumn.Time => issue.TimeMs is double time ? Time(Math.Truncate(time)) : "",
        _ => issue.Message
    };

    private void DrawAimodTable(ICanvas c, Rect bounds)
    {
        if (!aimodWidthsInitialized)
        {
            aimodColumnWidths[AimodColumn.Information] = Math.Max(64, bounds.Width - 13
                - aimodColumnWidths[AimodColumn.Time] - aimodColumnWidths[AimodColumn.Severity]);
            aimodWidthsInitialized = true;
        }
        LayoutAimodRows(c);
        float total = aimodColumns.Sum(col => aimodColumnWidths[col]);
        var outer = bounds;
        bounds = bounds with { Width = Math.Max(0, bounds.Width - 13) };
        aimodScrollX = Math.Clamp(aimodScrollX, 0, Math.Max(0, total - bounds.Width));
        aimodScrollTrack = total > bounds.Width ? new(bounds.X, bounds.Bottom - 12, bounds.Width, 12) : default;
        aimodListBounds = new(bounds.X, bounds.Y + 28, bounds.Width, Math.Max(0, bounds.Height - 28 - aimodScrollTrack.Height));
        aimodVerticalTrack = new(bounds.Right, aimodListBounds.Y, 13, aimodListBounds.Height);
        ScrollAimod(0);
        float thumbHeight = Math.Min(aimodVerticalTrack.Height, Math.Max(18, aimodListBounds.Height * aimodListBounds.Height / Math.Max(1, aimodRowOffsets[^1])));
        float travel = aimodVerticalTrack.Height - thumbHeight;
        aimodVerticalThumb = new(bounds.Right + 3, aimodVerticalTrack.Y + travel * aimodScrollY / Math.Max(1, aimodRowOffsets[^1] - aimodListBounds.Height), 7, thumbHeight);
        aimodHeaders.Clear(); aimodRowBounds.Clear();
        c.Fill(bounds, Background); c.Clip(bounds);
        float x = bounds.X - aimodScrollX;
        foreach (var column in aimodColumns)
        {
            var header = new Rect(x, bounds.Y, aimodColumnWidths[column], 28);
            aimodHeaders.Add((column, header));
            c.Fill(header, Surface);
            c.Text(L.Get("aimod.column" + column), x + 8, bounds.Y + 6, 12, Foreground, header.Width - 16, true);
            c.Line(header.Right, header.Y, header.Right, header.Bottom, Grid);
            if (aimodGesture == AimodGesture.Column && aimodDragColumn == column) c.Stroke(header, Accent, 2);
            x = header.Right;
        }
        c.Unclip(); c.Clip(aimodListBounds);
        int first = Array.BinarySearch(aimodRowOffsets, aimodScrollY);
        aimodFirst = Math.Clamp(first < 0 ? ~first - 1 : first, 0, Math.Max(0, aimodRows.Count - 1));
        aimodVisibleRows = 0;
        float y = aimodListBounds.Y + aimodRowOffsets[aimodFirst] - aimodScrollY;
        for (int index = aimodFirst; index < aimodRows.Count && y < aimodListBounds.Bottom; index++)
        {
            var issue = aimodRows[index];
            var lines = aimodRowLayouts[index];
            float rowHeight = aimodRowOffsets[index + 1] - aimodRowOffsets[index];
            float top = Math.Max(y, aimodListBounds.Y);
            var row = new Rect(bounds.X, top, bounds.Width, Math.Max(0, Math.Min(y + rowHeight, aimodListBounds.Bottom) - top));
            aimodRowBounds.Add((index, row)); aimodVisibleRows++;
            if (aimodSelected.Contains(index)) c.Fill(row, 0x31494B);
            else if ((index & 1) != 0) c.Fill(row, Panel);
            uint colour = issue.Severity == AimodSeverity.Error ? Error : issue.Severity == AimodSeverity.Warning ? 0xE3AD42u : Foreground;
            x = bounds.X - aimodScrollX;
            foreach (var column in aimodColumns)
            {
                float cw = aimodColumnWidths[column];
                c.Clip(new(x, y, cw, rowHeight));
                if (column == AimodColumn.Information)
                    for (int line = 0; line < lines.Length; line++) c.Text(lines[line].Text, x + 8, y + 5 + line * 19, 15, colour, cw - 16);
                else c.Text(AimodCell(issue, column), x + 8, y + 7, 12, column == AimodColumn.Time ? Accent : colour, cw - 16);
                c.Unclip(); c.Line(x + cw, y, x + cw, y + rowHeight, Grid);
                x += cw;
            }
            c.Line(bounds.X, row.Bottom, bounds.Right, row.Bottom, Grid);
            y += rowHeight;
        }
        if (aimodRows.Count == 0) c.Text(L.Get(aimodSnapshot is null ? "aimod.refreshHint" : "aimod.empty"), bounds.X + 8, y + 12, 13, Muted, bounds.Width - 16);
        aimodVisibleRows = Math.Max(1, aimodVisibleRows);
        c.Unclip();
        if (aimodScrollTrack.Width > 0)
        {
            c.Fill(aimodScrollTrack, Surface);
            c.Fill(new(bounds.X + aimodScrollX / total * bounds.Width, aimodScrollTrack.Y + 3, bounds.Width * bounds.Width / total, 6), Muted, 3);
        }
        c.Fill(aimodVerticalTrack, Surface);
        if (aimodRowOffsets[^1] > aimodListBounds.Height) c.Fill(aimodVerticalThumb, Muted, 3);
        c.Stroke(outer, Grid);
    }

    private bool AimodPointer(float x, float y, int button, bool shift, bool ctrl)
    {
        if (!AimodDocked || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        bool inside = aimodPanelBounds.Contains(x, y);
        if (!inside) { aimodFocused = false; return false; }
        mouseX = x; mouseY = y; aimodFocused = true;
        if (button != 0) return true;
        aimodDragX = x;
        if (aimodVerticalTrack.Contains(x, y))
        {
            aimodVerticalGrab = aimodVerticalThumb.Contains(x, y) ? y - aimodVerticalThumb.Y : aimodVerticalThumb.Height / 2;
            aimodGesture = AimodGesture.VerticalScroll; MoveAimodPointer(x, y); return true;
        }
        if (aimodScrollTrack.Contains(x, y)) { aimodGesture = AimodGesture.Scroll; MoveAimodPointer(x, y); return true; }
        if (aimodPanelBounds.Contains(x, y))
        {
            foreach (var header in aimodHeaders)
            {
                if (y < header.Bounds.Y || y >= header.Bounds.Bottom) continue;
                if (Math.Abs(x - header.Bounds.Right) <= 5)
                {
                    aimodDragColumn = header.Column; aimodOriginalWidth = header.Bounds.Width;
                    aimodGesture = AimodGesture.ResizeColumn; return true;
                }
            }
            foreach (var header in aimodHeaders)
                if (header.Bounds.Contains(x, y)) { aimodDragColumn = header.Column; aimodGesture = AimodGesture.Column; return true; }
            foreach (var row in aimodRowBounds)
            {
                if (!row.Bounds.Contains(x, y)) continue;
                var time = aimodHeaders.First(h => h.Column == AimodColumn.Time).Bounds;
                if (!ctrl && !shift && x >= time.X && x < time.Right && aimodRows[row.Index].TimeMs is not null)
                { LocateAimodIssue(aimodRows[row.Index]); return true; }
                SelectAimodRow(row.Index, shift, ctrl); return true;
            }
        }
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
        return true;
    }

    private void SelectAimodRow(int index, bool shift, bool ctrl)
    {
        if (!ctrl) aimodSelected.Clear();
        if (shift && aimodSelectionAnchor >= 0)
            for (int i = Math.Min(index, aimodSelectionAnchor); i <= Math.Max(index, aimodSelectionAnchor); i++) aimodSelected.Add(i);
        else
        {
            if (!ctrl || !aimodSelected.Remove(index)) aimodSelected.Add(index);
            aimodSelectionAnchor = index;
        }
    }

    private void MoveAimodPointer(float x, float y)
    {
        mouseX = x; mouseY = y;
        switch (aimodGesture)
        {
            case AimodGesture.ResizeColumn: aimodColumnWidths[aimodDragColumn] = Math.Clamp(aimodOriginalWidth + x - aimodDragX, 64, 2000); break;
            case AimodGesture.VerticalScroll:
                float range = Math.Max(0, aimodRowOffsets[^1] - aimodListBounds.Height);
                aimodScrollY = Math.Clamp((y - aimodVerticalTrack.Y - aimodVerticalGrab) / Math.Max(1, aimodVerticalTrack.Height - aimodVerticalThumb.Height) * range, 0, range);
                break;
            case AimodGesture.Scroll:
                float total = aimodColumns.Sum(col => aimodColumnWidths[col]);
                aimodScrollX = Math.Clamp((x - aimodScrollTrack.X) / aimodScrollTrack.Width * total - aimodScrollTrack.Width / 2, 0, total - aimodScrollTrack.Width);
                break;
        }
    }

    private void EndAimodPointer(float x, float y)
    {
        MoveAimodPointer(x, y);
        if (aimodGesture == AimodGesture.Column && Math.Abs(x - aimodDragX) > 4)
        {
            int target = aimodHeaders.Count(h => h.Column != aimodDragColumn && x > h.Bounds.X + h.Bounds.Width / 2);
            aimodColumns.Remove(aimodDragColumn); aimodColumns.Insert(target, aimodDragColumn);
        }
        aimodGesture = AimodGesture.None;
    }

    private void CopyAimodSelection()
    {
        if (aimodSelected.Count == 0) return;
        static string Tsv(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        string header = string.Join('\t', aimodColumns.Select(col => L.Get("aimod.column" + col)));
        string rows = string.Join(Environment.NewLine, aimodSelected.Order().Where(i => i < aimodRows.Count)
            .Select(i => string.Join('\t', aimodColumns.Select(col => Tsv(AimodCell(aimodRows[i], col))))));
        RequestCopyText?.Invoke(header + Environment.NewLine + rows);
    }

    private bool AimodKey(int key, bool ctrl, bool shift)
    {
        if (!AimodDocked || !aimodFocused || !DockContentInputAvailable) return false;
        if (key == 27 && aimodGesture != AimodGesture.None) { CancelAimodGesture(); return true; }
        if (key == 27) { aimodFocused = false; return true; }
        if (ctrl && key == 65 && !shift) { aimodSelected.Clear(); for (int i = 0; i < aimodRows.Count; i++) aimodSelected.Add(i); }
        else if (ctrl && key == 67) CopyAimodSelection();
        else if (ctrl && shift && key == 65) RefreshAimod();
        else if (key is 38 or 40 && aimodRows.Count > 0)
        {
            int current = aimodSelected.Count > 0 ? key == 38 ? aimodSelected.Min() : aimodSelected.Max() : aimodFirst;
            int next = Math.Clamp(current + (key == 38 ? -1 : 1), 0, aimodRows.Count - 1);
            SelectAimodRow(next, shift, ctrl);
            if (next < aimodRowOffsets.Length - 1)
            {
                if (aimodRowOffsets[next] < aimodScrollY) aimodScrollY = aimodRowOffsets[next];
                else if (aimodRowOffsets[next + 1] > aimodScrollY + aimodListBounds.Height) aimodScrollY = Math.Max(0, aimodRowOffsets[next + 1] - aimodListBounds.Height);
            }
        }
        else if (key == 13 && aimodSelected.Count > 0) LocateAimodIssue(aimodRows[aimodSelected.Min()]);
        else if (key == 116) RefreshAimod();
        return true;
    }

    private void CancelAimodGesture()
    {
        if (aimodGesture == AimodGesture.ResizeColumn) aimodColumnWidths[aimodDragColumn] = aimodOriginalWidth;
        aimodGesture = AimodGesture.None;
    }
}
