using System.Text.Json;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record WorkspaceClaim(string Project, Guid DifficultyId, string Name, string Target, string? AuthoringFile = null);

public static class WorkspaceAssociations
{
    public static string? FindProject(string workspace, string path)
    {
        var candidate = WorkspaceSynchronization.ReadStable(path);
        foreach (var group in Claims(workspace).GroupBy(c => c.Project))
        {
            var manifest = WorkspaceProject.ReadManifest(group.Key);
            if (group.Any(c => WorkspaceSynchronization.Paths.Equals(c.Target, path))) return group.Key;
            if (manifest.Difficulties.Any(d => d.Sync is { } baseline && !File.Exists(WorkspaceSynchronization.Target(d))
                && (WorkspaceSynchronization.ObjectSignature(baseline.Text) == WorkspaceSynchronization.ObjectSignature(candidate.Text)
                    || WorkspaceSynchronization.OnlineIdentity(baseline.Text) is { } identity && identity == WorkspaceSynchronization.OnlineIdentity(candidate.Text)))) return group.Key;
        }
        return null;
    }
    public static IReadOnlyList<WorkspaceClaim> Claims(string workspace)
    {
        lock (WorkspaceProject.Gate) return ClaimsLocked(workspace);
    }
    private static IReadOnlyList<WorkspaceClaim> ClaimsLocked(string workspace)
    {
        var result = new List<WorkspaceClaim>();
        if (!Directory.Exists(workspace)) return result;
        foreach (string directory in Directory.EnumerateDirectories(workspace))
        {
            if (!File.Exists(Path.Combine(directory, WorkspaceProject.ManifestName)) || directory.EndsWith(".previous") || directory.EndsWith(".saving")) continue;
            var manifest = WorkspaceProject.ReadManifest(directory, includeSync: false);
            foreach (var entry in manifest.Difficulties)
                if (WorkspaceSynchronization.Target(entry) is { } target)
                    result.Add(new(directory, entry.Id, entry.Name, Path.GetFullPath(target),
                        Path.GetFileName(entry.File) == entry.File ? Path.Combine(directory, entry.File) : null));
        }
        return result;
    }

    public static void EnsureOwner(WorkspaceSession session, Guid id, string path)
    {
        path = Path.GetFullPath(path);
        bool ownDuplicate = session.Manifest.Difficulties.Any(d => d.Id != id && WorkspaceSynchronization.Target(d) is { } target && WorkspaceSynchronization.Paths.Equals(target, path));
        bool otherDuplicate = Claims(Path.GetDirectoryName(session.Directory)!).Any(c =>
            !WorkspaceSynchronization.Paths.Equals(c.Project, session.Directory) && WorkspaceSynchronization.Paths.Equals(c.Target, path));
        if (ownDuplicate || otherDuplicate) throw new InvalidOperationException(L.Get("sync.duplicate", path));
    }

    public static void EnsureImport(WorkspaceSession? session, string workspace, string path)
    {
        if (Claims(workspace).Any(c => WorkspaceSynchronization.Paths.Equals(c.Target, path))
            || session?.Manifest.Difficulties.Any(d => WorkspaceSynchronization.Paths.Equals(WorkspaceSynchronization.Target(d), path)) == true)
            throw new InvalidOperationException(L.Get("sync.duplicate", path));
    }

    public static void DeleteLocalProject(string directory)
    {
        lock (WorkspaceProject.Gate)
        {
            var session = WorkspaceProject.Open(directory);
            string backup = WorkspaceSynchronization.Archive(session, "delete-local-project");
            Directory.Move(session.Directory, Path.Combine(backup, "retired-project"));
        }
    }

