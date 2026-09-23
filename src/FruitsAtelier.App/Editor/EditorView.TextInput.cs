using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public readonly record struct NativeTextField(string Id, Rect Bounds, string Text, float FontSize, bool SelectAll);

public sealed partial class EditorView
{
    public NativeTextField? ActiveTextField { get; private set; }

    public bool SetNativeText(string id, string text)
    {
        if (ActiveTextField?.Id != id || TextFieldId() != id || ErrorVisible || DiscardConfirmationVisible) return false;
        text = new string(text.Where(c => !char.IsControl(c)).ToArray());
        if (SongSetupVisible && songField.Length > 0)
        {
            if (text.Length > 4096) return false;
            songValues[songField] = text; songSelectAll = false; songError = "";
            if (songField == "Hex" && text.TrimStart('#').Length == 6) CommitSongHex();
        }
        else if (TimeJumpVisible)
        {
            if (text.Length > 128) return false;
            timeJumpText = text; timeJumpSelected = false; timeJumpError = "";
        }
        else if (DistanceEditing)
        {
            if (text.Length > 30 || text.Any(c => !char.IsAsciiDigit(c) && c != '-'
                && (editingXCoordinate || c != '.'))) return false;
            int dot = text.IndexOf('.');
            if (dot >= 0 && text.Length - dot - 1 > 2) return false;
            editBuffer = text; replaceText = false; PreviewDistanceText();
        }
        else if (LibraryVisible || ExportVisible)
        {
            if (text.Length > 4096) return false;
            LibraryFieldValue = text; libraryReplace = false;
        }
        else if (editField >= 0 && editField < fields.Count)
        {
            if (text.Length > 30 || text.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or '-' or '+' or 'e' or 'E')
                && (c != ':' || !fields[editField].Timestamp))) return false;
            editBuffer = text; replaceText = false; fieldError = "";
        }
        else return false;
        ResetTextCaret();
        return true;
    }

    private string TextFieldId() => SongSetupVisible ? "song:" + songField : TimeJumpVisible ? "time"
        : DistanceEditing ? "distance" : LibraryVisible || ExportVisible ? "library:" + libraryField
        : "editor:" + editField;

    private long caretEpoch = Environment.TickCount64;
    private bool textInputFocused = true, paintedCaret;
    private bool CaretVisible => textInputFocused && (Environment.TickCount64 - caretEpoch) % 1000 < 500;
    public bool TextCaretNeedsRedraw => IsEditingText && textInputFocused
        && (!(SongSetupVisible ? songSelectAll : TimeJumpVisible ? timeJumpSelected : LibraryTextFocused ? libraryReplace : replaceText) || (SongSetupVisible ? songValues.GetValueOrDefault(songField, "") : TimeJumpVisible ? timeJumpText : LibraryTextFocused ? LibraryFieldValue : editBuffer).Length == 0)
        && paintedCaret != CaretVisible;

    public void SetTextInputFocus(bool focused)
    {
        textInputFocused = focused;
        ResetTextCaret();
    }

    private void ResetTextCaret() => caretEpoch = Environment.TickCount64;

    private void DrawInputText(ICanvas c, Rect content, string value, float size, bool focused, bool selectAll)
    {
        if (focused && !ErrorVisible && !DiscardConfirmationVisible)
            ActiveTextField = new(TextFieldId(), content, value, size, selectAll);
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
