using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed class WorkspaceManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? SongsRoot { get; set; }
    public string? SourceDirectory { get; set; }
    public string? ExternalSourceDirectory { get; set; }
    public List<WorkspaceDifficulty> Difficulties { get; set; } = [];
}

public sealed class WorkspaceDifficulty
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string? Source { get; set; }
    public string? SourceHash { get; set; }
    public string? ExportTarget { get; set; }
    public string? ExportHash { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceSyncBaseline? Sync { get; set; }
    public string? SyncFile { get; set; }
}

public sealed record WorkspaceSession(string Directory, WorkspaceManifest Manifest, BeatmapProject Project)
{
    // Only the first source import offers conversion; opening its persisted project never does.
    public bool IsNewImport { get; init; }
}

public static class WorkspaceProject
{
    internal static readonly object Gate = new();
    public const string ManifestName = "project.catchdiff";
    public static WorkspaceManifest SnapshotManifest(WorkspaceManifest manifest) => new()
    {
        SchemaVersion = manifest.SchemaVersion, Id = manifest.Id, Name = manifest.Name, SongsRoot = manifest.SongsRoot,
        SourceDirectory = manifest.SourceDirectory, ExternalSourceDirectory = manifest.ExternalSourceDirectory,
        Difficulties = manifest.Difficulties.Select(e => new WorkspaceDifficulty
        {
            Id = e.Id, Name = e.Name, File = e.File, Source = e.Source, SourceHash = e.SourceHash,
            ExportTarget = e.ExportTarget, ExportHash = e.ExportHash, SyncFile = e.SyncFile,
            Sync = e.Sync is not { } s ? null : new WorkspaceSyncBaseline { Path = s.Path, Text = s.Text, Authoring = s.Authoring,
                AudioHash = s.AudioHash, AuthoringAudioHash = s.AuthoringAudioHash, ObjectSources = s.ObjectSources.ToList(), PreviousPaths = s.PreviousPaths.ToList(), LocalOverrides = s.LocalOverrides.ToList(),
                RetainedObjects = s.RetainedObjects.Select(r => new WorkspaceRetainedObjects(r.Sources.ToList(), r.ExternalLines.ToList())).ToList(), RetainedObjectsRecorded = s.RetainedObjectsRecorded }
        }).ToList()
    };
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static string SafeName(string value)
    {
        var clean = new string(value.Where(c => !char.IsControl(c) && !"<>:\"/\\|?*".Contains(c)).ToArray()).Trim().TrimEnd('.');
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(clean);
        if (elements.Length > 160) clean = clean[..elements[160]];
        if (string.IsNullOrEmpty(clean)) return "Untitled";
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(clean.Split('.')[0], StringComparer.OrdinalIgnoreCase)) clean = "_" + clean;
        return clean;
    }

    public static string DifficultyFileName(MapDocument document, string difficulty, string extension = ".catchdiff")
        => SafeName($"{Metadata(document, "Artist")} - {Metadata(document, "Title", document.Name)} ({Metadata(document, "Creator")}) [{difficulty}]") + extension;
    private static string Metadata(MapDocument d, string key, string fallback = "Unknown") => OsuBeatmapReader.Setting(d, "Metadata", key) is { Length: > 0 } v ? v : fallback;
    public static string Hash(string path) { using var file = System.IO.File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    public static bool HasExistingSongsFile(WorkspaceManifest manifest, string songs)
    {
        if (string.IsNullOrWhiteSpace(songs)) return false;
        return manifest.Difficulties.Any(d => Exists(d.Source) || Exists(d.ExportTarget));
        bool Exists(string? path) => path is not null && Within(songs, path) && File.Exists(path);
    }
    public static bool Within(string root, string path)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(fullPath, fullRoot, comparison) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }
    public static void RejectLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            if ((System.IO.File.Exists(part) || System.IO.Directory.Exists(part)) && (System.IO.File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("resource.link", part));
    }
    public static void ValidateRoots(string workspace, string songs, bool requireSongs = true)
    {
        RejectLinks(workspace);
        if (string.IsNullOrWhiteSpace(songs)) return;
        RejectLinks(songs);
        if (requireSongs && !System.IO.Directory.Exists(songs)) throw new DirectoryNotFoundException(songs);
        if (Within(songs, workspace) || Within(workspace, songs)) throw new InvalidOperationException(L.Get("library.rootsOverlap"));
    }

    public static WorkspaceSession Create(string workspace, BeatmapProject project, string songsRoot)
    {
        lock (Gate) return CreateLocked(workspace, project, songsRoot);
    }
    private static WorkspaceSession CreateLocked(string workspace, BeatmapProject project, string songsRoot)
    {
        ValidateRoots(workspace, songsRoot, false);
        var sources = project.Difficulties.Select(d => d.Document.SourcePath).Where(p => p is not null)
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p!))!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sources.Length > 0)
        {
            var database = new LibraryDatabase(workspace, songsRoot);
            foreach (string folder in sources)
                if (database.ProjectForSource(folder) is { } existing)
                    throw new InvalidOperationException(L.Get("library.sourceAlreadyLinked", existing));
        }
        var manifest = new WorkspaceManifest { Name = project.Name, SongsRoot = string.IsNullOrWhiteSpace(songsRoot) ? null : Path.GetFullPath(songsRoot) };
        var source = project.Difficulties.Select(d => d.Document.SourcePath).FirstOrDefault(p => p is not null && manifest.SongsRoot is not null && Within(manifest.SongsRoot, p));
        if (source is not null) manifest.SourceDirectory = Path.GetRelativePath(songsRoot, Path.GetDirectoryName(source)!);
        if (source is null && project.Difficulties.Select(d => d.Document.SourcePath).FirstOrDefault(p => p is not null) is { } external)
            manifest.ExternalSourceDirectory = Path.GetDirectoryName(Path.GetFullPath(external));
        string directory = Path.Combine(workspace, SafeName(project.Name) + " [" + manifest.Id.ToString("N")[..8] + "]");
        var session = new WorkspaceSession(directory, manifest, project);
        Save(session, project);
        return session;
    }

    public static WorkspaceSession Open(string directory) { lock (Gate) { return OpenLocked(directory); } }
    private static WorkspaceSession OpenLocked(string directory)
    {
        directory = Path.GetFullPath(directory);
        Recover(directory);
        WorkspaceAssociations.RecoverDeletion(directory);
        WorkspaceExportRecovery.Recover(directory);
        RejectLinks(directory);
        var manifest = ReadManifest(directory);
        var project = new BeatmapProject { Name = manifest.Name };
        foreach (var entry in manifest.Difficulties)
        {
            if (Path.GetFileName(entry.File) != entry.File || entry.File == ManifestName || !entry.File.EndsWith(".catchdiff", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Get("project.invalid"));
            string file = Path.Combine(directory, entry.File); RejectLinks(file);
            project.Difficulties.Add(new ProjectDifficulty { Id = entry.Id, Name = entry.Name, Document = ProjectSerializer.ReadFile(file) });
        }
        project.Validate();
        return new(directory, manifest, project);
    }

    public static WorkspaceManifest ReadManifest(string directory, bool includeSync = true)
    {
        string path = Path.Combine(directory, ManifestName);
        RejectLinks(path);
        if (new FileInfo(path).Length > ProjectSerializer.MaximumFileBytes) throw new InvalidDataException(L.Get("core.project.readLimit"));
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(System.IO.File.ReadAllText(path), json);
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Id == Guid.Empty || string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Difficulties is null || manifest.Difficulties.Count is < 1 or > 256
            || manifest.Difficulties.Any(d => d is null || d.Id == Guid.Empty || string.IsNullOrWhiteSpace(d.Name))
            || manifest.Difficulties.Select(d => d.Id).Distinct().Count() != manifest.Difficulties.Count
            || manifest.Difficulties.Select(d => d.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Difficulties.Count)
            throw new InvalidDataException(L.Get("project.invalid"));
        if (includeSync)
            foreach (var entry in manifest.Difficulties.Where(d => d.SyncFile is not null))
            {
                if (Path.GetFileName(entry.SyncFile) != entry.SyncFile || !entry.SyncFile!.EndsWith(".catchsync", StringComparison.Ordinal)) throw new InvalidDataException(L.Get("project.invalid"));
                string syncPath = Path.Combine(directory, entry.SyncFile); RejectLinks(syncPath);
                if (new FileInfo(syncPath).Length > 128L * 1024 * 1024) throw new InvalidDataException(L.Get("core.project.readLimit"));
                entry.Sync = JsonSerializer.Deserialize<WorkspaceSyncBaseline>(System.IO.File.ReadAllText(syncPath), json) ?? throw new InvalidDataException(L.Get("project.invalid"));
            }
        return manifest;
    }

    // A complete sibling snapshot is published by directory rename. A retained previous
    // directory makes an interrupted rename recoverable without mixing difficulty versions.
    public static void Save(WorkspaceSession session, BeatmapProject project) { lock (Gate) { SaveLocked(session, project); } }
    private static void SaveLocked(WorkspaceSession session, BeatmapProject project)
    {
        project.Validate();
        string directory = Path.GetFullPath(session.Directory);
        if (session.Manifest.SongsRoot is { } songs) ValidateRoots(Path.GetDirectoryName(directory)!, songs, false);
        RejectLinks(directory); Recover(directory);
        WorkspaceVersionHistory.ArchiveBeforeSave(session, project);
        string staging = directory + ".saving", previous = directory + ".previous";
        RejectLinks(staging); RejectLinks(previous);
        if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true);
        System.IO.Directory.CreateDirectory(staging);
        var entries = new List<WorkspaceDifficulty>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
        try
        {
            foreach (var diff in project.Difficulties)
            {
                var old = session.Manifest.Difficulties.FirstOrDefault(e => e.Id == diff.Id);
                string name = DifficultyFileName(diff.Document, diff.Name);
                if (!names.Add(name))
                {
                    string stem = Path.GetFileNameWithoutExtension(name); int suffix = 2;
                    do { name = stem + " (" + suffix++ + ").catchdiff"; } while (!names.Add(name));
                }
                string? source = diff.Document.SourcePath;
                if (old is null && source is not null && System.IO.File.Exists(source)
                    && LibraryDatabase.ReadMetadata(source)?.Difficulty != diff.Name) source = null;
                if (old is null && source is not null)
                {
                    WorkspaceAssociations.EnsureOwner(session, diff.Id, source);
                    if (entries.Any(e => WorkspaceSynchronization.Paths.Equals(WorkspaceSynchronization.Target(e), source)))
                        throw new InvalidOperationException(L.Get("sync.duplicate", source));
                }
                var entry = new WorkspaceDifficulty { Id = diff.Id, Name = diff.Name, File = name, Source = old is not null ? old.Source : source,
                    SourceHash = old is not null ? old.SourceHash : (source is not null && System.IO.File.Exists(source) ? Hash(source) : null), ExportTarget = old?.ExportTarget, ExportHash = old?.ExportHash, Sync = old?.Sync };
                if (old is null && source is not null && System.IO.File.Exists(source))
                    entry.Sync = WorkspaceSynchronization.Capture(source, diff.Document, directory);
                if (entry.Sync is not null)
                {
                    entry.SyncFile = entry.Id.ToString("N") + ".catchsync";
                    string syncText = JsonSerializer.Serialize(entry.Sync, json);
                    if (System.Text.Encoding.UTF8.GetByteCount(syncText) > 128L * 1024 * 1024) throw new InvalidDataException(L.Get("core.project.writeLimit"));
                    AtomicFile.Write(Path.Combine(staging, entry.SyncFile), syncText);
                }
                entries.Add(entry);
                // Encode relative paths against the final location, not the staging folder.
                AtomicFile.Write(Path.Combine(staging, name), ProjectSerializer.Serialize(diff.Document, Path.Combine(directory, name)));
            }
            var manifest = new WorkspaceManifest { Id = session.Manifest.Id, Name = project.Name, SongsRoot = session.Manifest.SongsRoot,
                SourceDirectory = session.Manifest.SourceDirectory, ExternalSourceDirectory = session.Manifest.ExternalSourceDirectory, Difficulties = entries };
            var persisted = new WorkspaceManifest { Id = manifest.Id, Name = manifest.Name, SongsRoot = manifest.SongsRoot,
                SourceDirectory = manifest.SourceDirectory, ExternalSourceDirectory = manifest.ExternalSourceDirectory,
                Difficulties = entries.Select(e => new WorkspaceDifficulty { Id = e.Id, Name = e.Name, File = e.File,
                    Source = e.Source, SourceHash = e.SourceHash, ExportTarget = e.ExportTarget, ExportHash = e.ExportHash, SyncFile = e.SyncFile }).ToList() };
            AtomicFile.Write(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(persisted, json));
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Move(directory, previous);
            try { System.IO.Directory.Move(staging, directory); }
            catch { if (System.IO.Directory.Exists(previous)) System.IO.Directory.Move(previous, directory); throw; }
            session.Manifest.Name = project.Name; session.Manifest.Difficulties = entries;
            if (System.IO.Directory.Exists(previous)) System.IO.Directory.Delete(previous, true);
        }
        finally { if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true); }
    }

    public static void Recover(string directory) { lock (Gate) { RecoverLocked(directory); } }
    private static void RecoverLocked(string directory)
    {
        string previous = directory + ".previous";
        RejectLinks(directory); RejectLinks(previous);
        if (!System.IO.Directory.Exists(previous)) return;
        if (!System.IO.Directory.Exists(directory)) System.IO.Directory.Move(previous, directory);
        else if (System.IO.File.Exists(Path.Combine(directory, ManifestName))) System.IO.Directory.Delete(previous, true);
    }

    public static IReadOnlyList<string> MissingResources(BeatmapProject project)
        => ResourceReferences(project).FindMissing();

    public static WorkspaceResourceReferences ResourceReferences(BeatmapProject project)
    {
        var paths = new HashSet<string>();
        foreach (var diff in project.Difficulties)
        {
            var d = diff.Document;
            Check(d.SourcePath); Check(d.AudioPath);
        }
        // Optional presentation and sample assets may be intentionally absent from a beatmap download.
        return new(paths.ToArray());
        void Check(string? path) { if (!string.IsNullOrWhiteSpace(path)) paths.Add(path); }
    }
    internal static string[] Csv(string line)
    {
        var fields = new List<string>(); var value = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append(c); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(value.ToString().Trim()); value.Clear(); }
            else value.Append(c);
        }
        fields.Add(value.ToString().Trim()); return fields.ToArray();
    }

}
