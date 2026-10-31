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
    private bool preparingError;
    private (string Message, string Language, bool Recovery, float Width, Type Canvas)? errorLayoutKey;
    private IReadOnlyList<string> errorLines = [];
    private void ClearErrorLayout() { errorLayoutKey = null; errorLines = []; }

    public void ShowRenderError(string message)
    {
        if (preparingError) return;
        preparingError = true;
        try { PrepareRecovery(message); }
        finally { preparingError = false; }
    }

    private void PrepareRecovery(string message)
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
        if (preparingError) return;
        ClearErrorLayout();
        RecoveryActive = false;
        operationError = null;
        recoveryNotice = null;
        hits.Clear(); fields.Clear();
    }

    public string[] SaveRecoveryCopy(string path)
    {
        if (preparingError) throw new InvalidOperationException(L.Get("core.history.saveDuringEdit"));
        // Pattern editing can retain another unsaved project with independent project notes.
        var copies = new List<(string Path, BeatmapProject Project)> { (path, CaptureRecoveryProject(difficulties, ProjectName, projectNotes)) };
        var retained = patternReturnSession ?? retainedPatternProject;
        if (retained is not null && !retained.Difficulties.All(difficulties.Contains))
        {
            string companion = Path.Combine(Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path) + ".retained.catchproj");
            copies.Add((companion, CaptureRecoveryProject(retained.Difficulties, retained.Name, retained.Notes)));
        }
        // Serialization validates both authored snapshots before any destination is created.
        var output = copies.Select(copy => (copy.Path, Text: ProjectSerializer.Serialize(copy.Project, copy.Path),
            Temporary: copy.Path + "." + Guid.NewGuid().ToString("N") + ".tmp")).ToArray();
        var written = new List<string>();
        try
        {
            foreach (var copy in output)
            {
                using var stream = new FileStream(copy.Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                writer.Write(copy.Text);
            }
            foreach (var copy in output) { File.Move(copy.Temporary, copy.Path); written.Add(copy.Path); }
        }
        catch
        {
            foreach (string created in written) RemoveRecoveryOutput(created);
            throw;
        }
        finally { foreach (var copy in output) RemoveRecoveryOutput(copy.Temporary); }
        string[] paths = output.Select(copy => copy.Path).ToArray();
        recoveryNotice = L.Get("recovery.saved", string.Join("; ", paths));
        return paths;
    }

    private static BeatmapProject CaptureRecoveryProject(IEnumerable<DifficultySession> sessions, string name, string notes) => new()
    {
        Name = name, Notes = notes,
        Difficulties = sessions.Select(d => new ProjectDifficulty
        {
            Id = d.Id, Name = d.Name, FaOnly = d.FaOnly,
            Document = d.History.HasActiveTransaction ? d.History.CaptureTransactionStart() : d.History.Document.DeepClone()
        }).ToList()
    };

    private static void RemoveRecoveryOutput(string path)
    {
        // A cleanup failure must not replace the destination failure that needs the user's attention.
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void RecoverySaveFailed(string message) => recoveryNotice = L.Get("recovery.failed", message);

    private void SaveRecovery()
    {
        try { RequestRecoverySave?.Invoke(); }
        catch (Exception error) { RecoverySaveFailed(error.Message); }
    }

    public void ShowError(string message)
    {
        if (ErrorVisible || preparingError) return;
        preparingError = true;
        try
        {
            ClearErrorLayout();
            operationError = message; errorScroll = 0;
            hits.Clear(); fields.Clear(); menu = -1; contextItems.Clear();
            try
            {
                CancelInteraction();
                if (objectSelection.Any(id => !ObjectExists(id))) SelectObjects(objectSelection.Where(ObjectExists), selection);
            }
            catch (Exception cleanup) { PrepareRecovery(message + "\n" + cleanup.Message); }
        }
        finally { preparingError = false; }
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
        float w = Math.Max(240, Math.Min(720, width - 40)), h = Math.Max(180, Math.Min(400, height - 40));
        float x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(x, y, w, h), Panel, 8);
        c.Stroke(new(x, y, w, h), Error, 2, 8);
        c.Text(L.Get(RecoveryActive ? "recovery.title" : "files.incomplete"), x + 24, y + 22, 20, Foreground, w - 48, true);
        var layoutKey = (operationError!, L.Language, RecoveryActive, w, c.GetType());
        if (errorLayoutKey != layoutKey)
        {
            errorLines = WrapDiagnosticPreview(c, RecoveryActive ? [L.Get("recovery.help"), operationError!] : [operationError!], 14, w - 48);
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
