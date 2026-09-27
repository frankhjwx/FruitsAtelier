using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal sealed class DiagnosticWaveProvider(IWaveProvider source, AudioDiagnosticLog log, long session) : IWaveProvider
{
    private long totalBytes, reads, shortReads;
    private double lastReadMs, maximumGapMs, maximumReadMs, nextAnomalyMs, lastRms;
    private long silentSamples, longestSilentSamples;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        double began = AudioDiagnosticLog.NowMs;
        int read;
        try { read = source.Read(buffer, offset, count); }
        catch (Exception ex)
        {
            log.Write("sourceReadFailed", new { session, requestedBytes = count, elapsedMs = AudioDiagnosticLog.NowMs - began,
                type = ex.GetType().FullName, ex.HResult });
            throw;
        }
        double elapsed = AudioDiagnosticLog.NowMs - began;
        if (WaveFormat.BitsPerSample == 16 && WaveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            double energy = 0;
            for (int i = offset; i + 1 < offset + read; i += 2)
            {
                short sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2));
                energy += (double)sample * sample;
                silentSamples = Math.Abs((int)sample) <= 1 ? silentSamples + 1 : 0;
                longestSilentSamples = Math.Max(longestSilentSamples, silentSamples);
            }
            Volatile.Write(ref lastRms, read == 0 ? 0 : Math.Sqrt(energy / (read / 2)) / 32768);
        }
        double gap = lastReadMs == 0 ? 0 : began - lastReadMs;
        if (began >= nextAnomalyMs && (elapsed > 10 || gap > 100 || read < count))
        {
            log.Write("sourceReadAnomaly", new { session, elapsedMs = elapsed, gapMs = gap, requestedBytes = count, returnedBytes = read });
            nextAnomalyMs = began + 250;
        }
        if (lastReadMs != 0) Volatile.Write(ref maximumGapMs, Math.Max(maximumGapMs, began - lastReadMs));
        lastReadMs = began;
        Volatile.Write(ref maximumReadMs, Math.Max(maximumReadMs, elapsed));
        long provided = Interlocked.Add(ref totalBytes, read);
        if (read < count) Interlocked.Increment(ref shortReads);
        long number = Interlocked.Increment(ref reads);
        // Only startup reads emit events on the output thread; subsequent reads update counters.
        if (number <= 3) log.Write("sourceRead", new { session, number, beganMs = began, elapsedMs = elapsed,
            requestedBytes = count, returnedBytes = read, providedMs = provided * 1000d / WaveFormat.AverageBytesPerSecond });
        return read;
    }

    public object Snapshot() => new
    {
        reads = Interlocked.Read(ref reads), shortReads = Interlocked.Read(ref shortReads),
        providedMs = Interlocked.Read(ref totalBytes) * 1000d / WaveFormat.AverageBytesPerSecond,
        maximumReadMs = Volatile.Read(ref maximumReadMs), maximumReadGapMs = Volatile.Read(ref maximumGapMs)
        , postGainRms = Volatile.Read(ref lastRms), longestNearSilentMs = Interlocked.Read(ref longestSilentSamples) * 1000d / (WaveFormat.SampleRate * WaveFormat.Channels)
    };
}
