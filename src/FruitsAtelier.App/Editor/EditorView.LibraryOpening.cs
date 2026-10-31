using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Task<WorkspaceSession>? libraryOpenTask;
    private LibraryMap? libraryOpeningMap;
    private Action? libraryOpenCompleted;
    private string? libraryOpenDifficultyPath;
    private string? pendingLibraryDifficultyPath;
    public bool LibraryOpening => difficultyPauseTask is not null || libraryOpenTask is not null || mediaOpening && !mediaExitPending && !ErrorVisible;

    public void BeginLibraryOpen(LibraryMap map, Action? completed = null, bool selectDifficulty = false)
    {
        if (RejectPatternProjectOperation()) return;
        if (LibraryOpening || !PrepareFileOperation()) return;
        var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
        libraryOpeningMap = map;
        libraryOpenCompleted = completed;
        libraryOpenDifficultyPath = selectDifficulty ? map.Path : null;
        // Workspace maintenance owns the same gate as Open; waiting belongs off the UI thread.
        libraryOpenTask = Task.Run(() => LibraryOperations.Open(map, settings));
        hits.Clear(); fields.Clear();
    }

    private void SelectLibraryDifficulty(string path)
    {
        pendingLibraryDifficultyPath = path;
        int index = FindLibraryDifficulty();
        if (index < 0) return;
        pendingLibraryDifficultyPath = null;
        SwitchDifficulty(index);
    }

    private int FindLibraryDifficulty()
    {
        if (pendingLibraryDifficultyPath is not { } path || WorkspaceSession is not { } session) return -1;
        var entry = session.Manifest.Difficulties.FirstOrDefault(d =>
            WorkspaceSynchronization.Paths.Equals(Path.Combine(session.Directory, d.File), path)
            || WorkspaceSynchronization.Paths.Equals(d.Source, path)
            || WorkspaceSynchronization.Paths.Equals(d.ExportTarget, path));
        return entry is null ? -1 : difficulties.FindIndex(d => d.Id == entry.Id);
    }

    private void PumpLibraryOpen()
    {
        if (libraryOpenTask is not { IsCompleted: true } task) return;
        libraryOpenTask = null;
        var visited = libraryOpeningMap; libraryOpeningMap = null;
        var path = libraryOpenDifficultyPath; libraryOpenDifficultyPath = null;
        var completed = libraryOpenCompleted; libraryOpenCompleted = null;
        try
        {
            LoadWorkspace(task.GetAwaiter().GetResult(), checkAdditionalDifficulties: true);
            if (path is not null) SelectLibraryDifficulty(path);
            if (visited is not null && WorkspaceSession is { } session) RecordLibraryVisit(visited, session.Directory);
            completed?.Invoke();
        }
        catch (Exception error) { ShowError(L.Reformat(error.Message)); }
    }
}
