using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal sealed class PreRollSampleProvider(ISampleProvider source, double milliseconds) : ISampleProvider
{
    private long remaining = (long)Math.Round(milliseconds * source.WaveFormat.SampleRate / 1000)
        * source.WaveFormat.Channels;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int silence = (int)Math.Min(count, remaining);
        Array.Clear(buffer, offset, silence);
        remaining -= silence;
        return silence + (silence == count ? 0 : source.Read(buffer, offset + silence, count - silence));
    }
}
