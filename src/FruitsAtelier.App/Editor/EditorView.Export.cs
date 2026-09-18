using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? ExportOverwriteTarget => WorkspaceSession?.Manifest.Difficulties
        .Where(d => d.Id == difficulties[activeDifficulty].Id)
        .Select(d => d.ExportTarget ?? d.Source).FirstOrDefault();

    private bool CanSubmitExport => exportMode == 2 ? !string.IsNullOrWhiteSpace(exportName)
        : !string.IsNullOrWhiteSpace(LibrarySettings.Songs)
            && (exportMode == 0 ? !string.IsNullOrWhiteSpace(exportName)
                : ExportOverwriteTarget is { } target && WorkspaceProject.Within(LibrarySettings.Songs, target));

    private void SubmitExport()
    {
        if (!CanSubmitExport) return;
        libraryField = -1;
        if (exportMode == 2) RequestOsuExport?.Invoke(exportName);
        else RequestWorkspaceExport?.Invoke(exportMode == 1, exportName);
    }

    private void SelectExportMode(int mode)
    {
        exportMode = mode; libraryField = -1;
        hits.Clear();
    }

    private void ExportKey(int key, bool ctrl)
    {
        if (key == 27) { CloseLibrary(); hits.Clear(); return; }
        if (ctrl && key is 83 or 69) return;
        if (key == 9)
        {
            if (exportMode != 1 && libraryField < 0) { libraryField = 3; libraryReplace = true; }
            else libraryField = -1;
            return;
        }
        if (libraryField == 3) { LibraryKey(key, ctrl); return; }
        if (key is 38 or 40) { SelectExportMode((exportMode + (key == 38 ? 2 : 1)) % 3); return; }
        if (key == 13) SubmitExport();
    }

    private void DrawExportOverlay(ICanvas c)
    {
        if (!ExportVisible) return;
        // Only modal controls may receive hits while the editor remains visible underneath.
        hits.Clear(); fields.Clear();
        float panelWidth = Math.Min(660, width - 32), panelHeight = exportMode == 0 ? 500 : exportMode == 1 ? 420 : 380;
        float x = (width - panelWidth) / 2, y = (height - panelHeight) / 2, inner = panelWidth - 48;
        c.Fill(new(x, y, panelWidth, panelHeight), Panel, 10);
        c.Stroke(new(x, y, panelWidth, panelHeight), Grid, 2, 10);
        c.Text(L.Get("library.export"), x + 24, y + 22, 22, Foreground, inner, true);
        c.Text(L.Get("library.exportDescription", CurrentDifficultyName), x + 24, y + 56, 13, Muted, inner);
        string[] choices = ["library.exportNew", "library.exportOverride", "library.exportFile"];
        for (int i = 0; i < choices.Length; i++)
        {
            int mode = i;
            var row = new Rect(x + 24, y + 88 + i * 42, inner, 38);
            if (exportMode == i) c.Fill(row, Surface, 5);
            c.Circle(row.X + 16, row.Y + 19, 7, exportMode == i ? Accent : Muted, false);
            if (exportMode == i) c.Circle(row.X + 16, row.Y + 19, 3, Accent);
            c.Text(L.Get(choices[i]), row.X + 36, row.Y + 11, 14, Foreground, inner - 48);
            hits.Add(new(row, () => SelectExportMode(mode), true));
        }
        float detailY = y + 232;
        if (exportMode != 1)
        {
            c.Text(L.Get("library.newDifficultyName"), x + 24, detailY, 14, Foreground, inner);
            var field = new Rect(x + 24, detailY + 26, inner, 40);
            c.Fill(field, Surface, 5); c.Stroke(field, libraryField == 3 ? Accent : Grid, radius: 5);
            DrawInputText(c, new(field.X + 12, field.Y + 12, field.Width - 24, 20), exportName, 14, libraryField == 3, libraryReplace);
            hits.Add(new(field, () => { libraryField = 3; libraryReplace = false; }, true));
            detailY += 84;
        }
        if (exportMode != 2)
        {
            c.Text(L.Get(exportMode == 1 ? "library.exportReplaceTarget" : "library.exportNewTarget"), x + 24, detailY, 14, Foreground, inner);
            string target = exportMode == 1 ? ExportOverwriteTarget ?? L.Get("library.noTarget") : NewExportTarget();
            DrawExportPath(c, target, x + 24, detailY + 26, inner);
        }
        float bottom = y + panelHeight - 62;
        Button(c, new(x + panelWidth - 304, bottom, 100, 38), L.Get("mac.cancel"), () => { CloseLibrary(); hits.Clear(); });
        var submit = new Rect(x + panelWidth - 192, bottom, 168, 38);
        c.Fill(submit, Surface, 5);
        string action = exportMode == 0 ? L.Get("library.exportCreate") : exportMode == 1
            ? L.Get("library.exportUpdate", CurrentDifficultyName) : L.Get("library.exportChooseLocation");
        Button(c, submit, action, SubmitExport, true, CanSubmitExport);
    }

    private string NewExportTarget()
    {
        if (string.IsNullOrWhiteSpace(LibrarySettings.Songs)) return L.Get("library.noTarget");
        string filename = WorkspaceProject.DifficultyFileName(Document, exportName, ".osu");
        if (WorkspaceSession is not { } session) return filename;
        string directory = session.Manifest.SourceDirectory is { } relative
            ? Path.GetFullPath(Path.Combine(LibrarySettings.Songs, relative))
            : Path.Combine(LibrarySettings.Songs, "FruitsAtelier " + WorkspaceProject.SafeName(session.Manifest.Name) + " " + session.Manifest.Id.ToString("N")[..8]);
        return Path.Combine(directory, filename);
    }

    private static void DrawExportPath(ICanvas c, string path, float x, float y, float available)
    {
        for (int line = 0; line < 4 && path.Length > 0; line++)
        {
            int length = path.Length;
            while (length > 1 && c.MeasureText(path[..length], 12) > available) length--;
            string text = path[..length];
            if (line == 3 && length < path.Length) text = text[..Math.Max(0, text.Length - 1)] + "…";
            c.Text(text, x, y + line * 18, 12, Muted, available);
            path = path[length..];
        }
    }
}
