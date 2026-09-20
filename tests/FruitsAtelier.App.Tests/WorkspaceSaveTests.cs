using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class WorkspaceSaveTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale);
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/workspace-save", Guid.NewGuid().ToString("N")));
                string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace");
                ui.View.LibrarySettings.Songs = songs;
                ui.View.NewProject();
                int exports = 0;
                ui.View.RequestSave = ui.View.SaveCurrentDifficulty;
                ui.View.RequestWorkspaceExport = (_, _) => exports++;
                ui.Key('S', ctrl: true);
                Check(ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible && !ui.View.IsDirty, "Workspace saves before the optional export prompt");
                string directory = ui.View.WorkspaceSession!.Directory;
                Check(WorkspaceProject.Open(directory).Project.Difficulties[0].Document.ContentEquals(ui.View.Document), "Workspace content is already persisted");
                var before = ui.View.Document.DeepClone();
                ui.Key(46); ui.Key('S', ctrl: true); ui.Key(116);
                Check(ui.View.Document.ContentEquals(before) && !ui.View.IsTestplaying && exports == 0, "Prompt blocks editor commands and repeated saves");
                ui.ClickText(L.Get("library.workspaceOnly"));
                Check(!ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible && Directory.GetFiles(songs, "*", SearchOption.AllDirectories).Length == 0,
                    "Keeping the workspace completes save without touching Songs");
                ui.Key('S', ctrl: true); ui.Key(27);
                Check(!ui.View.DiscardConfirmationVisible && !ui.View.IsDirty, "Escape keeps the completed save");
                ui.Key('S', ctrl: true); ui.ClickText(L.Get("library.exportToSongs"));
                Check(ui.View.ExportVisible && !ui.View.DiscardConfirmationVisible && exports == 0, "Consent opens export choices before writing Songs");
                ui.Key(27);
                Check(!ui.View.ExportVisible && !ui.View.IsDirty && ui.View.WorkspaceSession.Directory == directory, "Cancelling export preserves the workspace save");
                var entry = ui.View.WorkspaceSession.Manifest.Difficulties[0];
                entry.ExportTarget = Path.Combine(songs, "deleted.osu"); entry.ExportHash = "old";
                ui.Key('S', ctrl: true);
                Check(ui.View.DiscardConfirmationVisible && exports == 0, "Missing linked exports offer export after saving locally");
                ui.Key(27);
                ui.View.LibrarySettings.Songs = "";
                ui.Key('S', ctrl: true);
                Check(!ui.View.DiscardConfirmationVisible && !ui.View.ExportVisible, "Unconfigured Songs saves to workspace directly");
                ui.Key('S', ctrl: true, shift: true);
                Check(ui.View.WorkspaceSession.Directory == directory && exports == 0, "Save uses one workspace identity");
                ui.ClickText(L.Get("ui.file"));
                Check(!ui.Canvas.Texts.Any(t => t.Value.Contains("Shift + S")), "File menu exposes supported save actions");
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
