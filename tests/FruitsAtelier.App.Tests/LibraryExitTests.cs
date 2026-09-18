using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryExitTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-library-exit-" + Guid.NewGuid());
        var view = new EditorView();
        view.LibrarySettings.Workspace = root; view.LibrarySettings.Songs = "";
        var canvas = new RecordingCanvas();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Answer(string key)
        {
            canvas.Clear(); view.Render(canvas, 980, 620);
            var text = canvas.Texts.Last(t => t.Value == L.Get(key));
            view.PointerDown(text.X + 4, text.Y + 4, 0, false, false);
            view.PointerUp(text.X + 4, text.Y + 4, 0);
        }
        try
        {
            view.NewProject(); view.SaveWorkspace();
            string directory = view.WorkspaceSession!.Directory;
            view.ChangeAudioPath("unsaved.ogg");
            view.ShowLibrary();
            Check(view.DiscardConfirmationVisible && !view.LibraryVisible, "Leaving a dirty editor prompts immediately");
            Answer("mac.cancel");
            Check(view.HasEditorProject && view.IsDirty && !view.LibraryVisible, "Cancel keeps the editing session");
            view.ShowLibrary(); Answer("mac.discard");
            Check(view.LibraryVisible && !view.HasEditorProject && !view.IsDirty && view.WorkspaceSession is null, "Discard closes the editing session");
            view.CloseLibrary();
            Check(view.LibraryVisible, "Library cannot resume the closed editor");
            view.LoadWorkspace(WorkspaceProject.Open(directory));
            Check(view.Document.AudioPath != "unsaved.ogg", "Discard leaves saved content unchanged");
            view.ChangeAudioPath("saved.ogg");
            view.ShowLibrary(); Answer("mac.save");
            Check(view.LibraryVisible && !view.HasEditorProject, "Save closes the editor after persistence");
            view.LoadWorkspace(WorkspaceProject.Open(directory));
            Check(view.Document.AudioPath?.EndsWith("saved.ogg") == true, "Reopening reads the saved edit");
            view.ShowLibrary();
            Check(view.LibraryVisible && !view.DiscardConfirmationVisible, "A clean editor exits without a prompt");
            view.NewProject(); view.CloseLibrary(); view.ChangeAudioPath("failed.ogg");
            string blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "blocked");
            view.LibrarySettings.Workspace = blocked;
            view.ShowLibrary(); Answer("mac.save");
            Check(view.ErrorVisible && view.HasEditorProject && view.IsDirty && !view.LibraryVisible, "A failed save keeps edits in the editor");
        }
        finally
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (view.LibraryLoading && DateTime.UtcNow < deadline) Thread.Sleep(10);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
