using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Func<string, CancellationToken, Task<AudioWaveform>>? RequestWaveform { get; set; }
    private Task<AudioWaveform>? waveformTask;
    private CancellationTokenSource? waveformCancellation;
    private AudioWaveform? waveform;
    private string? waveformPath;
    private bool waveformFailed;
    private double waveformSpanMs = 10000;
    public bool WaveformNeedsRedraw => TimingPageVisible && waveformTask is { IsCompleted: true };
    internal Rect WaveformBounds => new(16, 150, Math.Max(80, rightPanel.X - 32), Math.Max(100, height - 280));

    public void ReleaseWaveform()
    {
        waveformCancellation?.Cancel(); waveformCancellation?.Dispose(); waveformCancellation = null;
        if (waveformTask != null)
            _ = waveformTask.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        waveformTask = null; waveform = null; waveformPath = null;
    }

    private void DrawTimingWaveform(ICanvas c)
    {
        if (waveformPath != Document.AudioPath)
        {
            ReleaseWaveform();
            waveformCancellation = new(); waveformPath = Document.AudioPath;
            waveform = null; waveformFailed = false;
            waveformTask = string.IsNullOrWhiteSpace(waveformPath) ? null : RequestWaveform?.Invoke(waveformPath, waveformCancellation.Token);
        }
        if (waveformTask is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully) waveform = task.Result;
            else { _ = task.Exception; waveformFailed = !task.IsCanceled; }
            waveformTask = null;
        }
        objectTimeline = default;
        var r = WaveformBounds;
        zoomSlider = default;
        c.Text(L.Get("timing.waveform"), r.X + 8, 97, 14, Foreground, r.Width - 180);
        snapSlider = new(r.Right - 106, 88, 106, 29);
        c.Text(L.Get("ui.snap"), snapSlider.X - 40, 97, 11, Muted, 40);
        float snapStart = snapSlider.X + 7, snapEnd = snapSlider.Right - 31;
        c.Line(snapStart, 103, snapEnd, 103, Accent, 2);
        c.Circle(snapStart + Array.IndexOf(SnapDivisors, divisor) / (float)(SnapDivisors.Length - 1) * (snapEnd - snapStart), 103, 6, Accent);
        c.Text(L.Get("ui.snapDivisor", divisor), snapSlider.Right - 28, 97, 10, Foreground, 40);
        c.Fill(r, Background);
        double start = playhead - waveformSpanMs / 2, msPerPixel = waveformSpanMs / r.Width;
        float center = r.Y + r.Height / 2;
        c.Clip(r);
        c.Line(r.X, center, r.Right, center, Grid);
        if (waveform != null)
            for (int x = 0; x < r.Width; x++)
            {
                float peak = waveform.Peak(start + x * msPerPixel, start + (x + 1) * msPerPixel) * (r.Height * .32f);
                if (peak > 0) c.Line(r.X + x, center - peak, r.X + x, center + peak, Accent);
            }
        else c.Text(L.Get(waveformFailed ? "timing.waveformError" : waveformTask != null ? "timing.waveformLoading" : "timing.waveformEmpty"),
            r.X + 16, center + 20, 13, Muted, r.Width - 32);
        double step = Math.Pow(10, Math.Floor(Math.Log10(waveformSpanMs / 8)));
        if (waveformSpanMs / step > 16) step *= 5;
        for (double time = Math.Max(0, Math.Ceiling(start / step) * step); time < start + waveformSpanMs; time += step)
        {
            float x = r.X + (float)((time - start) / msPerPixel);
            c.Line(x, r.Bottom - 22, x, r.Bottom, Grid);
            c.Text(Time(time), x + 3, r.Bottom - 18, 10, Muted, 80);
        }
        foreach (var point in Document.TimingPoints)
        {
            if (!point.Uninherited || point.TimeMs < start || point.TimeMs > start + waveformSpanMs) continue;
            float x = r.X + (float)((point.TimeMs - start) / msPerPixel);
            c.Line(x, r.Y + 30, x, r.Bottom - 24, Error);
            c.Text(TimingN(60000 / point.BeatLengthMs) + " BPM", x + 5, r.Y + 8, 12, Error, 120);
        }
        float head = r.X + r.Width / 2;
        c.Line(head, r.Y + 28, head, r.Bottom, Accent, 2);
        c.Text(Time(playhead), head + 5, r.Y + 30, 12, Foreground, 110);
        c.Unclip();
        c.Text(L.Get("timing.waveformHelp"), r.X + 8, r.Y - 25, 12, Muted, r.Width - 16);
    }

    private void TimingWaveformPointer(float x, float y, int button)
    {
        if (button != 0 || AudioLoading) return;
        var r = WaveformBounds;
        double time = playhead + (x - r.X - r.Width / 2) / r.Width * waveformSpanMs;
        var nearest = Document.TimingPoints.Where(p => p.Uninherited)
            .MinBy(p => Math.Abs(p.TimeMs - time));
        if (nearest != null && Math.Abs(nearest.TimeMs - time) * r.Width / waveformSpanMs <= 7)
        {
            int index = Document.TimingPoints.IndexOf(nearest);
            OpenTimingSetup(); timingSelected.Clear(); timingSelected.Add(index); timingAnchor = index;
            timingScroll = Math.Max(0, Array.FindIndex(VisibleTimingEntries(), e => e.Id == index) - 3);
        }
        else SeekTo(Math.Clamp(time, 0, TimelineDurationMs));
    }
}
