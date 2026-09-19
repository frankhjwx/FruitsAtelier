using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FruitsAtelier.App.Platform;

internal sealed class TestplayFramePacer : IDisposable
{
    private readonly double framesPerSecond;
    private readonly long spinReserveTicks;
    private readonly SafeWaitHandle timer;
    private readonly nint[] handles;
    private long nextFrame;

    public TestplayFramePacer(double framesPerSecond = 1000, double spinReserveMs = 0)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        this.framesPerSecond = framesPerSecond;
        if (!double.IsFinite(spinReserveMs) || spinReserveMs < 0 || spinReserveMs >= 1000 / framesPerSecond)
            throw new ArgumentOutOfRangeException(nameof(spinReserveMs));
        spinReserveTicks = (long)(Stopwatch.Frequency * spinReserveMs / 1000);
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
    public void FrameStarted() => nextFrame = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency / framesPerSecond);

    public void Wait()
    {
        long remaining = nextFrame - Stopwatch.GetTimestamp();
        if (remaining <= 0) return;
        // Return to the message pump during the bounded tail, so input remains interruptible.
        if (remaining <= spinReserveTicks) { Thread.SpinWait(32); return; }
        remaining -= spinReserveTicks;
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
