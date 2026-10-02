using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FruitsAtelier.Core;

public sealed record WorkspaceStoragePart(string Name, long Bytes);
public sealed record WorkspaceStorageReport(long TotalBytes, IReadOnlyList<WorkspaceStoragePart> Categories,
    IReadOnlyList<WorkspaceStoragePart> Folders, long ReclaimedBytes = 0, bool RecoveryPending = false);
public sealed record WorkspaceHistoryCompressionResult(int ConvertedFiles, long ReclaimedBytes, string? Error = null);

public static class WorkspaceStorage
{
    public const int RetentionDays = 30, VersionsPerProject = 100;
    public const long HistoryBudget = 1024L * 1024 * 1024;
    private static readonly Regex Hashes = new("[A-Fa-f0-9]{64}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private sealed record Snapshot(string Path, DateTime Time, long Bytes);
    private sealed record SnapshotReferences(long Length, DateTime Written, HashSet<string> Paths);
    private static readonly Dictionary<string, SnapshotReferences> snapshotReferences = new(WorkspaceSynchronization.Paths);

    public static WorkspaceStorageReport Inspect(string workspace)
    {
        lock (WorkspaceProject.Gate) return InspectCore(workspace);
    }

    private static WorkspaceStorageReport InspectCore(string workspace)
    {
        string root = Path.GetFullPath(workspace);
        var categories = new Dictionary<string, long>(); var folders = new Dictionary<string, long>();
        foreach (var file in Files(root))
        {
            string relative = Path.GetRelativePath(root, file.FullName), first = relative.Split(Path.DirectorySeparatorChar)[0];
            string category = first == ".sync-history"
                ? relative.StartsWith(Path.Combine(".sync-history", "resources") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "audio"
                    : relative.StartsWith(Path.Combine(".sync-history", "reviews") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "cache" : "history"
                : first is "Resources" or "Skins" ? "resources"
                : first.StartsWith("library.db", StringComparison.Ordinal) ? "cache"
                : relative.Contains(Path.DirectorySeparatorChar) && File.Exists(Path.Combine(root, first, WorkspaceProject.ManifestName)) ? "projects" : "other";
            categories[category] = categories.GetValueOrDefault(category) + file.Length;
            folders[first] = folders.GetValueOrDefault(first) + file.Length;
        }
        return new(categories.Values.Sum(), categories.Select(p => new WorkspaceStoragePart(p.Key, p.Value)).OrderByDescending(p => p.Bytes).ToArray(),
            folders.Select(p => new WorkspaceStoragePart(p.Key, p.Value)).OrderByDescending(p => p.Bytes).ToArray());
    }

    public static WorkspaceStorageReport Clean(string workspace, bool clearCache = false, DateTime? utcNow = null, IReadOnlyCollection<string>? protectedPaths = null, bool compactLegacy = true)
        => CleanCore(workspace, clearCache, utcNow, protectedPaths, compactLegacy: compactLegacy);

    public static Task<WorkspaceHistoryCompressionResult> CompressLegacyHistoryAsync(string workspace, CancellationToken cancellation = default, TimeSpan? interval = null, Func<bool>? isIdle = null)
        => Task.Factory.StartNew(() =>
        {
            var thread = Thread.CurrentThread;
            if (OperatingSystem.IsWindows()) thread.Priority = ThreadPriority.BelowNormal;
            cancellation.ThrowIfCancellationRequested();
            void WaitForIdle()
            {
                while (isIdle?.Invoke() == false)
                    if (cancellation.WaitHandle.WaitOne(100)) cancellation.ThrowIfCancellationRequested();
                cancellation.ThrowIfCancellationRequested();
            }
            WaitForIdle();
            string history = Path.Combine(Path.GetFullPath(workspace), ".sync-history");
            if (!Directory.Exists(history)) return new WorkspaceHistoryCompressionResult(0, 0);
            string staging = Path.Combine(history, ".compression");
            WorkspaceProject.RejectLinks(staging);
            string[] candidates, references;
            lock (WorkspaceProject.Gate)
            {
                var files = Files(history).Where(f => IsHistorySnapshot(f.FullName, history)).ToArray();
                candidates = files.Where(f => WorkspaceHistoryFile.Compressible(f.FullName)).Select(f => f.FullName).ToArray();
                references = files.Where(f => f.FullName.EndsWith(WorkspaceHistoryFile.Extension, StringComparison.OrdinalIgnoreCase)
                    && Path.GetExtension(WorkspaceHistoryFile.LogicalPath(f.FullName)).ToLowerInvariant() is ".catchproj" or ".catchdiff" or ".catchsync" or ".json")
                    .Select(f => f.FullName).ToArray();
            }
            int converted = 0; long reclaimed = 0; string? error = null;
            foreach (string path in references)
            {
                if (cancellation.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(10))) cancellation.ThrowIfCancellationRequested();
                WaitForIdle();
                try { PrimeSnapshotReferences(path, history); }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
                { error ??= e.Message; }
            }
            foreach (string path in candidates)
            {
                if (cancellation.WaitHandle.WaitOne(interval ?? TimeSpan.FromSeconds(2))) cancellation.ThrowIfCancellationRequested();
                WaitForIdle();
                try
                {
                    Directory.CreateDirectory(staging);
                    long saved = WorkspaceHistoryFile.CompactInBackground(path, staging, cancellation);
                    if (saved > 0) { converted++; reclaimed += saved; }
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                { error ??= e.Message; }
            }
            return new WorkspaceHistoryCompressionResult(converted, reclaimed, error);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static void PrimeSnapshotReferences(string path, string history)
    {
        FileInfo file;
        string text;
        lock (WorkspaceProject.Gate)
        {
            file = new(path);
            if (!file.Exists) return;
            if (snapshotReferences.TryGetValue(path, out var cached) && cached.Length == file.Length && cached.Written == file.LastWriteTimeUtc) return;
            text = WorkspaceHistoryFile.ReadText(path);
        }
        // Large history documents are parsed outside the lock used by project saves and opens.
        using var document = JsonDocument.Parse(text);
        var paths = new HashSet<string>(WorkspaceSynchronization.Paths);
        FindPaths(document.RootElement, Path.GetDirectoryName(WorkspaceHistoryFile.LogicalPath(path))!, new(WorkspaceSynchronization.Paths), paths, history);
        lock (WorkspaceProject.Gate)
        {
            var current = new FileInfo(path);
            if (!current.Exists || current.Length != file.Length || current.LastWriteTimeUtc != file.LastWriteTimeUtc) return;
            if (snapshotReferences.Count >= 2048) snapshotReferences.Clear();
            snapshotReferences[path] = new(file.Length, file.LastWriteTimeUtc, paths);
        }
    }

    private static void FindPaths(JsonElement element, string directory, HashSet<string> playback, HashSet<string> paths, string history, string? propertyName = null)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) FindPaths(property.Value, directory, playback, paths, history, property.Name);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) FindPaths(item, directory, playback, paths, history, propertyName);
        else if (element.ValueKind == JsonValueKind.String)
        {
            string? key = propertyName?.ToLowerInvariant();
            if (key is "authoring" or "project" or "manifest")
            {
                if (element.GetString() is not { Length: > 0 } nestedText) return;
                using var nested = JsonDocument.Parse(nestedText);
                FindPaths(nested.RootElement, directory, playback, paths, history);
                return;
            }
            // Object source lines and metadata are content, even when they contain
            // slashes or decimal points. Only persisted path fields form references.
            if (key is not ("audiopath" or "sourcepath" or "path" or "source" or "exporttarget" or "file" or "syncfile"
                or "directory" or "songsroot" or "sourcedirectory" or "externalsourcedirectory" or "previouspaths" or "backup")) return;
            if (element.GetString() is not { Length: > 0 } value) return;
            string portable = value.Replace('\\', '/');
            if (portable.Contains("resources/playback/", StringComparison.OrdinalIgnoreCase)) playback.Add(portable.Split('/')[^1]);
            try
            {
                string path = Path.GetFullPath(value, directory);
                paths.Add(path);
                if (WorkspaceProject.Within(Path.Combine(history, "resources", "playback"), path)) playback.Add(Path.GetFileName(path));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
            { throw new InvalidDataException(FruitsAtelier.Localization.Strings.Get("storage.invalidReference", propertyName), e); }
        }
    }

    private static bool IsHistorySnapshot(string path, string history)
    {
        string[] parts = Path.GetRelativePath(history, path).Split(Path.DirectorySeparatorChar);
        return parts.Length >= 3 && Guid.TryParseExact(parts[0], "N", out _) && IsSnapshot(parts[1]);
    }

    internal static void EnforceVersionLimit(WorkspaceSession session)
    {
        string workspace = Path.GetDirectoryName(session.Directory)!;
        string project = Path.Combine(workspace, ".sync-history", session.Manifest.Id.ToString("N"));
        if (Directory.EnumerateDirectories(project).Count(IsSnapshot) <= VersionsPerProject) return;
        CleanCore(workspace, false, null, session.Project.Difficulties.SelectMany(d => new[] { d.Document.AudioPath, d.Document.SourcePath }).OfType<string>().ToArray(), project);
    }

    private static bool IsSnapshot(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length >= 24 && DateTime.TryParseExact(name[..22], "yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);
    }

    private static WorkspaceStorageReport CleanCore(string workspace, bool clearCache, DateTime? utcNow, IReadOnlyCollection<string>? protectedPaths, string? limitProject = null, bool compactLegacy = true)
    {
        string root = Path.GetFullPath(workspace), history = Path.Combine(root, ".sync-history");
        DateTime now = utcNow ?? DateTime.UtcNow;
        lock (WorkspaceProject.Gate)
        {
            var files = Files(root).ToArray();
            // Recovery receipts and in-progress publications own their backups until recovery finishes.
            bool pending = files.Any(f => f.Name == "delete.json" || WorkspaceProject.Within(Path.Combine(history, "pending"), f.FullName)
                || Path.GetRelativePath(root, f.FullName).Split(Path.DirectorySeparatorChar).Any(p => p.EndsWith(".saving") || p.EndsWith(".previous")));
            if (pending) return Inspect(root) with { RecoveryPending = true };
            var snapshots = new List<Snapshot>();
            if (Directory.Exists(history)) foreach (string project in Directory.EnumerateDirectories(history))
            {
                if (!Guid.TryParseExact(Path.GetFileName(project), "N", out _)) continue;
                foreach (string path in Directory.EnumerateDirectories(project))
                {
                    string name = Path.GetFileName(path);
                    if (name.Length < 24 || !DateTime.TryParseExact(name[..22], "yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)) continue;
                    snapshots.Add(new(path, time, limitProject is not null ? 0 : files.Where(f => WorkspaceProject.Within(path, f.FullName)).Sum(f => f.Length)));
                }
            }
            // Parse every authoring/reference document before deleting anything. A damaged reference
            // graph must not turn an unreadable project's audio into apparently unused data.
            var documents = new Dictionary<string, HashSet<string>>();
            var playbackReferences = new Dictionary<string, HashSet<string>>();
            var pinned = new HashSet<string>(WorkspaceSynchronization.Paths);
            foreach (string live in protectedPaths ?? [])
                foreach (var snapshot in snapshots)
                    if (WorkspaceProject.Within(snapshot.Path, live)) pinned.Add(snapshot.Path);
            foreach (var file in files.Where(f => Path.GetExtension(WorkspaceHistoryFile.LogicalPath(f.FullName)).ToLowerInvariant() is ".catchdiff" or ".catchproj" or ".catchsync" or ".json"))
            {
                string logical = WorkspaceHistoryFile.LogicalPath(file.FullName);
                string? owner = snapshots.FirstOrDefault(s => WorkspaceProject.Within(s.Path, file.FullName))?.Path;
                bool cacheable = limitProject is not null && owner is not null && file.FullName.EndsWith(WorkspaceHistoryFile.Extension, StringComparison.OrdinalIgnoreCase);
                if (cacheable && snapshotReferences.TryGetValue(file.FullName, out var cached)
                    && cached.Length == file.Length && cached.Written == file.LastWriteTimeUtc)
                {
                    PinReferences(cached.Paths, owner);
                    continue;
                }
                string text = WorkspaceProject.Within(history, file.FullName) ? WorkspaceHistoryFile.ReadText(file.FullName) : File.ReadAllText(file.FullName);
                using var document = JsonDocument.Parse(text);
                if (limitProject is null) documents[file.FullName] = Hashes.Matches(text).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var playback = new HashSet<string>(WorkspaceSynchronization.Paths);
                playbackReferences[file.FullName] = playback;
                var paths = new HashSet<string>(WorkspaceSynchronization.Paths);
                FindPaths(document.RootElement, Path.GetDirectoryName(logical)!, playback, paths, history);
                PinReferences(paths, owner);
                if (cacheable)
                {
                    // Published history binaries are immutable; replacement invalidates their file stamp.
                    if (snapshotReferences.Count >= 2048) snapshotReferences.Clear();
                    snapshotReferences[file.FullName] = new(file.Length, file.LastWriteTimeUtc, paths);
                }
            }
            void PinReferences(IEnumerable<string> paths, string? owner)
            {
                foreach (string path in paths)
                    foreach (var snapshot in snapshots)
                        if (!WorkspaceSynchronization.Paths.Equals(snapshot.Path, owner) && WorkspaceProject.Within(snapshot.Path, path)) pinned.Add(snapshot.Path);
            }
            var remove = new HashSet<string>(WorkspaceSynchronization.Paths);
            foreach (var group in snapshots.GroupBy(s => Path.GetDirectoryName(s.Path)))
            {
                if (limitProject is not null && !WorkspaceSynchronization.Paths.Equals(group.Key, limitProject)) continue;
                var ordered = group.OrderByDescending(s => s.Time).ToArray();
                if (ordered.Length > 0) pinned.Add(ordered[0].Path);
                int unpinnedSlots = Math.Max(0, VersionsPerProject - ordered.Count(s => pinned.Contains(s.Path))), unpinnedIndex = 0;
                for (int i = 1; i < ordered.Length; i++)
                {
                    if (pinned.Contains(ordered[i].Path)) continue;
                    bool overLimit = unpinnedIndex++ >= unpinnedSlots;
                    if (!clearCache && (overLimit || limitProject is null && ordered[i].Time < now.AddDays(-RetentionDays)
                        && ordered[i].Time < now.AddDays(-1))) remove.Add(ordered[i].Path);
                }
            }
            long retained = snapshots.Where(s => !remove.Contains(s.Path)).Sum(s => s.Bytes);
            foreach (var snapshot in snapshots.OrderBy(s => s.Time))
                if (limitProject is null && !clearCache && retained > HistoryBudget && !remove.Contains(snapshot.Path) && !pinned.Contains(snapshot.Path) && snapshot.Time < now.AddDays(-1))
                { remove.Add(snapshot.Path); retained -= snapshot.Bytes; }
            var referencedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in documents)
                if (!remove.Any(path => WorkspaceProject.Within(path, pair.Key)))
                    referencedHashes.UnionWith(pair.Value);
            var referencedPlayback = new HashSet<string>(WorkspaceSynchronization.Paths);
            foreach (var pair in playbackReferences)
                if (!remove.Any(path => WorkspaceProject.Within(path, pair.Key))) referencedPlayback.UnionWith(pair.Value);
            foreach (string live in protectedPaths ?? [])
            {
                foreach (Match hash in Hashes.Matches(live)) referencedHashes.Add(hash.Value);
                if (WorkspaceProject.Within(Path.Combine(history, "resources", "playback"), live)) referencedPlayback.Add(Path.GetFileName(live));
            }
            long reclaimed = 0;
            foreach (string path in remove)
            {
                foreach (var file in files.Where(f => WorkspaceProject.Within(path, f.FullName))) Delete(file);
                foreach (string directory in Directory.EnumerateDirectories(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).OrderByDescending(p => p.Length))
                { WorkspaceProject.RejectLinks(directory); Directory.Delete(directory); }
                WorkspaceProject.RejectLinks(path);
                Directory.Delete(path);
            }
            if (limitProject is not null) return new(0, [], [], reclaimed);
            string resources = Path.Combine(history, "resources"), reviews = Path.Combine(history, "reviews");
            foreach (var file in files.Where(f => WorkspaceProject.Within(resources, f.FullName)))
            {
                var hashes = Hashes.Matches(file.Name);
                if (File.GetLastWriteTimeUtc(file.FullName) >= now.AddDays(-1)) continue;
                bool playback = WorkspaceProject.Within(Path.Combine(resources, "playback"), file.FullName);
                if (playback)
                {
                    if (referencedPlayback.Contains(file.Name) || hashes.Count == 0 && !Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _)) continue;
                }
                else if (file.Name.Length != 64 || hashes.Count != 1 || referencedHashes.Contains(file.Name)) continue;
                Delete(file);
            }
            foreach (var file in files.Where(f => WorkspaceProject.Within(reviews, f.FullName)))
                if (clearCache || file.LastWriteTimeUtc < now.AddDays(-RetentionDays)) Delete(file);
            if (!clearCache && compactLegacy)
                foreach (var file in files.Where(f => File.Exists(f.FullName) && WorkspaceHistoryFile.Compressible(f.FullName)
                    && snapshots.Any(s => !remove.Contains(s.Path) && WorkspaceProject.Within(s.Path, f.FullName))))
                    reclaimed += WorkspaceHistoryFile.Compact(file.FullName);
            return Inspect(root) with { ReclaimedBytes = reclaimed };
            void Delete(FileInfo file)
            {
                if (!WorkspaceProject.Within(history, file.FullName)) throw new IOException(FruitsAtelier.Localization.Strings.Get("project.invalid"));
                WorkspaceProject.RejectLinks(file.FullName);
                if (File.Exists(file.FullName)) { long bytes = new FileInfo(file.FullName).Length; File.Delete(file.FullName); reclaimed += bytes; }
            }
        }
    }

    private static IEnumerable<FileInfo> Files(string root)
    {
        WorkspaceProject.RejectLinks(root);
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out string? directory))
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                WorkspaceProject.RejectLinks(path);
                // Staging belongs to the compression worker and can disappear outside the save lock.
                if (Path.GetFileName(directory) == ".sync-history" && Path.GetFileName(path) == ".compression") continue;
                if (Directory.Exists(path)) pending.Push(path); else yield return new FileInfo(path);
            }
    }
}
