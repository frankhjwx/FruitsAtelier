using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private bool messageQueueFallback;
    private Func<int>? diagnosticGetMessageResult;
    private Func<bool>? diagnosticFrameWait;

    private bool WaitForPendingFrame()
    {
        try { return diagnosticFrameWait?.Invoke() ?? (canvas is not null && canvas.WaitForFrameOrInput()); }
        catch (Exception error)
        {
            HandleWindowFailure(hwnd, 0x000F, error);
            return false;
        }
    }

    private int ReadQueuedMessage(out Native.Message message)
    {
        message = default;
        if (!messageQueueFallback)
        {
            int result = diagnosticGetMessageResult?.Invoke() ?? Native.GetMessage(out message, 0, 0, 0);
            if (result >= 0) return result == 0 ? 0 : 1;
            messageQueueFallback = true;
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            HandleWindowFailure(hwnd, 0, error);
            EnterNativeRecovery(hwnd, error);
            return 2;
        }
        // Retain native save/close input if GetMessage rejects the ordinary queue read.
        while (!Native.PeekMessage(out message, 0, 0, 0, 1))
            if (!Native.WaitMessage()) Thread.Sleep(16);
        return message.Id == 0x0012 ? 0 : 1;
    }
}
