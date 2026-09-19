// Clock drift smoothing references osu.Framework/Timing/InterpolatingFramedClock.cs,
// ppy/osu-framework e01524d1492885d8b00ac88b38e7963d76d7d454.
// Copyright (c) ppy Pty Ltd. MIT Licence; see ../Conversion/LICENCE.osu-framework.txt.
namespace FruitsAtelier.Core;

/// <summary>A monotonic game clock interpolated between audio position samples.</summary>
public sealed class CatchTestplayClock(double start, double rate, double realtime, bool waitForAudio)
{
    private double anchorTime = start, anchorRealtime = realtime, correction, lastTime = start;
    private double lastAudioTime = start;
    private double audioLimit = double.PositiveInfinity;
    private bool running = !waitForAudio;
    public bool IsRunning => running;

    public double At(double now)
    {
        double elapsed = Math.Max(0, now - anchorRealtime);
        double time = running ? anchorTime + elapsed * rate + correction * (1 - Math.Pow(.5, elapsed / 50)) : anchorTime;
        return lastTime = Math.Max(lastTime, Math.Min(time, audioLimit));
    }

    public void Synchronize(double audioTime, double sampledAt, double now)
    {
        if (!double.IsFinite(audioTime)) return;
        double current = At(now);
        // A stalled device must not let interpolation run through the rest of the beatmap.
        audioLimit = audioTime + 100 * rate;
        if (audioTime <= lastAudioTime) return;
        lastAudioTime = audioTime;
        double source = audioTime + Math.Max(0, now - sampledAt) * rate;
        anchorRealtime = now;
        if (!running || Math.Abs(source - current) > (1000d / 60 * 2) * rate)
        {
            anchorTime = Math.Max(current, source);
            correction = 0;
        }
        else
        {
            anchorTime = current;
            // Correct device-clock drift continuously; resampling must not quantise input time.
            correction = Math.Clamp(source - current, -25 * rate, 25 * rate);
        }
        running = true;
    }
}
