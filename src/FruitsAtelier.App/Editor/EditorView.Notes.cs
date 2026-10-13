using System.Globalization;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string projectNotes = "", savedProjectNotes = "";
    private bool notesFocused, notesSelecting, notesRevealCaret;
    private float notesScroll;
    internal Rect NotesBounds { get; private set; }
    private bool NotesFocused => DockContentInputAvailable && notesFocused && PanelVisible(DockPanel.Notes) && !LibraryVisible && !TimingPageVisible;
    private readonly Stack<string> notesUndo = new(), notesRedo = new();
    private readonly List<(int Start, string Text, int[] Positions, float[] Widths)> notesLines = [];
    private (string Text, float Width, Type Canvas)? notesLayout;
    private void SetNotes(string value, bool record = true)
    {
        if (projectNotes == value) return;
        if (record)
        {
            if (notesUndo.Count >= 128)
            {
                var recent = notesUndo.Take(127).Reverse().ToArray(); notesUndo.Clear();
                foreach (string previous in recent) notesUndo.Push(previous);
            }
            notesUndo.Push(projectNotes); notesRedo.Clear();
        }
        projectNotes = value; notesRevealCaret = true;
    }
    private void InsertNotes(string text) => SetNotes(InsertInput("notes", projectNotes,
        text.Replace("\r\n", "\n").Replace('\r', '\n'), 65536));
    private void DrawNotesDock(ICanvas c, Rect bounds)
    {
        NotesBounds = bounds;
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, NotesFocused ? Accent : Grid, radius: 4);
        float lineWidth = Math.Max(16, bounds.Width - 16);
        if (notesLayout != (projectNotes, lineWidth, c.GetType()))
        {
            notesLayout = (projectNotes, lineWidth, c.GetType()); notesLines.Clear();
            int start = 0;
            var positions = new List<int> { 0 }; var widths = new List<float> { 0 };
            foreach (int index in StringInfo.ParseCombiningCharacters(projectNotes))
            {
                string glyph = StringInfo.GetNextTextElement(projectNotes, index);
                float glyphWidth = glyph == "\n" ? 0 : c.MeasureText(glyph, 12);
                if (glyph == "\n" || widths[^1] + glyphWidth > lineWidth && index > start)
                {
                    notesLines.Add((start, projectNotes[start..index], positions.ToArray(), widths.ToArray()));
                    start = glyph == "\n" ? index + 1 : index;
                    positions = [0]; widths = [0];
                }
                if (glyph != "\n") { positions.Add(index + glyph.Length - start); widths.Add(widths[^1] + glyphWidth); }
            }
            notesLines.Add((start, projectNotes[start..], positions.ToArray(), widths.ToArray()));
        }
        if (NotesFocused && notesRevealCaret)
        {
            int line = NotesCaretLine(); float top = line * 20;
            if (top < notesScroll) notesScroll = top;
            else if (top + 20 > notesScroll + bounds.Height - 16) notesScroll = top + 36 - bounds.Height;
            notesRevealCaret = false;
        }
        notesScroll = Math.Clamp(notesScroll, 0, Math.Max(0, notesLines.Count * 20 - bounds.Height + 16));
        c.Clip(new(bounds.X + 6, bounds.Y + 6, bounds.Width - 12, Math.Max(0, bounds.Height - 12)));
        if (projectNotes.Length == 0 && !NotesFocused) c.Text(L.Get("dock.notesHint"), bounds.X + 8, bounds.Y + 8, 12, Muted, lineWidth);
        for (int i = Math.Max(0, (int)(notesScroll / 20)); i < notesLines.Count; i++)
        {
            var line = notesLines[i]; float y = bounds.Y + 8 + i * 20 - notesScroll;
            if (y >= bounds.Bottom) break;
            float At(int offset) => line.Widths[Math.Clamp(Array.BinarySearch(line.Positions, Math.Clamp(offset, 0, line.Text.Length)), 0, line.Widths.Length - 1)];
            if (NotesFocused && textEditor.HasSelection && textEditor.End > line.Start && textEditor.Start <= line.Start + line.Text.Length)
            {
                float left = At(textEditor.Start - line.Start), right = At(textEditor.End - line.Start);
                c.Fill(new(bounds.X + 8 + left, y, Math.Max(2, right - left), 20), Accent, opacity: .25f);
            }
            c.Text(line.Text, bounds.X + 8, y, 12, Foreground, lineWidth);
            if (NotesFocused && i == NotesCaretLine())
            {
                float caret = bounds.X + 8 + At(textEditor.Caret - line.Start);
                c.Line(caret, y, caret, y + 17, Accent);
            }
        }
        c.Unclip();
    }
    private int NotesCaretLine()
    {
        int line = 0;
        for (int i = 1; i < notesLines.Count && notesLines[i].Start <= textEditor.Caret; i++) line = i;
        return line;
    }
    private void PositionNotesCaret(float x, float y, bool shift)
    {
        if (notesLines.Count == 0) return;
        var line = notesLines[Math.Clamp((int)((y - NotesBounds.Y - 8 + notesScroll) / 20), 0, notesLines.Count - 1)];
        float desired = x - NotesBounds.X - 8;
        int index = 0;
        while (index + 1 < line.Widths.Length && desired > (line.Widths[index] + line.Widths[index + 1]) / 2) index++;
        textEditor.Focus("notes", projectNotes, line.Start + line.Positions[index], shift); ResetTextCaret();
    }
    private bool NotesPointer(float x, float y, int button, bool shift)
    {
        if (menu >= 0 || contextItems.Count > 0) return false;
        if (!PanelVisible(DockPanel.Notes) || !NotesBounds.Contains(x, y)) { notesFocused = false; return false; }
        if (button == 0) { notesFocused = notesSelecting = true; PositionNotesCaret(x, y, shift); }
        return true;
    }
    private bool NotesKey(int key, bool ctrl, bool shift)
    {
        if (!NotesFocused || menu >= 0 || contextItems.Count > 0) return false;
        if (ctrl && key == 83) return false;
        if (key == 27) { notesFocused = false; return true; }
        if (ctrl && key is 90 or 89)
        {
            var source = key == 89 || shift ? notesRedo : notesUndo;
            var target = key == 89 || shift ? notesUndo : notesRedo;
            if (source.TryPop(out string? value))
            { target.Push(projectNotes); SetNotes(value, false); textEditor.Focus("notes", projectNotes, projectNotes.Length); }
        }
        else if (!ctrl && key == 13) InsertNotes("\n");
        else if (!ctrl && key == 9) InsertNotes("    ");
        else if (!ctrl && key is 38 or 40)
        {
            int current = NotesCaretLine(), next = Math.Clamp(current + (key == 38 ? -1 : 1), 0, notesLines.Count - 1);
            int column = textEditor.Caret - notesLines[current].Start;
            textEditor.Focus("notes", projectNotes, notesLines[next].Start + Math.Min(column, notesLines[next].Text.Length), shift);
        }
        else
        {
            string value = projectNotes;
            InputKey("notes", ref value, key, ctrl, shift, 65536, RequestPasteField); SetNotes(value);
        }
        notesRevealCaret = true; return true;
    }
}
