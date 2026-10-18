using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckWindowPreferences()
    {
        var original = view.LibrarySettings.WindowsWindow;
        var preference = view.RequestViewPreference;
        string folder = Path.Combine(Artifacts, "tests", "window-preferences-native");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        view.RequestViewPreference = () => view.LibrarySettings.Save(path);
        try
        {
            foreach (int state in new[] { 1, 3 })
            foreach (bool minimized in new[] { false, true })
            foreach (bool useFullscreen in new[] { false, true })
            {
                Native.ShowWindow(hwnd, 1);
                Native.SetWindowPos(hwnd, 0, 40, 40, 1800, 1100, 0x0004 | 0x0010);
                Native.ShowWindow(hwnd, state);
                var expected = CaptureWindowPreferences()!;
                if (useFullscreen) SetFullscreen(true);
                if (minimized) Native.ShowWindow(hwnd, 6);
                SaveWindowPreferences();
                var saved = LibrarySettings.Load(path).WindowsWindow!;
                if (saved.Width != expected.Width || saved.Height != expected.Height || saved.Maximized != (state == 3))
                    throw new InvalidOperationException("Window persistence captured minimized or fullscreen bounds.");
                Native.ShowWindow(hwnd, 1);
                SetFullscreen(false);
                Native.ShowWindow(hwnd, 1);
                Native.SetWindowPos(hwnd, 0, 20, 20, 1000, 700, 0x0004 | 0x0010);
                view.LibrarySettings.WindowsWindow = saved;
                RestoreWindowPreferences();
                var restored = CaptureWindowPreferences()!;
                if (restored.Width != expected.Width || restored.Height != expected.Height || restored.Maximized != expected.Maximized)
                    throw new InvalidOperationException("Window persistence did not restore normal size and maximization.");
            }
            view.BeginFirstRunSetup(); FitSetupWindow();
            if (CaptureWindowPreferences() is not null) throw new InvalidOperationException("Setup window replaced editor preferences.");
            view.CancelFirstRunSetup(); FitEditorWindow();
            view.LibrarySettings.WindowsWindow = new() { Width = 100000, Height = 100000 };
            Native.ShowWindow(hwnd, 1); RestoreWindowPreferences();
            Native.GetWindowRect(hwnd, out var bounds);
            var work = CurrentWorkArea();
            if (bounds.Right - bounds.Left > work.Right - work.Left || bounds.Bottom - bounds.Top > work.Bottom - work.Top)
                throw new InvalidOperationException("Restored window exceeds the available monitor work area.");
            AppLog.Write("Window preference checks passed: settings round-trip, normal/maximized/minimized/fullscreen restoration, setup isolation and work-area bounds.");
        }
        finally
        {
            view.CancelFirstRunSetup(); SetFullscreen(false);
            view.LibrarySettings.WindowsWindow = original;
            view.RequestViewPreference = preference;
            Native.ShowWindow(hwnd, 0);
        }
    }
}
