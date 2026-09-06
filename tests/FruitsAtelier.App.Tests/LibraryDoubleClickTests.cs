using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryDoubleClickTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-library-click-" + Guid.NewGuid());
        try
        {
            string workspace = Path.Combine(root, "Workspace");
            var session = WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments([new MapDocument { Name = "Click project", IsDemo = false }]), "");
            var view = new EditorView();
            view.InitializeLibrary(true, new LibrarySettings { Workspace = workspace });
            view.ChangeAudioPath("unsaved.ogg");
            var canvas = new RecordingCanvas();
            int opens = 0; LibraryMap? target = null;
            view.RequestLibraryOpen = map => { opens++; target = map; };
            Settle(); Click("library.projects"); Settle();
            var title = canvas.Texts.Single(t => t.Value == "Click project" && t.X < 600);
            float x = title.X + 2, y = title.Y + 2;
            view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0);
            Check(opens == 0, "single click only selects");
            view.PointerDoubleClick(x, y, false, false);
            Check(opens == 1 && target?.ProjectPath == session.Directory, "double click invokes the existing project-open callback exactly once");
            Check(view.IsDirty && view.Document.AudioPath == "unsaved.ogg", "double click leaves discard decisions and document replacement to the host");
            view.PointerDoubleClick(220, 110, false, false);
            view.PointerDoubleClick(20, 190, false, false);
            view.PointerDoubleClick(230, 600, false, false);
            Check(opens == 1, "search, sidebar and blank area do not open projects");
            Click("library.settings"); Settle();
            view.PointerDoubleClick(x, y, false, false);
            Check(opens == 1, "settings cannot activate a hidden card");
            view.CloseLibrary(); view.ShowLibrary(); Settle();
            Click("library.all"); Settle();
            view.PointerDoubleClick(x, y, false, false);
            Check(opens == 1, "empty search results cannot activate stale cards");

            void Settle()
            {
                for (int i = 0; i < 200; i++)
                {
                    canvas.Clear(); view.Render(canvas, 980, 620);
                    if (i > 20 && !view.LibraryLoading) return;
                    Thread.Sleep(20);
                }
                throw new Exception("Library did not settle");
            }
            void Click(string key)
            {
                var text = canvas.Texts.Single(t => t.Value == L.Get(key));
                view.PointerDown(text.X + 2, text.Y + 2, 0, false, false); view.PointerUp(text.X + 2, text.Y + 2, 0);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
