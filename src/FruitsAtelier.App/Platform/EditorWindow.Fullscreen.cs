using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private bool fullscreen;
    private nint windowedStyle;
    private Native.WindowPlacement windowedPlacement;

    private void SetFullscreen(bool enabled)
    {
        if (hwnd == 0 || fullscreen == enabled) return;
        if (enabled)
        {
            windowedPlacement = new() { Length = (uint)Marshal.SizeOf<Native.WindowPlacement>() };
            if (!Native.GetWindowPlacement(hwnd, ref windowedPlacement)) throw new Win32Exception();
            if (windowedPlacement.ShowCommand == 0) windowedPlacement.ShowCommand = 1;
            windowedStyle = Native.GetWindowLongPtr(hwnd, -16);
            fullscreen = true;
            Native.SetWindowLongPtr(hwnd, -16, windowedStyle & ~(nint)Native.WindowStyle);
            FitFullscreenMonitor();
        }
        else
        {
            fullscreen = false;
            Native.SetWindowLongPtr(hwnd, -16, windowedStyle);
            if (!Native.SetWindowPlacement(hwnd, ref windowedPlacement)) throw new Win32Exception();
            Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }
        Invalidate();
    }

    private void FitFullscreenMonitor()
    {
        var info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2), ref info)) throw new Win32Exception();
        var bounds = info.Monitor;
        Native.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top, 0x0004 | 0x0010 | 0x0020);
    }
}
