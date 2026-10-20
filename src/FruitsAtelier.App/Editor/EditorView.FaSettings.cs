using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool CurrentDifficultyFaOnly => difficulties[activeDifficulty].FaOnly;
    private string faOnlyPromptPath = "";

    private void ShowAddDifficulty()
    {
        if (!PrepareFileOperation()) return;
        newDifficultyFaOnly = false;
        ShowDiscardConfirmation(answer =>
        {
            if (answer != 6 || !AddDifficulty(faOnly: newDifficultyFaOnly)) return;
            if (!newDifficultyFaOnly && !string.IsNullOrWhiteSpace(LibrarySettings.Songs)) ShowWorkspaceExport();
        });
        addDifficultyConfirmation = true;
    }

    private void DrawFaSettings(ICanvas c, Rect bounds, bool live)
    {
        bool faOnly = live ? CurrentDifficultyFaOnly : songFaOnly;
        ToggleSwitch(c, new(bounds.X, bounds.Y, bounds.Width, 38), L.Get("song.syncToOsu"), !faOnly, () =>
        {
            if (live) RequestDifficultyFaOnly(!CurrentDifficultyFaOnly);
            else songFaOnly = !songFaOnly;
        });
        var lines = WrapSyncText(c, L.Get(faOnly ? "song.faOnlyHelp" : "song.syncHelp"), bounds.Width);
        for (int i = 0; i < lines.Length; i++)
            c.Text(lines[i].Text, bounds.X, bounds.Y + 52 + i * 22, 13, Muted, bounds.Width);
    }

    private void RequestDifficultyFaOnly(bool faOnly)
    {
        if (faOnly == CurrentDifficultyFaOnly || NotifySynchronizationBlocked()) return;
        Guid id = difficulties[activeDifficulty].Id;
        var entry = WorkspaceSession?.Manifest.Difficulties.FirstOrDefault(d => d.Id == id);
        string? source = entry is null ? null : WorkspaceSynchronization.Target(entry);
        if (faOnly && source is not null && File.Exists(source))
        {
            try
            {
                string expected = WorkspaceProject.Hash(source);
                ShowDiscardConfirmation(answer =>
                {
                    if (answer is 6 or 7) BeginDifficultyFaOnly(id, true, answer == 7, expected);
                    else songFaOnly = CurrentDifficultyFaOnly;
                });
                faOnlyPromptPath = source;
                faOnlyConfirmation = true;
            }
            catch (Exception error) { ShowError(error.Message); }
        }
        else BeginDifficultyFaOnly(id, faOnly, false, null);
    }

    private void BeginDifficultyFaOnly(Guid id, bool faOnly, bool deleteOsu, string? expected)
    {
        if (WorkspaceSession is not { } original)
        {
            difficulties.Single(d => d.Id == id).FaOnly = faOnly;
            projectStructureDirty = true;
            return;
        }
        var project = CaptureProject();
        var session = DetachedSession(original, project);
        syncDifficulty = id; syncPreserveHistory = true; syncCommitPreservesActiveDifficulty = true;
        syncPage = "applying";
        if (!faOnly && WorkspaceSynchronization.Target(session.Manifest.Difficulties.Single(d => d.Id == id)) is null)
            afterSynchronization = ShowWorkspaceExport;
        syncCommitTask = Task.Run(() => WorkspaceDifficultySettings.SetFaOnly(session, project, id, faOnly, deleteOsu, expected));
        hits.Clear(); fields.Clear();
    }

    private void DrawDifficultySettingsConfirmation(ICanvas c)
    {
        float w = Math.Min(660, width - 32), h = faOnlyConfirmation ? 340 : 260;
        float x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get(faOnlyConfirmation ? "song.faOnlyPrompt" : "project.add"), x + 22, y + 22, 18, Foreground, w - 44, true);
        var lines = WrapSyncText(c, L.Get(faOnlyConfirmation ? "song.faOnlyDeleteHelp" : "song.newDifficultySyncHelp"), w - 44);
        for (int i = 0; i < lines.Length; i++) c.Text(lines[i].Text, x + 22, y + 60 + i * 22, 13, Muted, w - 44);
        if (faOnlyConfirmation)
        {
            c.Text(faOnlyPromptPath, x + 22, y + 136, 11, Muted, w - 44);
            Button(c, new(x + 22, y + h - 158, w - 44, 36), L.Get("song.faOnlyKeepFile"), () => AnswerDiscard(6), true);
            Button(c, new(x + 22, y + h - 112, w - 44, 36), L.Get("song.faOnlyDeleteFile"), () => AnswerDiscard(7));
            Button(c, new(x + 22, y + h - 66, w - 44, 36), L.Get("mac.cancel"), () => AnswerDiscard(2));
        }
        else
        {
            ToggleSwitch(c, new(x + 22, y + 124, w - 44, 38), L.Get("song.faOnly"), newDifficultyFaOnly,
                () => newDifficultyFaOnly = !newDifficultyFaOnly);
            Button(c, new(x + 22, y + h - 66, (w - 54) / 2, 36), L.Get("mac.cancel"), () => AnswerDiscard(2));
            Button(c, new(x + 32 + (w - 54) / 2, y + h - 66, (w - 54) / 2, 36), L.Get("project.add"), () => AnswerDiscard(6), true);
        }
    }
}
