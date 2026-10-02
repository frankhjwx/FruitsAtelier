using System.Globalization;
using FruitsAtelier.App.Editor;

internal static class PerformanceMetricsTests
{
    public static void Run()
    {
        var metrics = new EditorPerformanceMetrics();
        metrics.Record(EditorPerformanceStage.InputDispatch, 100);
        if (metrics.Drain() is not null) throw new Exception("Disabled metrics recorded a sample.");
        metrics.Enabled = true;
        metrics.Record(EditorPerformanceStage.InputDispatch, 2);
        if (metrics.Drain() is not null) throw new Exception("Fast-only interval generated a log.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            metrics.Record(EditorPerformanceStage.InputDispatch, 16);
            metrics.Record(EditorPerformanceStage.InputDispatch, 4);
            string text = metrics.Drain() ?? throw new Exception("Slow interval was lost.");
            if (!text.Contains("InputDispatch=2/10.00/16.00/1;")) throw new Exception(text);
            if (metrics.Drain() is not null) throw new Exception("Samples survived draining.");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        foreach (var stage in new[] { EditorPerformanceStage.MessageDispatch, EditorPerformanceStage.MessageWait,
            EditorPerformanceStage.FrameWait, EditorPerformanceStage.FrameGap, EditorPerformanceStage.LogWrite })
        {
            metrics.Record(stage, 800);
            string stalled = metrics.Drain() ?? throw new Exception($"{stage} stall was lost.");
            if (!stalled.Contains($"{stage}=1/800.00/800.00/1;")) throw new Exception(stalled);
            if (metrics.Drain() is not null) throw new Exception("Stall samples survived draining.");
        }

        var view = new EditorView();
        view.Performance.Enabled = true;
        var before = view.Document.DeepClone();
        var canvas = new RecordingCanvas();
        view.Render(canvas, 1440, 900);
        view.Render(canvas, 1440, 900);
        view.Performance.Record(EditorPerformanceStage.Frame, 16);
        string report = view.Performance.Drain()!;
        if (!report.Contains("ConversionCheck=") || !report.Contains("ConversionRebuild=1/")
            || !report.Contains("ExportReadback=1/")) throw new Exception(report);
        if (!before.ContentEquals(view.Document) || view.IsDirty) throw new Exception("Diagnostics changed document state.");
        view.NewProject();
    }
}
