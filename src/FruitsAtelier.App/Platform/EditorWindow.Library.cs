using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void ConfigureLibrary()
    {
        view.RequestLibraryDrop = paths => FileOperation(() =>
        {
            foreach (string skin in paths.Where(p => Path.GetExtension(p).Equals(".osk", StringComparison.OrdinalIgnoreCase)))
                view.ImportSkin(skin);
            var maps = paths.Where(p => Path.GetExtension(p).Equals(".osz", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string map in maps.SkipLast(1)) LibraryOperations.ImportPath(map, view.LibrarySettings);
            if (maps.Length > 0) OpenPath(maps[^1]);
        });
        view.RequestSkinPreference = () => view.LibrarySettings.Save();
        view.RequestDefaultSkinArchive = () => FileOperation(() =>
        {
            var archive = SkinFileDialog.SelectArchive(hwnd);
            if (archive is not null) view.SetDefaultSkinArchive(archive);
        });
        view.RequestOpenExternalPath = path => FileOperation(() => LibraryOperations.OpenExternalPath(path));
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
                ConfirmDiscard(() =>
                {
                    var path = MapFileDialog.Select(hwnd, false, L.Get("library.importFile"), MapFileDialog.OpenFilter);
                    if (path is not null) OpenPath(path);
                });
            }
        });
        view.RequestLibraryOpen = map => FileOperation(() =>
        {
            if (view.TryResumeLibraryProject(map)) return;
            ConfirmDiscard(() =>
            {
                view.LoadWorkspace(LibraryOperations.Open(map, view.LibrarySettings), checkAdditionalDifficulties: true);
                ResetAudio(); if (!string.IsNullOrWhiteSpace(view.Document.AudioPath)) audio.Load(view.Document.AudioPath);
            });
        });
        view.RequestLibraryDelete = map =>
        {
            if (map.ProjectPath is not { } project) return;
            view.ShowDeleteProjectConfirmation(confirmed =>
            {
                if (!confirmed) return;
                FileOperation(() => { LibraryOperations.DeleteProject(project, view.LibrarySettings); view.RefreshLibrary(); });
            });
        };
        view.RequestLibraryOszExport = map => FileOperation(() =>
        {
            string filename = WorkspaceProject.SafeName(map.Artist + " - " + map.Title) + ".osz";
            var path = MapFileDialog.Select(hwnd, true, L.Get("library.exportOsz"), MapFileDialog.OszFilter, filename, "osz");
            if (path is null) return;
            LibraryOperations.ExportOsz(LibraryOperations.ExportProject(map), path, view.CompensateTinyDroplets);
            view.SetNotice(L.Get("library.exported", path));
        });
        view.RequestOszExport = () => FileOperation(() =>
        {
            var project = view.CaptureProject();
            string filename = WorkspaceProject.SafeName(project.Name) + ".osz";
            var path = MapFileDialog.Select(hwnd, true, L.Get("library.exportOsz"), MapFileDialog.OszFilter, filename, "osz");
            if (path is null) return;
            LibraryOperations.ExportOsz(project, path, view.CompensateTinyDroplets);
            view.SetNotice(L.Get("library.exported", path));
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
            if (!view.PrepareFileOperation()) return;
            if ((overwrite || view.WorkspaceSession is null) && !view.SaveWorkspace()) return;
            if (view.WorkspaceSession is null) return;
            var project = view.CaptureProject();
            var plan = WorkspaceExport.Plan(view.WorkspaceSession, project.Difficulties[view.ActiveDifficultyIndex], view.LibrarySettings.Songs, overwrite, name, view.CompensateTinyDroplets);
            LibraryOperations.Export(view.WorkspaceSession, project, plan);
            view.LibraryExportFinished(plan); view.SetNotice(L.Get("library.exported", plan.Target));
        });
    }
}
