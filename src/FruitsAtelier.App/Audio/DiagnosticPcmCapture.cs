using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

// A fixed memory buffer keeps file I/O and allocation out of the audio callback.
internal sealed class DiagnosticPcmCapture : IWaveProvider, IDisposable
{
    private readonly IWaveProvider source;
    private readonly AudioDiagnosticLog log;
    private readonly byte[] data;
    private readonly string path;
    private readonly Func<bool> claim;
    private int length;
    private bool started, finished;
    private Task writer = Task.CompletedTask;
    public WaveFormat WaveFormat => source.WaveFormat;

    public DiagnosticPcmCapture(IWaveProvider source, AudioDiagnosticLog log, long session,
        Func<bool> claim, double positionMs, double speed, string profile, int seconds = 20)
    {
        this.source = source; this.log = log; this.claim = claim;
        data = new byte[checked(source.WaveFormat.AverageBytesPerSecond * seconds)];
        path = Path.ChangeExtension(log.FilePath!, null) + $"-session-{session}.wav";
        log.Write("pcmCaptureArmed", new { session, positionMs, speed, profile, seconds,
            file = Path.GetFileName(path), format = WaveFormat.ToString() });
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (finished || read == 0) return read;
        if (!started)
        {
            started = true;
            if (!claim()) { finished = true; return read; }
        }
        int copied = Math.Min(read, data.Length - length);
        Buffer.BlockCopy(buffer, offset, data, length, copied);
        length += copied;
        if (length == data.Length) Complete();
        return read;
    }

    private void Complete()
    {
        if (finished) return;
        finished = true;
        if (length == 0) return;
        writer = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var wave = new WaveFileWriter(path, WaveFormat)) wave.Write(data, 0, length);
                log.Write("pcmCaptureSaved", new { file = Path.GetFileName(path), bytes = length,
                    durationMs = length * 1000d / WaveFormat.AverageBytesPerSecond });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { log.Write("pcmCaptureFailed", new { type = ex.GetType().Name, ex.HResult }); }
        });
    }

    public void Dispose() { Complete(); writer.GetAwaiter().GetResult(); }
}
