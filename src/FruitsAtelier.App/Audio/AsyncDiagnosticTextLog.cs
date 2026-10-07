using System.Threading.Channels;

namespace FruitsAtelier.App.Audio;

internal sealed class AsyncDiagnosticTextLog : IDisposable
{
    private readonly Channel<string> entries = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Task writer;
    private long dropped;
    internal long Dropped => Interlocked.Read(ref dropped);
    internal Task Completion => writer;
    internal volatile bool Failed;
    internal volatile bool LimitReached;

    internal AsyncDiagnosticTextLog(string path, long maximumBytes = long.MaxValue, Task? writerGate = null)
    {
        writer = Task.Run(async () =>
        {
            if (writerGate is not null) await writerGate;
            long written = 0;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await foreach (string entry in entries.Reader.ReadAllAsync())
                {
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(entry);
                    if (written + data.Length > maximumBytes)
                    {
                        LimitReached = true;
                        entries.Writer.TryComplete();
                        return;
                    }
                    // Sharing failures and slow storage affect only this bounded background writer.
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    await stream.WriteAsync(data);
                    written += data.Length;
                }
            }
            catch (Exception)
            {
                Failed = true;
                entries.Writer.TryComplete();
            }
        });
    }

    internal void Write(string entry)
    {
        if (!entries.Writer.TryWrite(entry)) Interlocked.Increment(ref dropped);
    }

    public void Dispose()
    {
        entries.Writer.TryComplete();
        writer.Wait(TimeSpan.FromMilliseconds(250));
    }
}
