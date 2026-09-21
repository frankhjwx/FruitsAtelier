using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal sealed class DiagnosticWaveProvider(IWaveProvider source, AudioDiagnosticLog log, long session) : IWaveProvider
{
    private long totalBytes, reads, shortReads;
    private double lastReadMs, maximumGapMs, maximumReadMs;
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        double began = AudioDiagnosticLog.NowMs;
        int read = source.Read(buffer, offset, count);
        double elapsed = AudioDiagnosticLog.NowMs - began;
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
    };
}
