using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FruitsAtelier.App.Platform;

internal sealed class TestplayFramePacer : IDisposable
{
    public const double FramesPerSecond = 1000;
    private readonly SafeWaitHandle timer;
    private readonly nint[] handles;
    private long nextFrame;

    public TestplayFramePacer()
    {
        timer = CreateWaitableTimerEx(0, null, 2, 0x1F0003);
        if (timer.IsInvalid)
        {
            timer.Dispose();
            timer = CreateWaitableTimerEx(0, null, 0, 0x1F0003);
        }
        if (timer.IsInvalid) throw new Win32Exception();
        handles = [timer.DangerousGetHandle()];
    }

    public bool FrameDue => Stopwatch.GetTimestamp() >= nextFrame;
    public void FrameStarted() => nextFrame = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency / FramesPerSecond);

    public void Wait()
    {
        long remaining = nextFrame - Stopwatch.GetTimestamp();
        if (remaining <= 0) return;
        long due = -Math.Max(1, (long)(remaining * (10_000_000d / Stopwatch.Frequency)));
        if (!SetWaitableTimer(timer, ref due, 0, 0, 0, false)) throw new Win32Exception();
        // Waiting for a frame must remain interruptible by keyboard, focus and window messages.
        if (MsgWaitForMultipleObjectsEx(1, handles, uint.MaxValue, 0x04FF, 0x0004) == uint.MaxValue)
            throw new Win32Exception();
    }

    public void Dispose() => timer.Dispose();
    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, nint completion, nint argument, bool resume);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MsgWaitForMultipleObjectsEx(uint count, nint[] handles, uint milliseconds, uint wakeMask, uint flags);
}
