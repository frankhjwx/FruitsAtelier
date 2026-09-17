using NAudio.Wave;
using SoundTouch;

namespace FruitsAtelier.App.Audio;

internal sealed class TempoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly SoundTouchProcessor processor;
    private readonly float[] input;
    private bool ended;
    public WaveFormat WaveFormat => source.WaveFormat;

    public TempoSampleProvider(ISampleProvider source, double speed)
    {
        this.source = source;
        processor = new() { SampleRate = source.WaveFormat.SampleRate, Channels = source.WaveFormat.Channels, Tempo = speed };
        input = new float[2048 * source.WaveFormat.Channels];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = WaveFormat.Channels, written = 0;
        while (written < count - channels + 1)
        {
            int frames = processor.ReceiveSamples(buffer.AsSpan(offset + written, count - written), (count - written) / channels);
            written += frames * channels;
            if (frames > 0) continue;
            if (ended) break;
            int read = source.Read(input, 0, input.Length);
            if (read == 0) { processor.Flush(); ended = true; }
            else processor.PutSamples(input.AsSpan(0, read), read / channels);
        }
        return written;
    }
}
