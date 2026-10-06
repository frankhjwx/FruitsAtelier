using System.Globalization;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly List<(int Start, int End, int[] Positions, float[] Widths)> songTagRows = [];
    private string? songTagText;
    private float songTagWidth, songTagSize = 13;
    private float songTagWindowHeight;
    private Rect songTagContent;
    private float SongSetupHeight => Math.Min(Math.Max(580, songTab == 0 ? 500 + songTagRows.Count * (songTagSize + 5) : 580), height - 32);

    private void PrepareSongTags(ICanvas c)
    {
        string value = songValues.GetValueOrDefault("Tags", "");
        float availableWidth = Math.Max(20, Math.Min(840, width - 32) - 280);
        if (songTagText == value && songTagWidth == availableWidth && songTagWindowHeight == height) return;
        songTagText = value; songTagWidth = availableWidth; songTagWindowHeight = height;
        songTagSize = 13;
        Wrap();
        while (songTagSize > 10 && 500 + songTagRows.Count * (songTagSize + 5) > height - 32)
        { songTagSize--; Wrap(); }

        void Wrap()
        {
            songTagRows.Clear();
            int[] positions = [.. StringInfo.ParseCombiningCharacters(value), value.Length];
            int first = 0;
            while (first < positions.Length - 1)
            {
                int last = first + 1, space = -1;
                while (last < positions.Length && c.MeasureText(value[positions[first]..positions[last]], songTagSize) <= availableWidth)
                {
                    if (char.IsWhiteSpace(value[positions[last] - 1])) space = last;
                    last++;
                }
                int end = Math.Max(first + 1, last - 1);
                if (last < positions.Length && space > first) end = space;
                int[] rowPositions = positions[first..(end + 1)];
                float[] widths = rowPositions.Select(p => c.MeasureText(value[positions[first]..p], songTagSize)).ToArray();
                songTagRows.Add((positions[first], positions[end], rowPositions, widths));
                first = end;
            }
            if (songTagRows.Count == 0) songTagRows.Add((0, 0, [0], [0]));
        }
    }

    private void DrawSongTags(ICanvas c, Rect box, bool focused)
    {
        songTagContent = new(box.X + 9, box.Y + 7, box.Width - 18, box.Height - 14);
        c.Clip(songTagContent);
        int caret = focused && textEditor.Field == "song:Tags" ? textEditor.Caret : -1;
        for (int i = 0; i < songTagRows.Count; i++)
        {
            var row = songTagRows[i];
            float y = songTagContent.Y + i * (songTagSize + 5);
            float X(int index) => c.MeasureText(songTagText![row.Start..Math.Clamp(index, row.Start, row.End)], songTagSize);
            if (focused && textEditor.Field == "song:Tags" && textEditor.HasSelection)
            {
                int from = Math.Max(row.Start, textEditor.Start), to = Math.Min(row.End, textEditor.End);
                if (to > from) c.Fill(new(songTagContent.X + X(from), y, X(to) - X(from), songTagSize + 5), 0x365D77);
            }
            c.Text(songTagText![row.Start..row.End], songTagContent.X, y, songTagSize, Foreground, songTagContent.Width);
            if (caret >= row.Start && (caret < row.End || i == songTagRows.Count - 1) && !textEditor.HasSelection)
            {
                paintedCaret = CaretVisible;
                if (paintedCaret) c.Line(songTagContent.X + X(caret), y + 1, songTagContent.X + X(caret), y + songTagSize + 4, Foreground);
            }
        }
        c.Unclip();
    }

    private void FocusSongTags(string value, float x, float y, bool shift)
    {
        if (songTagRows.Count == 0) return;
        int rowIndex = Math.Clamp((int)((y - songTagContent.Y) / (songTagSize + 5)), 0, songTagRows.Count - 1);
        var row = songTagRows[rowIndex];
        int position = row.End;
        float target = x - songTagContent.X;
        for (int i = 1; i < row.Positions.Length; i++)
            if (target < (row.Widths[i - 1] + row.Widths[i]) / 2) { position = row.Positions[i - 1]; break; }
        textEditor.Focus("song:Tags", value, position, shift);
        textSelecting = true; ResetTextCaret();
    }
}
