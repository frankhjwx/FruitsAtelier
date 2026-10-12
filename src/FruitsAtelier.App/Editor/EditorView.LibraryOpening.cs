using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Task<WorkspaceSession>? libraryOpenTask;
    private Action? libraryOpenCompleted;
    public bool LibraryOpening => libraryOpenTask is not null;

    public void BeginLibraryOpen(LibraryMap map, Action? completed = null)
    {
        if (LibraryOpening || !PrepareFileOperation()) return;
        var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
        libraryOpenCompleted = completed;
        // Workspace maintenance owns the same gate as Open; waiting belongs off the UI thread.
        libraryOpenTask = Task.Run(() => LibraryOperations.Open(map, settings));
        hits.Clear(); fields.Clear();
    }

    private void PumpLibraryOpen()
    {
        if (libraryOpenTask is not { IsCompleted: true } task) return;
        libraryOpenTask = null;
        var completed = libraryOpenCompleted; libraryOpenCompleted = null;
        try
        {
            LoadWorkspace(task.GetAwaiter().GetResult(), checkAdditionalDifficulties: true);
            completed?.Invoke();
        }
        catch (Exception error) { ShowError(L.Reformat(error.Message)); }
    }
}
