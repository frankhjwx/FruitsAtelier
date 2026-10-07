using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Task<ProjectDifficulty?>? previousSaveRead;
    private Task<ProjectDifficulty>? previousSaveRestore;
    private WorkspaceSession? previousSaveSession;
    private Guid previousSaveDifficulty;
    private bool previousSaveConfirmation;

    private void RequestPreviousSave()
    {
        if (previousSaveRead is not null || previousSaveRestore is not null || DiscardConfirmationVisible) return;
        if (WorkspaceSession is not { } session) { SetNotice(L.Get("history.noPreviousSave")); return; }
        if (NotifySynchronizationBlocked() || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        previousSaveSession = session;
        previousSaveDifficulty = difficulties[activeDifficulty].Id;
        Guid id = previousSaveDifficulty;
        previousSaveRead = Task.Run(() =>
        {
            foreach (var version in WorkspaceVersionHistory.List(session).Where(v => !v.WorkingCopy && v.Operation == "save"))
            {
                var difficulty = WorkspaceVersionHistory.Read(session, version).Difficulties.FirstOrDefault(d => d.Id == id);
                if (difficulty is not null) return difficulty;
            }
            return null;
        });
    }

    private bool PreviousSaveMatches => ReferenceEquals(previousSaveSession, WorkspaceSession)
        && difficulties[activeDifficulty].Id == previousSaveDifficulty;

    private void PumpPreviousSave()
    {
        try
        {
            if (previousSaveRead is { IsCompleted: true } read)
            {
                previousSaveRead = null;
                var historical = read.GetAwaiter().GetResult();
                if (!PreviousSaveMatches) return;
                if (historical is null) { SetNotice(L.Get("history.noPreviousSave")); return; }
                if (DiscardConfirmationVisible || IsTestplaying || IsEditingText || !PrepareFileOperation()) return;
                ShowDiscardConfirmation(answer =>
                {
                    if (answer != 7 || !PreviousSaveMatches) return;
                    var current = CaptureProject();
                    var session = previousSaveSession!;
                    previousSaveRestore = Task.Run(() =>
                    {
                        WorkspaceVersionHistory.ArchiveCurrent(session, current);
                        return historical;
                    });
                });
                previousSaveConfirmation = true;
            }
            if (previousSaveRestore is { IsCompleted: true } restored)
            {
                previousSaveRestore = null;
                var historical = restored.GetAwaiter().GetResult();
                if (PreviousSaveMatches) ApplyHistoricalDifficulty(historical);
            }
        }
        catch (Exception error) { ShowError(L.Get("history.failed", error.Message)); }
    }
}
