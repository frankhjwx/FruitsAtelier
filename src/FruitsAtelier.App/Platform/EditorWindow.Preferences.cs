using System.Runtime.InteropServices;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void RestoreWindowPreferences()
    {
        if (view.StartupDialogVisible || view.LibrarySettings.WindowsWindow is not { } saved) return;
        if (saved.HasValidSize())
        {
            var work = CurrentWorkArea();
            var minimum = EditorMinimumTrackSize();
            int width = Math.Clamp((int)saved.Width, minimum.X, work.Right - work.Left);
            int height = Math.Clamp((int)saved.Height, minimum.Y, work.Bottom - work.Top);
            Native.SetWindowPos(hwnd, 0, work.Left + (work.Right - work.Left - width) / 2,
                work.Top + (work.Bottom - work.Top - height) / 2, width, height, 0x0004 | 0x0010);
        }
        if (saved.Maximized) Native.ShowWindow(hwnd, 3);
    }

    private EditorWindowPreferences? CaptureWindowPreferences()
    {
        if (view.StartupDialogVisible || (!fullscreen
            && (Native.GetWindowLongPtr(hwnd, -16) & (nint)Native.WindowStyle) != (nint)Native.WindowStyle)) return null;
        var placement = new Native.WindowPlacement { Length = (uint)Marshal.SizeOf<Native.WindowPlacement>() };
        if (fullscreen) placement = windowedPlacement;
        else if (!Native.GetWindowPlacement(hwnd, ref placement)) return null;
        var bounds = placement.NormalPosition;
        return new()
        {
            Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top,
            Maximized = placement.ShowCommand == 3 || placement.ShowCommand == 2 && (placement.Flags & 2) != 0
        };
    }

    private void SaveWindowPreferences()
    {
        if (CaptureWindowPreferences() is not { } saved) return;
        view.LibrarySettings.WindowsWindow = saved;
        view.RequestViewPreference?.Invoke();
    }
}
