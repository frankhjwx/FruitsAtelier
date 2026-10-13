using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckAcknowledgementsWindow()
    {
        var originalPreference = view.RequestViewPreference;
        var originalPending = pendingStartupFiles;
        bool originalLowLatency = view.LibrarySettings.LowLatencyDisplay, originalFullscreen = view.LibrarySettings.Fullscreen;
        view.RequestViewPreference = null;
        try
        {
            foreach (bool newUser in new[] { false, true })
            foreach (bool lowLatency in new[] { false, true })
            foreach (bool startFullscreen in new[] { false, true })
                CheckTransition(newUser, lowLatency, startFullscreen);
        }
        finally
        {
            Native.ShowWindow(hwnd, 0);
            view.CancelFirstRunSetup();
            SetFullscreen(false);
            view.LibrarySettings.LowLatencyDisplay = originalLowLatency;
            view.LibrarySettings.Fullscreen = originalFullscreen;
            view.RequestViewPreference = originalPreference;
            pendingStartupFiles = originalPending;
        }
        AppLog.Write("Acknowledgements window passed compact sizing, native input and synchronous setup/editor transitions in normal, low-latency and fullscreen modes.");
    }

    private void CheckTransition(bool newUser, bool lowLatency, bool startFullscreen)
    {
        SetFullscreen(false);
        view.LibrarySettings.LowLatencyDisplay = lowLatency;
        view.LibrarySettings.Fullscreen = startFullscreen;
        string path = Path.Combine(Artifacts, "tests", "acknowledgements-native", Guid.NewGuid().ToString("N") + ".json");
        view.BeginAcknowledgements(() => { if (newUser) view.BeginFirstRunSetup(Path.ChangeExtension(path, ".setup.json")); }, path);
        FitSetupWindow();
        Native.ShowWindow(hwnd, 4);
        Native.GetWindowRect(hwnd, out var bounds);
        Native.GetClientRect(hwnd, out var client);
        var work = CurrentWorkArea();
        var expected = EditorView.AcknowledgementsWindowSize(work.Right - work.Left, work.Bottom - work.Top, dpi / 96d);
        if ((Native.GetWindowLongPtr(hwnd, -16) & (nint)Native.WindowStyle) != 0
            || bounds.Right - bounds.Left != client.Right || bounds.Bottom - bounds.Top != client.Bottom
            || Math.Abs(client.Right - expected.Width * dpi / 96) > 1
            || Math.Abs(client.Bottom - expected.Height * dpi / 96) > 1)
            throw new InvalidOperationException("Acknowledgements must occupy a compact undecorated window.");

        nint HitAt(int y) => HandleMessage(hwnd, 0x0084, 0,
            (nint)(((long)(ushort)(bounds.Top + y) << 16) | (ushort)(bounds.Left + 20)));
        if (HitAt(20) != 2 || HitAt((int)(120 * dpi / 96)) != 1)
            throw new InvalidOperationException("Acknowledgements must drag only from their header.");
        canvas!.Resize(client.Right, client.Bottom, dpi);
        canvas.Begin(); view.Render(canvas, client.Right * 96 / dpi, client.Bottom * 96 / dpi); canvas.End();
        Click(view.AcknowledgementsConfirmBounds);
        if (!view.AcknowledgementsVisible || File.Exists(path)) throw new InvalidOperationException("Unchecked native confirmation must remain blocked.");
        Click(view.AcknowledgementsCheckboxBounds);
        canvas.Begin(); view.Render(canvas, client.Right * 96 / dpi, client.Bottom * 96 / dpi); canvas.End();
        int before = frames;
        pendingStartupFiles = () =>
        {
            if (!NativeModalScope.Active) throw new InvalidOperationException("Startup work must not expose intermediate frames.");
            WndProc(hwnd, 0x000F, 0, 0);
            if (frames != before) throw new InvalidOperationException("Startup work presented a frame before the final window geometry.");
        };
        Click(view.AcknowledgementsConfirmBounds);
        if (frames != before + 1 || startupWindowTransition || failed || framePending)
            throw new InvalidOperationException($"Confirm must synchronously present one complete final-size frame: new={newUser}, immediate={lowLatency}, fullscreen={startFullscreen}, frames={before}->{frames}, pending={framePending}, failed={failed}.");
        if (view.AcknowledgementsVisible || !AcknowledgementsPreference.IsAccepted(path) || view.FirstRunSetupVisible != newUser)
            throw new InvalidOperationException("Accepted users must enter the appropriate startup screen.");
        uint expectedStyle = newUser || startFullscreen ? 0u : Native.WindowStyle;
        if ((Native.GetWindowLongPtr(hwnd, -16) & (nint)Native.WindowStyle) != expectedStyle)
            throw new InvalidOperationException("Confirmed window decorations do not match the startup screen.");
        VerifyClientSize();
        if (newUser)
        {
            view.CancelFirstRunSetup();
            before = frames;
            view.RequestSetupFinished!.Invoke();
            if (frames != before + 1 || startupWindowTransition || failed || framePending)
                throw new InvalidOperationException("Leaving setup must synchronously present one complete editor frame.");
            VerifyClientSize();
        }
        File.Delete(path);

        void VerifyClientSize()
        {
            Native.GetClientRect(hwnd, out var finalClient);
            var presented = canvas!.CapturePixels();
            if (presented.Width != finalClient.Right || presented.Height != finalClient.Bottom)
                throw new InvalidOperationException("Startup transition returned before preparing the final-size back buffer.");
        }

        void Click(FruitsAtelier.App.Rendering.Rect r)
        {
            int x = (int)((r.X + 8) * dpi / 96), y = (int)((r.Y + 8) * dpi / 96);
            nint position = (nint)(((long)(ushort)y << 16) | (ushort)x);
            HandleMessage(hwnd, 0x0201, 1, position);
            HandleMessage(hwnd, 0x0202, 0, position);
        }
    }
}
