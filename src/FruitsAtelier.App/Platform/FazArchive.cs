using System.IO.Compression;
using System.Text.Json;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

public sealed class FazManifest
{
    public string Format { get; set; } = "FruitsAtelier";
    public int Version { get; set; } = 1;
    public bool Complete { get; set; }
    public Dictionary<string, string> Files { get; set; } = [];
    public Dictionary<Guid, string> SourceHashes { get; set; } = [];
    public Dictionary<Guid, string> ResourceRoots { get; set; } = [];
}

public sealed record FazProjectCandidate(LibraryMap Map, string[] Targets, int DifficultyCount);

public sealed record FazAssociations(IReadOnlyList<LibraryMap>[] Candidates, Dictionary<string, string> Hashes, Dictionary<string, string> Owners, List<FazProjectCandidate> Projects);

public sealed class FazPackage : IDisposable
{
    public required string Directory { get; init; }
    public required string Fingerprint { get; init; }
    public required FazManifest Manifest { get; init; }
    public required BeatmapProject Project { get; init; }
    public IReadOnlyList<LibraryMap>[] Candidates { get; set; } = [];
    public Dictionary<string, string> CandidateHashes { get; set; } = [];
    public Dictionary<string, string> Owners { get; set; } = [];
    public List<FazProjectCandidate> Projects { get; set; } = [];
    public string? ExistingProject { get; init; }
    public void Dispose() => FazStaging.Release(Directory);
}

public static class FazArchive
{
    private static readonly object ImportGate = new();
    private const long Limit = 1024L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static bool IsPackage(string path) => Path.GetExtension(path).Equals(".faz", StringComparison.OrdinalIgnoreCase);
    private static string Temporary(string workspace) => FazStaging.Create(workspace);
    public static void CleanupTemporaryFiles(string workspace) => FazStaging.Cleanup(workspace);

    public static void Export(BeatmapProject snapshot, string destination, bool complete, bool compensate, string workspace)
        => ExportPackage(snapshot, destination, complete, compensate, workspace, "FruitsAtelier");

