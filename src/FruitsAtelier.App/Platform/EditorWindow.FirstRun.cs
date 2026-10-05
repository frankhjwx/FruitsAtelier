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
        return new() { X = Math.Min((int)(640 * dpi / 96), (int)((work.Right - work.Left) * .85)),
            Y = Math.Min((int)(440 * dpi / 96), (int)((work.Bottom - work.Top) * .85)) };
    }

    private void FitSetupWindow() => FitWindow(setup: true);
    private void FitEditorWindow() => FitWindow(setup: false);
    private void FitWindow(bool setup)
    {
        var work = CurrentWorkArea();
        var size = setup ? EditorView.FirstRunWindowSize(work.Right - work.Left, work.Bottom - work.Top, dpi / 96d)
            : (Width: 1440d, Height: 900d);
        var rect = new Native.Rectangle { Right = (int)(size.Width * dpi / 96), Bottom = (int)(size.Height * dpi / 96) };
        uint style = setup ? 0x80000000u : Native.WindowStyle;
        Native.SetWindowLongPtr(hwnd, -16, (nint)style);
        Native.AdjustWindowRectExForDpi(ref rect, style, false, 0, (uint)dpi);
        int w = Math.Min(rect.Right - rect.Left, setup ? (int)((work.Right - work.Left) * .85) : work.Right - work.Left - 32);
        int h = Math.Min(rect.Bottom - rect.Top, setup ? (int)((work.Bottom - work.Top) * .85) : work.Bottom - work.Top - 32);
        Native.SetWindowPos(hwnd, 0, work.Left + (work.Right - work.Left - w) / 2,
            work.Top + (work.Bottom - work.Top - h) / 2, w, h, 0x0004 | 0x0010 | 0x0020);
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
