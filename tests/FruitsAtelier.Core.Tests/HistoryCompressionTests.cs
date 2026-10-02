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
        foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode })
        {
            string text = "{\"Name\":\"中文\"}", bomPath = Path.Combine(root, "bom.catchproj");
            byte[] encodedText = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
            File.WriteAllBytes(bomPath, encodedText);
            Check(WorkspaceHistoryFile.ReadText(bomPath) == text, "legacy text retains BOM detection");
            WorkspaceHistoryFile.Write(bomPath, encodedText);
            Check(WorkspaceHistoryFile.ReadText(bomPath) == text && WorkspaceHistoryFile.Read(bomPath).SequenceEqual(encodedText), "compressed legacy text preserves encoding and exact bytes");
            File.Delete(bomPath + WorkspaceHistoryFile.Extension);
        }
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

        string cappedWorkspace = Path.Combine(root, "capped-workspace");
        var map = new MapDocument { IsDemo = false };
        map.Fruits.Add(new Fruit { TimeMs = 1000, X = 100 });
        var project = BeatmapProject.FromDocuments([map]);
        var session = WorkspaceProject.Create(cappedWorkspace, project, "");
        string projectHistory = Path.Combine(cappedWorkspace, ".sync-history", session.Manifest.Id.ToString("N"));
        var rounds = Enumerable.Range(0, 101).Select(i => Path.Combine(projectHistory, DateTime.UtcNow.AddMinutes(-200 + i).ToString("yyyyMMddTHHmmssfffffff") + "-save")).ToArray();
        foreach (string round in rounds)
        {
            Directory.CreateDirectory(round);
            WorkspaceHistoryFile.WriteProject(project, Path.Combine(round, "saved.catchproj"));
        }
        string other = Path.Combine(cappedWorkspace, ".sync-history", Guid.NewGuid().ToString("N"), "20200101T0000000000000-save");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "saved.catchproj"), "{}");
        project.Difficulties[0].Document.Fruits[0].X = 200;
        WorkspaceVersionHistory.ArchiveCurrent(session, project);
        Check(Directory.GetDirectories(projectHistory).Length == 100 && !Directory.Exists(rounds[0]) && !Directory.Exists(rounds[1]), "new backup trims a set to 100 rounds without the 24-hour exemption");
        Check(Directory.Exists(other), "write-time retention leaves other sets alone");
        var newest = WorkspaceVersionHistory.List(session).First(v => v.WorkingCopy);
        Check(WorkspaceVersionHistory.Read(session, newest).Difficulties[0].Document.Fruits[0].X == 200, "latest unsaved authoring survives write-time pruning");
        BackgroundMigration(Path.Combine(root, "background"));
    }

    private static void BackgroundMigration(string workspace)
    {
        string snapshot = Path.Combine(workspace, ".sync-history", Guid.NewGuid().ToString("N"), "20200101T0000000000000-save");
        Directory.CreateDirectory(snapshot);
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(new string('中', 20000))).ToArray();
        string source = Path.Combine(snapshot, "saved.catchproj");
        File.WriteAllBytes(source, bytes);
        using (var cancel = new CancellationTokenSource())
        {
            var task = WorkspaceStorage.CompressLegacyHistoryAsync(workspace, cancel.Token, TimeSpan.FromSeconds(30));
            cancel.Cancel();
            try { task.GetAwaiter().GetResult(); throw new Exception("canceled migration completed"); }
            catch (OperationCanceledException) { }
        }
        Check(File.Exists(source) && !File.Exists(source + WorkspaceHistoryFile.Extension), "canceling before conversion preserves the original");
        string conflicting = Path.Combine(snapshot, "conflict.catchdiff");
        File.WriteAllBytes(conflicting, bytes);
        byte[] replacement = Encoding.UTF8.GetBytes(new string('y', 20000));
        WorkspaceHistoryFile.Write(conflicting, replacement);
        string damaged = Path.Combine(snapshot, "damaged.catchsync");
        File.WriteAllBytes(damaged, bytes); File.WriteAllText(damaged + WorkspaceHistoryFile.Extension, "broken");
        string audio = Path.Combine(snapshot, "audio.mp3"), active = Path.Combine(workspace, "active.catchproj");
        File.WriteAllBytes(audio, bytes); File.WriteAllBytes(active, bytes);
        var result = WorkspaceStorage.CompressLegacyHistoryAsync(workspace, interval: TimeSpan.Zero).GetAwaiter().GetResult();
        Check(result.ConvertedFiles == 1 && result.ReclaimedBytes == bytes.Length - new FileInfo(source + WorkspaceHistoryFile.Extension).Length, "migration reports exact reclaimed bytes");
        Check(result.Error is not null && File.Exists(damaged), "damaged binary retains its original while other conversions continue");
        Check(!File.Exists(source) && WorkspaceHistoryFile.Read(source).SequenceEqual(bytes), "background conversion publishes a readable exact copy before deleting the original");
        Check(File.Exists(conflicting) && WorkspaceHistoryFile.Read(conflicting).SequenceEqual(replacement), "migration preserves a different existing binary and its raw copy");
        Check(File.Exists(audio) && File.Exists(active), "migration only converts recovery documents");
        Check(!Directory.EnumerateFiles(Path.Combine(workspace, ".sync-history", ".compression")).Any(), "migration removes staging files after success and failure");

        File.Delete(conflicting); File.Delete(damaged);

        string racing = Path.Combine(snapshot, "racing.catchproj");
        byte[] noise = new byte[1024 * 1024]; new Random(731).NextBytes(noise);
        File.WriteAllBytes(racing, noise.Concat(noise).ToArray());
        // The staging file exists while Brotli is running, before publication under the save lock.
        var race = WorkspaceStorage.CompressLegacyHistoryAsync(workspace, interval: TimeSpan.Zero);
        string staging = Path.Combine(workspace, ".sync-history", ".compression");
        var deadline = Stopwatch.StartNew();
        while (!Directory.EnumerateFiles(staging, "*.tmp").Any() && !race.IsCompleted && deadline.Elapsed.TotalSeconds < 15) Thread.Sleep(1);
        Check(!race.IsCompleted && deadline.Elapsed.TotalSeconds < 15, "background compression reaches staging before publication");
        bool acquired = Monitor.TryEnter(WorkspaceProject.Gate, 100);
        try
        {
            Check(acquired, "expensive compression leaves the save lock available");
            File.WriteAllBytes(racing, replacement);
        }
        finally { if (acquired) Monitor.Exit(WorkspaceProject.Gate); }
        race.GetAwaiter().GetResult();
        Check(File.ReadAllBytes(racing).SequenceEqual(replacement) && !File.Exists(racing + WorkspaceHistoryFile.Extension), "source changed during compression survives without a stale binary");
        File.WriteAllBytes(racing, noise.Concat(noise).ToArray());
        using var interrupted = new CancellationTokenSource();
        var inProgress = WorkspaceStorage.CompressLegacyHistoryAsync(workspace, interrupted.Token, TimeSpan.Zero);
        deadline.Restart();
        while (!Directory.EnumerateFiles(staging, "*.tmp").Any() && !inProgress.IsCompleted && deadline.Elapsed.TotalSeconds < 15) Thread.Sleep(1);
        Check(!inProgress.IsCompleted && deadline.Elapsed.TotalSeconds < 15, "cancellation test reaches an unfinished compressed file");
        interrupted.Cancel();
        try { inProgress.GetAwaiter().GetResult(); throw new Exception("interrupted compression completed"); }
        catch (OperationCanceledException) { }
        Check(File.Exists(racing) && !File.Exists(racing + WorkspaceHistoryFile.Extension) && !Directory.EnumerateFiles(staging).Any(),
            "canceling during compression retains the original and removes unfinished staging");
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
