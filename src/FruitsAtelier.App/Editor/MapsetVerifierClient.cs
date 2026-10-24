using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed record MapsetVerifierInput(Guid Id, string Name, MapDocument Document, string? Interpretation = null);
public sealed record MapsetVerifierIssue(string Check, string Title, string Level, string Message, string? Cause = null, Guid? DifficultyId = null);
public sealed record MapsetVerifierDifficulty(Guid Id, string Name, string Interpretation, double Stars, MapsetVerifierIssue[] Issues);
public sealed record MapsetVerifierReport(int Schema, MapsetVerifierIssue[] General, MapsetVerifierDifficulty[] Difficulties)
{
    public string UpstreamTag { get; init; } = "";
}
public sealed record MapsetVerifierText(string Text, double? TimeMs = null);

public static partial class MapsetVerifierClient
{
    private static readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> resourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".ogg", ".wav", ".flac", ".aac", ".m4a", ".mp4", ".avi", ".webm", ".mov", ".flv", ".mkv", ".wmv", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".osb" };

    // Consume each timestamp independently, including lists and the upstream combo suffix.
    [GeneratedRegex(@"(?<![\d:])(?<minutes>\d{1,6}):(?<seconds>[0-5]\d):(?<milliseconds>\d{3})(?:\s*\([\d,|]+\))?|(?<!\S)(?<negative>-\d+)(?= - )", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamps();

    public static IReadOnlyList<MapsetVerifierText> Tokenize(string message)
    {
        var result = new List<MapsetVerifierText>();
        int offset = 0;
        foreach (Match match in Timestamps().Matches(message))
        {
            if (match.Index > offset) result.Add(new(message[offset..match.Index]));
            double time = match.Groups["negative"].Success
                ? double.Parse(match.Groups["negative"].Value, CultureInfo.InvariantCulture)
                : int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture) * 60000d
                    + int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture) * 1000d
                    + int.Parse(match.Groups["milliseconds"].Value, CultureInfo.InvariantCulture);
            result.Add(new(match.Value, time)); offset = match.Index + match.Length;
        }
        if (offset < message.Length) result.Add(new(message[offset..]));
        return result;
    }

    public static string EngineDirectory
    {
        get
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "mapset-verifier");
            if (File.Exists(Path.Combine(bundled, "upstream.json"))) return bundled;
            for (var root = new DirectoryInfo(AppContext.BaseDirectory); root is not null; root = root.Parent)
                if (File.Exists(Path.Combine(root.FullName, "eng", "mapset-verifier.json")))
                    return Path.Combine(root.FullName, "artifacts", "mapset-verifier", RuntimeInformation.RuntimeIdentifier);
            return bundled;
        }
    }

    public static async Task<MapsetVerifierReport> AnalyzeAsync(MapsetVerifierInput[] inputs, bool compensation, CancellationToken cancellation)
    {
        string engine = EngineDirectory;
        string executable = Path.Combine(engine, "FruitsAtelier.MapsetVerifier" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(executable)) throw new FileNotFoundException("python scripts/Build-MapsetVerifier.py", executable);
        var temporary = Directory.CreateTempSubdirectory("fa-mapset-verifier-");
        try
        {
            string mapset = Path.Combine(temporary.FullName, "mapset"); Directory.CreateDirectory(mapset);
            var request = PrepareSnapshot(inputs, compensation, mapset, cancellation);
            string requestPath = Path.Combine(temporary.FullName, "request.json"), resultPath = Path.Combine(temporary.FullName, "result.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), cancellation);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = engine,
                RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(requestPath); start.ArgumentList.Add(resultPath);
            using var process = Process.Start(start) ?? throw new IOException("Could not start Mapset Verifier.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); throw; }
            _ = await output;
            string details = await error;
            if (process.ExitCode != 0) throw new InvalidDataException(details.Length > 4000 ? details[..4000] : details);
            if (new FileInfo(resultPath).Length > 64 * 1024 * 1024) throw new InvalidDataException("Mapset Verifier report exceeds 64 MiB.");
            var report = JsonSerializer.Deserialize<MapsetVerifierReport>(await File.ReadAllTextAsync(resultPath, cancellation), json)
                ?? throw new InvalidDataException("Empty Mapset Verifier report.");
            if (report.Schema != 1 || report.General is null || report.Difficulties is null
                || report.Difficulties.Any(d => d.Issues is null || !inputs.Any(i => i.Id == d.Id)))
                throw new InvalidDataException("Unsupported Mapset Verifier report.");
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(engine, "upstream.json"), cancellation));
            return report with { UpstreamTag = metadata.RootElement.GetProperty("tag").GetString() ?? "" };
        }
        finally { try { temporary.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    internal sealed record ExportedDifficulty(Guid Id, string File, string? Interpretation);
    internal sealed record SnapshotRequest(string Folder, ExportedDifficulty[] Difficulties);

    internal static SnapshotRequest PrepareSnapshot(MapsetVerifierInput[] inputs, bool compensation, string destination, CancellationToken cancellation)
    {
        long bytes = 0; int files = 0;
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (input.Document.SourcePath is not { } source) continue;
            string root = Path.GetDirectoryName(Path.GetFullPath(source))!;
            if (root.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidDataException("Network mapsets are not supported.");
            if (!Directory.Exists(root) || !roots.Add(root)) continue;
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                cancellation.ThrowIfCancellationRequested();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string child in Directory.EnumerateDirectories(directory))
                    if (!Path.GetFileName(child).StartsWith('.') && Path.GetFileName(child) is not ("artifacts" or "bin" or "obj")) pending.Push(child);
                // Only mapset media/storyboards enter the engine snapshot; project files and unrelated data stay outside it.
                foreach (string file in Directory.EnumerateFiles(directory))
                    if (!Path.GetFileName(file).StartsWith('.') && (resourceExtensions.Contains(Path.GetExtension(file)) || Path.GetFileName(file).Equals("skin.ini", StringComparison.OrdinalIgnoreCase)))
                        Copy(file, Path.GetRelativePath(root, file));
            }
        }
        var difficulties = new List<ExportedDifficulty>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            cancellation.ThrowIfCancellationRequested();
            var exported = OsuBeatmapWriter.Serialize(input.Document, compensation);
            string name = WorkspaceProject.DifficultyFileName(exported.ReadBack, input.Name, ".osu");
            if (!names.Add(name)) throw new InvalidDataException("Difficulties have conflicting export filenames: " + name);
            File.WriteAllText(Path.Combine(destination, name), exported.Text);
            string audioName = SongSetup.Get(exported.ReadBack, "General", "AudioFilename");
            if (input.Document.AudioPath is { } audio && File.Exists(audio) && !string.IsNullOrWhiteSpace(audioName)) Copy(audio, audioName);
            difficulties.Add(new(input.Id, name, input.Interpretation));
        }
        return new(destination, difficulties.ToArray());

        void Copy(string source, string relative)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
            string target = Path.GetFullPath(Path.Combine(destination, relative));
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Resource path escapes the mapset.");
            if (copied.TryGetValue(target, out var previous))
            {
                if (string.Equals(Path.GetFullPath(previous), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) return;
                throw new InvalidDataException("Conflicting mapset resource: " + relative);
            }
            if (++files > 50000 || (bytes += new FileInfo(source).Length) > 512L * 1024 * 1024) throw new InvalidDataException("Mapset resources exceed the 512 MiB / 50000 file limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false); copied[target] = source;
        }
    }
}
