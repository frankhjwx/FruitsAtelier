using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class LegacyDropletWorkerTests
{
    private static readonly MethodInfo commit = typeof(EditorView).GetMethod("CommitContentHistory", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo historyProperty = typeof(EditorView).GetProperty("history", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void Run()
    {
        FailedSliderPlacementClearsSelection();
        var ui = new Ui(false);
        ui.LoadDocument(Map(600));
        var before = ui.View.Document.DeepClone();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        Queue(ui.View, document => { document.RandomizeDropletStrength = 8; document.RandomizeDropletSeed = 99; });
        watch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(ui.View.LegacyDropletRefreshBusy, "Legacy refresh did not queue detached validation");
        Check(watch.Elapsed.TotalMilliseconds < 100 && allocated < 32_000_000,
            $"Legacy commit performed full-map fitting on the UI thread: {watch.Elapsed.TotalMilliseconds:F2} ms / {allocated} B");
        var render = Stopwatch.StartNew();
        ui.View.Render(ui.Canvas, ui.Width, ui.Height);
        render.Stop();
        Check(render.Elapsed.TotalMilliseconds < 100, "Rendering waited for Legacy worker completion");
        Check(!ui.View.PrepareFileOperation(), "Unvalidated Legacy content reached file operations");
        Complete(ui);
        Check(ui.View.Document.ImportedSliders.All(s => s.RandomizationTrack is not null), "Validated Legacy baselines were not published");
        var edited = ui.View.Document.DeepClone();
        ui.Key('Z', ctrl: true); Check(ui.View.Document.ContentEquals(before), "Detached Legacy edit was not one undo step");
        ui.Key('Y', ctrl: true); Check(ui.View.Document.ContentEquals(edited), "Detached Legacy redo changed prepared output");
        Console.WriteLine($"Legacy worker submission600: {watch.Elapsed.TotalMilliseconds:F2} ms/{allocated} B; first render {render.Elapsed.TotalMilliseconds:F2} ms.");

        ui.LoadDocument(Map(10)); before = ui.View.Document.DeepClone();
        Queue(ui.View, document => { document.RandomizeDropletStrength = 8; document.ImportedSliders[0].PathType = 'Z'; });
        Complete(ui);
        Check(ui.View.Document.ContentEquals(before) && !History(ui.View).HasActiveTransaction, "Failed detached validation changed source or left history open");

        var breaks = Map(1); breaks.DurationMs = 8000;
        breaks.Fruits.Add(new() { TimeMs = 7000, X = 100 });
        OsuTimeline.AddBreak(breaks, 1700, 6100);
        ui.LoadDocument(breaks);
        Queue(ui.View, document => { document.RandomizeDropletStrength = 8; document.Fruits.Add(new() { TimeMs = 3000, X = 250 }); });
        Complete(ui);
        var warmed = (OsuWriteResult)typeof(EditorView).GetField("playableExport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        Check(!OsuTimeline.Breaks(ui.View.Document).SequenceEqual(OsuTimeline.Breaks(breaks))
            && OsuTimeline.Breaks(ui.View.Document).SequenceEqual(OsuTimeline.Breaks(warmed.ReadBack)),
            "Prepared playback retained breaks from before the transaction");

        ui.LoadDocument(Map(600));
        Queue(ui.View, document => document.RandomizeDropletStrength = 8);
        var owner = History(ui.View);
        owner.Cancel(); owner.Begin("New transaction"); owner.Document.Fruits[0].X = 321;
        ui.View.Render(ui.Canvas, ui.Width, ui.Height);
        Check(!ui.View.LegacyDropletRefreshBusy && owner.HasActiveTransaction && owner.Document.Fruits[0].X == 321,
            "A stale worker cancelled or replaced a newer transaction");
        owner.Cancel();

        Queue(ui.View, document => document.RandomizeDropletStrength = 9);
        var replacement = Map(1); replacement.Name = "Replacement";
        ui.View.LoadDocument(replacement);
        ui.View.Render(ui.Canvas, ui.Width, ui.Height);
        Check(!ui.View.LegacyDropletRefreshBusy && ui.View.Document.ContentEquals(replacement), "Old worker published into a replacement document");
    }

    private static void FailedSliderPlacementClearsSelection()
    {
        var ui = new Ui(false);
        ui.LoadDocument(Map(1));
        ui.View.ToggleOsuCanvas(); ui.Paint();
        var before = ui.View.Document.DeepClone();
        var slider = new ImportedSlider { TimeMs = 3000, X = 100, Y = 192, PixelLength = 100, PathType = 'Z' };
        slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
        Queue(ui.View, document => { document.RandomizeDropletStrength = 8; document.ImportedSliders.Add(slider); });
        typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ui.View, [new[] { slider.Id }, slider.Id]);
        Complete(ui);
        Check(ui.View.Document.ContentEquals(before), "Failed slider placement did not restore content");
        Check(ui.View.SelectedObjectIds.Count == 0, "Failed slider placement retained a removed source selection");
        ui.Paint(); ui.Paint();
        ui.Key('W'); ui.Paint();
        Check(ui.View.Document.ContentEquals(before), "Post-failure assist input edited restored content");
    }

    private static EditorHistory History(EditorView view) => (EditorHistory)historyProperty.GetValue(view)!;
    private static void Queue(EditorView view, Action<MapDocument> change)
    {
        History(view).Begin("Legacy effects");
        change(view.Document);
        commit.Invoke(view, null);
    }
    private static void Complete(Ui ui)
    {
        var timeout = Stopwatch.StartNew();
        while (ui.View.LegacyDropletRefreshBusy && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            ui.View.Render(ui.Canvas, ui.Width, ui.Height);
            Thread.Sleep(1);
        }
        Check(!ui.View.LegacyDropletRefreshBusy, "Legacy worker did not finish");
    }
    private static MapDocument Map(int count)
    {
        var map = new MapDocument { DurationMs = count * 1000 + 5000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        for (int i = 0; i < count; i++)
        {
            var slider = new ImportedSlider { TimeMs = 1000 + i * 1000, X = 100, Y = 192, PixelLength = 192, PathType = 'L', SourceOrder = i };
            slider.ControlPoints.AddRange([new(100, 192), new(200, 192)]);
            map.ImportedSliders.Add(slider);
        }
        map.Fruits.Add(new() { TimeMs = 750, X = 100 });
        return map;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
