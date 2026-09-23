using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Updates;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckUpdateRefresh()
    {
        CheckUpdateRefresh(false);
        CheckUpdateRefresh(true);
    }

    private void CheckUpdateRefresh(bool postedNotifications)
    {
        var previousService = updates;
        var previousStatus = view.UpdateStatus;
        var previousPolledStatus = lastUpdateStatus;
        Native.GetWindowRect(hwnd, out var originalBounds);
        var backend = new RefreshCheckBackend();
        using var service = new UpdateService(backend,
            Path.Combine(Artifacts, "update-refresh-check", Guid.NewGuid().ToString("N"), "updates.json"),
            AppLog.Write, postedNotifications ? NotifyUpdateStatusChanged : null);
        try
        {
            updates = service;
            lastUpdateStatus = null;
            if (postedNotifications)
            {
                // Hidden windows have no paint region. Exercise a visible, off-screen owner without taking focus.
                Native.SetWindowPos(hwnd, 0, -32000, -32000, 0, 0, 0x0001 | 0x0004 | 0x0010);
                Native.ShowWindow(hwnd, 4);
            }
            Native.ValidateRect(hwnd, 0);
            var check = service.Check(DateTimeOffset.UtcNow);
            PaintAndVerify(new(UpdatePhase.Checking));
            backend.CheckResult.SetResult("0.8.3");
            check.GetAwaiter().GetResult();
            PaintAndVerify(new(UpdatePhase.Available, "0.8.3"));
            var download = service.Download();
            PaintAndVerify(new(UpdatePhase.Downloading, "0.8.3", 42));
            backend.DownloadResult.SetResult();
            download.GetAwaiter().GetResult();
            PaintAndVerify(new(UpdatePhase.Ready, "0.8.3", 100));
            backend.CheckResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
            check = service.Check(DateTimeOffset.UtcNow);
            PaintAndVerify(new(UpdatePhase.Checking));
            backend.CheckResult.SetResult(null);
            check.GetAwaiter().GetResult();
            PaintAndVerify(new(UpdatePhase.Current));
            backend.CheckResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
            check = service.Check(DateTimeOffset.UtcNow);
            PaintAndVerify(new(UpdatePhase.Checking));
            backend.CheckResult.SetException(new IOException("Injected update check failure."));
            check.GetAwaiter().GetResult();
            PaintAndVerify(new(UpdatePhase.Failed));
            AppLog.Write($"Update refresh check passed: all check/download states via {(postedNotifications ? "posted wakeups from an idle window" : "paint polling")} without timer messages.");
        }
        finally
        {
            if (postedNotifications)
            {
                Native.ShowWindow(hwnd, 0);
                Native.SetWindowPos(hwnd, 0, originalBounds.Left, originalBounds.Top, 0, 0, 0x0001 | 0x0004 | 0x0010);
            }
            updates = previousService;
            view.UpdateStatus = previousStatus;
            lastUpdateStatus = previousPolledStatus;
        }

        void PaintAndVerify(UpdateStatus expected)
        {
            int before = frames;
            if (postedNotifications)
            {
                if (Native.GetUpdateRect(hwnd, out _, false))
                    throw new InvalidOperationException("Update check did not start with an idle paint region.");
                int notifications = 0;
                while (Native.PeekMessage(out var message, hwnd, updateStatusChangedMessage, updateStatusChangedMessage, 1))
                {
                    Native.DispatchMessage(ref message);
                    notifications++;
                }
                if (notifications == 0 || !Native.GetUpdateRect(hwnd, out _, false))
                    throw new InvalidOperationException($"Background update did not request a repaint for {expected}.");
                Native.UpdateWindow(hwnd);
            }
            else WndProc(hwnd, 0x000F, 0, 0);
            if (frames != before + 1 || view.UpdateStatus != expected)
                throw new InvalidOperationException($"Paint did not refresh update status to {expected}.");
            Native.ValidateRect(hwnd, 0);
        }
    }

    private sealed class RefreshCheckBackend : IUpdateBackend
    {
        internal TaskCompletionSource<string?> CheckResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DownloadResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsInstalled => true;
        public string? PendingVersion => null;
        public Task<string?> Check() => CheckResult.Task;
        public Task Download(Action<int> progress, CancellationToken cancellation)
        {
            progress(42);
            return DownloadResult.Task;
        }
        public void Apply() => throw new InvalidOperationException("The refresh check must not restart the application.");
    }

    private void CheckPaintLifecycle()
    {
        var document = view.Document.DeepClone();
        Native.GetClientRect(hwnd, out var size);
        canvas!.Begin(); canvas.Clip(new(0, 0, 50, 50)); canvas.AbortDraw();
        canvas.Begin(); canvas.End();

        // Simulate messages dispatched by a nested STA pump while an outer frame is drawing.
        int before = frames;
        painting = true;
        try
        {
            canvas.Begin();
            WndProc(hwnd, 0x000F, 0, 0);
            WndProc(hwnd, 0x0113, 1, 0);
            canvas.End();
        }
        finally { painting = false; }
        if (frames != before) throw new InvalidOperationException("Nested paint submitted an extra frame.");

        using (var outer = new NativeModalScope(hwnd))
        {
            using (var inner = new NativeModalScope(hwnd)) { }
            if (!NativeModalScope.Active) throw new InvalidOperationException("Nested modal scope resumed its owner early.");
            canvas.Begin();
            WndProc(hwnd, 0x000F, 0, 0);
            WndProc(hwnd, 0x0113, 1, 0);
            canvas.End();
            if (frames != before) throw new InvalidOperationException("Modal owner continued rendering.");
        }
        if (NativeModalScope.Active) throw new InvalidOperationException("Modal scope did not release its owner.");

        // Two BeginDraw calls reproduce the live D2DERR_WRONG_STATE at EndDraw.
        var previous = canvas;
        canvas.Begin();
        WndProc(hwnd, 0x000F, 0, 0);
        if (ReferenceEquals(previous, canvas) || failed || !view.ErrorVisible)
            throw new InvalidOperationException("Paint failure did not rebuild the renderer and expose its error.");
        WndProc(hwnd, 0x000F, 0, 0);
        if (recoveringRenderer || failed) throw new InvalidOperationException("Recovered renderer could not draw its error.");
        view.KeyDown(13, false, false); view.KeyUp(13);
        WndProc(hwnd, 0x000F, 0, 0);
        if (view.ErrorVisible || !document.ContentEquals(view.Document))
            throw new InvalidOperationException("Paint recovery lost document content or trapped input.");
        AppLog.Write("Paint lifecycle check passed: nested paint, modal owner, abandoned frame, Direct2D recovery.");
    }
}
