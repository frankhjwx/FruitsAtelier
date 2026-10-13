using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryOpeningTests
{
    public static void Run()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-open-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var project = BeatmapProject.FromDocuments([new MapDocument { Name = "Open fixture", IsDemo = false }]);
            var session = WorkspaceProject.Create(root, project, "");
            var map = new LibraryMap("", "", "", "", "", "", "", "", "", "", "", "", session.Directory);
            var gate = typeof(WorkspaceProject).GetField("Gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var view = new EditorView(false);
            var before = view.Document;
            var canvas = new RecordingCanvas();
            int completed = 0;
            lock (gate)
            {
                var timer = Stopwatch.StartNew();
                view.BeginLibraryOpen(map, () => completed++);
                view.BeginLibraryOpen(map, () => completed += 100);
                view.Render(canvas, 980, 620);
                Check(timer.Elapsed < TimeSpan.FromSeconds(2), "UI returns while the workspace gate is held");
                Check(view.LibraryOpening && ReferenceEquals(before, view.Document), "pending load retains the current document");
                Check(view.SynchronizationNeedsRedraw, "pending load keeps the loading overlay animated");
                Check(!view.PrepareFileOperation(), "pending load blocks competing file operations");
                Check(canvas.Texts.Any(t => t.Value == L.Get("library.opening")), "pending load draws localized feedback");
                view.ShowLibrary();
                Check(completed == 0, "completion waits for the worker and UI publication");
            }
            Pump(view, canvas);
            Check(completed == 1 && view.WorkspaceSession?.Directory == session.Directory, "one result publishes and completes once");

            view = new EditorView(false); before = view.Document;
            view.BeginLibraryOpen(map with { ProjectPath = Path.Combine(root, "missing") }, () => completed++);
            Pump(view, canvas);
            Check(view.ErrorVisible && ReferenceEquals(before, view.Document) && completed == 1, "failed open retains the document and skips completion");

            view = new EditorView(false);
            Task retired;
            lock (gate)
            {
                view.BeginLibraryOpen(map, () => completed++);
                retired = (Task)typeof(EditorView).GetField("libraryOpenTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
                view.LoadProject(project);
            }
            Check(retired.Wait(TimeSpan.FromSeconds(10)), "retired worker finishes");
            view.Render(canvas, 980, 620);
            Check(!view.LibraryOpening && view.WorkspaceSession is null && completed == 1, "project replacement retires a pending open");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Pump(EditorView view, RecordingCanvas canvas)
    {
        var timer = Stopwatch.StartNew();
        while (view.LibraryOpening && timer.Elapsed < TimeSpan.FromSeconds(10))
        { view.Render(canvas, 980, 620); Thread.Sleep(10); }
        Check(!view.LibraryOpening, "open completes");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
}
