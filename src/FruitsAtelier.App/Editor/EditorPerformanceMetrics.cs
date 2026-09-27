using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace FruitsAtelier.App.Editor;

public enum EditorPerformanceStage
{
    InputQueue, InputDispatch, Poll, PrepareFrame, ViewRender, Submit, Frame,
    ConversionCheck, ConversionRebuild, ExportReadback, InputToSubmit
}

// UI-thread counters; recording never allocates or writes to disk.
public sealed class EditorPerformanceMetrics
{
    private struct Sample
    {
        public long Count, Slow;
        public double Total, Max;
    }
    private readonly Sample[] samples = new Sample[Enum.GetValues<EditorPerformanceStage>().Length];
    public bool Enabled { get; set; }
    public long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
    public void End(EditorPerformanceStage stage, long start)
    {
        if (Enabled) Record(stage, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    public void Record(EditorPerformanceStage stage, double milliseconds)
    {
        if (!Enabled) return;
        ref var sample = ref samples[(int)stage];
        sample.Count++;
        sample.Total += milliseconds;
        sample.Max = Math.Max(sample.Max, milliseconds);
        if (milliseconds >= 16) sample.Slow++;
    }
    public string? Drain()
    {
        bool slow = samples.Any(s => s.Slow > 0);
        string? result = null;
        if (slow)
        {
            var text = new StringBuilder("UI performance (ms; count/avg/max/>=16):");
            foreach (var stage in Enum.GetValues<EditorPerformanceStage>())
            {
                var s = samples[(int)stage];
                if (s.Count == 0) continue;
                text.Append(CultureInfo.InvariantCulture, $" {stage}={s.Count}/{s.Total / s.Count:F2}/{s.Max:F2}/{s.Slow};");
            }
            result = text.ToString();
        }
        Array.Clear(samples);
        return result;
    }
}
