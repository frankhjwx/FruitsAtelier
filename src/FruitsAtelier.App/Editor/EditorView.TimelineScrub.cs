namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool resumeAfterTimelineScrub;

    private void BeginTimelineScrub()
    {
        resumeAfterTimelineScrub = AudioReady && AudioPlaying;
        if (resumeAfterTimelineScrub)
        {
            Pause(snapToGrid: false);
            // Pause callbacks stop the transport before the next host snapshot arrives.
            AudioPlaying = false;
            ResetHitsounds();
        }
    }

    private void EndTimelineScrub()
    {
        bool resume = resumeAfterTimelineScrub;
        resumeAfterTimelineScrub = false;
        if (resume && AudioReady && !AudioLoading)
        {
            SeekTo(playhead);
            Play();
        }
    }
}
