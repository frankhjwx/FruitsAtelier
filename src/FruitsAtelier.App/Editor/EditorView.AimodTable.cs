using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private enum AimodColumn { Severity, Information, Time }
    private enum AimodGesture { None, Column, ResizeColumn, Scroll, Move, Resize }
    private readonly List<AimodColumn> aimodColumns = [AimodColumn.Severity, AimodColumn.Information, AimodColumn.Time];
    private readonly Dictionary<AimodColumn, float> aimodColumnWidths = new()
    { [AimodColumn.Severity] = 94, [AimodColumn.Information] = 540, [AimodColumn.Time] = 112 };
    private readonly List<(AimodColumn Column, Rect Bounds)> aimodHeaders = [];
    private readonly List<(int Index, Rect Bounds)> aimodRowBounds = [];
    private readonly HashSet<int> aimodSelected = [];
    private int aimodSelectionAnchor = -1;
    private AimodGesture aimodGesture;
    private AimodColumn aimodDragColumn;
    private float aimodDragX, aimodDragY, aimodOriginalWidth, aimodScrollX;
    private Rect aimodDragBounds, aimodScrollTrack;
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
        float total = aimodColumns.Sum(col => aimodColumnWidths[col]);
        aimodScrollX = Math.Clamp(aimodScrollX, 0, Math.Max(0, total - bounds.Width));
        aimodScrollTrack = total > bounds.Width ? new(bounds.X, bounds.Bottom - 12, bounds.Width, 12) : default;
        aimodListBounds = new(bounds.X, bounds.Y + 28, bounds.Width, Math.Max(0, bounds.Height - 28 - aimodScrollTrack.Height));
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
        aimodFirst = Math.Clamp(aimodFirst, 0, Math.Max(0, aimodRows.Count - 1));
        aimodVisibleRows = 0;
        float y = aimodListBounds.Y;
        for (int index = aimodFirst; index < aimodRows.Count && y < aimodListBounds.Bottom; index++)
        {
            var issue = aimodRows[index];
            var lines = WrapSyncText(c, issue.Message, aimodColumnWidths[AimodColumn.Information] - 16);
            float rowHeight = Math.Max(30, lines.Length * 19 + 10);
            if (aimodVisibleRows > 0 && y + rowHeight > aimodListBounds.Bottom) break;
            var row = new Rect(bounds.X, y, bounds.Width, Math.Min(rowHeight, aimodListBounds.Bottom - y));
            aimodRowBounds.Add((index, row)); aimodVisibleRows++;
            if (aimodSelected.Contains(index)) c.Fill(row, 0x31494B);
            else if ((index & 1) != 0) c.Fill(row, Panel);
            uint colour = issue.Severity == AimodSeverity.Error ? Error : issue.Severity == AimodSeverity.Warning ? 0xE3AD42u : Foreground;
            x = bounds.X - aimodScrollX;
            foreach (var column in aimodColumns)
            {
                float cw = aimodColumnWidths[column];
                c.Clip(new(x, y, cw, row.Height));
                if (column == AimodColumn.Information)
                    for (int line = 0; line < lines.Length; line++) c.Text(lines[line].Text, x + 8, y + 5 + line * 19, 15, colour, cw - 16);
                else c.Text(AimodCell(issue, column), x + 8, y + 7, 12, column == AimodColumn.Time ? Accent : colour, cw - 16);
                c.Unclip(); c.Line(x + cw, y, x + cw, y + row.Height, Grid);
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
        c.Stroke(bounds, Grid);
    }

    private bool AimodPointer(float x, float y, int button, bool shift, bool ctrl)
    {
        if (!AimodVisible && (!AimodDocked || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0)) return false;
        bool inside = AimodVisible || aimodPanelBounds.Contains(x, y);
        if (!inside) { aimodFocused = false; return false; }
        mouseX = x; mouseY = y; aimodFocused = true;
        if (button != 0) return true;
        aimodDragX = x; aimodDragY = y; aimodDragBounds = aimodFloatingBounds;
        if (AimodVisible && aimodResizeBounds.Contains(x, y)) { aimodGesture = AimodGesture.Resize; return true; }
        if (AimodVisible && aimodTitleBounds.Contains(x, y)) { aimodGesture = AimodGesture.Move; return true; }
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
            case AimodGesture.Move: aimodFloatingBounds = aimodDragBounds with { X = aimodDragBounds.X + x - aimodDragX, Y = aimodDragBounds.Y + y - aimodDragY }; break;
            case AimodGesture.Resize: aimodFloatingBounds = aimodDragBounds with { Width = Math.Max(480, aimodDragBounds.Width + x - aimodDragX), Height = Math.Max(360, aimodDragBounds.Height + y - aimodDragY) }; break;
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
        if (aimodGesture == AimodGesture.Move && !TimingPageVisible && DockDropSide(x, y) is { } left) AttachAimod(left);
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
        if (!AimodVisible && (!AimodDocked || !aimodFocused || !DockContentInputAvailable)) return false;
        if (key == 27 && aimodGesture != AimodGesture.None) { CancelAimodGesture(); return true; }
        if (key == 27) { if (AimodVisible) CloseAimod(); else aimodFocused = false; return true; }
        if (ctrl && key == 65 && !shift) { aimodSelected.Clear(); for (int i = 0; i < aimodRows.Count; i++) aimodSelected.Add(i); }
        else if (ctrl && key == 67) CopyAimodSelection();
        else if (ctrl && shift && key == 65) RefreshAimod();
        else if (key is 38 or 40 && aimodRows.Count > 0)
        {
            int current = aimodSelected.Count > 0 ? key == 38 ? aimodSelected.Min() : aimodSelected.Max() : aimodFirst;
            int next = Math.Clamp(current + (key == 38 ? -1 : 1), 0, aimodRows.Count - 1);
            SelectAimodRow(next, shift, ctrl);
            if (next < aimodFirst || next >= aimodFirst + aimodVisibleRows) aimodFirst = next;
        }
        else if (key == 13 && aimodSelected.Count > 0) LocateAimodIssue(aimodRows[aimodSelected.Min()]);
        else if (key == 116) RefreshAimod();
        return true;
    }

    private void CancelAimodGesture()
    {
        if (aimodGesture == AimodGesture.ResizeColumn) aimodColumnWidths[aimodDragColumn] = aimodOriginalWidth;
        if (aimodGesture is AimodGesture.Move or AimodGesture.Resize) aimodFloatingBounds = aimodDragBounds;
        aimodGesture = AimodGesture.None;
    }
}
