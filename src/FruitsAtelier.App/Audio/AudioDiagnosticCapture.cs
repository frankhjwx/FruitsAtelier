using System.IO.Compression;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Audio;

internal static class AudioDiagnosticCapture
{
    internal static bool Enabled { get; private set; }
    internal static bool Frames { get; private set; }
    internal static string? Directory { get; private set; }
    private static string selectedProfile = "event-10";
    internal static string Profile => Environment.GetEnvironmentVariable("FRUITSATELIER_AUDIO_PROFILE") ?? selectedProfile;
    internal static volatile bool Failed;
    internal static volatile bool FileLimitReached;
    internal static bool LimitReached => FileLimitReached || Interlocked.Read(ref bytes) >= MaximumBytes;
    private const long MaximumBytes = 64 * 1024 * 1024;
    private static long bytes;

    internal static void Configure(LibrarySettings settings)
    {
        Enabled = settings.AudioDiagnostics;
        Frames = settings.AudioDiagnosticFrames;
        selectedProfile = settings.AudioDiagnosticProfile;
        if (!AudioDiagnosticLog.Requested) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
        string logs = root is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FruitsAtelier", "logs")
            : Path.Combine(root.FullName, "artifacts", "logs");
        Directory = Path.Combine(logs, "audio-captures", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}");
    }

    internal static bool Reserve(int count) => Directory is null || Interlocked.Add(ref bytes, count) <= MaximumBytes;

    internal static Task<string> ExportAsync(string directory, object? context = null) => Task.Run(() =>
    {
        // Copy only the bytes present at opening; active writers cannot make export run forever.
        string destination = Path.Combine(directory, $"audio-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        try
        {
            using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
            using (var notes = new StreamWriter(archive.CreateEntry("README.txt").Open()))
                notes.WriteLine("FruitsAtelier audio diagnostic snapshot. Active logs may end with an incomplete final JSONL record. Software clock/buffer measurements do not measure acoustic output latency. Compare one output profile per app run; include perceived delay, device connection (wired/Bluetooth), playback speed, and whether screen recording changes the result when reporting. Audio and map contents are excluded.");
            using (var manifest = new StreamWriter(archive.CreateEntry("capture.json").Open()))
                manifest.Write(System.Text.Json.JsonSerializer.Serialize(new
                {
                    utc = DateTimeOffset.UtcNow, profile = AudioDiagnosticProfile.Select(AudioDiagnosticLog.Requested, Profile).Name,
                    recording = AudioDiagnosticLog.Requested, frames = Frames,
                    writeFailed = Failed, limitReached = LimitReached, eventBytes = Interlocked.Read(ref bytes), context
                }));
            long budget = 80 * 1024 * 1024;
            foreach (string path in System.IO.Directory.EnumerateFiles(directory)
                .Where(p => Path.GetFileName(p) == "editor.log" || Path.GetFileName(p).StartsWith("audio-", StringComparison.Ordinal)
                    && Path.GetExtension(p) == ".jsonl").Take(256))
            {
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long remaining = Math.Min(Math.Min(input.Length, 16 * 1024 * 1024), budget);
                budget -= remaining;
                using var output = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.Fastest).Open();
                byte[] buffer = new byte[65536];
                while (remaining > 0)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) break;
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
            return destination;
        }
        catch
        {
            try { File.Delete(destination); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    });
}
