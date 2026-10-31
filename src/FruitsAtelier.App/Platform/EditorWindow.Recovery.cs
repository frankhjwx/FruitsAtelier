using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private bool nativeRecovery;
    private Action? diagnosticPaintFailure, diagnosticPollFailure, diagnosticRecreateFailure;
    private Action<string>? diagnosticNativeError;
    private Func<string?>? diagnosticRecoveryPath;
    private Func<int>? diagnosticRecoveryClose;
    private bool diagnosticControl;
    private bool recoveryDialogActive;

    private bool OwnerInputBlocked(uint message)
        => (NativeModalScope.Active || recoveryDialogActive)
            && (message == 0x0010 || message is >= 0x0100 and <= 0x0109 || message is >= 0x0200 and <= 0x020E);

    private static string NativeErrorPreview(string message)
        => message.Length <= 8192 ? message : message[..8192] + "…";

    private bool IgnoreRecoveryKeyRepeat(uint message, nuint key, nint keyData)
        => view.RecoveryActive && message == 0x0100 && (key == 13 || key == 83)
            && ((long)keyData & (1L << 30)) != 0;

    private static void TryRecoveryStep(Action action)
    {
        try { action(); }
        catch (Exception error) { AppLog.Write(error.ToString()); }
    }

    private void PollWindowUpdates()
    {
        // Recovery must remain drawable even when a native poll repeatedly fails.
        if (view.ErrorVisible) return;
        diagnosticPollFailure?.Invoke();
        PollUpdates(); PollAudio(); PollOsuLinks();
    }

    private void HandleWindowFailure(nint window, uint message, Exception exception)
    {
        AppLog.Write(exception.ToString());
        if (failed) return;
        failed = true;
        try
        {
            TryRecoveryStep(() => view.CancelInteraction());
            TryRecoveryStep(() => { if (Native.GetCapture() == window) Native.ReleaseCapture(); });
            TryRecoveryStep(() => audio.Pause());
            TryRecoveryStep(() => view.StopTestplay());
            view.ShowRenderError(L.Get("window.operationFailed", L.Localized(exception.Message)));
            if (message == 0x000F)
            {
                if (recoveringRenderer) { EnterNativeRecovery(window, exception); return; }
                recoveringRenderer = true;
                var previous = canvas; canvas = null;
                TryRecoveryStep(() => previous?.Dispose());
                try
                {
                    diagnosticRecreateFailure?.Invoke();
                    Native.GetClientRect(window, out var size);
                    canvas = new Rendering.D2DCanvas(window, Math.Max(1, size.Right), Math.Max(1, size.Bottom), uiDpi)
                    { Performance = view.Performance };
                    AppLog.Write("Renderer recreated after paint failure; document retained.");
                }
                catch (Exception recoveryError) { EnterNativeRecovery(window, recoveryError); }
            }
        }
        catch (Exception recoveryError) { EnterNativeRecovery(window, recoveryError); }
        finally
        {
            failed = false;
            if (!nativeRecovery) Invalidate();
        }
    }

    private void EnterNativeRecovery(nint window, Exception error)
    {
        AppLog.Write(error.ToString());
        if (nativeRecovery) return;
        nativeRecovery = true;
        framePending = false;
        Native.SetWindowText(window, L.Get("recovery.title") + " - " + L.Get("app.name"));
        string message = L.Get("recovery.nativeHelp") + "\n\n" + L.Localized(NativeErrorPreview(error.Message));
        TryRecoveryStep(() =>
        {
            if (diagnosticNativeError is { } report) report(message);
            else Native.ShowError(window, message, L.Get("app.name"));
        });
    }

    private bool HandleNativeRecoveryMessage(nint window, uint message, nuint key, nint keyData = 0)
    {
        if (message == 0x0010)
        {
            int answer;
            recoveryDialogActive = true;
            try { answer = diagnosticRecoveryClose?.Invoke() ?? Native.ConfirmRecoveryClose(window); }
            finally { recoveryDialogActive = false; }
            if (answer == 7 || answer == 6 && SaveRecoveryCopy()) Native.DestroyWindow(window);
            return true;
        }
        if (message == 0x0100)
        {
            if (((long)keyData & (1L << 30)) != 0) return true;
            if (key == 83 && (Native.Control || diagnosticControl)) SaveRecoveryCopy();
            else if (key == 13)
            {
                nativeRecovery = false; recoveringRenderer = false;
                view.RetryRender();
                if (canvas is null)
                {
                    try
                    {
                        diagnosticRecreateFailure?.Invoke();
                        Native.GetClientRect(window, out var size);
                        canvas = new Rendering.D2DCanvas(window, Math.Max(1, size.Right), Math.Max(1, size.Bottom), uiDpi)
                        { Performance = view.Performance };
                    }
                    catch (Exception error) { HandleWindowFailure(window, 0, error); EnterNativeRecovery(window, error); }
                }
                if (!nativeRecovery) { lastTitle = ""; UpdateTitle(); Invalidate(); }
            }
            return true;
        }
        // Keep native lifetime messages active, but isolate editor input and background polling.
        return message == 0x0113 || message is >= 0x0101 and <= 0x0109 || message is >= 0x0200 and <= 0x020E;
    }

    private bool SaveRecoveryCopy()
    {
        if (recoveryDialogActive) return false;
        recoveryDialogActive = true;
        try
        {
            string? path = diagnosticRecoveryPath is { } choose ? choose() : MapFileDialog.Select(hwnd, true,
                L.Get("recovery.save"), MapFileDialog.ProjectFilter, SafeName(view.ProjectName) + "-recovery.catchproj", "catchproj");
            if (path is null) return false;
            view.SaveRecoveryCopy(path);
            if (nativeRecovery && diagnosticNativeError is null)
                Native.ShowError(hwnd, L.Get("recovery.saved", path), L.Get("app.name"));
            if (!nativeRecovery) Invalidate();
            return true;
        }
        catch (Exception error)
        {
            AppLog.Write(error.ToString());
            view.RecoverySaveFailed(L.Reformat(error.Message));
            if (nativeRecovery) TryRecoveryStep(() => Native.ShowError(hwnd, L.Get("recovery.failed", L.Localized(NativeErrorPreview(error.Message))), L.Get("app.name")));
        }
        finally { recoveryDialogActive = false; }
        if (!nativeRecovery) Invalidate();
        return false;
    }
}

