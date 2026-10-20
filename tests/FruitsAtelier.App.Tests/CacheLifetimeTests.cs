using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class CacheLifetimeTests
{
    public static void Run()
    {
        var view = new EditorView(false);
        view.InitializeLibrary(true, new LibrarySettings
        {
            Workspace = Path.GetFullPath("artifacts/tests/cache-lifetime/" + Guid.NewGuid().ToString("N")),
            FirstRunSetupCompleted = true
        });
        var canvas = new RecordingCanvas();
        try
        {
            for (int round = 0; round < 2; round++)
            {
                var references = WarmAndClose(view, canvas);
                for (int i = 0; i < 10; i++) { canvas.Clear(); view.Render(canvas, 1440, 900); Thread.Sleep(10); }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                if (references.Any(r => r.IsAlive)) throw new Exception("Closed difficulty retains its document, conversion or exported read-back.");
                if (!view.LibraryVisible || view.HasEditorProject) throw new Exception("Closing did not return to the library.");
            }
        }
        finally { view.StopFileMonitoring(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] WarmAndClose(EditorView view, RecordingCanvas canvas)
    {
        var map = OsuCanvasRenderTests.Fixture();
        view.LoadDocument(map); view.CloseLibrary();
        var timer = Stopwatch.StartNew();
        do { canvas.Clear(); view.Render(canvas, 1440, 900); Thread.Sleep(10); }
        while (view.StarRatingsRefreshing && timer.Elapsed.TotalSeconds < 10);
        if (view.StarRatingsRefreshing) throw new TimeoutException("Rating worker did not finish.");
        view.ToggleOsuCanvas(); canvas.Clear(); view.Render(canvas, 1440, 900);
        view.KeyDown('A', true, false); view.KeyUp('A');
        view.KeyDown('C', true, false); view.KeyUp('C');
        var output = (OsuWriteResult)typeof(EditorView).GetField("playableExport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var references = new[] { new WeakReference(view.Document), new WeakReference(view.Conversion), new WeakReference(output.ReadBack) };
        if (view.IsDirty) throw new Exception("Warming presentation caches changed content.");
        view.ShowLibrary();
        return references;
    }
}
