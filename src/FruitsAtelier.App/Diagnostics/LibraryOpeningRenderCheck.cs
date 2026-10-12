using System.Diagnostics;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class LibraryOpeningRenderCheck
{
    internal static void Run(D2DCanvas canvas)
    {
        string root = Path.GetFullPath(Path.Combine("artifacts", "tests", "library-opening", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var session = WorkspaceProject.Create(root, BeatmapProject.FromDocuments([new MapDocument { Name = "Library open", IsDemo = false }]), "");
        var map = new LibraryMap("", "", "", "", "", "", "", "", "", "", "", "", session.Directory);
        var gate = typeof(WorkspaceProject).GetField("Gate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (1440, 900), (980, 620) })
            {
                L.SetLanguage(locale);
                var view = new EditorView(false);
                view.InitializeLibrary(true, new LibrarySettings { Workspace = root });
                canvas.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                int completed = 0;
                lock (gate)
                {
                    var timer = Stopwatch.StartNew();
                    view.BeginLibraryOpen(map, () => completed++);
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                    view.PointerDown(50, 50, 0, false, false); view.PointerUp(50, 50, 0);
                    if (!view.LibraryOpening || view.HasEditorProject || completed != 0 || view.PrepareFileOperation())
                        throw new InvalidOperationException("Pending library open did not isolate input.");
                    if (timer.Elapsed > TimeSpan.FromSeconds(2))
                        throw new InvalidOperationException("Pending library open blocked native rendering.");
                    AppLog.Write($"Library opening native pending frame passed: {locale}, {size}, DPI={dpi}, {timer.Elapsed.TotalMilliseconds:F2} ms.");
                }
                var deadline = Stopwatch.StartNew();
                while (view.LibraryOpening && deadline.Elapsed < TimeSpan.FromSeconds(10))
                { Thread.Sleep(10); canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End(); }
                if (view.LibraryOpening || completed != 1 || view.WorkspaceSession?.Directory != session.Directory)
                    throw new InvalidOperationException("Library open result did not publish on the UI thread.");
            }
            AppLog.Write("Library opening native check passed: 12 loading/input/publication cases.");
        }
        finally { L.SetLanguage(language); }
    }
}
