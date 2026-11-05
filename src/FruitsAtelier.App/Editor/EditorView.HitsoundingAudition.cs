using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Func<Hitsound, CancellationToken, Task<double>>? RequestStartLibraryAudition { get; set; }
    public Action? RequestStopLibraryAudition { get; set; }
    private string? hitsoundingAuditionPath;
    private Task<double>? hitsoundingAuditionTask;
    private CancellationTokenSource? hitsoundingAuditionCancellation;
    private double hitsoundingAuditionStarted, hitsoundingAuditionDuration;
    private (Rect Bounds, Hitsound Sound)? hitsoundingPreciseAuditionButton;
    internal Rect HitsoundingPreciseAuditionBounds => hitsoundingPreciseAuditionButton?.Bounds ?? default;
    private static string HitsoundingAuditionKey(Hitsound sound) => sound.FilePath ?? $"builtin:{sound.Kind}/{sound.SampleSet}/{sound.Name}";
    private float HitsoundingAuditionProgress => hitsoundingAuditionDuration <= 0 ? 0
        : (float)Math.Clamp((CanvasDimNowMs - hitsoundingAuditionStarted) / hitsoundingAuditionDuration, 0, 1);

    private void ToggleHitsoundingLibraryAudition(Hitsound sound)
    {
        string key = HitsoundingAuditionKey(sound);
        bool stop = hitsoundingAuditionPath == key;
        StopHitsoundingLibraryAudition();
        if (stop || RequestStartLibraryAudition is null) return;
        hitsoundingAuditionPath = key;
        hitsoundingAuditionCancellation = new();
        try { hitsoundingAuditionTask = RequestStartLibraryAudition(sound, hitsoundingAuditionCancellation.Token); }
        catch { StopHitsoundingLibraryAudition(); throw; }
        PumpHitsoundingAudition();
    }

    private void DrawHitsoundingPreciseAudition(ICanvas c, Rect box, Hitsound sound)
    {
        var row = new Rect(Math.Clamp(box.Right - 34, hitsoundingGridBounds.X, Math.Max(hitsoundingGridBounds.X, hitsoundingGridBounds.Right - 34)),
            Math.Max(hitsoundingGridBounds.Y, box.Y - 32), 34, 30);
        c.Fill(row, Surface, 5);
        DrawHitsoundingAudition(c, row, sound, library: true);
        hitsoundingPreciseAuditionButton = (new(row.X + 3, row.Y + 3, 28, row.Height - 6), sound);
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
