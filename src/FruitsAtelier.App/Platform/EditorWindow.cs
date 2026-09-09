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
    private readonly EditorView view = new();
    private D2DCanvas? canvas;
    private nint hwnd;
    private float dpi = 96;
    private bool failed, disposed;
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
        view.RequestClose = Close;
        view.RequestLoadSkin = () =>
        {
            view.CancelInteraction();
            if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
            try
            {
                string? archive = SkinFileDialog.SelectArchive(hwnd);
                if (archive is not null) LoadSkinArchive(archive);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { view.SetNotice(L.Get("window.skinFailed", L.Localized(error.Message))); }
            Invalidate();
        };
        view.RequestResetDemo = () =>
        {
            if (ConfirmDiscard()) { ResetAudio(); projectPath = null; view.LoadDocument(FruitsAtelier.Core.DemoMap.Create()); Invalidate(); }
        };
    }

    public int Run(bool renderCheck = false, string? initialPath = null)
    {
        string defaultSkin = Path.Combine(AppContext.BaseDirectory, "assets", "skins", "default.osk");
        if (File.Exists(defaultSkin))
        {
            try { LoadSkinArchive(defaultSkin); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { view.SetNotice(L.Get("window.defaultSkinFailed", L.Localized(error.Message))); }
        }
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
        hwnd = Native.CreateWindowEx(0, className, L.Get("window.initialTitle"), Native.WindowStyle,
            work.Left + (work.Right - work.Left - width) / 2, work.Top + (work.Bottom - work.Top - height) / 2,
            width, height, 0, 0, instance, 0);
        if (hwnd == 0) throw new Win32Exception();
        dpi = Native.GetDpiForWindow(hwnd);
        int dark = 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        Native.GetClientRect(hwnd, out var client);
        canvas = new D2DCanvas(hwnd, client.Right, client.Bottom, dpi);
        AppLog.Write($"Window ready. Adapter={canvas.AdapterName}; DPI={dpi}; Client={client.Right}x{client.Bottom}");
        if (initialPath is not null) OpenPath(initialPath);
        if (renderCheck)
        {
            Diagnostics.RenderCheck.Run(canvas, view);
            Native.DestroyWindow(hwnd);
            return 0;
        }
        UpdateTitle();
        Native.SetTimer(hwnd, 1, 16, 0);
        Native.ShowWindow(hwnd, 5);
        Native.UpdateWindow(hwnd);
        int result;
        while ((result = Native.GetMessage(out var msg, 0, 0, 0)) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
        if (result < 0) throw new Win32Exception();
        return 0;
    }

    private bool ConfirmDiscard()
    {
        if (!view.PrepareFileOperation()) return false;
        if (Native.GetCapture() == hwnd) Native.ReleaseCapture();
        if (!view.IsDirty) return true;
        int answer = Native.MessageBox(hwnd, L.Get("window.confirmDiscard"),
            L.Get("app.name"), 0x00000003 | 0x00000030 | 0x00000200);
        return answer == 7 || answer == 6 && SaveProject(false);
    }
    private void Close() { if (ConfirmDiscard()) Native.DestroyWindow(hwnd); }
    private void Invalidate() { if (hwnd != 0) Native.InvalidateRect(hwnd, 0, false); }

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
                Native.MessageBox(window, L.Get("window.operationFailed", exception.Message), L.Get("app.name"), 0x10);
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
            case 0x000F: // WM_PAINT
                Native.BeginPaint(window, out var paint);
                try
                {
                    Native.GetClientRect(window, out var rect);
                    if (canvas is not null && rect.Right > 0 && rect.Bottom > 0 && !Native.IsIconic(window))
                    {
                        PollAudio();
                        renderTimer.Restart();
                        canvas.Resize(rect.Right, rect.Bottom, dpi);
                        canvas.Begin();
                        view.Render(canvas, rect.Right * 96 / dpi, rect.Bottom * 96 / dpi);
                        canvas.End();
                        RecordPlaybackRate();
                        renderTimer.Stop();
                        if (++frames == 1) AppLog.Write($"First frame: {renderTimer.Elapsed.TotalMilliseconds:F2}ms");
                    }
                }
                finally { Native.EndPaint(window, ref paint); }
                // Present(1) paces continuous playback paints to the display's vertical refresh.
                if (audio.IsPlaying && !Native.IsIconic(window)) Invalidate();
                return 0;
            case 0x0014: return 1; // WM_ERASEBKGND
            case 0x0113: PollAudio(); return 0; // WM_TIMER
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
                Native.SetFocus(window);
                view.PointerDown(x, y, message == 0x0207 ? 1 : message == 0x0204 ? 2 : 0, Native.Shift, Native.Control);
                if (view.WantsCapture) Native.SetCapture(window);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0203: // WM_LBUTTONDBLCLK
                Native.SetFocus(window);
                view.PointerDoubleClick(x, y, Native.Shift, Native.Control);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0200:
                view.PointerMove(x, y, Native.Shift, Native.Control);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0202:
            case 0x0205:
            case 0x0208:
                view.PointerUp(x, y, message == 0x0208 ? 1 : message == 0x0205 ? 2 : 0);
                if (!view.WantsCapture && Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x020A:
                var point = new Native.Point { X = (short)((long)lParam & 0xFFFF), Y = (short)(((long)lParam >> 16) & 0xFFFF) };
                Native.ScreenToClient(window, ref point);
                view.Wheel(point.X * 96f / dpi, point.Y * 96f / dpi, (short)((ulong)wParam >> 16), (wParam & 0x0008) != 0);
                Invalidate(); return 0;
            case 0x0100:
                if ((int)wParam == 86 && Native.Control && view.LibraryTextFocused)
                { view.PasteLibraryText(Native.ReadClipboardText(window)); Invalidate(); return 0; }
                view.KeyDown((int)wParam, Native.Control, Native.Shift);
                if (!view.WantsCapture && Native.GetCapture() == window) Native.ReleaseCapture();
                UpdateTitle(); Invalidate(); return 0;
            case 0x0102:
                if (!Native.Control) view.TextInput((char)wParam);
                UpdateTitle(); Invalidate(); return 0;
            case 0x0008: // WM_KILLFOCUS
            case 0x001F: // WM_CANCELMODE
                view.CancelInteraction();
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
        string title = L.Get("window.title", view.ProjectName, view.IsDirty ? " *" : "", L.Get(view.Document.IsDemo ? "window.demo" : "window.milestone"));
        if (title == lastTitle) return;
        Native.SetWindowText(hwnd, title); lastTitle = title;
    }
    private void LoadSkinArchive(string archive)
    {
        string cache = Path.Combine(Path.GetDirectoryName(AppLog.Path)!, "..", "skins");
        view.LoadSkin(SkinArchive.Import(archive, Path.GetFullPath(cache)));
    }
    private nint largeBrandIcon, smallBrandIcon;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (largeBrandIcon != 0) { Native.DestroyIcon(largeBrandIcon); largeBrandIcon = 0; }
        if (smallBrandIcon != 0) { Native.DestroyIcon(smallBrandIcon); smallBrandIcon = 0; }
        hitsounds.Dispose();
        audio.Dispose();
        canvas?.Dispose();
        AppLog.Write($"Window closed. Frames={frames}");
        GC.KeepAlive(procedure);
    }
}