    public static WorkspaceSession ReimportDifficulty(WorkspaceSession session, BeatmapProject project, Guid id, bool compensate)
    {
        lock (WorkspaceProject.Gate)
        {
            var status = WorkspaceSynchronization.Scan(session with { Project = project }, session.Manifest.SongsRoot ?? "").Difficulties.Single(d => d.DifficultyId == id);
            if (status.State is WorkspaceSyncState.Duplicate or WorkspaceSyncState.Ambiguous or WorkspaceSyncState.Unavailable
                || status.Candidate is not { } external) throw new InvalidOperationException(L.Get("sync.unresolved"));
            var entry = session.Manifest.Difficulties.Single(d => d.Id == id);
            EnsureOwner(session, id, external.Path);
            WorkspaceHistoryFile.WriteProject(project, Path.Combine(WorkspaceSynchronization.Archive(session, "reimport"), "current.catchproj"));
            var diff = project.Difficulties.Single(d => d.Id == id);
            diff.Document = external.Document.DeepClone();
            diff.Name = OsuBeatmapReader.Setting(diff.Document, "Metadata", "Version") ?? diff.Name;
            entry.Sync = null;
            WorkspaceSynchronization.Accept(session, entry, external, diff.Document, compensate);
            WorkspaceProject.Save(session, project);
            return WorkspaceProject.Open(session.Directory);
        }
    }

