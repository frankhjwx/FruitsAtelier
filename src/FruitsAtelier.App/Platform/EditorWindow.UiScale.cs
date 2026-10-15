using System.Runtime.InteropServices;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private static float ClientUiDpi(int width, int height)
        => 168 * Math.Min(width / 2520f, height / 1575f);

    private float InitialUiDpi()
    {
        var info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        return Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2), ref info)
            ? 168 * Math.Min((info.Monitor.Right - info.Monitor.Left) / 3840f,
                (info.Monitor.Bottom - info.Monitor.Top) / 2160f) : 168;
    }

    private void RefreshUiScale(nint window)
    {
        Native.GetClientRect(window, out var client);
        if (client.Right <= 0 || client.Bottom <= 0) return;
        // The editor reference is its 1440 × 900 logical startup window rendered at 175%.
        float next = view.AcknowledgementsVisible ? 96 * Math.Min(client.Right / 820f, client.Bottom / 740f)
            : view.FirstRunSetupVisible ? 96 * Math.Min(client.Right / 880f, client.Bottom / 620f)
            : ClientUiDpi(client.Right, client.Bottom) * view.UiScaleFactor;
        uiDpi = next;
    }
}
