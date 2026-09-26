using System.Globalization;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class TextEditor
    {
        public string Field = "";
        public int Anchor, Caret;
        public bool HasSelection => Anchor != Caret;
        public int Start => Math.Min(Anchor, Caret);
        public int End => Math.Max(Anchor, Caret);
        public void Clamp(string value)
        { Anchor = Math.Clamp(Anchor, 0, value.Length); Caret = Math.Clamp(Caret, 0, value.Length); }
        public void Focus(string field, string value, int position, bool shift = false)
        {
            if (Field != field) { Field = field; Anchor = Caret = value.Length; }
            if (!shift) Anchor = position;
            Caret = position;
        }
        public void SelectAll(string field, string value)
        { Field = field; Anchor = 0; Caret = value.Length; }
        public string Selected(string value) => value[Start..End];
        public string Replace(string value, string insertion, int limit)
        {
            if (value.Length - (End - Start) + insertion.Length > limit) return value;
            string result = value[..Start] + insertion + value[End..];
            Anchor = Caret = Start + insertion.Length;
            return result;
        }
        public string Delete(string value, bool backward)
        {
            if (HasSelection) return Replace(value, "", int.MaxValue);
            int boundary = backward ? Previous(value, Caret) : Next(value, Caret);
            if (boundary == Caret) return value;
            Anchor = Math.Min(boundary, Caret); Caret = Math.Max(boundary, Caret);
            return Replace(value, "", int.MaxValue);
        }
        public void Move(string value, int key, bool shift)
        {
            int position = key switch
            {
                36 => 0, 35 => value.Length,
                37 when HasSelection && !shift => Start,
                39 when HasSelection && !shift => End,
                37 => Previous(value, Caret), 39 => Next(value, Caret),
                _ => Caret
            };
            Caret = position;
            if (!shift) Anchor = position;
        }
        private static int Previous(string value, int index)
        {
            if (index <= 0) return 0;
            return StringInfo.ParseCombiningCharacters(value).LastOrDefault(start => start < index);
        }
        private static int Next(string value, int index)
        {
            foreach (int start in StringInfo.ParseCombiningCharacters(value))
                if (start > index) return start;
            return value.Length;
        }
    }

    private readonly TextEditor textEditor = new();
    private bool textSelecting;
    public string ActiveTextInputField => textEditor.Field;
    public Action? RequestPasteField { get; set; }
    public void PasteFieldText(string text, string? expectedField = null)
    {
        if (expectedField is not null && textEditor.Field != expectedField) return;
        if (DistanceSnapDialogVisible && dsBaseFocused)
        {
            string filtered = new(text.Where(value => char.IsAsciiDigit(value) || value == '.').ToArray());
            SetDistanceBaseText(InsertInput("ds:base", dsBaseText, filtered, 16));
        }
        else if (DistanceEditing)
        {
            string filtered = new(text.Where(value => char.IsAsciiDigit(value) || value == '-' || !editingXCoordinate && value == '.').ToArray());
            string next = InsertInput("distance", editBuffer, filtered, 30);
            int dot = next.IndexOf('.');
            if (dot < 0 || next.Length - dot - 1 <= 2) { editBuffer = next; replaceText = false; PreviewDistanceText(); }
        }
        else if (editField >= 0 && editField < fields.Count)
        {
            string filtered = new(text.Where(value => char.IsAsciiDigit(value) || value is '.' or '-' or '+' or 'e' or 'E'
                || value == ':' && fields[editField].Timestamp).ToArray());
            editBuffer = InsertInput("numeric:" + editField, editBuffer, filtered, 30);
            replaceText = false; fieldError = "";
        }
    }
    private readonly Dictionary<string, (Rect Content, float Left, int[] Positions, float[] Widths)> textLayouts = [];
    private readonly Dictionary<string, (string Value, float Size, int[] Positions, float[] Widths)> textMetrics = [];
    private long caretEpoch = Environment.TickCount64;
    private bool textInputFocused = true, paintedCaret;
    private bool CaretVisible => textInputFocused && (Environment.TickCount64 - caretEpoch) % 1000 < 500;
    public bool TextCaretNeedsRedraw => IsEditingText && textInputFocused && textEditor.Field.Length > 0
        && !textEditor.HasSelection && paintedCaret != CaretVisible;

    public void SetTextInputFocus(bool focused)
    { textInputFocused = focused; ResetTextCaret(); }
    private void ResetTextCaret() => caretEpoch = Environment.TickCount64;

    private void FocusInput(string key, string value, float x, bool shift = false, bool selectAll = false)
    {
        if (selectAll) { SelectInput(key, value); return; }
        if (!textLayouts.TryGetValue(key, out var layout)) return;
        int position = value.Length;
        float target = x - layout.Left;
        for (int i = 1; i < layout.Positions.Length; i++)
        {
            if (target < (layout.Widths[i - 1] + layout.Widths[i]) / 2)
            { position = layout.Positions[i - 1]; break; }
        }
        textEditor.Focus(key, value, position, shift);
        textSelecting = true;
        ResetTextCaret();
    }
    private void MoveInputSelection(float x)
    {
        string key = textEditor.Field;
        string value = key switch
        {
            "time" => timeJumpText,
            "distance" => editBuffer,
            "ds:base" => dsBaseText,
            _ when key.StartsWith("song:") => songValues.GetValueOrDefault(key[5..], ""),
            _ when key.StartsWith("library:") => LibraryFieldValue,
            _ when key.StartsWith("numeric:") => editBuffer,
            _ => ""
        };
        FocusInput(key, value, x, true);
    }
    private void SelectInput(string key, string value)
    { textEditor.SelectAll(key, value); ResetTextCaret(); }

    private bool InputKey(string key, ref string value, int virtualKey, bool ctrl, bool shift, int limit, Action? paste = null)
    {
        if (textEditor.Field != key) textEditor.Focus(key, value, value.Length);
        textEditor.Clamp(value);
        if (ctrl)
        {
            if (virtualKey == 65) SelectInput(key, value);
            else if (virtualKey == 67 && textEditor.HasSelection) RequestCopyText?.Invoke(textEditor.Selected(value));
            else if (virtualKey == 88 && textEditor.HasSelection)
            { RequestCopyText?.Invoke(textEditor.Selected(value)); value = textEditor.Replace(value, "", limit); }
            else if (virtualKey == 86) paste?.Invoke();
            else return false;
        }
        else if (virtualKey is 37 or 39 or 36 or 35) textEditor.Move(value, virtualKey, shift);
        else if (virtualKey is 8 or 46) value = textEditor.Delete(value, virtualKey == 8);
        else return false;
        ResetTextCaret();
        return true;
    }

    private string InsertInput(string key, string value, string text, int limit)
    {
        if (textEditor.Field != key) textEditor.Focus(key, value, value.Length);
        textEditor.Clamp(value);
        string result = textEditor.Replace(value, text, limit);
        ResetTextCaret();
        return result;
    }

    private void DrawInputText(ICanvas c, Rect content, string value, float size, bool focused, string key, bool centered = false)
    {
        float textWidth = c.MeasureText(value, size);
        int caret = focused && textEditor.Field == key ? Math.Clamp(textEditor.Caret, 0, value.Length) : value.Length;
        float caretWidth = c.MeasureText(value[..caret], size);
        float left = content.X - (focused ? Math.Max(0, caretWidth - content.Width + 2) : 0);
        if (centered && textWidth <= content.Width - 2) left = content.X + (content.Width - textWidth) / 2;
        if (!textMetrics.TryGetValue(key, out var metrics) || metrics.Value != value || metrics.Size != size)
        {
            int[] positions = [.. StringInfo.ParseCombiningCharacters(value).Append(value.Length).Distinct()];
            if (positions.Length == 0 || positions[0] != 0) positions = [0, .. positions];
            float[] widths = new float[positions.Length];
            for (int i = 1; i < positions.Length; i++)
                widths[i] = value.Length <= 128 ? c.MeasureText(value[..positions[i]], size)
                    : widths[i - 1] + c.MeasureText(value[positions[i - 1]..positions[i]], size);
            metrics = (value, size, positions, widths);
            textMetrics[key] = metrics;
        }
        textLayouts[key] = (content, left, metrics.Positions, metrics.Widths);
        c.Clip(content);
        if (focused && textEditor.Field == key && textEditor.HasSelection)
        {
            float start = c.MeasureText(value[..Math.Clamp(textEditor.Start, 0, value.Length)], size);
            float end = c.MeasureText(value[..Math.Clamp(textEditor.End, 0, value.Length)], size);
            c.Fill(new(left + start, content.Y, end - start, content.Height), textInputFocused ? 0x365D77u : Grid);
        }
        c.Text(value, left, content.Y, size, Foreground, Math.Max(content.Width, textWidth + 2));
        if (focused)
        {
            paintedCaret = CaretVisible;
            if (textEditor.Field == key && !textEditor.HasSelection && paintedCaret)
                c.Line(left + caretWidth, content.Y + 1, left + caretWidth, content.Bottom - 1, Foreground);
        }
        c.Unclip();
    }
}
