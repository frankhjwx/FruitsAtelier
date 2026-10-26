using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class CacheLifetimeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed record Retained(string Name, WeakReference Reference);

    public static void Run()
    {
        var view = new EditorView(false);
        view.InitializeLibrary(true, new LibrarySettings
        {
            Workspace = Path.GetFullPath("artifacts/tests/cache-lifetime/" + Guid.NewGuid().ToString("N")),
            FirstRunSetupCompleted = true
        });
        var canvas = new RecordingCanvas();
        var failures = new List<string>();
        try
        {
            foreach (bool replace in new[] { false, true })
            for (int round = 0; round < 2; round++)
            {
                var references = WarmAndRetire(view, canvas, replace);
                Settle(view, canvas);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var retained = references.Where(r => r.Reference.IsAlive).Select(r => r.Name).ToArray();
                Console.WriteLine($"Cache retirement {(replace ? "replacement" : "library")} round {round + 1}: {references.Length - retained.Length}/{references.Length} warmed objects collectible; retained: {string.Join(", ", retained)}");
                if (retained.Length != 0) failures.Add($"{(replace ? "replacement" : "library")} round {round + 1}: " + string.Join(", ", retained));
                if (replace ? view.LibraryVisible || !view.HasEditorProject : !view.LibraryVisible || view.HasEditorProject)
                    throw new Exception("Difficulty retirement did not reach the requested destination.");
            }
            if (failures.Count != 0) throw new Exception("Retired difficulty retains warmed data: " + string.Join("; ", failures));
        }
        finally { view.StopFileMonitoring(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Retained[] WarmAndRetire(EditorView view, RecordingCanvas canvas, bool replace)
    {
        var map = OsuCanvasRenderTests.Fixture();
        map.DurationMs = 20000;
        map.Fruits.Add(new() { TimeMs = 19000, X = 300, Y = 192 });
        map.CollaborationEditableRanges = [new(0, 20000)];
        OsuTimeline.AddBreak(map, 6800, 18000);
        view.LibrarySettings.LazerEditing = true;
        view.LoadDocument(map); view.CloseLibrary();
        Settle(view, canvas);
        if (!view.IsOsuCanvas) view.ToggleOsuCanvas();
        view.UpdateTransport(2500, 20000, true, false, false, null, null);
        Paint(view, canvas);
        Invoke(view, "EnsureOsuDistanceReferences");
        Invoke(view, "OsuObjectSnapTargets");
        var geometry = ((IEnumerable)Field(view, "osuCanvasObjects")).Cast<object>()
            .Select(item => item.GetType().GetField("Path")!.GetValue(item)!)
            .First(path => ((IEnumerable)path).Cast<object>().Any());
        if (view.IsOsuCanvas) view.ToggleOsuCanvas();
        view.UpdateTransport(9500, 20000, true, false, false, null, null);
        Paint(view, canvas);
        view.KeyDown('A', true, false); view.KeyUp('A');
        view.KeyDown('C', true, false); view.KeyUp('C');
        Invoke(view, "EnsureLazerGuideMovement", (object?)null);
        Invoke(view, "CanSelectCollaborationObject", map.Fruits[0].Id);
        typeof(EditorView).GetProperty("movementAnalysis", Private)!.SetValue(view, true);
        view.KeyDown('2', false, false); view.KeyUp('2');
        float x = view.PlayfieldBounds.X + 250f / 512 * view.PlayfieldBounds.Width;
        float y = view.CanvasPlotBounds.Bottom - (float)((10000 - view.ViewStartMs) * view.PixelsPerMs);
        view.PointerMove(x, y, false, false);
        Paint(view, canvas);
        Invoke(view, "MovementBreakPeriods");
        if (((IReadOnlyList<ConvertedCatchObject>)Field(view, "placementBreakObjects")).Count == 0
            || ((Array)Field(view, "lazerGuideMovementContexts")).Length == 0)
            throw new Exception("Retirement fixture did not warm placement and lazer contexts.");
        var labels = ((IEnumerable)Field(view, "distanceLayout")).Cast<object>().ToArray();
        if (labels.Length == 0) throw new Exception("Retirement fixture did not create distance labels.");
        var output = (OsuWriteResult)Field(view, "playableExport");
        var references = new List<Retained>
        {
            Capture("document", view.Document), Capture("conversion", view.Conversion), Capture("read-back", output.ReadBack),
            Capture("sampled standard geometry", geometry), Capture("distance label", labels[0])
        };
        foreach (string name in new[] { "distanceLayoutSource", "distanceLayoutAuthoredSource", "distanceLayoutTiming", "distanceLayoutBreaks",
            "placementBreakIntervalSource", "placementBreakObjects", "placementBreakPreview", "lazerGuideMovementSource",
            "lazerGuideMovementObjects", "lazerGuideMovementContexts", "osuDistanceSource", "osuObjectSnapSource",
            "collaborationSelectionConversion", "placementOrder", "movementStates", "movementIndices" }) references.Add(Capture(name, Field(view, name)));
        if (view.IsDirty || !map.ContentEquals(view.Document)) throw new Exception("Warming presentation caches changed content.");
        if (replace)
        {
            view.LoadDocument(new MapDocument { Name = "Replacement", DurationMs = 2000, IsDemo = false });
            Paint(view, canvas);
            if (view.Conversion.Objects.Count != 0 || view.Document.Name != "Replacement")
                throw new Exception("Replacement reused retired playable content.");
        }
        else view.ShowLibrary();
        return references.ToArray();
    }

    private static Retained Capture(string name, object value)
    {
        if (value is IEnumerable items && !items.Cast<object>().Any())
            throw new Exception("Warm path retained an empty collection for " + name);
        return new(name, new WeakReference(value));
    }
    private static object Field(EditorView view, string name) => typeof(EditorView).GetField(name, Private)!.GetValue(view)
        ?? throw new Exception("Warm path did not populate " + name);
    private static object? Invoke(EditorView view, string name, params object?[] args) => typeof(EditorView).GetMethod(name, Private)!.Invoke(view, args);
    private static void Paint(EditorView view, RecordingCanvas canvas) { canvas.Clear(); view.Render(canvas, 1440, 900); }
    private static void Settle(EditorView view, RecordingCanvas canvas)
    {
        var timer = Stopwatch.StartNew();
        do { Paint(view, canvas); Thread.Sleep(10); }
        while (view.StarRatingsRefreshing && timer.Elapsed.TotalSeconds < 10);
        if (view.StarRatingsRefreshing) throw new TimeoutException("Rating worker did not finish.");
        for (int i = 0; i < 10; i++) { Paint(view, canvas); Thread.Sleep(10); }
    }
}
