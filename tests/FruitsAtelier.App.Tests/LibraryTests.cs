using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-library-ui-" + Guid.NewGuid());
        string songs = Path.Combine(root, "Songs"); Directory.CreateDirectory(songs);
        try
        {
            var view = new EditorView(); view.NewProject();
            view.LibrarySettings.Workspace = Path.Combine(root, "Workspace"); view.LibrarySettings.Songs = songs;
            Check(view.SaveWorkspace(), "save creates workspace project");
            view.ChangeAudioPath(Path.Combine(songs, "missing.mp3"));
            view.SaveWorkspace();
            var canvas = new RecordingCanvas(); view.Render(canvas, 1440, 900);
            Check(canvas.Texts.Any(t => t.Value.Contains("missing.mp3")), "missing references visible inside editor");
            var before = view.CaptureProject();
            view.ShowLibrary(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.title")), "separate library page");
            view.KeyDown(70, false, false); view.PointerDown(700, 480, 0, false, false); view.PointerUp(700, 480, 0);
            Check(view.CaptureProject().Difficulties[0].Document.ContentEquals(before.Difficulties[0].Document), "library input cannot edit map");
            view.CloseLibrary(); view.ShowWorkspaceExport(); canvas.Clear(); view.Render(canvas, 980, 620);
            Check(canvas.Texts.Any(t => t.Value == L.Get("library.exportNew")) && canvas.Texts.Any(t => t.Value == L.Get("library.exportOverride")), "explicit export modes");
            view.CloseLibrary();
            Check(Directory.GetFiles(songs).Length == 0, "navigation and saving never write Songs");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