    public static void DeleteDifficulty(WorkspaceSession session, BeatmapProject project, Guid id)
    {
        lock (WorkspaceProject.Gate)
        {
            var entry = session.Manifest.Difficulties.Single(d => d.Id == id);
            string? path = WorkspaceSynchronization.Target(entry);
            if (path is not null && !Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Get("project.invalid"));
            if (path is not null) EnsureOwner(session, id, path);
            WorkspaceSyncCandidate? observed = null;
            if (path is not null)
            {
                var status = WorkspaceSynchronization.Scan(session with { Project = project }, session.Manifest.SongsRoot ?? "").Difficulties.Single(d => d.DifficultyId == id);
                if (status.State is WorkspaceSyncState.Duplicate or WorkspaceSyncState.Ambiguous or WorkspaceSyncState.Unavailable
                    || status.Candidate is { } candidate && !WorkspaceSynchronization.Paths.Equals(candidate.Path, path))
                    throw new InvalidOperationException(L.Get("sync.unresolved"));
                observed = status.Candidate;
            }
            string backup = WorkspaceSynchronization.Archive(session, "delete");
            WorkspaceHistoryFile.WriteProject(project, Path.Combine(backup, "current.catchproj"));
            string journal = Path.Combine(session.Directory, "delete.json");
            bool exists = path is not null && File.Exists(path);
            if (exists)
            {
                WorkspaceProject.RejectLinks(path!);
                WorkspaceHistoryFile.Write(Path.Combine(backup, "external.osu"), File.ReadAllBytes(path!));
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(WorkspaceHistoryFile.Read(Path.Combine(backup, "external.osu"))));
                if (observed is null || observed.Hash != hash) throw new IOException(L.Get("library.exportConflict", path));
                if (WorkspaceProject.Hash(path!) != hash) throw new IOException(L.Get("library.exportConflict", path));
                AtomicFile.Write(journal, JsonSerializer.Serialize(new Deletion(entry.Id, path!, backup, hash)));
                if (WorkspaceProject.Hash(path!) != hash) { File.Delete(journal); throw new IOException(L.Get("library.exportConflict", path)); }
                File.Delete(path!);
            }
            var remaining = new BeatmapProject { Name = project.Name };
            remaining.Difficulties.AddRange(project.Difficulties.Where(d => d.Id != id));
            try
            {
                if (remaining.Difficulties.Count == 0)
                {
                    Directory.Move(session.Directory, Path.Combine(backup, "retired-project"));
                    session.Manifest.Difficulties.Clear();
                }
                else WorkspaceProject.Save(session, remaining);
                project.Difficulties.RemoveAll(d => d.Id == id);
            }
            catch
            {
                if (exists && !File.Exists(path)) File.WriteAllBytes(path!, WorkspaceHistoryFile.Read(Path.Combine(backup, "external.osu")));
                File.Delete(journal);
                throw;
            }
        }
    }

    private sealed record Deletion(Guid DifficultyId, string Path, string Backup, string Hash);
    public static void RecoverDeletion(string directory)
    {
        string journal = Path.Combine(directory, "delete.json");
        if (!File.Exists(journal)) return;
        var deletion = JsonSerializer.Deserialize<Deletion>(File.ReadAllText(journal)) ?? throw new InvalidDataException(L.Get("project.invalid"));
        string workspace = Path.GetDirectoryName(directory)!;
        if (!WorkspaceProject.Within(Path.Combine(workspace, ".sync-history"), deletion.Backup)) throw new InvalidDataException(L.Get("project.invalid"));
        var manifest = WorkspaceProject.ReadManifest(directory);
        if (manifest.Difficulties.Any(d => d.Id == deletion.DifficultyId && WorkspaceSynchronization.Paths.Equals(WorkspaceSynchronization.Target(d), deletion.Path)))
        {
            WorkspaceProject.RejectLinks(deletion.Path);
            string source = Path.Combine(deletion.Backup, "external.osu");
            byte[] bytes = WorkspaceHistoryFile.Read(source);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) != deletion.Hash) throw new InvalidDataException(L.Get("project.invalid"));
            if (!File.Exists(deletion.Path)) File.WriteAllBytes(deletion.Path, bytes);
        }
        File.Delete(journal);
    }

    public static WorkspaceSession KeepOnly(WorkspaceSession current, BeatmapProject currentProject, string target, WorkspaceClaim keep)
    {
        lock (WorkspaceProject.Gate)
        {
            WorkspaceProject.Save(current, currentProject);
            var claims = Claims(Path.GetDirectoryName(current.Directory)!).Where(c => WorkspaceSynchronization.Paths.Equals(Path.GetDirectoryName(c.Target), Path.GetDirectoryName(target))).ToArray();
            if (!claims.Any(c => WorkspaceSynchronization.Paths.Equals(c.Project, keep.Project) && c.DifficultyId == keep.DifficultyId))
                throw new InvalidOperationException(L.Get("sync.unresolved"));
            var winner = WorkspaceProject.Open(keep.Project);
            WorkspaceSynchronization.Archive(winner, "deduplicate");
            var redundant = winner.Manifest.Difficulties.Where(d => d.Id != keep.DifficultyId
                && WorkspaceSynchronization.Paths.Equals(WorkspaceSynchronization.Target(d), keep.Target)).Select(d => d.Id).ToHashSet();
            winner.Project.Difficulties.RemoveAll(d => redundant.Contains(d.Id));
            winner.Manifest.Difficulties.RemoveAll(d => redundant.Contains(d.Id));
            var retired = new List<WorkspaceSession>();
            var targets = winner.Manifest.Difficulties.Select(WorkspaceSynchronization.Target).OfType<string>().ToHashSet(WorkspaceSynchronization.Paths);
            foreach (string directory in claims.Select(c => c.Project).Distinct(WorkspaceSynchronization.Paths))
            {
                if (WorkspaceSynchronization.Paths.Equals(directory, winner.Directory)) continue;
                var session = WorkspaceProject.Open(directory);
                WorkspaceSynchronization.Archive(session, "deduplicate");
                foreach (var diff in session.Project.Difficulties)
                {
                    var entry = session.Manifest.Difficulties.Single(d => d.Id == diff.Id);
                    if (WorkspaceSynchronization.Target(entry) is { } source && !targets.Add(source)) continue;
                    if (winner.Project.Difficulties.Any(d => d.Id == diff.Id)) throw new InvalidOperationException(L.Get("sync.unresolved"));
                    if (entry.Sync is { } baseline)
                    {
                        var authoring = ProjectSerializer.Read(baseline.Authoring, Path.Combine(session.Directory, "sync.catchdiff"));
                        baseline.Authoring = ProjectSerializer.Serialize(authoring, Path.Combine(winner.Directory, "sync.catchdiff"));
                    }
                    winner.Project.Difficulties.Add(diff); winner.Manifest.Difficulties.Add(entry);
                }
                retired.Add(session);
            }
            winner.Project.Validate();
            WorkspaceProject.Save(winner, winner.Project);
            // Publish the complete winner before retiring any other container. An interruption leaves recoverable duplicates.
            foreach (var session in retired)
            {
                string backup = WorkspaceSynchronization.Archive(session, "retired");
                Directory.Move(session.Directory, Path.Combine(backup, "retired-project"));
            }
            return winner;
        }
    }
}
