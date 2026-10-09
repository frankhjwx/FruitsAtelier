using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal sealed class PlaybackTransition(ISampleProvider source, bool fadeIn) : ISampleProvider
{
    internal const int DurationMs = 5;
    private readonly int frames = Math.Max(2, source.WaveFormat.SampleRate * DurationMs / 1000);
    private readonly TaskCompletionSource<double> faded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long samplesRead;
    private int requested, fadeFrame;
    private bool fading, silent;
    private float gain = fadeIn ? 0 : 1, fadeStart;
    public WaveFormat WaveFormat => source.WaveFormat;
    internal Task<double> FadeOut() { Volatile.Write(ref requested, 1); return faded.Task; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (silent) { Array.Clear(buffer, offset, count); samplesRead += count; return count; }
        int read = source.Read(buffer, offset, count);
        if (read == 0 && Volatile.Read(ref requested) != 0)
            faded.TrySetResult(samplesRead * 1000d / WaveFormat.Channels / WaveFormat.SampleRate);
        double? endMs = null;
        for (int i = 0; i < read; i++)
        {
            if (samplesRead % WaveFormat.Channels == 0)
            {
                if (!fading && Volatile.Read(ref requested) != 0)
                { fading = true; fadeStart = gain; fadeFrame = 0; }
                if (fading)
                {
                    gain = fadeStart * Math.Max(0, 1 - fadeFrame / (float)(frames - 1));
                    fadeFrame++;
                }
                else if (fadeIn)
                    gain = Math.Min(1, samplesRead / WaveFormat.Channels / (float)(frames - 1));
            }
            buffer[offset + i] *= gain;
            samplesRead++;
            if (fading && gain == 0 && samplesRead % WaveFormat.Channels == 0 && !silent)
            {
                silent = true;
                endMs = samplesRead * 1000d / WaveFormat.Channels / WaveFormat.SampleRate;
            }
        }
        if (endMs is { } end) faded.TrySetResult(end);
        return read;
    }
}
