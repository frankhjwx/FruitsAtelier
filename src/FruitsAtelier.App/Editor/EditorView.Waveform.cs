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

    internal float WaveformRulerY => WaveformBounds.Y + WaveformBounds.Height * .66f + 28;

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
        float center = r.Y + r.Height / 2, rulerY = WaveformRulerY;
        c.Clip(r);
        float gridTop = r.Y + 96;
        for (float y = center; y >= gridTop; y -= 40)
            c.Line(r.X, y, r.Right, y, y == center ? 0x3C4653u : 0x262D37u);
        for (float y = center + 40; y < rulerY; y += 40)
            c.Line(r.X, y, r.Right, y, 0x262D37);
        foreach (var tick in renderedTiming!.Grid(Math.Max(0, start), start + waveformSpanMs, divisor))
        {
            float x = r.X + (float)((tick.TimeMs - start) / msPerPixel);
            var style = GridStyle(tick);
            c.Line(x, gridTop, x, rulerY, style.Color, style.Width, .35f);
        }
        c.Stroke(new(r.X, gridTop, r.Width, rulerY - gridTop), Grid);
        if (waveform != null)
            // Anchor peak windows to audio time so playback only translates the envelope.
            for (double bin = Math.Floor(start / msPerPixel); bin * msPerPixel < start + waveformSpanMs; bin++)
            {
                float peak = waveform.Peak(bin * msPerPixel, (bin + 1) * msPerPixel) * (r.Height * .16f);
                float x = r.X + (float)(bin - start / msPerPixel);
                // Overlapping filled columns avoid antialiased hairline seams at fractional DPI.
                if (peak > 0) c.Fill(new(x, center - peak, 2, peak * 2), Accent);
            }
        else c.Text(L.Get(waveformFailed ? "timing.waveformError" : waveformTask != null ? "timing.waveformLoading" : "timing.waveformEmpty"),
            r.X + 16, center + 20, 13, Muted, r.Width - 32);
        double step = Math.Pow(10, Math.Floor(Math.Log10(waveformSpanMs / 8)));
        if (waveformSpanMs / step > 16) step *= 5;
        foreach (var tick in renderedTiming!.Grid(Math.Max(0, start), start + waveformSpanMs, divisor))
        {
            float x = r.X + (float)((tick.TimeMs - start) / msPerPixel);
            var style = GridStyle(tick);
            c.Line(x, rulerY - style.Height, x, rulerY, style.Color, style.Width);
        }
        c.Line(r.X, rulerY, r.Right, rulerY, Grid);
        for (double time = Math.Max(0, Math.Ceiling(start / step) * step); time < start + waveformSpanMs; time += step)
        {
            float x = r.X + (float)((time - start) / msPerPixel);
            c.Text(Time(time), x + 3, rulerY + 6, 10, Muted, 80);
        }
        Span<float> labelRights = stackalloc float[4];
        labelRights.Fill(r.X);
        foreach (var point in Document.TimingPoints.OrderBy(p => p.TimeMs))
        {
            if (!point.Uninherited || point.TimeMs < start || point.TimeMs > start + waveformSpanMs) continue;
            float x = r.X + (float)((point.TimeMs - start) / msPerPixel);
            string label = TimingN(60000 / point.BeatLengthMs) + " BPM";
            float labelWidth = Math.Min(r.Width - 1, c.MeasureText(label, 12));
            float labelX = Math.Clamp(x + 5, r.X, r.Right - labelWidth - 1);
            int row = 0;
            while (row < labelRights.Length && labelX < labelRights[row]) row++;
            if (row == labelRights.Length)
            {
                row = 0;
                for (int i = 1; i < labelRights.Length; i++)
                    if (labelRights[i] < labelRights[row]) row = i;
            }
            float labelY = r.Y + 8 + row * 18;
            c.Line(x, labelY + 22, x, rulerY, Error);
            c.Text(label, labelX, labelY, 12, Error, labelWidth + 1);
            labelRights[row] = Math.Max(labelRights[row], labelX + labelWidth + 8);
        }
        float head = r.X + r.Width / 2;
        c.Line(head, r.Y + 28, head, rulerY + 24, Accent, 2);
        c.Text(Time(playhead), head + 5, r.Y + 84, 12, Foreground, 110);
        c.Unclip();
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
