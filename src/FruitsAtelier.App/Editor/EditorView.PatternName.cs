using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Action<string?>? patternNameCompletion;
    private string patternNameTitle = "", patternNameText = "";
    private Rect patternNameField;
    private bool PatternNameVisible => patternNameCompletion is not null;

    private void ShowPatternNameDialog(string title, Action<string?> completion)
    {
        patternNameTitle = title; patternNameText = "";
        patternNameCompletion = completion;
        menu = -1; contextItems.Clear();
        SelectInput("gallery:name", patternNameText);
    }

    private void FinishPatternName(bool save)
    {
        if (save && string.IsNullOrWhiteSpace(patternNameText)) return;
        var completion = patternNameCompletion;
        patternNameCompletion = null;
        textEditor.Field = "";
        completion?.Invoke(save ? patternNameText.Trim() : null);
    }

    private void DrawPatternNameDialog(ICanvas c)
    {
        if (!PatternNameVisible) return;
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), 0x10141B, opacity: .8f);
        float w = Math.Min(440, width - 32), h = 192;
        var box = new Rect((width - w) / 2, (height - h) / 2, w, h);
        c.Fill(box, Panel, 8); c.Stroke(box, Grid, radius: 8);
        c.Text(patternNameTitle, box.X + 20, box.Y + 20, 16, Foreground, w - 40, true);
        patternNameField = new(box.X + 20, box.Y + 62, w - 40, 36);
        c.Fill(patternNameField, Surface, 4); c.Stroke(patternNameField, Accent, radius: 4);
        DrawInputText(c, new(patternNameField.X + 10, patternNameField.Y + 9, patternNameField.Width - 20, 20),
            patternNameText, 13, true, "gallery:name");
        Button(c, new(box.Right - 208, box.Bottom - 52, 88, 32), L.Get("mac.cancel"), () => FinishPatternName(false));
        Button(c, new(box.Right - 108, box.Bottom - 52, 88, 32), L.Get("gallery.save"), () => FinishPatternName(true),
            enabled: !string.IsNullOrWhiteSpace(patternNameText));
    }

    private void PatternNamePointerDown(float x, float y, int button)
    {
        if (button != 0) return;
        if (patternNameField.Contains(x, y)) { FocusInput("gallery:name", patternNameText, x); return; }
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); return; }
    }

    private void PatternNameKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { FinishPatternName(false); return; }
        if (key == 13) { FinishPatternName(true); return; }
        InputKey("gallery:name", ref patternNameText, key, ctrl, shift, 160, RequestPasteField);
    }

    private void PatternNameText(string text)
        => patternNameText = InsertInput("gallery:name", patternNameText, new string(text.Where(c => !char.IsControl(c)).ToArray()), 160);
}
