using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? operationError;
    private int errorScroll, errorLineCount, errorVisibleLines;
    public bool ErrorVisible => operationError is not null;

    public void ShowError(string message)
    {
        if (ErrorVisible) return;
        CancelInteraction(); menu = -1; contextItems.Clear();
        operationError = message; errorScroll = 0;
        hits.Clear(); fields.Clear();
    }

    private void DismissError()
    {
        operationError = null; hits.Clear(); fields.Clear();
    }

    private void DrawError(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        float w = Math.Min(720, width - 40), h = Math.Min(400, height - 40);
        float x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(x, y, w, h), Panel, 8);
        c.Stroke(new(x, y, w, h), Error, 2, 8);
        c.Text(L.Get("files.incomplete"), x + 24, y + 22, 20, Foreground, w - 48, true);
        var lines = new List<string>();
        foreach (string paragraph in L.Reformat(operationError!).Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (var rune in paragraph.EnumerateRunes())
            {
                string next = line + rune;
                if (line.Length > 0 && c.MeasureText(next, 14) > w - 48) { lines.Add(line); line = ""; }
                line += rune;
            }
            lines.Add(line);
        }
        errorLineCount = lines.Count; errorVisibleLines = Math.Max(1, (int)((h - 140) / 21));
        errorScroll = Math.Clamp(errorScroll, 0, Math.Max(0, errorLineCount - errorVisibleLines));
        for (int i = 0; i < errorVisibleLines && i + errorScroll < lines.Count; i++)
            c.Text(lines[i + errorScroll], x + 24, y + 64 + i * 21, 14, Foreground, w - 48);
        if (errorLineCount > errorVisibleLines)
            c.Text($"{errorScroll + 1}–{Math.Min(errorLineCount, errorScroll + errorVisibleLines)} / {errorLineCount}", x + 24, y + h - 47, 12, Muted, w - 200);
        Button(c, new(x + w - 140, y + h - 60, 116, 36), L.Get("mac.ok"), DismissError, true);
    }
}
