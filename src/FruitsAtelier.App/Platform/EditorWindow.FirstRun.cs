using FruitsAtelier.App.Audio;
using FruitsAtelier.App.Editor;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private Native.Rectangle CurrentWorkArea()
    {
        var info = new Native.MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MonitorInfo>() };
        if (Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, 2), ref info)) return info.Work;
        Native.SystemParametersInfo(0x0030, 0, out var work, 0);
        return work;
    }

    private Native.Point SetupMinimumTrackSize()
    {
        var work = CurrentWorkArea();
        float scale = InitialUiDpi() / 96;
        return new() { X = Math.Min((int)(640 * scale), (int)((work.Right - work.Left) * .85)),
            Y = Math.Min((int)(440 * scale), (int)((work.Bottom - work.Top) * .85)) };
    }

    private Native.Point EditorMinimumTrackSize()
    {
        var work = CurrentWorkArea();
        float scale = InitialUiDpi() / 96;
        return new() { X = Math.Min((int)(980 * scale), work.Right - work.Left),
            Y = Math.Min((int)(620 * scale), work.Bottom - work.Top) };
    }

    private void FitSetupWindow() => FitWindow(setup: true);
    private void FitEditorWindow() { FitWindow(setup: false); RestoreWindowPreferences(); }
    private bool startupWindowTransition;

    private void TransitionStartupWindow(Action transition)
    {
        startupWindowTransition = true;
        try
        {
            // Geometry changes can pump messages; present only after the final client size is ready.
            using (new NativeModalScope(hwnd)) transition();
            Native.RedrawWindow(hwnd, 0, 0, 0x0001 | 0x0020 | 0x0100 | 0x0400);
        }
        finally { startupWindowTransition = false; }
    }

    // Hidden swap chains need not signal frame readiness, so decoration changes must preserve visibility.
    internal static uint FirstRunWindowStyle(uint currentStyle, bool setup)
        => (currentStyle & ~(Native.WindowStyle | 0x80000000u)) | (setup ? 0x80000000u : Native.WindowStyle);
    private void FitWindow(bool setup)
    {
        var work = CurrentWorkArea();
        float initialUiDpi = InitialUiDpi();
        var size = setup && view.AcknowledgementsVisible ? EditorView.AcknowledgementsWindowSize(work.Right - work.Left, work.Bottom - work.Top, initialUiDpi / 96d)
            : setup ? EditorView.FirstRunWindowSize(work.Right - work.Left, work.Bottom - work.Top, initialUiDpi / 96d)
            : (Width: 1440d, Height: 900d);
        var rect = new Native.Rectangle { Right = (int)(size.Width * initialUiDpi / 96), Bottom = (int)(size.Height * initialUiDpi / 96) };
        uint currentStyle = (uint)Native.GetWindowLongPtr(hwnd, -16);
        uint style = FirstRunWindowStyle(currentStyle, setup);
        if (currentStyle != style) Native.SetWindowLongPtr(hwnd, -16, (nint)style);
        Native.AdjustWindowRectExForDpi(ref rect, style, false, 0, (uint)dpi);
        int w = Math.Min(rect.Right - rect.Left, setup ? (int)((work.Right - work.Left) * .85) : work.Right - work.Left - 32);
        int h = Math.Min(rect.Bottom - rect.Top, setup ? (int)((work.Bottom - work.Top) * .85) : work.Bottom - work.Top - 32);
        Native.SetWindowPos(hwnd, 0, work.Left + (work.Right - work.Left - w) / 2,
            work.Top + (work.Bottom - work.Top - h) / 2, w, h,
            0x0004 | 0x0010 | 0x0020 | (startupWindowTransition ? 0x0008u | 0x0100u : 0));
    }

    private AudioTransport? setupAudio;
    private bool setupPlayRequested;

    private void ControlSetupAudio(SetupAudioCommand command)
    {
        if (command == SetupAudioCommand.Stop)
        {
            setupPlayRequested = false;
            var retiring = setupAudio; setupAudio = null;
            retiring?.Dispose();
        }
        else if (command == SetupAudioCommand.Pause)
        {
            setupPlayRequested = false;
            setupAudio?.Pause();
        }
        else
        {
            if (setupAudio is null)
            {
                setupAudio = new AudioTransport();
                view.ApplyAudioVolume();
                setupAudio.Load(EditorView.SetupAudioPath);
            }
            setupPlayRequested = true;
        }
        PollSetupAudio();
        Invalidate();
    }

    private void PollSetupAudio()
    {
        if (setupAudio is null) return;
        var state = setupAudio.State;
        if (setupPlayRequested && state.CanPlay)
        {
            setupPlayRequested = false;
            setupAudio.Play(); state = setupAudio.State;
        }
        if (state.Error is not null) setupPlayRequested = false;
        view.UpdateSetupAudio(state.PositionMs, state.DurationMs, state.IsPlaying, state.IsLoading,
            state.Error is null ? null : FruitsAtelier.Localization.Strings.Reformat(state.Error));
    }
}
