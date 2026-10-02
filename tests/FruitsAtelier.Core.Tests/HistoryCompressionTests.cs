using FruitsAtelier.Core;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class HistoryCompressionTests
{
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/history-compression", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "current.catchproj");
        byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("中文 paths ../audio.mp3 and authoring\n", 10000)));
        WorkspaceHistoryFile.Write(path, bytes);
        Check(WorkspaceHistoryFile.Read(path).SequenceEqual(bytes), "binary round trip preserves exact UTF-8 bytes");
        Check(new FileInfo(path + WorkspaceHistoryFile.Extension).Length < bytes.Length / 10, "repetitive authoring is compressed");
        byte[] encoded = File.ReadAllBytes(path + WorkspaceHistoryFile.Extension);
        encoded[12] ^= 1;
        File.WriteAllBytes(path + WorkspaceHistoryFile.Extension, encoded);
        Reject(() => WorkspaceHistoryFile.Read(path), "checksum damage accepted");
        encoded[12] ^= 1;
        File.WriteAllBytes(path + WorkspaceHistoryFile.Extension, encoded[..^8]);
        Reject(() => WorkspaceHistoryFile.Read(path), "truncated data accepted");
        BitConverter.GetBytes(ProjectSerializer.MaximumFileBytes + 1).CopyTo(encoded, 8);
        File.WriteAllBytes(path + WorkspaceHistoryFile.Extension, encoded);
        Reject(() => WorkspaceHistoryFile.Read(path), "unbounded decompression accepted");

        string workspace = Path.Combine(root, "workspace");
        string snapshot = Path.Combine(workspace, ".sync-history", Guid.NewGuid().ToString("N"), "20200101T0000000000000-save");
        Directory.CreateDirectory(snapshot);
        string hash = new('E', 64), audio = Path.Combine(workspace, ".sync-history", "resources", hash);
        Directory.CreateDirectory(Path.GetDirectoryName(audio)!);
        File.WriteAllText(audio, "referenced audio");
        File.SetLastWriteTimeUtc(audio, DateTime.UtcNow.AddDays(-10));
        string document = Path.Combine(snapshot, "current.catchproj");
        File.WriteAllText(document, JsonSerializer.Serialize(new { AuthoringAudioHash = hash }));
        WorkspaceStorage.Clean(workspace);
        Check(File.Exists(document + WorkspaceHistoryFile.Extension) && !File.Exists(document), "maintenance compacts retained legacy history");
        WorkspaceStorage.Clean(workspace);
        Check(File.Exists(audio), "compressed history retains its referenced audio");
        var valid = File.ReadAllBytes(document + WorkspaceHistoryFile.Extension);
        valid[12] ^= 1;
        File.WriteAllBytes(document + WorkspaceHistoryFile.Extension, valid);
        string orphan = Path.Combine(Path.GetDirectoryName(audio)!, new string('F', 64));
        File.WriteAllText(orphan, "orphan"); File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-10));
        Reject(() => WorkspaceStorage.Clean(workspace), "corrupt compressed references accepted");
        Check(File.Exists(orphan), "corrupt compressed history prevents cleanup before deletion");
    }

    public static void Benchmark(string history)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts/history-compression", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        var files = Directory.GetFiles(history, "*", SearchOption.AllDirectories);
        var cache = new Dictionary<string, long>();
        long original = 0, compressed = 0;
        var clock = Stopwatch.StartNew(); double next = 0;
        for (int i = 0; i < files.Length; i++)
        {
            byte[] bytes = File.ReadAllBytes(files[i]); original += bytes.Length;
            string ext = Path.GetExtension(files[i]).ToLowerInvariant();
            if (ext is ".catchproj" or ".catchdiff" or ".catchsync" or ".osu")
            {
                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!cache.TryGetValue(hash, out long size))
                {
                    string target = Path.Combine(output, hash);
                    WorkspaceHistoryFile.Write(target, bytes);
                    Check(WorkspaceHistoryFile.Read(target).SequenceEqual(bytes), "real history round trip failed");
                    size = new FileInfo(target + WorkspaceHistoryFile.Extension).Length;
                    cache[hash] = size;
                }
                compressed += size;
            }
            else compressed += bytes.Length;
            if (clock.Elapsed.TotalSeconds >= next)
            {
                Console.WriteLine($"History compression: {i + 1}/{files.Length} files, {clock.Elapsed.TotalSeconds:F1}s, {original / 1048576d:F2} -> {compressed / 1048576d:F2} MiB");
                next = clock.Elapsed.TotalSeconds + 10;
            }
        }
        var result = new { Snapshots = Directory.GetDirectories(history).Length, Files = files.Length, OriginalBytes = original, CompressedBytes = compressed,
            SavedPercent = 100d * (original - compressed) / original, UniquePayloads = cache.Count, Seconds = clock.Elapsed.TotalSeconds };
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "report.json"), json);
        Console.WriteLine(json); Console.WriteLine($"Report: {output}");
    }

    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception(message);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