    internal static void ExportPackage(BeatmapProject snapshot, string destination, bool complete, bool compensate, string workspace, string format)
    {
        string staging = Temporary(workspace);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var project = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(snapshot));
            var manifest = new FazManifest { Complete = complete, Format = format };
            var resourceGroups = new Dictionary<string, string>(StringComparer.Ordinal);
            if (complete) LibraryOperations.ExportOsz(snapshot, Path.Combine(staging, "beatmap.osz"), compensate);
            foreach (var difficulty in project.Difficulties)
            {
                var d = difficulty.Document;
                string group = (d.SourcePath is null ? "" : Path.GetDirectoryName(d.SourcePath)) + "\0" + d.AudioPath + "\0" + OsuBeatmapReader.Setting(d, "General", "AudioFilename");
                bool first = !resourceGroups.TryGetValue(group, out var relative);
                if (first) resourceGroups[group] = relative = complete ? "song" : "resources/" + difficulty.Id.ToString("N");
                manifest.ResourceRoots[difficulty.Id] = relative!;
                string resources = Path.Combine(staging, relative!);
                if (!complete && first)
                {
                    Directory.CreateDirectory(resources);
                    BeatmapResources.Copy(d, resources, d);
                    LibraryOperations.CopyArchiveExtras(d, resources);
                    File.WriteAllText(Path.Combine(resources, ".fa-resource"), "FruitsAtelier resource root");
                }
                string? audio = OsuBeatmapReader.Setting(d, "General", "AudioFilename");
                if (!string.IsNullOrWhiteSpace(d.AudioPath))
                {
                    if (string.IsNullOrWhiteSpace(audio) || !File.Exists(d.AudioPath)) throw new IOException(L.Get("faz.missingAudio"));
                    d.AudioPath = BeatmapArchive.Within(resources, BeatmapArchive.ValidateRelativePath(audio));
                }
                d.SourcePath = Path.Combine(resources, complete ? WorkspaceProject.DifficultyFileName(d, difficulty.Name, ".osu") : ".fa-resource");
            }
            // Package references use slash separators on every host; workspace saves use native paths.
            foreach (var diff in project.Difficulties)
            {
                diff.Document.SourcePath = Path.GetRelativePath(staging, diff.Document.SourcePath!).Replace('\\', '/');
                if (diff.Document.AudioPath is { } audio) diff.Document.AudioPath = Path.GetRelativePath(staging, audio).Replace('\\', '/');
            }
            ProjectSerializer.WriteFile(project, Path.Combine(staging, "project.catchproj"));
            foreach (var d in snapshot.Difficulties)
                if (d.Document.SourcePath is { } original && File.Exists(original)) manifest.SourceHashes[d.Id] = WorkspaceProject.Hash(original);
            foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                manifest.Files.Add(Path.GetRelativePath(staging, file).Replace('\\', '/'), WorkspaceProject.Hash(file));
            WriteAtomic(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
            CheckSize(staging);
            WorkspaceProject.RejectLinks(destination);
            ZipFile.CreateFromDirectory(staging, temporary, CompressionLevel.Optimal, false);
            if (new FileInfo(temporary).Length > Limit) throw new InvalidDataException(L.Get("archive.expansionLimit"));
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); FazStaging.Release(staging); }
    }

    public static FazPackage Prepare(string path, LibrarySettings settings, bool discoverAssociations = true)
        => PreparePackage(path, settings, discoverAssociations, "FruitsAtelier");

    internal static FazPackage PreparePackage(string path, LibrarySettings settings, bool discoverAssociations, string format)
    {
        Recover(settings);
        string staging = Temporary(settings.Workspace);
        try
        {
            WorkspaceProject.RejectLinks(path);
            string fingerprint = WorkspaceProject.Hash(path);
            long extracted = 0; int count = 0;
            Extract(path, staging, ref extracted, ref count);
            string manifestPath = Path.Combine(staging, "manifest.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 8 * 1024 * 1024) throw Invalid();
            var manifest = JsonSerializer.Deserialize<FazManifest>(File.ReadAllText(manifestPath), Json) ?? throw Invalid();
            if (manifest.Format != format || manifest.Version != 1 || manifest.Files is null || manifest.SourceHashes is null || manifest.ResourceRoots is null) throw Invalid();
            var files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(staging, p).Replace('\\', '/')).Where(p => p != "manifest.json").ToArray();
            if (files.Length != manifest.Files.Count || files.Any(p => !manifest.Files.ContainsKey(p))) throw Invalid();
            foreach (var (name, hash) in manifest.Files)
                if (WorkspaceProject.Hash(BeatmapArchive.Within(staging, BeatmapArchive.ValidateRelativePath(name))) != hash) throw Invalid();
            if (manifest.Complete) Extract(Path.Combine(staging, "beatmap.osz"), Path.Combine(staging, "song"), ref extracted, ref count);
            var project = ProjectSerializer.ReadProjectFile(Path.Combine(staging, "project.catchproj"));
            foreach (var diff in project.Difficulties)
            {
                var d = diff.Document;
                if (!manifest.ResourceRoots.TryGetValue(diff.Id, out var relative)) throw Invalid();
                if (manifest.Complete ? relative != "song" : !relative.StartsWith("resources/", StringComparison.Ordinal)) throw Invalid();
                string root = BeatmapArchive.Within(staging, BeatmapArchive.ValidateRelativePath(relative));
                if (d.SourcePath is null || !WorkspaceProject.Within(root, d.SourcePath) || !File.Exists(d.SourcePath)
                    || !WorkspaceSynchronization.Paths.Equals(Path.GetDirectoryName(d.SourcePath), root)
                    || d.AudioPath is { } a && (!WorkspaceProject.Within(root, a) || !File.Exists(a))) throw Invalid();
            }
            string? existing = Directory.EnumerateDirectories(settings.Workspace)
                .FirstOrDefault(p => File.Exists(Path.Combine(p, WorkspaceProject.ManifestName))
                    && WorkspaceProject.ReadManifest(p, includeSync: false).ArchiveFingerprint == fingerprint);
            var package = new FazPackage { Directory = staging, Fingerprint = fingerprint, Manifest = manifest, Project = project, ExistingProject = existing };
            if (discoverAssociations) ApplyAssociations(package, FindAssociations(package, settings, refreshLibrary: true));
            return package;
        }
        catch { FazStaging.Release(staging); throw; }
    }

    public static FazAssociations FindAssociations(FazPackage package, LibrarySettings settings, bool refreshLibrary = false)
    {
        var db = new LibraryDatabase(settings.Workspace, settings.Songs);
        if (refreshLibrary) db.Scan();
        var candidates = package.Project.Difficulties.Select(d => db.AssociationCandidates(d.Document)).ToArray();
        var hashes = candidates.SelectMany(c => c).Select(c => c.Path).Distinct(WorkspaceSynchronization.Paths)
            .ToDictionary(p => p, WorkspaceProject.Hash, WorkspaceSynchronization.Paths);
        var owners = WorkspaceAssociations.Claims(settings.Workspace).GroupBy(c => c.Target, WorkspaceSynchronization.Paths)
            .ToDictionary(g => g.Key, g => g.First().Project, WorkspaceSynchronization.Paths);
        var projects = new List<FazProjectCandidate>();
        foreach (string folder in candidates.SelectMany(c => c).Select(c => c.Directory).Distinct(WorkspaceSynchronization.Paths))
            if (MatchProject(package, settings, folder) is { } match) projects.Add(match);
        return new(candidates, hashes, owners, projects);
    }

    public static void ApplyAssociations(FazPackage package, FazAssociations associations)
    {
        package.Candidates = associations.Candidates; package.CandidateHashes = associations.Hashes;
        package.Owners = associations.Owners; package.Projects = associations.Projects;
    }

    public static string? Recommend(ProjectDifficulty difficulty, IReadOnlyList<LibraryMap> candidates, string? sourceHash = null, IReadOnlyDictionary<string, string>? hashes = null)
    {
        long.TryParse(OsuBeatmapReader.Setting(difficulty.Document, "Metadata", "BeatmapID"), out long id);
        var exact = candidates.Where(c => id > 0 && c.BeatmapId == id).ToArray();
        if (exact.Length == 0) exact = candidates.Where(c => c.Difficulty == difficulty.Name).ToArray();
        if (exact.Length > 1 && sourceHash is not null && hashes is not null)
        {
            var same = exact.Where(c => hashes.GetValueOrDefault(c.Path) == sourceHash).ToArray();
            if (same.Length == 1) return same[0].Path;
        }
        return exact.Length == 1 ? exact[0].Path : null;
    }

    public static WorkspaceSession Import(FazPackage package, LibrarySettings settings, IReadOnlyList<string?> targets, bool installSongs, bool compensate)
    {
        lock (ImportGate) return ImportLocked(package, settings, targets, installSongs, compensate);
    }

    public static FazProjectCandidate? MatchProject(FazPackage package, LibrarySettings settings, string folder)
    {
        folder = Path.GetFullPath(folder);
        if (string.IsNullOrWhiteSpace(settings.Songs) || !WorkspaceProject.Within(settings.Songs, folder) || !Directory.Exists(folder)) return null;
        WorkspaceProject.RejectLinks(folder);
        var maps = Directory.EnumerateFiles(folder, "*.osu").Select(LibraryDatabase.ReadMetadata).OfType<LibraryMap>().ToArray();
        var hashes = maps.ToDictionary(m => m.Path, m => WorkspaceProject.Hash(m.Path), WorkspaceSynchronization.Paths);
        var targets = package.Project.Difficulties.Select(d =>
        {
            string Get(string key) => OsuBeatmapReader.Setting(d.Document, "Metadata", key) ?? "";
            long.TryParse(Get("BeatmapID"), out long id);
            long.TryParse(Get("BeatmapSetID"), out long setId);
            var eligible = maps.Where(m => id > 0 && m.BeatmapId == id || setId > 0 && m.BeatmapSetId == setId
                || Get("Artist").Length > 0 && Get("Title").Length > 0 && m.Artist == Get("Artist") && m.Title == Get("Title")).ToArray();
            return Recommend(d, eligible, package.Manifest.SourceHashes.GetValueOrDefault(d.Id), hashes);
        }).ToArray();
        if (targets.Any(t => t is null) || targets.Distinct(WorkspaceSynchronization.Paths).Count() != targets.Length) return null;
        return new(maps.First(m => WorkspaceSynchronization.Paths.Equals(m.Path, targets[0])), targets.Select(t => t!).ToArray(), maps.Length);
    }

    private static WorkspaceSession ImportLocked(FazPackage package, LibrarySettings settings, IReadOnlyList<string?> targets, bool installSongs, bool compensate)
    {
        if (targets.Count != package.Project.Difficulties.Count || installSongs && !package.Manifest.Complete) throw Invalid();
        if (targets.Any(t => t is not null))
        {
            if (installSongs || targets.Any(t => t is null)) throw new IOException(L.Get("faz.incompleteProject"));
            string folder = Path.GetDirectoryName(Path.GetFullPath(targets[0]!))!;
            var match = MatchProject(package, settings, folder);
            if (match is null || !targets.Select(t => Path.GetFullPath(t!)).SequenceEqual(match.Targets, WorkspaceSynchronization.Paths))
                throw new IOException(L.Get("faz.incompleteProject"));
        }
        WorkspaceProject.ValidateRoots(settings.Workspace, settings.Songs, installSongs);
        if (installSongs && string.IsNullOrWhiteSpace(settings.Songs)) throw new IOException(L.Get("library.bindForExport"));
        var project = ProjectSerializer.ReadProject(ProjectSerializer.Serialize(package.Project));
        var manifest = new WorkspaceManifest { Name = project.Name, Notes = project.Notes, SongsRoot = settings.Songs, SongsExportOfferShown = true, ArchiveFingerprint = package.Fingerprint };
        string directory = Path.Combine(settings.Workspace, WorkspaceProject.SafeName(project.Name) + " [" + manifest.Id.ToString("N")[..8] + "]");
        string resources = Path.Combine(settings.Workspace, "Resources", "FAZ-" + manifest.Id.ToString("N"));
        string? song = installSongs ? Path.Combine(settings.Songs, WorkspaceProject.SafeName(project.Name) + " [FA " + manifest.Id.ToString("N")[..8] + "]") : null;
        var session = new WorkspaceSession(directory, manifest, project);
        var associations = new List<(WorkspaceDifficulty Entry, WorkspaceSyncCandidate Candidate)>();
        var installedResources = new Dictionary<string, string>(WorkspaceSynchronization.Paths);
        var used = new HashSet<string>(WorkspaceSynchronization.Paths);
        var claims = WorkspaceAssociations.Claims(settings.Workspace);
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] is not { } target) continue;
            target = Path.GetFullPath(target);
            if (string.IsNullOrWhiteSpace(settings.Songs) || !WorkspaceProject.Within(settings.Songs, target) || !target.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Get("faz.songsOnly"));
            WorkspaceProject.RejectLinks(target);
            if (!used.Add(target) || claims.Any(c => WorkspaceSynchronization.Paths.Equals(c.Target, target))) throw new IOException(L.Get("sync.duplicate", target));
            var candidate = WorkspaceSynchronization.ReadStable(target);
            if (candidate.Document.AudioPath is not { } a || !File.Exists(a)) throw new IOException(L.Get("faz.missingAudio"));
        }
        if (Directory.Exists(directory) || Directory.Exists(resources) || song is not null && Directory.Exists(song)) throw Invalid();
        string journalPath = Path.Combine(settings.Workspace, ".faz-import-" + manifest.Id.ToString("N") + ".json");
        var journal = new ImportJournal(directory, resources, song, false);
        WriteAtomic(journalPath, JsonSerializer.Serialize(journal, Json));
        try
        {
            if (song is not null) CopyTree(Path.Combine(package.Directory, "song"), song);
            for (int i = 0; i < project.Difficulties.Count; i++)
            {
                var diff = project.Difficulties[i]; var d = diff.Document;
                string oldRoot = Path.GetDirectoryName(d.SourcePath!)!;
                string? target = targets[i];
                if (target is null && song is not null) target = Path.Combine(song, Path.GetRelativePath(Path.Combine(package.Directory, "song"), d.SourcePath!));
                diff.Id = Guid.NewGuid();
                var entry = new WorkspaceDifficulty { Id = diff.Id, Name = diff.Name };
                manifest.Difficulties.Add(entry);
                if (target is not null)
                {
                    var external = WorkspaceSynchronization.ReadStable(target);
                    d.SourcePath = external.Path; d.AudioPath = external.Document.AudioPath;
                    RebaseBackground(d, external.Document);
                    SongSetup.Set(d, "General", "AudioFilename", OsuBeatmapReader.Setting(external.Document, "General", "AudioFilename") ?? "");
                    entry.Source = external.Path; entry.SourceHash = external.Hash;
                    entry.ExportTarget = external.Path; entry.ExportHash = external.Hash; entry.ExportConfirmed = true;
                    // No inherited baseline: differing authoring is reviewed before any external publication.
                    associations.Add((entry, external));
                }
                else
                {
                    if (!installedResources.TryGetValue(oldRoot, out string? local))
                    {
                        local = Path.Combine(resources, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        CopyTree(oldRoot, local); installedResources.Add(oldRoot, local);
                    }
                    if (d.AudioPath is not null) d.AudioPath = Path.Combine(local, Path.GetRelativePath(oldRoot, d.AudioPath));
                    d.SourcePath = Path.Combine(local, Path.GetFileName(d.SourcePath!));
                }
            }
            foreach (var (entry, external) in associations)
            {
                if (WorkspaceProject.Hash(external.Path) != external.Hash) throw new IOException(L.Get("library.exportConflict", external.Path));
                WorkspaceAssociations.EnsureOwner(session, entry.Id, external.Path);
                if (song is not null && WorkspaceProject.Within(song, external.Path))
                {
                    var document = project.Difficulties.Single(d => d.Id == entry.Id).Document;
                    SongSetup.Set(document, "Metadata", "BeatmapID", "0");
                    WorkspaceSynchronization.Accept(session, entry, external, document, compensate);
                }
            }
            WorkspaceProject.Save(session, project, archiveBeforeSave: false);
            WriteAtomic(journalPath, JsonSerializer.Serialize(journal with { Completed = true }, Json));
            new LibraryDatabase(settings.Workspace, settings.Songs).ReindexProjects();
            File.Delete(journalPath);
            return session;
        }
        catch { Recover(settings); throw; }
    }

    private sealed record ImportJournal(string Project, string Resources, string? Song, bool Completed);
    private static void RebaseBackground(MapDocument local, MapDocument external)
    {
        var target = external.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines)
            .FirstOrDefault(l => l.TrimStart().StartsWith("0,", StringComparison.Ordinal));
        if (target is null) return;
        var events = local.OriginalSections.FirstOrDefault(s => s.Name == "Events");
        if (events is null) return;
        int index = events.Lines.ToList().FindIndex(l => l.TrimStart().StartsWith("0,", StringComparison.Ordinal));
        if (index >= 0) events.Lines[index] = target;
    }
    public static void Recover(LibrarySettings settings)
    {
        CleanupTemporaryFiles(settings.Workspace);
        lock (ImportGate) RecoverLocked(settings);
    }
    private static void RecoverLocked(LibrarySettings settings)
    {
        if (!Directory.Exists(settings.Workspace)) return;
        foreach (string file in Directory.EnumerateFiles(settings.Workspace, ".faz-import-*.json"))
        {
            WorkspaceProject.RejectLinks(file);
            var journal = JsonSerializer.Deserialize<ImportJournal>(File.ReadAllText(file), Json) ?? throw Invalid();
            string id = Path.GetFileNameWithoutExtension(file)[".faz-import-".Length..];
            if (!Guid.TryParseExact(id, "N", out _) || Path.GetDirectoryName(journal.Project) != Path.GetFullPath(settings.Workspace)
                || !Path.GetFileName(journal.Project).EndsWith(" [" + id[..8] + "]", StringComparison.Ordinal)
                || journal.Resources != Path.Combine(settings.Workspace, "Resources", "FAZ-" + id)
                || journal.Song is not null && (Path.GetDirectoryName(journal.Song) != Path.GetFullPath(settings.Songs)
                    || !Path.GetFileName(journal.Song).EndsWith(" [FA " + id[..8] + "]", StringComparison.Ordinal))) throw Invalid();
            bool published = File.Exists(Path.Combine(journal.Project, WorkspaceProject.ManifestName));
            if (!journal.Completed && !published)
            {
                RemoveDirectory(journal.Project, settings.Workspace);
                RemoveDirectory(journal.Project + ".saving", settings.Workspace);
                RemoveDirectory(journal.Resources, Path.Combine(settings.Workspace, "Resources"));
                if (journal.Song is not null)
                {
                    if (string.IsNullOrWhiteSpace(settings.Songs)) throw Invalid();
                    RemoveDirectory(journal.Song, settings.Songs);
                }
            }
            File.Delete(file);
        }
    }

    private static void Extract(string source, string destination, ref long total, ref int count)
    {
        if (new FileInfo(source).Length > Limit) throw Invalid();
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(source);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            long maximum = entry.FullName == "beatmap.osz" ? Limit : ProjectSerializer.MaximumFileBytes;
            if (++count > 20000 || entry.Length > maximum || (total += entry.Length) > Limit) throw Invalid();
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0) throw Invalid();
            string name = BeatmapArchive.ValidateRelativePath(entry.FullName.TrimEnd('/'));
            if (!names.Add(name)) throw Invalid();
            string target = BeatmapArchive.Within(destination, name);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew);
            byte[] buffer = new byte[65536]; long written = 0; int n;
            while ((n = input.Read(buffer)) > 0) { written += n; if (written > entry.Length) throw Invalid(); output.Write(buffer, 0, n); }
            if (written != entry.Length) throw Invalid();
        }
    }
    private static void CheckSize(string root)
    {
        long total = 0; int count = 0;
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            long maximum = Path.GetFileName(file) == "beatmap.osz" ? Limit : ProjectSerializer.MaximumFileBytes;
            if (++count > 20000 || new FileInfo(file).Length > maximum || (total += new FileInfo(file).Length) > Limit) throw Invalid();
        }
    }
    private static void CopyTree(string source, string destination)
    {
        WorkspaceProject.RejectLinks(source); WorkspaceProject.RejectLinks(destination);
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source)) { WorkspaceProject.RejectLinks(file); File.Copy(file, Path.Combine(destination, Path.GetFileName(file))); }
        foreach (string child in Directory.EnumerateDirectories(source)) CopyTree(child, Path.Combine(destination, Path.GetFileName(child)));
    }
    internal static void RemoveDirectory(string path, string parent)
    {
        if (!WorkspaceProject.Within(parent, path) || Path.GetFullPath(path) == Path.GetFullPath(parent)) throw Invalid();
        WorkspaceProject.RejectLinks(path);
        if (!Directory.Exists(path)) return;
        foreach (string file in Directory.EnumerateFiles(path)) { WorkspaceProject.RejectLinks(file); File.Delete(file); }
        foreach (string child in Directory.EnumerateDirectories(path)) RemoveDirectory(child, path);
        Directory.Delete(path);
    }
    private static void WriteAtomic(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text)); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static InvalidDataException Invalid() => new(L.Get("faz.invalid"));
}
