using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Func<Hitsound, CancellationToken, Task<double>>? RequestStartLibraryAudition { get; set; }
    public Action? RequestStopLibraryAudition { get; set; }
    private string? hitsoundingAuditionPath;
    private Task<double>? hitsoundingAuditionTask;
    private CancellationTokenSource? hitsoundingAuditionCancellation;
    private double hitsoundingAuditionStarted, hitsoundingAuditionDuration;
    private float HitsoundingAuditionProgress => hitsoundingAuditionDuration <= 0 ? 0
        : (float)Math.Clamp((CanvasDimNowMs - hitsoundingAuditionStarted) / hitsoundingAuditionDuration, 0, 1);

    private void ToggleHitsoundingLibraryAudition(Hitsound sound)
    {
        bool stop = hitsoundingAuditionPath == sound.FilePath;
        StopHitsoundingLibraryAudition();
        if (stop || sound.FilePath is null || RequestStartLibraryAudition is null) return;
        hitsoundingAuditionPath = sound.FilePath;
        hitsoundingAuditionCancellation = new();
        try { hitsoundingAuditionTask = RequestStartLibraryAudition(sound, hitsoundingAuditionCancellation.Token); }
        catch { StopHitsoundingLibraryAudition(); throw; }
        PumpHitsoundingAudition();
    }

    private void StopHitsoundingLibraryAudition()
    {
        hitsoundingAuditionCancellation?.Cancel(); hitsoundingAuditionCancellation?.Dispose(); hitsoundingAuditionCancellation = null;
        if (hitsoundingAuditionTask is { } task) _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        hitsoundingAuditionTask = null;
        if (hitsoundingAuditionPath is not null) RequestStopLibraryAudition?.Invoke();
        hitsoundingAuditionPath = null; hitsoundingAuditionDuration = 0;
    }

    private void PumpHitsoundingAudition()
    {
        if (hitsoundingAuditionPath is null) return;
        if (!HitsoundingPageVisible || LibraryVisible || IsTestplaying) { StopHitsoundingLibraryAudition(); return; }
        if (hitsoundingAuditionTask is { IsCompleted: true } task)
        {
            hitsoundingAuditionTask = null;
            if (!task.IsCompletedSuccessfully || !double.IsFinite(task.Result) || task.Result <= 0)
            {
                string? error = task.Exception?.GetBaseException().Message;
                StopHitsoundingLibraryAudition();
                if (error is not null) ShowError(error);
                return;
            }
            hitsoundingAuditionDuration = task.Result; hitsoundingAuditionStarted = CanvasDimNowMs;
        }
        if (hitsoundingAuditionDuration > 0 && HitsoundingAuditionProgress >= 1) StopHitsoundingLibraryAudition();
    }
}
