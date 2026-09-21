using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.CoreAudioApi;

namespace FruitsAtelier.App.Audio;

internal sealed class AudioDiagnosticLog : IDisposable
{
    private const long MaximumBytes = 16 * 1024 * 1024;
    private readonly Channel<string> entries = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Task writer;
    private long dropped;
    public bool Enabled { get; }
    public string? FilePath { get; }

    internal AudioDiagnosticLog(string? directory = null)
    {
        Enabled = directory is not null || Environment.GetEnvironmentVariable("FRUITSATELIER_AUDIO_DIAGNOSTICS") == "1"
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "audio-diagnostics.enabled"));
        if (!Enabled) { writer = Task.CompletedTask; return; }
        if (directory is null)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
            directory = root is not null ? Path.Combine(root.FullName, "artifacts", "logs")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "logs");
        }
        FilePath = Path.Combine(directory, $"audio-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");
        Write("environment", new
        {
            version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription, processors = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, maximumBytes = MaximumBytes
        });
        writer = Task.Run(WriteAsync);
    }

    public void Write(string kind, object data)
    {
        if (!Enabled) return;
        if (!entries.Writer.TryWrite(Serialize(kind, data))) Interlocked.Increment(ref dropped);
    }

    private static string Serialize(string kind, object data) => JsonSerializer.Serialize(new
    {
        utc = DateTimeOffset.UtcNow, monotonicMs = NowMs,
        thread = Environment.CurrentManagedThreadId, kind, data
    });

    internal static double NowMs => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;

    private async Task WriteAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath!)!);
            using var stream = new FileStream(FilePath!, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var text = new StreamWriter(stream) { AutoFlush = true };
            // Endpoint queries and disk writes stay off the transport and device callback threads.
            try
            {
                using var devices = new MMDeviceEnumerator();
                foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                {
                    using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, role);
                    using var client = device.AudioClient;
                    await text.WriteLineAsync(Serialize("defaultEndpoint", new { role = role.ToString(), name = device.FriendlyName,
                        mixFormat = client.MixFormat.ToString(), defaultPeriodMs = client.DefaultDevicePeriod / 10000d,
                        minimumPeriodMs = client.MinimumDevicePeriod / 10000d }));
                }
            }
            catch (Exception ex) { await text.WriteLineAsync(Serialize("endpointQueryFailed", new { type = ex.GetType().Name, ex.HResult })); }
            await foreach (string line in entries.Reader.ReadAllAsync())
            {
                if (stream.Position >= MaximumBytes)
                {
                    await text.WriteLineAsync(JsonSerializer.Serialize(new { kind = "sizeLimitReached", maximumBytes = MaximumBytes }));
                    entries.Writer.TryComplete();
                    return;
                }
                await text.WriteLineAsync(line);
            }
            await text.WriteLineAsync(JsonSerializer.Serialize(new { kind = "logClosed", dropped = Interlocked.Read(ref dropped) }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            entries.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        entries.Writer.TryComplete();
        writer.GetAwaiter().GetResult();
    }
}
