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
    private uint slowestMessage;
    private double slowestMessageMs;
    private bool slowestMessageTestplay;
    private long previousFrame;
    private bool previousFrameActive, previousFrameTestplay;
    private double longestFrameGapMs;
    private bool frameGapFromTestplay, frameGapToTestplay;
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

    private void EndMessageSample(uint message, long start, bool testplay)
    {
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        view.Performance.Record(EditorPerformanceStage.MessageDispatch, elapsed);
        if (elapsed > slowestMessageMs)
        { slowestMessageMs = elapsed; slowestMessage = message; slowestMessageTestplay = testplay; }
    }

    private void RecordFrameGap()
    {
        long now = Stopwatch.GetTimestamp();
        bool active = (view.IsTestplaying || audio.IsPlaying) && !Native.IsIconic(hwnd) && !NativeModalScope.Active;
        if (previousFrame != 0 && previousFrameActive && active)
        {
            double gap = Stopwatch.GetElapsedTime(previousFrame, now).TotalMilliseconds;
            view.Performance.Record(EditorPerformanceStage.FrameGap, gap);
            if (gap > longestFrameGapMs)
            { longestFrameGapMs = gap; frameGapFromTestplay = previousFrameTestplay; frameGapToTestplay = view.IsTestplaying; }
        }
        previousFrame = now; previousFrameActive = active; previousFrameTestplay = view.IsTestplaying;
    }

    private void FlushPerformance(bool force = false)
    {
        if (!force && Stopwatch.GetElapsedTime(performanceFlush).TotalSeconds < 5) return;
        string? summary = view.Performance.Drain();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        if (summary is not null)
        {
            string context = $"slowestInput=0x{slowestInputMessage:X4}; slowestMessage=0x{slowestMessage:X4}; slowestMessageTestplay={slowestMessageTestplay}; frameGapTestplay={frameGapFromTestplay}/{frameGapToTestplay}; GC={gen0 - performanceGen0}/{gen1 - performanceGen1}/{gen2 - performanceGen2}; {view.PerformanceContext}";
            // The summary's own LogWrite sample belongs to the next interval.
            AppLog.Write($"{summary} {context}");
        }
        performanceGen0 = gen0; performanceGen1 = gen1; performanceGen2 = gen2;
        slowestInputMs = 0; slowestInputMessage = 0;
        slowestMessageMs = 0; slowestMessage = 0; slowestMessageTestplay = false;
        longestFrameGapMs = 0; frameGapFromTestplay = frameGapToTestplay = false;
        performanceFlush = Stopwatch.GetTimestamp();
    }
}
