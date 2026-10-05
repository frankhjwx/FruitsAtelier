using FruitsAtelier.App.Editor;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private void ResizeForSetup(bool setup)
    {
        SystemDecorations = setup ? Avalonia.Controls.SystemDecorations.None : Avalonia.Controls.SystemDecorations.Full;
        var work = Screens.ScreenFromWindow(this)?.WorkingArea;
        double scale = RenderScaling;
        double availableWidth = work?.Width ?? 1920, availableHeight = work?.Height ?? 1080;
        var size = setup ? EditorView.FirstRunWindowSize(availableWidth, availableHeight, scale)
            : (Width: Math.Min(1440, availableWidth / scale - 32), Height: Math.Min(900, availableHeight / scale - 32));
        MinWidth = setup ? Math.Min(640, size.Width) : 980;
        MinHeight = setup ? Math.Min(400, size.Height) : 620;
        Width = size.Width; Height = size.Height;
        if (work is { } area) Position = new Avalonia.PixelPoint(area.X + (area.Width - (int)(Width * scale)) / 2,
            area.Y + (area.Height - (int)(Height * scale)) / 2);
    }

    private MacAudio? setupAudio;
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
                setupAudio = new MacAudio();
                View.ApplyAudioVolume();
                _ = setupAudio.LoadAsync(EditorView.SetupAudioPath);
            }
            setupPlayRequested = true;
        }
        PollSetupAudio(); editor.Refresh();
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
        View.UpdateSetupAudio(state.PositionMs, state.DurationMs, state.IsPlaying, state.IsLoading,
            state.Error is null ? null : FruitsAtelier.Localization.Strings.Reformat(state.Error));
    }
}
