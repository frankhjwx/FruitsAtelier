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
    private float dpi = 96;
    private bool failed, disposed, painting, recoveringRenderer;
    private string lastTitle = "";
    private int frames;
    private readonly Stopwatch renderTimer = new();
    private readonly Stopwatch playbackSampleTimer = new();
    private int playbackSampleFrames;
    private bool playbackSampleComplete;

    public EditorWindow()
    {
        procedure = WndProc;
        ConfigureFiles();
        view.RequestCopyText = text => Native.WriteClipboardText(hwnd, text);
        view.RequestPasteTime = () => view.PasteTimeJumpText(Native.ReadClipboardText(hwnd), view.TimeJumpSession);
        view.RequestPasteSongSetup = () => view.PasteSongSetupText(Native.ReadClipboardText(hwnd), view.SongSetupInputSession);
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

    public int Run(bool renderCheck = false, string? initialPath = null, string? profileMap = null, double profileStartMs = 70000)
    {
        view.InitializeLibrary(!renderCheck && profileMap is null, renderCheck || profileMap is not null
            ? new FruitsAtelier.Core.LibrarySettings { Workspace = Path.Combine(Artifacts, "render-library") } : null);
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
        var rect = new Native.Rectangle { Right = (int)(1440 * dpi / 96), Bottom = (int)(900 * dpi / 96) };
        Native.AdjustWindowRectExForDpi(ref rect, Native.WindowStyle, false, 0, (uint)dpi);
        int width = Math.Min(rect.Right - rect.Left, work.Right - work.Left - 32);
        int height = Math.Min(rect.Bottom - rect.Top, work.Bottom - work.Top - 32);
        hwnd = Native.CreateWindowEx(0, className, view.WindowTitle, Native.WindowStyle,
            work.Left + (work.Right - work.Left - width) / 2, work.Top + (work.Bottom - work.Top - height) / 2,
            width, height, 0, 0, instance, 0);
        if (hwnd == 0) throw new Win32Exception();
        Native.DragAcceptFiles(hwnd, true);
        dpi = Native.GetDpiForWindow(hwnd);
        int dark = 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        Native.GetClientRect(hwnd, out var client);
        canvas = new D2DCanvas(hwnd, client.Right, client.Bottom, dpi);
        AppLog.Write($"Window ready. Adapter={canvas.AdapterName}; DPI={dpi}; Client={client.Right}x{client.Bottom}");
        if (profileMap is not null)
        {
            Diagnostics.RenderCheck.ProfileMap(canvas, view, profileMap, dpi, profileStartMs);
            Native.DestroyWindow(hwnd);
            return 0;
        }
        if (initialPath is not null) FileOperation(() => OpenPath(initialPath));
        if (renderCheck)
        {
            view.LoadDocument(FruitsAtelier.Core.DemoMap.Create()); view.CloseLibrary();
            CheckPaintLifecycle();
            CheckUpdateRefresh();
            CheckNativeText();
            Diagnostics.RenderCheck.Run(canvas, view, hwnd);
            Native.DestroyWindow(hwnd);
            return 0;
        }
        ConfigureUpdates();
        UpdateTitle();
        view.RequestRunTestplay = session => new TestplayInputThread(hwnd, session, () => audio.State);
        Native.SetTimer(hwnd, 1, 16, 0);
        Native.ShowWindow(hwnd, 5);
        Native.UpdateWindow(hwnd);
        while (true)
        {
            Native.Message msg;
            if (view.IsTestplaying && !Native.IsIconic(hwnd))
            {
                if (!Native.PeekMessage(out msg, 0, 0, 0, 1))
                {
                    if (canvas is not null && canvas.WaitForFrameOrInput()) Invalidate();
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
            // IME-owned key messages lose their original key after TranslateMessage.
            if (msg.Window == nativeText && msg.Id == 0x0100)
            {
                int key = (int)msg.WParam;
                if (key is 9 or 13 or 27)
                {
                    view.KeyDown(key, Native.Control, Native.Shift);
                    Invalidate();
                    continue;
                }
                if (key == 65 && Native.Control)
                {
                    Native.SendMessage(nativeText, 0x00B1, 0, -1);
                    continue;
                }
            }
            if (msg.Window == hwnd && msg.Id == 0x0100 && Native.Control && Native.Shift)
            {
                uint key = msg.WParam == 0xE5 ? Native.ImmGetVirtualKey(hwnd) : (uint)msg.WParam;
                if (key == 70)
                {
                    view.KeyDown(70, true, true);
                    if (!view.WantsCapture && Native.GetCapture() == hwnd) Native.ReleaseCapture();
                    UpdateTitle(); Invalidate();
                    continue;
                }
            }
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
        return 0;
    }

    private void ConfirmDiscard(Action continuation)
    {
        if (view.DiscardConfirmationVisible || !view.PrepareFileOperation()) return;
        if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
        if (!view.IsDirty) { FileOperation(continuation); return; }
        view.ShowDiscardConfirmation(answer => FileOperation(() =>
        {
            if (answer == 7 || answer == 6 && SaveProject()) continuation();
        }));
        Invalidate();
    }
    private void Close() => ConfirmDiscard(() => { view.SaveLibraryMemory(); Native.DestroyWindow(hwnd); });
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
                        canvas = new D2DCanvas(window, Math.Max(1, size.Right), Math.Max(1, size.Bottom), dpi);
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
        float x = (short)((long)lParam & 0xFFFF) * 96f / dpi;
        float y = (short)(((long)lParam >> 16) & 0xFFFF) * 96f / dpi;
        switch (message)
        {
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
                        // Continuous repainting can starve WM_TIMER, including update status polling.
                        PollUpdates(); PollAudio();
                        renderTimer.Restart();
                        canvas.Resize(rect.Right, rect.Bottom, dpi);
                        if (view.IsTestplaying && !canvas.TryAcquireFrame()) return 0;
                        canvas.Begin();
                        view.Render(canvas, rect.Right * 96 / dpi, rect.Bottom * 96 / dpi);
                        canvas.End(lowLatency: view.IsTestplaying);
                        recoveringRenderer = false;
                        RecordPlaybackRate();
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
                // DXGI readiness wakes testplay drawing; window messages can interrupt that wait.
                if (ownsPaint) SyncNativeText();
                if (audio.IsPlaying && !view.IsTestplaying && !Native.IsIconic(window)) Invalidate();
                return 0;
            case 0x0111: if (HandleNativeTextCommand(wParam, lParam)) return 0; break; // WM_COMMAND
            case 0x0133: // WM_CTLCOLOREDIT
                if (lParam == nativeText && nativeTextBrush != 0)
                {
                    Native.SetTextColor((nint)wParam, 0xF2EBE7);
                    Native.SetBkColor((nint)wParam, 0x3A2F28);
                    return nativeTextBrush;
                }
                break;
            case 0x0014: return 1; // WM_ERASEBKGND
            case 0x0113: // WM_TIMER
                if (painting || failed || NativeModalScope.Active) return 0;
                PollUpdates(); PollAudio();
                if ((view.TextCaretNeedsRedraw || view.SliderHoldNeedsRedraw || view.MarqueeScrollNeedsRedraw) && !Native.IsIconic(window)) Invalidate();
                return 0;
            case 0x0005: Invalidate(); return 0;
            case 0x02E0: // WM_DPICHANGED
                view.CancelInteraction();
                if (Native.GetCapture() == window) Native.ReleaseCapture();
                dpi = (uint)wParam & 0xFFFF;
                var suggested = Marshal.PtrToStructure<Native.Rectangle>(lParam);
                Native.SetWindowPos(window, 0, suggested.Left, suggested.Top, suggested.Right - suggested.Left,
                    suggested.Bottom - suggested.Top, 0x0004 | 0x0010);
                AppLog.Write($"DPI changed: {dpi}");
                Invalidate(); return 0;
            case 0x0024: // WM_GETMINMAXINFO
                var minMax = Marshal.PtrToStructure<Native.MinMaxInfo>(lParam);
                minMax.MinTrackSize = new Native.Point { X = (int)(980 * dpi / 96), Y = (int)(620 * dpi / 96) };
                Marshal.StructureToPtr(minMax, lParam, false); return 0;
            case 0x0201:
            case 0x0204:
            case 0x0207:
                view.SetModifiers(Native.Alt, Native.Shift);
                Native.SetFocus(window);
                if (message == 0x0201) nativeTextClick = (x, y);
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
                { Native.SetCursor(Native.LoadCursor(0, (nint)(view.TimelineResizeCursor || view.PreviewResizeCursor ? 32644 : 32512))); return 1; }
                break;
            case 0x0200:
                view.SetModifiers(Native.Alt, Native.Shift);
                view.PointerMove(x, y, Native.Shift, Native.Control);
                Native.SetCursor(Native.LoadCursor(0, (nint)(view.TimelineResizeCursor || view.PreviewResizeCursor ? 32644 : 32512)));
                UpdateTitle(); Invalidate(); return 0;
            case 0x0202:
            case 0x0205:
            case 0x0208:
                view.PointerUp(x, y, message == 0x0208 ? 1 : message == 0x0205 ? 2 : 0);
                if (!view.WantsCapture && Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x020A:
                view.SetModifiers(Native.Alt, Native.Shift);
                var point = new Native.Point { X = (short)((long)lParam & 0xFFFF), Y = (short)(((long)lParam >> 16) & 0xFFFF) };
                Native.ScreenToClient(window, ref point);
                view.Wheel(point.X * 96f / dpi, point.Y * 96f / dpi, (short)((ulong)wParam >> 16), (wParam & 0x0008) != 0);
                Invalidate(); return 0;
            case 0x0100:
                view.SetModifiers(Native.Alt, Native.Shift);
                if ((int)wParam == 86 && Native.Control && view.LibraryTextFocused && !view.DiscardConfirmationVisible && !view.ErrorVisible)
                { view.PasteLibraryText(Native.ReadClipboardText(window)); Invalidate(); return 0; }
                view.KeyDown((int)wParam, Native.Control, Native.Shift);
                if (!view.WantsCapture && Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0104: // WM_SYSKEYDOWN: Alt changes editor snapping without opening the system menu.
                view.SetModifiers(Native.Alt, Native.Shift);
                if ((view.CapturingTestplayKey || view.IsTestplaying) && !((int)wParam == 115 && Native.Alt))
                {
                    view.KeyDown((int)wParam, Native.Control, Native.Shift);
                    UpdateTitle(); Invalidate(); return 0;
                }
                if ((int)wParam == 0x12) { Invalidate(); return 0; }
                break;
            case 0x0101: // WM_KEYUP
            case 0x0105: // WM_SYSKEYUP
                view.KeyUp((int)wParam);
                view.SetModifiers(Native.Alt, Native.Shift);
                Invalidate();
                if ((int)wParam is 0x12 or 0x10) return 0;
                break;
            case 0x0102:
                if (!Native.Control) view.TextInput((char)wParam);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0007: view.SetTextInputFocus(true); Invalidate(); return 0; // WM_SETFOCUS
            case 0x0008: // WM_KILLFOCUS
                if (lParam == nativeText) return 0;
                view.SetTextInputFocus(false);
                goto case 0x001F;
            case 0x001F: // WM_CANCELMODE
                view.CancelInteraction();
                if (Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0215: // WM_CAPTURECHANGED
                if (view.WantsCapture) view.CancelInteraction();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0010: Close(); return 0;
            case 0x0002: Native.KillTimer(window, 1); DisposeNativeText(); Native.PostQuitMessage(0); return 0;
        }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }

    private void RecordPlaybackRate()
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
        AppLog.Write($"Playback render rate: {playbackSampleFrames / playbackSampleTimer.Elapsed.TotalSeconds:F1} FPS over {playbackSampleTimer.Elapsed.TotalSeconds:F2}s (Present sync interval 1)");
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
        updates?.Dispose();
        view.StopTestplay();
        if (largeBrandIcon != 0) { Native.DestroyIcon(largeBrandIcon); largeBrandIcon = 0; }
        if (smallBrandIcon != 0) { Native.DestroyIcon(smallBrandIcon); smallBrandIcon = 0; }
        hitsounds.Dispose();
        DisposeNativeText();
        audio.Dispose();
        canvas?.Dispose();
        AppLog.Write($"Window closed. Frames={frames}");
        GC.KeepAlive(procedure);
    }
}
