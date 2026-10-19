using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DirtyExitTests
{
    public static void Run()
    {
        foreach (string action in new[] { "mac.cancel", "mac.discard", "mac.save" })
        {
            string root = Path.GetFullPath(Path.Combine("artifacts/tests/dirty-exit", Guid.NewGuid().ToString("N")));
            string songs = Path.Combine(root, "Songs"), source = Path.Combine(songs, "set", "map.osu");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "osu file format v14\n[General]\nMode:2\n[Metadata]\nTitle:Exit\nArtist:Artist\nVersion:Test\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,0,0:0:0:0:\n");
            var ui = new Ui(false);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); ui.View.LibrarySettings.Songs = songs;
            var session = WorkspaceProject.Create(ui.View.LibrarySettings.Workspace, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(source)]), songs);
            session.Manifest.Difficulties[0].ExportConfirmed = true;
            WorkspaceProject.Save(session, session.Project);
            ui.View.LoadWorkspace(session); SynchronizationUiTests.Wait(ui);
            string native = Path.Combine(session.Directory, session.Manifest.Difficulties[0].File);
            string nativeHash = WorkspaceProject.Hash(native), externalHash = WorkspaceProject.Hash(source);
            var history = (EditorHistory)typeof(EditorView).GetProperty("history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ui.View)!;
            history.Begin("Move fruit"); history.Document.Fruits[0].X = 180; history.Commit();
            ui.View.RefreshSynchronization(quiet: true); SynchronizationUiTests.Wait(ui);
            Check(ui.View.IsDirty && !ui.View.SynchronizationVisible && !ui.View.SynchronizationWaitVisible, "background checks keep edits dirty without a modal");
            Check(WorkspaceProject.Hash(native) == nativeHash && WorkspaceProject.Hash(source) == externalHash, "background checks do not save either file");
            ui.View.ShowLibrary(); ui.Paint();
            Check(ui.View.DiscardConfirmationVisible && !ui.View.LibraryVisible, "dirty exit asks before leaving");
            ui.ClickText(L.Get(action)); SynchronizationUiTests.Wait(ui);
            if (action == "mac.cancel")
            {
                Check(ui.View.IsDirty && !ui.View.LibraryVisible, "cancel keeps editing");
                File.WriteAllText(source, File.ReadAllText(source).Replace("100,192", "220,192"));
                ui.View.RefreshSynchronization(quiet: true); SynchronizationUiTests.Wait(ui);
                Check(!ui.View.SynchronizationVisible && !ui.View.SynchronizationWaitVisible && ui.View.IsDirty, "external conflicts do not interrupt dirty editing");
                ui.Key('2'); ui.ClickMap(500, 190);
                Check(ui.View.Document.Fruits.Count == 2 && !ui.View.SynchronizationVisible, "normal placement remains available after background conflicts");
                ui.View.ShowLibrary(); ui.Paint(); ui.ClickText(L.Get("mac.discard")); SynchronizationUiTests.Wait(ui);
                Check(ui.View.LibraryVisible && WorkspaceProject.Hash(native) == nativeHash, "discard remains available with external conflicts");
            }
            else
            {
                Check(ui.View.LibraryVisible, "save or discard leaves the editor");
                Check(ProjectSerializer.ReadFile(native).Fruits[0].X == (action == "mac.save" ? 180 : 100), "only Save persists dirty edits");
                Check(WorkspaceProject.Hash(source) == externalHash, "exit does not silently publish dirty content");
            }
            ui.View.StopFileMonitoring();
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}

