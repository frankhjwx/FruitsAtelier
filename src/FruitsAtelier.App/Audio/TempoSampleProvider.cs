using NAudio.Wave;
using SoundTouch;

namespace FruitsAtelier.App.Audio;

internal sealed class TempoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly SoundTouchProcessor processor;
    private readonly float[] input;
    private bool ended;
    private readonly bool diagnostics;
    private long inputFrames, outputFrames, sourceReads;
    private double maximumSourceReadMs, maximumProcessingMs, maximumCallMs, lastRms;
    private int silentFrames, longestSilentFrames;
    public WaveFormat WaveFormat => source.WaveFormat;

    public TempoSampleProvider(ISampleProvider source, double speed, bool diagnostics = false)
    {
        this.source = source;
        this.diagnostics = diagnostics;
        processor = new() { SampleRate = source.WaveFormat.SampleRate, Channels = source.WaveFormat.Channels, Tempo = speed };
        input = new float[2048 * source.WaveFormat.Channels];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        double began = diagnostics ? AudioDiagnosticLog.NowMs : 0, sourceMs = 0;
        int channels = WaveFormat.Channels, written = 0;
        while (written < count - channels + 1)
        {
            int frames = processor.ReceiveSamples(buffer.AsSpan(offset + written, count - written), (count - written) / channels);
            written += frames * channels;
            if (frames > 0) continue;
            if (ended) break;
            double sourceBegan = diagnostics ? AudioDiagnosticLog.NowMs : 0;
            int read = source.Read(input, 0, input.Length);
            if (diagnostics)
            {
                double elapsed = AudioDiagnosticLog.NowMs - sourceBegan;
                sourceMs += elapsed;
                maximumSourceReadMs = Math.Max(maximumSourceReadMs, elapsed);
                sourceReads++;
                inputFrames += read / channels;
            }
            if (read == 0) { processor.Flush(); ended = true; }
            else processor.PutSamples(input.AsSpan(0, read), read / channels);
        }
        if (diagnostics)
        {
            double elapsed = AudioDiagnosticLog.NowMs - began;
            maximumCallMs = Math.Max(maximumCallMs, elapsed);
            maximumProcessingMs = Math.Max(maximumProcessingMs, elapsed - sourceMs);
            outputFrames += written / channels;
            double energy = 0;
            for (int frame = 0; frame < written; frame += channels)
            {
                bool silent = true;
                for (int channel = 0; channel < channels; channel++)
                {
                    float sample = buffer[offset + frame + channel];
                    energy += sample * sample;
                    if (Math.Abs(sample) > .000001f) silent = false;
                }
                silentFrames = silent ? silentFrames + 1 : 0;
                longestSilentFrames = Math.Max(longestSilentFrames, silentFrames);
            }
            lastRms = written == 0 ? 0 : Math.Sqrt(energy / written);
        }
        return written;
    }

    internal object Snapshot() => new { inputFrames, outputFrames, sourceReads, maximumSourceReadMs,
        maximumProcessingMs, maximumCallMs, lastRms, longestSilenceMs = longestSilentFrames * 1000d / WaveFormat.SampleRate,
        sourceEnded = ended };
}
