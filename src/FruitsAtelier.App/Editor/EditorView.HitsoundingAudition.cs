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
        if (box.X < hitsoundingGridBounds.X || Math.Min(box.Right, hitsoundingGridBounds.Right) - box.X < 48 || box.Height < 34) return;
        var button = new Rect(box.X + 4, box.Bottom - 18, 16, 16);
        c.Circle(button.X + 8, button.Y + 8, 5.5f, Muted, false, 1, .6f);
        if (HitsoundingAuditionKey(sound) == hitsoundingAuditionPath) c.Fill(new(button.X + 5, button.Y + 5, 6, 6), Foreground, 1);
        else c.Text("▶", button.X + 5, button.Y + 3, 8, Foreground, 10);
        hitsoundingPreciseAuditionButton = (button, sound);
        hits.Add(new(button, () => ToggleHitsoundingLibraryAudition(sound), true));
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
