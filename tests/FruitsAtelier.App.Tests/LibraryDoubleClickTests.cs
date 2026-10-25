using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryDoubleClickTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-library-click-" + Guid.NewGuid());
        EditorView? view = null;
        try
        {
            string workspace = Path.Combine(root, "Workspace");
            var session = WorkspaceProject.Create(workspace, BeatmapProject.FromDocuments([MyMapsTests.Document("Click project", "Mapper")]), "");
            view = new EditorView(loadDemo: false);
            view.InitializeLibrary(true, new LibrarySettings { Workspace = workspace, MapperUsernames = ["Mapper"] });
            var canvas = new RecordingCanvas();
            int opens = 0; LibraryMap? target = null; bool selectedDifficulty = false;
            view.RequestLibraryOpen = (map, selectDifficulty) => { opens++; target = map; selectedDifficulty = selectDifficulty; };
            Settle(); Click("library.myMaps"); Settle();
            var title = canvas.Texts.Single(t => t.Value == "Click project" && t.X < 600);
            float x = title.X + 2, y = title.Y + 2;
            view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0);
            Check(opens == 0, "single click only selects");
            view.PointerDoubleClick(x, y, false, false);
            Check(opens == 1 && target?.ProjectPath == session.Directory, "double click invokes the existing project-open callback exactly once");
            Check(!view.HasEditorProject && !view.IsDirty, "double click leaves project loading to the host");
            Check(!selectedDifficulty, "card opening does not request a particular difficulty");
            Settle();
            var difficulty = canvas.Texts.Single(t => t.X == 692 && t.Y == 337);
            view.PointerDown(difficulty.X + 2, difficulty.Y + 2, 0, false, false);
            view.PointerUp(difficulty.X + 2, difficulty.Y + 2, 0);
            Check(opens == 2 && selectedDifficulty && target?.Path == Path.Combine(session.Directory, session.Manifest.Difficulties[0].File),
                "single click on a detail row requests its exact difficulty");
            opens = 1;
            string? openedPath = null;
            view.RequestOpenExternalPath = path => openedPath = path;
            view.PointerDown(x, y, 2, false, false); Settle();
            Click("library.openProjectFolder");
            Check(openedPath == session.Directory, "context menu opens the clicked project's folder");
            openedPath = null;
            view.PointerDown(x, y, 2, false, false); Settle();
            Click("project.openSongsFolder");
            Check(openedPath is null, "a project without a source cannot open a Songs folder");
            view.KeyDown(27, false, false);
            string sourceFolder = Path.Combine(root, "Songs", "Source set");
            Directory.CreateDirectory(sourceFolder);
            session.Manifest.ExternalSourceDirectory = sourceFolder;
            WorkspaceProject.Save(session, session.Project);
            view.PointerDown(x, y, 2, false, false); Settle();
            Click("project.openSongsFolder");
            Check(openedPath == sourceFolder, "context menu resolves the associated source folder from the manifest");
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
                    if (!view.LibraryLoading) return;
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
        finally
        {
            view?.StopFileMonitoring(); view?.CompleteLibraryCacheWrites(); view?.CloseLibrary();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
