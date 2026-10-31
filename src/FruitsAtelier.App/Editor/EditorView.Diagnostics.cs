using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private static string[] WrapDiagnosticPreview(ICanvas canvas, IEnumerable<string> messages, float size, float maxWidth,
        int maximumCharacters = 8192, int maximumLines = 256)
    {
        if (maximumCharacters <= 0 || maximumLines <= 0) return [];
        var lines = new List<string>();
        bool truncated = false;
        int remaining = maximumCharacters;
        foreach (string original in messages)
        {
            if (remaining <= 0 || lines.Count >= maximumLines) { truncated = true; break; }
            string message = L.Reformat(original);
            if (message.Length > remaining)
            {
                int length = char.IsHighSurrogate(message[remaining - 1]) ? remaining - 1 : remaining;
                message = message[..length]; truncated = true;
            }
            remaining -= Math.Max(1, message.Length);
            foreach (string paragraph in message.Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (var rune in paragraph.EnumerateRunes())
                {
                    string next = line + rune;
                    if (line.Length > 0 && (line.Length >= 256 || canvas.MeasureText(next, size) > maxWidth))
                    {
                        lines.Add(line); line = "";
                        if (lines.Count >= maximumLines) { truncated = true; break; }
                    }
                    line += rune;
                }
                if (lines.Count >= maximumLines) { truncated = true; break; }
                lines.Add(line);
            }
        }
        if (truncated)
        {
            if (lines.Count >= maximumLines) lines[maximumLines - 1] = "…";
            else lines.Add("…");
        }
        return lines.ToArray();
    }
}
