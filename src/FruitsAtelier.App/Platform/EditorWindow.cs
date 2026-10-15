using L = FruitsAtelier.Localization.Strings;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow : IDisposable
{
    private readonly Native.WindowProc procedure;
    private readonly EditorView view = new(loadDemo: false);
    private D2DCanvas? canvas;
    private nint hwnd;
    private float uiDpi = 168;
    private float dpi = 96;
    private bool failed, disposed, painting, recoveringRenderer;
    private bool framePending;
    private bool testplayCursorMode;
    private bool ImmediatePresentation => view.IsTestplaying ||
        (canvas?.DiagnosticImmediatePresentation ?? view.LibrarySettings.LowLatencyDisplay);
    private string lastTitle = "";
    private int frames;
    private readonly Stopwatch renderTimer = new();
    private readonly Stopwatch playbackSampleTimer = new();
    private int playbackSampleFrames;
    private bool playbackSampleComplete;

    public EditorWindow()
    {
        procedure = WndProc;
        view.SupportsDisplayMode = true;
        view.SupportsFullscreen = true;
        view.RequestFullscreen = SetFullscreen;
        view.RequestScreenshot = () => { screenshotPending = true; Invalidate(); };
        view.Performance.Enabled = true;
        AppLog.Performance = view.Performance;
        ConfigureFiles();
        view.RequestCopyText = text => Native.WriteClipboardText(hwnd, text);
        view.RequestPasteTime = () => view.PasteTimeJumpText(Native.ReadClipboardText(hwnd), view.TimeJumpSession);
        view.RequestPasteSongSetup = () => view.PasteSongSetupText(Native.ReadClipboardText(hwnd), view.SongSetupInputSession);
        view.RequestPasteTiming = () => view.PasteTimingText(Native.ReadClipboardText(hwnd), view.TimingInputSession);
        view.RequestTimingSampleHelp = () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://osu.ppy.sh/wiki/en/Beatmapping/Hitsound") { UseShellExecute = true });
        view.RequestPasteLibrary = () => view.PasteLibraryText(Native.ReadClipboardText(hwnd));
        view.RequestPasteField = () => view.PasteFieldText(Native.ReadClipboardText(hwnd));
        view.RequestClose = Close;
        view.RequestLoadSkin = () =>
        {
            view.CancelInteraction();
            if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
            try
            {
                string? archive = SkinFileDialog.SelectArchive(hwnd);
                if (archive is not null) view.ImportSkin(archive);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { AppLog.Write(error.ToString()); view.ShowError(L.Get("window.skinFailed", L.Localized(error.Message))); }
            Invalidate();
        };
    }

    public int Run(bool renderCheck = false, string? initialPath = null, string? profileMap = null, double profileStartMs = 70000, bool testplayCheck = false, bool firstRunSetup = false, AudioSettingsRestartState? resumeAudio = null, bool hitsoundCopierCheck = false, bool screenshotCheck = false, string? difficultySwitchPath = null)
    {
        string? acknowledgementsPreviewPath = Environment.GetCommandLineArgs().Contains("--acknowledgements-preview")
            ? Path.Combine(Artifacts, "tests", "acknowledgements-preview", Guid.NewGuid().ToString("N") + ".json") : null;
        view.InitializeLibrary(!renderCheck && profileMap is null, renderCheck || profileMap is not null
            ? new FruitsAtelier.Core.LibrarySettings { Workspace = Path.Combine(Artifacts, "render-library") } : null,
            forceFirstRunSetup: firstRunSetup, requireAcknowledgements: !renderCheck && profileMap is null,
            acknowledgementsPath: acknowledgementsPreviewPath);
        pendingStartupFiles = () =>
        {
            if (initialPath is not null) FileOperation(() => OpenPath(initialPath));
            if (resumeAudio is not null) FileOperation(() => RestoreAudioSettings(resumeAudio));
        };
        view.InitializeSkin();
        var instance = Native.GetModuleHandle(null);
        var className = "FruitsAtelier." + Environment.ProcessId;
        string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "app-icon.ico");
        largeBrandIcon = Native.LoadImage(0, iconPath, 1, 32, 32, 0x10);
        smallBrandIcon = Native.LoadImage(0, iconPath, 1, 16, 16, 0x10);
        var windowClass = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Style = 3 | 0x0008,
            Procedure = procedure, Instance = instance, ClassName = className,
            Icon = largeBrandIcon, SmallIcon = smallBrandIcon,
            Cursor = Native.LoadCursor(0, (nint)32512)
        };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception();
        dpi = Native.GetDpiForSystem();
        Native.SystemParametersInfo(0x0030, 0, out var work, 0);
        float initialUiDpi = InitialUiDpi();
        var desired = view.AcknowledgementsVisible
            ? EditorView.AcknowledgementsWindowSize(work.Right - work.Left, work.Bottom - work.Top, initialUiDpi / 96d)
            : view.FirstRunSetupVisible ? EditorView.FirstRunWindowSize(work.Right - work.Left, work.Bottom - work.Top, initialUiDpi / 96d)
            : (Width: 1440d, Height: 900d);
        var rect = new Native.Rectangle { Right = (int)(desired.Width * initialUiDpi / 96), Bottom = (int)(desired.Height * initialUiDpi / 96) };
        uint startupStyle = view.StartupDialogVisible ? 0x80000000u : Native.WindowStyle;
        Native.AdjustWindowRectExForDpi(ref rect, startupStyle, false, 0, (uint)dpi);
        int width = Math.Min(rect.Right - rect.Left, view.StartupDialogVisible ? (int)((work.Right - work.Left) * .85) : work.Right - work.Left - 32);
        int height = Math.Min(rect.Bottom - rect.Top, view.StartupDialogVisible ? (int)((work.Bottom - work.Top) * .85) : work.Bottom - work.Top - 32);
        hwnd = Native.CreateWindowEx(0, className, view.WindowTitle, startupStyle,
            work.Left + (work.Right - work.Left - width) / 2, work.Top + (work.Bottom - work.Top - height) / 2,
            width, height, 0, 0, instance, 0);
        if (hwnd == 0) throw new Win32Exception();
        Native.DragAcceptFiles(hwnd, true);
        dpi = Native.GetDpiForWindow(hwnd);
        int dark = 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        Native.GetClientRect(hwnd, out var client);
        RefreshUiScale(hwnd);
        canvas = new D2DCanvas(hwnd, client.Right, client.Bottom, uiDpi) { Performance = view.Performance };
        AppLog.Write($"Window ready. Adapter={canvas.AdapterName}; DPI={dpi}; Client={client.Right}x{client.Bottom}");
        AppLog.Write($"UI diagnostics enabled. Version={typeof(EditorView).Assembly.GetName().Version}; Runtime={Environment.Version}; OS={Environment.OSVersion}; Process={Environment.ProcessId}");
        if (profileMap is not null)
        {
            Diagnostics.RenderCheck.ProfileMap(canvas, view, profileMap, uiDpi, profileStartMs);
            Native.DestroyWindow(hwnd);
            return 0;
        }
        if (renderCheck && initialPath is not null) FileOperation(() => OpenPath(initialPath));
        if (hitsoundCopierCheck)
        {
            foreach(int testDpi in new[]{96,144,192})
            foreach(var size in new[]{(1440,900),(980,620)})
            {
                canvas.Resize(size.Item1*testDpi/96,size.Item2*testDpi/96,testDpi);
                Diagnostics.HitsoundCopierRenderCheck.Run(canvas,size.Item1,size.Item2);
                AppLog.Write($"Hitsound Copier native rendering passed: {size.Item1}x{size.Item2}, DPI {testDpi}, English and Chinese.");
            }
            return 0;
        }
        if (renderCheck)
        {
            if (Environment.GetCommandLineArgs().Contains("--workspace-save-render-check"))
            {
                view.RequestViewPreference = null;
                view.LoadDocument(FruitsAtelier.Core.DemoMap.Create()); view.CloseLibrary();
                Diagnostics.RenderCheck.CheckWorkspaceSave(canvas, view);
                AppLog.Write("Workspace save native checks passed in English and Chinese at both window sizes.");
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--difficulty-tabs-render-check"))
            {
                Diagnostics.DifficultyTabsRenderCheck.Run(canvas);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--docking-render-check"))
            {
                Diagnostics.DockingRenderCheck.Run(canvas);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--ui-scale-render-check"))
            {
                CheckUiScale();
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--dpi-render-check"))
            {
                CheckDpiScaling();
                Native.ShowWindow(hwnd, 0);
                CheckSetupWindow();
                CheckAcknowledgementsWindow();
                CheckFullscreen();
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--banana-edit-render-check"))
            {
                Diagnostics.BananaEditRenderCheck.Run(canvas);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--acknowledgements-render-check"))
            {
                CheckAcknowledgementsWindow();
                Diagnostics.AcknowledgementsRenderCheck.Run(canvas);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (Environment.GetCommandLineArgs().Contains("--library-opening-render-check"))
            {
                Diagnostics.LibraryOpeningRenderCheck.Run(canvas);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (difficultySwitchPath is not null)
            {
                Diagnostics.DifficultySwitchRenderCheck.Run(canvas, view, difficultySwitchPath, uiDpi);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (screenshotCheck) { CheckScreenshot(); Native.DestroyWindow(hwnd); return 0; }
            if (resumeAudio is not null)
            {
                RestoreAudioSettings(resumeAudio);
                canvas.Begin(); view.Render(canvas, client.Right * 96 / uiDpi, client.Bottom * 96 / uiDpi); canvas.End();
                if (!view.AudioDiagnosticSettingsVisible || view.LibraryVisible != resumeAudio.LibraryVisible
                    || view.ActiveDifficultyIndex != resumeAudio.Difficulty
                    || Math.Abs(view.PlayheadMs - resumeAudio.PositionMs) > .01
                    || view.PlaybackSpeed != resumeAudio.PlaybackSpeed
                    || Math.Abs(view.AudioDiagnosticSettingsScroll - resumeAudio.Scroll) > .1)
                    throw new InvalidOperationException("Audio settings restart did not restore its context.");
                AppLog.Write("Audio settings resume check passed.");
                Native.DestroyWindow(hwnd);
                return 0;
            }
            if (testplayCheck)
            {
                CheckTestplayKeyboard();
                Diagnostics.TestplayRenderCheck.Run(canvas);
                Native.DestroyWindow(hwnd);
                return 0;
            }
            // Native diagnostics exercise view gestures without saving personal preferences.
            view.RequestViewPreference = null;
            view.LoadDocument(FruitsAtelier.Core.DemoMap.Create()); view.CloseLibrary();
            // DXGI need not signal frame readiness for an entirely hidden window.
            if (ImmediatePresentation) Native.ShowWindow(hwnd, 4);
            try { CheckDifficultyAudioReset(); CheckPaintLifecycle(); CheckUpdateRefresh(); CheckDisplayPreference(); CheckFullscreen(); CheckSetupWindow(); }
            finally { Native.ShowWindow(hwnd, 0); }
            Diagnostics.RenderCheck.Run(canvas, view, hwnd);
            Native.DestroyWindow(hwnd);
            return 0;
        }
        ConfigureUpdates();
        CompleteStartupFiles();
        SetFullscreen(view.LibrarySettings.Fullscreen);
        UpdateTitle();
        view.RequestRunTestplay = session => new TestplayInputThread(hwnd, session, () => audio.State);
        Native.SetTimer(hwnd, 1, 16, 0);
        Native.ShowWindow(hwnd, 5);
        Native.UpdateWindow(hwnd);
        while (true)
        {
            Native.Message msg;
            bool activeWait = (view.IsTestplaying || audio.IsPlaying) && !Native.IsIconic(hwnd) && !NativeModalScope.Active;
            long messageWait = view.Performance.Start();
            if (ImmediatePresentation && (framePending || view.IsTestplaying || audio.IsPlaying) && !Native.IsIconic(hwnd))
            {
                if (!Native.PeekMessage(out msg, 0, 0, 0, 1))
                {
                    if (activeWait) view.Performance.End(EditorPerformanceStage.MessageWait, messageWait);
                    long frameWait = view.Performance.Start();
                    bool ready;
                    try { ready = canvas is not null && canvas.WaitForFrameOrInput(); }
                    finally { view.Performance.End(EditorPerformanceStage.FrameWait, frameWait); }
                    if (ready) Invalidate();
                    continue;
                }
                if (msg.Id == 0x0012) break;
            }
            else
            {
                int result = Native.GetMessage(out msg, 0, 0, 0);
                if (result < 0) throw new Win32Exception();
                if (result == 0) break;
            }
            if (activeWait) view.Performance.End(EditorPerformanceStage.MessageWait, messageWait);
            long messageStart = view.Performance.Start();
            bool messageTestplay = view.IsTestplaying;
            long inputStart = BeginInputSample(msg);
            try
            {
                if (DispatchShortcutBeforeTranslation(msg)) continue;
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            finally
            {
                EndInputSample(msg.Id, inputStart);
                EndMessageSample(msg.Id, messageStart, messageTestplay);
                FlushPerformance();
            }
        }
        return 0;
    }

    private void ConfirmDiscard(Action continuation)
    {
        if (view.WaitForSynchronization(ready => { if (ready) ConfirmDiscard(continuation); })) { Invalidate(); return; }
        if (view.DiscardConfirmationVisible || !view.PrepareFileOperation()) return;
        if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
        if (!view.IsDirty) { FileOperation(continuation); return; }
        view.ShowDiscardConfirmation(answer => FileOperation(() =>
        {
            if (answer == 7) continuation();
            else if (answer == 6) SaveProject(continuation);
        }));
        Invalidate();
    }
    private void Close()
    {
        if (view.AcknowledgementsVisible) { Native.DestroyWindow(hwnd); return; }
        view.CancelFirstRunSetup();
        ConfirmDiscard(() => { view.SaveLibraryMemory(); Native.DestroyWindow(hwnd); });
    }
    private void Invalidate() { if (hwnd != 0 && !failed && !NativeModalScope.Active) Native.InvalidateRect(hwnd, 0, false); }

    private nint WndProc(nint window, uint message, nuint wParam, nint lParam)
    {
        try { return HandleMessage(window, message, wParam, lParam); }
        catch (Exception exception)
        {
            AppLog.Write(exception.ToString());
            view.CancelInteraction();
            if (Native.GetCapture() == window) Native.ReleaseCapture();
            if (!failed)
            {
                failed = true;
                if (message == 0x000F)
                {
                    audio.Pause();
                    view.StopTestplay();
                    if (recoveringRenderer)
                    {
                        Native.ShowError(window, L.Get("window.operationFailed", exception.Message), L.Get("app.name"));
                        return 0;
                    }
                    recoveringRenderer = true;
                    try
                    {
                        canvas?.Dispose(); canvas = null;
                        Native.GetClientRect(window, out var size);
                        canvas = new D2DCanvas(window, Math.Max(1, size.Right), Math.Max(1, size.Bottom), uiDpi) { Performance = view.Performance };
                        view.ShowError(L.Get("window.operationFailed", L.Localized(exception.Message)));
                        AppLog.Write("Renderer recreated after paint failure; document retained.");
                        failed = false;
                        Invalidate();
                    }
                    catch (Exception recoveryError)
                    {
                        AppLog.Write(recoveryError.ToString());
                        Native.ShowError(window, L.Get("window.operationFailed", recoveryError.Message), L.Get("app.name"));
                    }
                }
                else if (message == 0x0001)
                    Native.ShowError(window, L.Get("window.operationFailed", exception.Message), L.Get("app.name"));
                else
                {
                    if (Native.GetCapture() == window) Native.ReleaseCapture();
                    view.ShowError(L.Get("window.operationFailed", L.Localized(exception.Message)));
                    failed = false;
                    Invalidate();
                }
            }
            return 0;
        }
    }

    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        float x = (short)((long)lParam & 0xFFFF) * 96f / uiDpi;
        float y = (short)(((long)lParam >> 16) & 0xFFFF) * 96f / uiDpi;
        switch (message)
        {
            case 0x0084 when view.StartupDialogVisible: // WM_NCHITTEST
                var setupPoint = new Native.Point { X = (short)((long)lParam & 0xFFFF), Y = (short)(((long)lParam >> 16) & 0xFFFF) };
                Native.ScreenToClient(window, ref setupPoint);
                return view.StartupHeaderDraggable && setupPoint.Y >= 0 && setupPoint.Y * 96f / uiDpi < (view.FirstRunSetupComplete ? 32 : 60) ? 2 : 1;
            case updateStatusChangedMessage:
                Invalidate();
                return 0;
            case 0x0233: // WM_DROPFILES
                var dropped = Native.TakeDroppedFiles((nint)wParam);
                if (!NativeModalScope.Active) FileOperation(() => view.DropLibraryFiles(dropped));
                return 0;
            case 0x000F: // WM_PAINT
                Native.BeginPaint(window, out var paint);
                bool ownsPaint = false;
                try
                {
                    if (painting || failed || NativeModalScope.Active) return 0;
                    painting = ownsPaint = true;
                    Native.GetClientRect(window, out var rect);
                    if (canvas is not null && rect.Right > 0 && rect.Bottom > 0 && !Native.IsIconic(window))
                    {
                        RefreshUiScale(window);
                        RecordFrameGap();
                        // Continuous repainting can starve WM_TIMER, including update status polling.
                        renderTimer.Restart();
                        long phase = view.Performance.Start();
                        PollUpdates(); PollAudio();
                        view.Performance.End(EditorPerformanceStage.Poll, phase);
                        phase = view.Performance.Start();
                        canvas.Resize(rect.Right, rect.Bottom, uiDpi);
                        bool immediatePresentation = ImmediatePresentation && !startupWindowTransition;
                        // BeginPaint consumes the invalid region even when DXGI cannot accept a frame yet.
                        // Keep that repaint pending so a paused editor also retries when the slot is ready.
                        framePending = true;
                        if (immediatePresentation && !canvas.TryAcquireFrame()) return 0;
                        canvas.Begin();
                        view.Performance.End(EditorPerformanceStage.PrepareFrame, phase);
                        phase = view.Performance.Start();
                        view.Render(canvas, rect.Right * 96 / uiDpi, rect.Bottom * 96 / uiDpi);
                        if (testplayCursorMode != view.TestplayUsesCursor)
                        {
                            testplayCursorMode = view.TestplayUsesCursor;
                            if (Native.GetCursorPos(out var pointer) && Native.WindowFromPoint(pointer) == hwnd)
                                Native.SetCursor(testplayCursorMode ? 0 : Native.LoadCursor(0, (nint)32512));
                        }
                        canvas.DrawDisplayDiagnostics(view.PlayheadMs, displayedAudioState ?? audio.State);
                        view.Performance.End(EditorPerformanceStage.ViewRender, phase);
                        phase = view.Performance.Start();
                        framePending = !canvas.End(lowLatency: immediatePresentation, capture: screenshotPending ? CopyScreenshot : null);
                        view.Performance.End(EditorPerformanceStage.Submit, phase);
                        view.Performance.Record(EditorPerformanceStage.Frame, renderTimer.Elapsed.TotalMilliseconds);
                        if (!framePending) RecordInputSubmission();
                        recoveringRenderer = false;
                        RecordPlaybackRate(immediatePresentation);
                        renderTimer.Stop();
                        if (++frames == 1) AppLog.Write($"First frame: {renderTimer.Elapsed.TotalMilliseconds:F2}ms");
                    }
                }
                finally
                {
                    if (ownsPaint)
                    {
                        try { canvas?.AbortDraw(); }
                        catch (Exception abortError) { AppLog.Write(abortError.ToString()); }
                    }
                    Native.EndPaint(window, ref paint);
                    if (ownsPaint) painting = false;
                }
                // DXGI readiness wakes immediate-mode drawing; window messages can interrupt that wait.
                if (audio.IsPlaying && !ImmediatePresentation && !Native.IsIconic(window)) Invalidate();
                return 0;
            case 0x0014: return 1; // WM_ERASEBKGND
            case 0x0113: // WM_TIMER
                if (painting || failed || NativeModalScope.Active) return 0;
                long pollStart = view.Performance.Start();
                PollUpdates(); PollAudio();
                view.Performance.End(EditorPerformanceStage.Poll, pollStart);
                if ((view.TextCaretNeedsRedraw || view.SliderHoldNeedsRedraw || view.MarqueeScrollNeedsRedraw
                    || view.CanvasBackgroundDimNeedsRedraw || view.VolumePopoverNeedsRedraw || view.WaveformNeedsRedraw || view.ConversionNeedsRedraw || view.SynchronizationNeedsRedraw) && !Native.IsIconic(window)) Invalidate();
                return 0;
            case 0x0005: RefreshUiScale(window); Invalidate(); return 0; // WM_SIZE
            case 0x02E4: // WM_GETDPISCALEDSIZE
                var pendingSize = Marshal.PtrToStructure<Native.Point>(lParam);
                uint style = (uint)Native.GetWindowLongPtr(window, -16);
                uint extendedStyle = (uint)Native.GetWindowLongPtr(window, -20);
                var oldFrame = new Native.Rectangle();
                var newFrame = new Native.Rectangle();
                Native.AdjustWindowRectExForDpi(ref oldFrame, style, false, extendedStyle, (uint)dpi);
                Native.AdjustWindowRectExForDpi(ref newFrame, style, false, extendedStyle, (uint)wParam);
                // Preserve the pending client pixel size, including a simultaneous user resize.
                pendingSize.X += newFrame.Right - newFrame.Left - (oldFrame.Right - oldFrame.Left);
                pendingSize.Y += newFrame.Bottom - newFrame.Top - (oldFrame.Bottom - oldFrame.Top);
                Marshal.StructureToPtr(pendingSize, lParam, false);
                return 1;
            case 0x02E0: // WM_DPICHANGED
                view.CancelInteraction();
                if (Native.GetCapture() == window) Native.ReleaseCapture();
                dpi = (uint)wParam & 0xFFFF;
                var suggested = Marshal.PtrToStructure<Native.Rectangle>(lParam);
                if (view.StartupDialogVisible) FitSetupWindow();
                else if (fullscreen) FitFullscreenMonitor();
                else Native.SetWindowPos(window, 0, suggested.Left, suggested.Top, suggested.Right - suggested.Left,
                    suggested.Bottom - suggested.Top, 0x0004 | 0x0010);
                AppLog.Write($"DPI changed: {dpi}");
                Invalidate(); return 0;
            case 0x0024: // WM_GETMINMAXINFO
                var minMax = Marshal.PtrToStructure<Native.MinMaxInfo>(lParam);
                minMax.MinTrackSize = fullscreen ? new Native.Point { X = 1, Y = 1 }
                    : view.StartupDialogVisible ? SetupMinimumTrackSize()
                    : EditorMinimumTrackSize();
                Marshal.StructureToPtr(minMax, lParam, false); return 0;
            case 0x0201:
            case 0x0204:
            case 0x0207:
                view.SetModifiers(Native.Alt, Native.Shift);
                Native.SetFocus(window);
                view.PointerDown(x, y, message == 0x0207 ? 1 : message == 0x0204 ? 2 : 0, Native.Shift, Native.Control);
                if (view.WantsCapture) Native.SetCapture(window);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0203: // WM_LBUTTONDBLCLK
                view.SetModifiers(Native.Alt, Native.Shift);
                Native.SetFocus(window);
                view.PointerDoubleClick(x, y, Native.Shift, Native.Control);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0020: // WM_SETCURSOR
                if ((lParam.ToInt64() & 0xffff) == 1)
                { Native.SetCursor(view.TestplayUsesCursor ? 0 : Native.LoadCursor(0, (nint)(view.TimelineResizeCursor || view.PreviewResizeCursor || view.AimodColumnResizeCursor || view.SelectionScaleCursor ? 32644 : 32512))); return 1; }
                break;
            case 0x02A3: // WM_MOUSELEAVE
                view.PointerLeave(); Invalidate(); return 0;
            case 0x0200:
                var tracking = new Native.MouseTracking { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MouseTracking>(), Flags = 2, Window = window };
                Native.TrackMouseEvent(ref tracking);
                view.SetModifiers(Native.Alt, Native.Shift);
                view.PointerMove(x, y, Native.Shift, Native.Control);
                Native.SetCursor(view.TestplayUsesCursor ? 0 : Native.LoadCursor(0, (nint)(view.TimelineResizeCursor || view.PreviewResizeCursor || view.AimodColumnResizeCursor || view.SelectionScaleCursor ? 32644 : 32512)));
                UpdateTitle(); Invalidate(); return 0;
            case 0x0202:
            case 0x0205:
            case 0x0208:
                view.PointerUp(x, y, message == 0x0208 ? 1 : message == 0x0205 ? 2 : 0, Native.Shift);
                if (!view.WantsCapture && Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x020A:
                view.SetModifiers(Native.Alt, Native.Shift);
                var point = new Native.Point { X = (short)((long)lParam & 0xFFFF), Y = (short)(((long)lParam >> 16) & 0xFFFF) };
                Native.ScreenToClient(window, ref point);
                view.Wheel(point.X * 96f / uiDpi, point.Y * 96f / uiDpi, (short)((ulong)wParam >> 16),
                    (wParam & 0x0008) != 0, Native.Shift, Native.Alt);
                Invalidate(); return 0;
            case 0x0100:
                view.SetModifiers(Native.Alt, Native.Shift);
                view.KeyDown((int)ResolveVirtualKey(wParam, lParam, down: true), Native.Control, Native.Shift);
                if (!view.WantsCapture && Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0104: // WM_SYSKEYDOWN: Alt changes editor snapping without opening the system menu.
                view.SetModifiers(Native.Alt, Native.Shift);
                if ((int)wParam == 13 && ((long)lParam & (1L << 29)) != 0)
                {
                    view.SetModifiers(true, Native.Shift);
                    view.KeyDown(13, Native.Control, Native.Shift);
                    UpdateTitle(); Invalidate(); return 0;
                }
                if ((int)wParam is 37 or 38 or 39 or 40 || (int)wParam == 69 && Native.Control)
                {
                    view.KeyDown((int)wParam, Native.Control, Native.Shift);
                    UpdateTitle(); Invalidate(); return 0;
                }
                if ((view.CapturingTestplayKey || view.IsTestplaying) && !((int)wParam == 115 && Native.Alt))
                {
                    view.KeyDown((int)wParam, Native.Control, Native.Shift);
                    UpdateTitle(); Invalidate(); return 0;
                }
                if ((int)wParam == 0x12) { Invalidate(); return 0; }
                break;
            case 0x0101: // WM_KEYUP
            case 0x0105: // WM_SYSKEYUP
                view.KeyUp((int)ResolveVirtualKey(wParam, lParam, down: false));
                view.SetModifiers(Native.Alt, Native.Shift);
                Invalidate();
                if ((int)wParam is 0x12 or 0x10) return 0;
                break;
            case 0x0102:
                if (!Native.Control) view.TextInput((char)wParam);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0106: // WM_SYSCHAR
                if ((int)wParam == 13) return 0;
                break;
            case 0x0007: view.SetTextInputFocus(true); view.CheckFilesOnActivation(); Invalidate(); return 0; // WM_SETFOCUS
            case 0x0008: // WM_KILLFOCUS
                view.SetTextInputFocus(false);
                view.CancelInteraction(preserveTestplay: true);
                if (Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x001F: // WM_CANCELMODE
                view.CancelInteraction(preserveTestplay: view.IsTestplaying);
                if (Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0215: // WM_CAPTURECHANGED
                if (view.WantsCapture) view.CancelInteraction();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0010: Close(); return 0;
            case 0x0002: Native.KillTimer(window, 1); Native.PostQuitMessage(0); return 0;
        }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }

    private void RecordPlaybackRate(bool immediatePresentation)
    {
        if (!audio.IsPlaying)
        {
            playbackSampleTimer.Reset(); playbackSampleFrames = 0; playbackSampleComplete = false;
            return;
        }
        if (playbackSampleComplete) return;
        if (!playbackSampleTimer.IsRunning) { playbackSampleTimer.Start(); return; }
        playbackSampleFrames++;
        if (playbackSampleTimer.Elapsed.TotalSeconds < 5) return;
        AppLog.Write($"Playback render rate: {playbackSampleFrames / playbackSampleTimer.Elapsed.TotalSeconds:F1} FPS over {playbackSampleTimer.Elapsed.TotalSeconds:F2}s (Present sync interval {(immediatePresentation ? 0 : 1)})");
        playbackSampleComplete = true;
    }

    private void UpdateTitle()
    {
        string title = view.WindowTitle;
        if (title == lastTitle) return;
        Native.SetWindowText(hwnd, title); lastTitle = title;
    }
    private nint largeBrandIcon, smallBrandIcon;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        view.StopFileMonitoring();
        updates?.Dispose();
        view.StopTestplay();
        view.ReleaseWaveform();
        if (largeBrandIcon != 0) { Native.DestroyIcon(largeBrandIcon); largeBrandIcon = 0; }
        if (smallBrandIcon != 0) { Native.DestroyIcon(smallBrandIcon); smallBrandIcon = 0; }
        hitsounds.Dispose();
        setupAudio?.Dispose();
        audio.Dispose();
        canvas?.Dispose();
        FlushPerformance(force: true);
        AppLog.Write($"Window closed. Frames={frames}");
        if (AppLog.Performance == view.Performance) AppLog.Performance = null;
        GC.KeepAlive(procedure);
    }
}
