namespace FruitsAtelier.Core;

/// <summary>Multiresolution peak envelope, independent of the playback device.</summary>
public sealed class AudioWaveform
{
    private readonly List<float[]> levels = [];
    public double BinMs { get; }
    public double DurationMs => levels[0].Length * BinMs;
    public AudioWaveform(float[] peaks, double binMs)
    {
        BinMs = binMs;
        levels.Add(peaks);
        while (peaks.Length > 1)
        {
            var next = new float[(peaks.Length + 1) / 2];
            for (int i = 0; i < next.Length; i++) next[i] = Math.Max(peaks[i * 2], peaks[Math.Min(i * 2 + 1, peaks.Length - 1)]);
            levels.Add(next); peaks = next;
        }
    }
    public float Peak(double startMs, double endMs)
    {
        if (endMs <= 0 || startMs >= DurationMs) return 0;
        int start = Math.Max(0, (int)Math.Floor(startMs / BinMs));
        int end = Math.Min(levels[0].Length, (int)Math.Ceiling(endMs / BinMs));
        float peak = 0;
        for (int level = 0; start < end; level++, start /= 2, end /= 2)
        {
            if ((start & 1) != 0) peak = Math.Max(peak, levels[level][start++]);
            if ((end & 1) != 0) peak = Math.Max(peak, levels[level][--end]);
        }
        return peak;
    }

    public static AudioWaveform Read(int sampleRate, int channels, Func<float[], int> read, CancellationToken token)
    {
        if (sampleRate <= 0 || channels is < 1 or > 32) throw new InvalidDataException("Invalid audio format.");
        int framesPerBin = Math.Max(1, sampleRate / 1000), samplesPerBin = framesPerBin * channels;
        var peaks = new List<float>(); var buffer = new float[4096 * channels];
        int inBin = 0, count; float peak = 0;
        while ((count = read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < count; i++)
            {
                if (float.IsFinite(buffer[i])) peak = Math.Max(peak, Math.Min(1, Math.Abs(buffer[i])));
                if (++inBin != samplesPerBin) continue;
                if (peaks.Count >= 3_600_000) throw new InvalidDataException("Audio waveform exceeds one hour.");
                peaks.Add(peak); peak = 0; inBin = 0;
            }
        }
        if (inBin > 0) peaks.Add(peak);
        return new(peaks.ToArray(), framesPerBin * 1000d / sampleRate);
    }
}
