using FruitsAtelier.Core;

internal static class WaveformTests
{
    public static void Run()
    {
        var reference = HitsoundSamples.Create(CatchObjectKind.Droplet);
        var tick = HitsoundSamples.Create(new Hitsound(CatchObjectKind.Droplet, null, 1, "metronome-tick"));
        for (int i = 0; i < reference.Length; i++)
            if (Math.Abs(tick[i] - reference[i] * 5) > .00001) throw new Exception("Metronome gain differs from five times the base sample.");
        var samples = new float[4410 * 2];
        samples[2001] = -.8f; samples[3998] = .3f;
        int offset = 0;
        var waveform = AudioWaveform.Read(44100, 2, buffer =>
        {
            int count = Math.Min(137, samples.Length - offset);
            Array.Copy(samples, offset, buffer, 0, count); offset += count; return count;
        }, default);
        if (Math.Abs(waveform.Peak(0, 100) - .8f) > .0001 || waveform.Peak(0, 10) != 0
            || Math.Abs(waveform.Peak(40, 50) - .3f) > .0001 || waveform.Peak(-100, -1) != 0)
            throw new Exception("Waveform loses stereo peaks or shifts chunk boundaries.");
        var random = new Random(19);
        var peaks = Enumerable.Range(0, 301).Select(_ => (float)random.NextDouble()).ToArray();
        waveform = new(peaks, 1);
        for (int i = 0; i < 300; i++)
        {
            int start = random.Next(peaks.Length), end = random.Next(start + 1, peaks.Length + 1);
            if (waveform.Peak(start, end) != peaks[start..end].Max())
                throw new Exception("Multiresolution waveform omits a transient.");
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { AudioWaveform.Read(44100, 2, b => 1, cancellation.Token); throw new Exception("Decode was not cancelled."); }
        catch (OperationCanceledException) { }
    }
}
