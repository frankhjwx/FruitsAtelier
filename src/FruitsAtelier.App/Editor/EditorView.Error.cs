using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? operationError;
    private int errorScroll, errorLineCount, errorVisibleLines;
    public bool ErrorVisible => operationError is not null;
    public bool RecoveryActive { get; private set; }
    public Action? RequestRecoverySave { get; set; }
    private string? recoveryNotice;
    private (string Message, string Language, bool Recovery, float Width, Type Canvas)? errorLayoutKey;
    private readonly List<string> errorLines = [];
    private void ClearErrorLayout() { errorLayoutKey = null; errorLines.Clear(); }


    public void ShowRenderError(string message)
    {
        ClearErrorLayout();
        RecoveryActive = true;
        operationError = message;
        errorScroll = 0;
        recoveryNotice = null;
        hits.Clear(); fields.Clear(); menu = -1; contextItems.Clear();
        try { CancelInteraction(); }
        catch (Exception cleanup) { operationError += "\n" + cleanup.Message; }
        try { RequestPausePlayback?.Invoke(); }
        catch (Exception cleanup) { operationError += "\n" + cleanup.Message; }
        try
        {
            if (objectSelection.Any(id => !ObjectExists(id))) SelectObjects(objectSelection.Where(ObjectExists), selection);
        }
        catch (Exception cleanup) { operationError += "\n" + cleanup.Message; }
    }

    public void RetryRender()
    {
        ClearErrorLayout();
        RecoveryActive = false;
        operationError = null;
        recoveryNotice = null;
        hits.Clear(); fields.Clear();
    }

    public void SaveRecoveryCopy(string path)
    {
        // Recovery persists authored state without conversion or changing the original save baseline.
        string text = ProjectSerializer.Serialize(CaptureProject(), path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                writer.Write(text);
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        recoveryNotice = L.Get("recovery.saved", path);
    }

    public void RecoverySaveFailed(string message) => recoveryNotice = L.Get("recovery.failed", message);

    private void SaveRecovery()
    {
        try { RequestRecoverySave?.Invoke(); }
        catch (Exception error) { RecoverySaveFailed(error.Message); }
    }

    public void ShowError(string message)
    {
        if (ErrorVisible) return;
        CancelInteraction(); menu = -1; contextItems.Clear();
        if (objectSelection.Any(id => !ObjectExists(id))) SelectObjects(objectSelection.Where(ObjectExists), selection);
        ClearErrorLayout();
        operationError = message; errorScroll = 0;
        hits.Clear(); fields.Clear();
    }

    private void DismissError()
    {
        if (RecoveryActive) return;
        ClearErrorLayout();
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
        c.Text(L.Get(RecoveryActive ? "recovery.title" : "files.incomplete"), x + 24, y + 22, 20, Foreground, w - 48, true);
        var layoutKey = (operationError!, L.Language, RecoveryActive, w, c.GetType());
        if (errorLayoutKey != layoutKey)
        {
            errorLines.Clear();
            const int maximumCharacters = 8192, maximumLines = 256;
            string message = L.Reformat(operationError!);
            bool truncated = message.Length > maximumCharacters;
            if (truncated)
            {
                int length = maximumCharacters;
                if (char.IsHighSurrogate(message[length - 1])) length--;
                message = message[..length];
            }
            if (RecoveryActive) message = L.Get("recovery.help") + "\n" + message;
            foreach (string paragraph in message.Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (var rune in paragraph.EnumerateRunes())
                {
                    string next = line + rune;
                    if (line.Length > 0 && (line.Length >= 256 || c.MeasureText(next, 14) > w - 48))
                    {
                        errorLines.Add(line); line = "";
                        if (errorLines.Count >= maximumLines) { truncated = true; break; }
                    }
                    line += rune;
                }
                if (errorLines.Count >= maximumLines) { truncated = true; break; }
                errorLines.Add(line);
            }
            if (truncated) errorLines.Add("…");
            errorLayoutKey = layoutKey;
        }
        var lines = errorLines;
        errorLineCount = lines.Count; errorVisibleLines = Math.Max(1, (int)((h - (RecoveryActive ? 180 : 140)) / 21));
        errorScroll = Math.Clamp(errorScroll, 0, Math.Max(0, errorLineCount - errorVisibleLines));
        for (int i = 0; i < errorVisibleLines && i + errorScroll < lines.Count; i++)
            c.Text(lines[i + errorScroll], x + 24, y + 64 + i * 21, 14, Foreground, w - 48);
        if (errorLineCount > errorVisibleLines)
            c.Text($"{errorScroll + 1}–{Math.Min(errorLineCount, errorScroll + errorVisibleLines)} / {errorLineCount}", x + 24, y + h - (RecoveryActive ? 119 : 47), 12, Muted, w - 200);
        if (RecoveryActive)
        {
            if (recoveryNotice is not null)
            {
                string notice = L.Reformat(recoveryNotice);
                if (notice.Length > 512) notice = notice[..(char.IsHighSurrogate(notice[511]) ? 511 : 512)] + "…";
                c.Text(notice, x + 24, y + h - 94, 13, Foreground, w - 48);
            }
            Button(c, new(x + 24, y + h - 60, w - 188, 36), L.Get("recovery.save"), SaveRecovery, true);
            Button(c, new(x + w - 140, y + h - 60, 116, 36), L.Get("testplay.retry"), RetryRender, true);
        }
        else Button(c, new(x + w - 140, y + h - 60, 116, 36), L.Get("mac.ok"), DismissError, true);
    }
}
