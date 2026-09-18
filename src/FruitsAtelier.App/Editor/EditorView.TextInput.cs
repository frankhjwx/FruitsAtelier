using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private long caretEpoch = Environment.TickCount64;
    private bool textInputFocused = true, paintedCaret;
    private bool CaretVisible => textInputFocused && (Environment.TickCount64 - caretEpoch) % 1000 < 500;
    public bool TextCaretNeedsRedraw => IsEditingText && textInputFocused
        && (!(LibraryTextFocused ? libraryReplace : replaceText) || (LibraryTextFocused ? LibraryFieldValue : editBuffer).Length == 0)
        && paintedCaret != CaretVisible;

    public void SetTextInputFocus(bool focused)
    {
        textInputFocused = focused;
        ResetTextCaret();
    }

    private void ResetTextCaret() => caretEpoch = Environment.TickCount64;

    private void DrawInputText(ICanvas c, Rect content, string value, float size, bool focused, bool selectAll)
    {
        float textWidth = c.MeasureText(value, size);
        float left = content.X - (focused ? Math.Max(0, textWidth - content.Width + 2) : 0);
        c.Clip(content);
        if (focused && selectAll && value.Length > 0)
            c.Fill(new(left, content.Y, textWidth, content.Height), textInputFocused ? 0x365D77u : Grid);
        c.Text(value, left, content.Y, size, Foreground, Math.Max(content.Width, textWidth + 2));
        if (focused)
        {
            paintedCaret = CaretVisible;
            if ((!selectAll || value.Length == 0) && paintedCaret)
                c.Line(left + textWidth, content.Y + 1, left + textWidth, content.Bottom - 1, Foreground);
        }
        c.Unclip();
    }
}
