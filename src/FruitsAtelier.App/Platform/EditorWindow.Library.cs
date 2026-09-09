using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void ConfigureLibrary()
    {
        view.InitializeLibrary(false);
        view.RequestLibraryFolder = workspace => FileOperation(() =>
        {
            var path = MapFileDialog.SelectFolder(hwnd, L.Get(workspace ? "library.workspace" : "library.songs"));
            if (path is not null) view.SetLibraryFolder(workspace, path);
        });
        view.RequestLibraryImport = folder => FileOperation(() =>
        {
            if (folder)
            {
                var path = MapFileDialog.SelectFolder(hwnd, L.Get("library.importFolder"));
                if (path is null) return;
                LibraryOperations.ImportFolder(path, view.LibrarySettings); view.RefreshLibrary();
            }
            else
            {
                if (!ConfirmDiscard()) return;
                var path = MapFileDialog.Select(hwnd, false, L.Get("library.importFile"), MapFileDialog.OpenFilter);
                if (path is not null) OpenPath(path);
            }
        });
        view.RequestLibraryOpen = map => FileOperation(() =>
        {
            if (view.WorkspaceSession?.Directory == map.ProjectPath) { view.CloseLibrary(); return; }
            if (!ConfirmDiscard()) return;
            view.LoadWorkspace(LibraryOperations.Open(map, view.LibrarySettings));
            ResetAudio(); if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) audio.Load(view.Document.AudioPath);
        });
        view.RequestOsuExport = name => FileOperation(() =>
        {
            var project = view.CaptureProject();
            var document = LibraryOperations.StandaloneExportDocument(project.Difficulties[view.ActiveDifficultyIndex], name);
            string filename = WorkspaceProject.DifficultyFileName(document, name, ".osu");
            var path = MapFileDialog.Select(hwnd, true, L.Get("library.exportFile"), MapFileDialog.OsuFilter, filename, "osu");
            if (path is null) return;
            OsuBeatmapWriter.WriteFile(document, path, view.CompensateTinyDroplets);
            view.CloseLibrary(); view.SetNotice(L.Get("library.exported", path));
        });
        view.RequestWorkspaceExport = (overwrite, name) => FileOperation(() =>
        {
            if (!view.SaveWorkspace() || view.WorkspaceSession is null) return;
            var project = view.CaptureProject();
            var plan = WorkspaceExport.Plan(view.WorkspaceSession, project.Difficulties[view.ActiveDifficultyIndex], view.LibrarySettings.Songs, overwrite, name, view.CompensateTinyDroplets);
            LibraryOperations.Export(view.WorkspaceSession, project, plan);
            view.LibraryExportFinished(); view.SetNotice(L.Get("library.exported", plan.Target));
        });
    }
}
