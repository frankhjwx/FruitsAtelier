using System.Diagnostics;
using FruitsAtelier.App.Editor;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private long performanceFlush = Stopwatch.GetTimestamp();
    private long pendingInput;
    private double pendingInputQueue;
    private uint slowestInputMessage;
    private double slowestInputMs;
    private int performanceGen0 = GC.CollectionCount(0), performanceGen1 = GC.CollectionCount(1), performanceGen2 = GC.CollectionCount(2);

    private long BeginInputSample(Native.Message message)
    {
        if (message.Window != hwnd || !(message.Id is 0x0100 or 0x0101 or 0x0102 or 0x0104 or 0x0105
            or 0x0200 or 0x0201 or 0x0202 or 0x0203 or 0x0204 or 0x0205 or 0x0207 or 0x0208 or 0x020A)) return 0;
        long start = Stopwatch.GetTimestamp();
        // MSG.time and TickCount share the wrapping 32-bit system uptime clock.
        double queued = Math.Max(0, unchecked((int)((uint)Environment.TickCount - message.Time)));
        view.Performance.Record(EditorPerformanceStage.InputQueue, queued);
        if (pendingInput == 0) { pendingInput = start; pendingInputQueue = queued; }
        return start;
    }

    private void EndInputSample(uint message, long start)
    {
        if (start == 0) return;
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        view.Performance.Record(EditorPerformanceStage.InputDispatch, elapsed);
        if (elapsed > slowestInputMs) { slowestInputMs = elapsed; slowestInputMessage = message; }
    }

    private void RecordInputSubmission()
    {
        if (pendingInput == 0) return;
        view.Performance.Record(EditorPerformanceStage.InputToSubmit,
            pendingInputQueue + Stopwatch.GetElapsedTime(pendingInput).TotalMilliseconds);
        pendingInput = 0;
    }

    private void FlushPerformance(bool force = false)
    {
        if (!force && Stopwatch.GetElapsedTime(performanceFlush).TotalSeconds < 5) return;
        string? summary = view.Performance.Drain();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        if (summary is not null)
            AppLog.Write($"{summary} slowestInput=0x{slowestInputMessage:X4}; GC={gen0 - performanceGen0}/{gen1 - performanceGen1}/{gen2 - performanceGen2}; {view.PerformanceContext}");
        performanceGen0 = gen0; performanceGen1 = gen1; performanceGen2 = gen2;
        slowestInputMs = 0; slowestInputMessage = 0;
        performanceFlush = Stopwatch.GetTimestamp();
    }
}
