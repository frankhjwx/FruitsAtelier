using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckRepeatedRecovery()
    {
        string directory = Path.Combine(Artifacts, "tests", "paint-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var document = view.Document.DeepClone();
        int pollAttempts = 0, renderAttempts = 0, nativeErrors = 0;
        diagnosticNativeError = _ => nativeErrors++;
        try
        {
            diagnosticPollFailure = () => { pollAttempts++; throw new InvalidOperationException("Persistent poll fault"); };
            WndProc(hwnd, 0x0113, 1, 0);
            for (int i = 0; i < 4; i++)
            {
                WndProc(hwnd, 0x0113, 1, 0);
                WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
            }
            if (pollAttempts != 1 || failed || !view.RecoveryActive || nativeErrors != 0)
                throw new InvalidOperationException("Persistent native polling blocked recovery.");
            diagnosticRecoveryPath = () => Path.Combine(directory, "poll.catchproj");
            int repeatedSaveCalls = 0;
            var recoverySave = view.RequestRecoverySave;
            view.RequestRecoverySave = () => repeatedSaveCalls++;
            try
            {
                WndProc(hwnd, 0x0100, 83, (nint)(1L << 30));
                WndProc(hwnd, 0x0100, 13, (nint)(1L << 30));
                DispatchShortcutBeforeTranslation(new Native.Message { Window = hwnd, Id = 0x0100, WParam = 83, LParam = (nint)(1L << 30) });
                DispatchShortcutBeforeTranslation(new Native.Message { Window = hwnd, Id = 0x0100, WParam = 13, LParam = (nint)(1L << 30) });
                if (repeatedSaveCalls != 0 || !view.RecoveryActive)
                    throw new InvalidOperationException("Held recovery keys repeated save or retry.");
            }
            finally { view.RequestRecoverySave = recoverySave; }
            view.KeyDown(83, true, false); view.KeyUp(83);
            AssertRecoveryCopy("poll.catchproj");
            diagnosticRecoveryClose = () => 2;
            WndProc(hwnd, 0x0010, 0, 0);
            if (!view.RecoveryActive) throw new InvalidOperationException("Recovery close cancellation resumed editing.");
            diagnosticRecoveryClose = () => 6;
            diagnosticRecoveryPath = () => null;
            WndProc(hwnd, 0x0010, 0, 0);
            WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
            if (!view.RecoveryActive) throw new InvalidOperationException("Cancelled recovery save closed the editor.");
            diagnosticPollFailure = null;
            view.RetryRender(); WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();

            diagnosticPaintFailure = () =>
            {
                if (!view.RecoveryActive) { renderAttempts++; throw new InvalidOperationException("Persistent render fault"); }
            };
            for (int attempt = 0; attempt < 2; attempt++)
            {
                WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
                for (int i = 0; i < 4; i++) { WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint(); }
                if (!view.RecoveryActive || nativeRecovery || nativeErrors != 0 || renderAttempts != attempt + 1)
                    throw new InvalidOperationException("Persistent rendering retried without user input.");
                diagnosticRecoveryPath = () => Path.Combine(directory, $"render-{attempt}.catchproj");
                view.KeyDown(83, true, false); view.KeyUp(83);
                AssertRecoveryCopy($"render-{attempt}.catchproj");
                view.RetryRender();
            }

            diagnosticRecreateFailure = () => throw new InvalidOperationException("Unavailable renderer");
            WndProc(hwnd, 0x000F, 0, 0);
            for (int i = 0; i < 4; i++) { WndProc(hwnd, 0x000F, 0, 0); WndProc(hwnd, 0x0113, 1, 0); }
            if (!nativeRecovery || failed || nativeErrors != 1)
                throw new InvalidOperationException("Unavailable renderer trapped recovery or repeated its dialog.");
            diagnosticRecoveryPath = () => Path.Combine(directory, "native.catchproj");
            diagnosticControl = true;
            WndProc(hwnd, 0x0100, 83, 0);
            WndProc(hwnd, 0x0100, 83, (nint)(1L << 30));
            diagnosticControl = false;
            AssertRecoveryCopy("native.catchproj");
            diagnosticPaintFailure = diagnosticRecreateFailure = null;
            WndProc(hwnd, 0x0100, 13, 0);
            WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
            if (nativeRecovery || view.RecoveryActive || failed || !document.ContentEquals(view.Document))
                throw new InvalidOperationException("Native recovery retry lost content or remained paused.");

            view.ShowRenderError("Recovery screen fault");
            recoveringRenderer = false;
            diagnosticPaintFailure = () => throw new InvalidOperationException("Recovery screen fault");
            WndProc(hwnd, 0x000F, 0, 0);
            WndProc(hwnd, 0x000F, 0, 0);
            if (!nativeRecovery || nativeErrors != 2 || failed)
                throw new InvalidOperationException("Broken recovery screen failed to expose native recovery.");
            diagnosticPaintFailure = null;
            WndProc(hwnd, 0x0100, 13, 0);
            WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
            CheckMessageBoundaryRecovery(directory);
            AppLog.Write("Persistent poll/render and unavailable-renderer recovery passed; four saved copies reopened unchanged.");
        }
        finally
        {
            diagnosticPaintFailure = diagnosticPollFailure = diagnosticRecreateFailure = null;
            diagnosticNativeError = null; diagnosticRecoveryPath = null; diagnosticControl = false;
            diagnosticRecoveryClose = null;
        }

        void AssertRecoveryCopy(string name)
        {
            var restored = ProjectSerializer.ReadProject(File.ReadAllText(Path.Combine(directory, name)), Path.Combine(directory, name));
            if (!document.ContentEquals(restored.Difficulties[view.ActiveDifficultyIndex].Document)
                || !document.ContentEquals(view.Document) || !view.RecoveryActive)
                throw new InvalidOperationException("Recovery backup changed or lost document content.");
        }
    }

    private void CheckMessageBoundaryRecovery(string directory)
    {
        var requestSeek = view.RequestSeek;
        int seeks = 0, closePrompts = 0, savePrompts = 0;
        try
        {
            view.RequestSeek = _ => seeks++;
            using (var modal = new NativeModalScope(hwnd))
            {
                WndProc(hwnd, 0x0100, 36, 0);
                DispatchShortcutBeforeTranslation(new Native.Message { Window = hwnd, Id = 0x0100, WParam = 36 });
            }
            if (seeks != 0) throw new InvalidOperationException("Native dialog owner executed keyboard input.");

            view.RequestSeek = _ => throw new InvalidOperationException("Pretranslation callback fault");
            if (!DispatchShortcutBeforeTranslation(new Native.Message { Window = hwnd, Id = 0x0100, WParam = 36 })
                || !view.RecoveryActive || failed)
                throw new InvalidOperationException("Pretranslation callback escaped recovery.");
            view.RequestSeek = requestSeek;
            diagnosticRecoveryClose = () => { closePrompts++; return 2; };
            diagnosticRecoveryPath = () =>
            {
                savePrompts++;
                diagnosticControl = true;
                WndProc(hwnd, 0x0100, 83, 0);
                diagnosticControl = false;
                WndProc(hwnd, 0x0100, 13, 0);
                WndProc(hwnd, 0x0010, 0, 0);
                DispatchShortcutBeforeTranslation(new Native.Message { Window = hwnd, Id = 0x0100, WParam = 13 });
                if (SaveRecoveryCopy()) throw new InvalidOperationException("Recovery copy reentered its save dialog.");
                return Path.Combine(directory, "dispatch.catchproj");
            };
            if (!SaveRecoveryCopy() || savePrompts != 1 || closePrompts != 0 || !view.RecoveryActive)
                throw new InvalidOperationException("Recovery save dialog accepted reentrant owner commands.");
            view.RetryRender();

            diagnosticFrameWait = () => throw new InvalidOperationException("Frame wait fault");
            if (WaitForPendingFrame() || !view.RecoveryActive)
                throw new InvalidOperationException("Frame wait escaped recovery.");
            WaitForPendingFrame();
            if (!nativeRecovery || failed) throw new InvalidOperationException("Repeated frame wait prevented native recovery.");
            diagnosticFrameWait = null;
            WndProc(hwnd, 0x0100, 13, 0);
            WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();

            int queueReads = 0;
            diagnosticGetMessageResult = () =>
            {
                queueReads++;
                System.Runtime.InteropServices.Marshal.SetLastPInvokeError(6);
                return -1;
            };
            if (ReadQueuedMessage(out _) != 2 || !nativeRecovery || !view.RecoveryActive)
                throw new InvalidOperationException("Failed message read escaped recovery.");
            const uint marker = 0x8000 + 567;
            Native.PostMessage(hwnd, marker, 0, 0);
            bool found = false;
            for (int i = 0; i < 256; i++)
            {
                if (ReadQueuedMessage(out var message) != 1) break;
                if (message.Id == marker) { found = true; break; }
                Native.DispatchMessage(ref message);
            }
            if (!found || queueReads != 1) throw new InvalidOperationException("Failed message reader did not retain native input.");
            WndProc(hwnd, 0x0100, 13, 0);
            WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
            string? nativePreview = null;
            var reportNativeError = diagnosticNativeError;
            diagnosticNativeError = message => nativePreview = message;
            view.ShowRenderError("Large native diagnostic");
            EnterNativeRecovery(hwnd, new InvalidOperationException(new string('x', 100000)));
            if (nativePreview is null || nativePreview.Length > 9000 || !nativePreview.EndsWith("…", StringComparison.Ordinal))
                throw new InvalidOperationException("Native recovery expanded an unbounded exception message.");
            diagnosticNativeError = reportNativeError;
            WndProc(hwnd, 0x0100, 13, 0);
            WndProc(hwnd, 0x000F, 0, 0); CompletePendingCheckPaint();
            AppLog.Write("Native dispatch recovery passed: modal owner isolation, shortcut callback, save reentry, frame wait and message read.");
        }
        finally
        {
            view.RequestSeek = requestSeek;
            diagnosticFrameWait = null; diagnosticGetMessageResult = null; messageQueueFallback = false;
            diagnosticControl = false; diagnosticRecoveryClose = null; diagnosticRecoveryPath = null;
        }
    }
}

