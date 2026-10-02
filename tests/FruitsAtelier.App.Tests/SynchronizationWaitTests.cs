using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class SynchronizationWaitTests
{
    private static FieldInfo Field(string name) => typeof(EditorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static void Run()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            foreach (Action<Ui> blocked in new Action<Ui>[] {
                ui => ui.View.PrepareFileOperation(), ui => ui.View.SwitchDifficulty(0),
                ui => ui.Key('Z', ctrl: true), ui => ui.View.PointerDown(300, 300, 0, false, false),
                ui => ui.View.Wheel(300, 300, 120, false), ui => ui.View.TextInput('x'),
                ui => ui.View.OpenSettings(), ui => ui.View.SaveCurrentDifficulty(), ui => ui.View.ShowDeleteDifficulty(0) })
            {
                var ui = new Ui(false); ui.View.NewProject(); ui.Paint();
                var before = ui.View.Document.DeepClone();
                Field("syncCommitTask").SetValue(ui.View, new TaskCompletionSource<WorkspaceSession>().Task);
                ui.Paint();
                Check(!ui.View.SynchronizationWaitVisible, "Background sync must not open the waiting overlay.");
                ui.View.PointerMove(310, 310, false, false); ui.Paint();
                Check(!ui.View.SynchronizationWaitVisible, "Hover must not open the waiting overlay.");
                blocked(ui); ui.Paint();
                Check(ui.View.SynchronizationWaitVisible && ui.Canvas.Texts.Any(t => t.Value == L.Get("sync.waitHelp")),
                    "Every blocked operation must show the localized waiting overlay.");
                Check(ui.View.Document.ContentEquals(before), "Blocked input must not change authoring.");
                Field("syncCommitTask").SetValue(ui.View, null); ui.Paint();
                Check(!ui.View.SynchronizationWaitVisible, "The waiting overlay must disappear when sync is idle.");
            }
            foreach (bool fail in new[] { false, true })
            {
                var ui = new Ui(false); ui.View.NewProject();
                ui.View.LibrarySettings.Workspace = Path.GetFullPath(Path.Combine("artifacts/tests/sync-wait", Guid.NewGuid().ToString("N")));
                ui.View.LibrarySettings.Songs = "";
                Check(ui.View.BeginWorkspaceSave(), "Save starts.");
                var actual = (Task<WorkspaceSession>)Field("workspaceSaveTask").GetValue(ui.View)!;
                var gate = new TaskCompletionSource<WorkspaceSession>();
                Field("workspaceSaveTask").SetValue(ui.View, gate.Task);
                ui.Paint(); Check(!ui.View.SynchronizationWaitVisible, "Ordinary saves stay unobtrusive.");
                ui.View.ShowLibrary(); ui.Paint();
                Check(ui.View.SynchronizationWaitVisible && !ui.View.LibraryVisible, "Library waits for saving.");
                bool? duplicate = null;
                ui.View.WaitForSynchronization(ready => duplicate = ready);
                Check(duplicate == false, "A second pending exit does not replace the first.");
                Check(actual.Wait(TimeSpan.FromSeconds(15)), "Isolated save finishes.");
                if (fail) gate.SetException(new IOException("Injected save failure")); else gate.SetResult(actual.Result);
                ui.Paint();
                Check(!ui.View.SynchronizationWaitVisible && ui.View.LibraryVisible == !fail && ui.View.ErrorVisible == fail,
                    "A successful save resumes Library; a failed save keeps the map and shows the error.");
                ui.View.StopFileMonitoring();
            }
        }
        L.SetLanguage("en");
    }
}
