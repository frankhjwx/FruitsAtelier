using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal sealed class PlaybackGain(ISampleProvider source, Func<float> gain) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        float volume = gain();
        for (int i = offset; i < offset + read; i++) buffer[i] *= volume;
        return read;
    }
}
