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
        var session = WorkspaceProject.Create(root, BeatmapProject.FromDocuments([new MapDocument { Name = "Library open", IsDemo = false }, new MapDocument { Name = "Second", IsDemo = false }]), "");
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
                WaitForLibrary();
                view.PointerDown(40, 150, 0, false, false); view.PointerUp(40, 150, 0);
                WaitForLibrary();
                var ratings = (Dictionary<string, double?>)typeof(EditorView).GetField("libraryRatings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                foreach (var entry in session.Manifest.Difficulties) ratings[Path.Combine(session.Directory, entry.File)] = 8.75;
                canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                canvas.End(capture: () =>
                {
                    var frame = canvas.CapturePixels();
                    string folder = Path.GetFullPath("artifacts/tests/library-opening");
                    using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"details-{locale}-{size.Item1}-{dpi}.bmp")));
                    writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                    writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height);
                    writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(frame.Pixels.Length);
                    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                });
                int completed = 0;
                view.RequestLibraryOpen = (selected, selectDifficulty) => view.BeginLibraryOpen(selected, () => completed++, selectDifficulty);
                void WaitForLibrary()
                {
                    for (int i = 0; i < 400; i++)
                    {
                        canvas.Begin(); view.Render(canvas, size.Item1, size.Item2); canvas.End();
                        if (i > 30 && !view.LibraryLoading) return;
                        Thread.Sleep(10);
                    }
                    throw new InvalidOperationException("Native library details did not settle.");
                }
                lock (gate)
                {
                    var timer = Stopwatch.StartNew();
                    view.PointerDown(size.Item1 - 280, 377, 0, false, false);
                    view.PointerUp(size.Item1 - 280, 377, 0);
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
                if (view.LibraryOpening || completed != 1 || view.WorkspaceSession?.Directory != session.Directory || view.ActiveDifficultyIndex != 1)
                    throw new InvalidOperationException("Library open result did not publish on the UI thread.");
            }
            AppLog.Write("Library opening native check passed: 12 loading/input/publication cases.");
        }
        finally { L.SetLanguage(language); }
    }
}
