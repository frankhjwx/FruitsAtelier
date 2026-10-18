using System.Globalization;
using System.Text.Json;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record WorkspaceVersion(string Path, DateTime TimeUtc, string Operation, bool WorkingCopy);

public static class WorkspaceVersionHistory
{
    public static IReadOnlyList<WorkspaceVersion> List(WorkspaceSession session)
    {
        string root = HistoryRoot(session);
        WorkspaceProject.RejectLinks(root);
        if (!Directory.Exists(root)) return [];
        var versions = new List<WorkspaceVersion>();
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string name = System.IO.Path.GetFileName(directory);
            if (name.Length < 24 || !DateTime.TryParseExact(name[..22], "yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)) continue;
            WorkspaceProject.RejectLinks(directory);
            foreach (string file in new[] { "current.catchproj", "saved.catchproj" })
            {
                string path = System.IO.Path.Combine(directory, file);
                if (WorkspaceHistoryFile.Exists(path) || File.Exists(System.IO.Path.Combine(directory, file == "current.catchproj" ? "current.snapshot" : "saved.snapshot")))
                    versions.Add(new(path, time, name[23..], file == "current.catchproj"));
            }
        }
        return versions.OrderByDescending(v => v.TimeUtc).ThenByDescending(v => v.WorkingCopy).ToArray();
    }

    public static BeatmapProject Read(WorkspaceSession session, WorkspaceVersion version)
    {
        if (!WorkspaceProject.Within(HistoryRoot(session), version.Path)
            || System.IO.Path.GetFileName(version.Path) is not ("current.catchproj" or "saved.catchproj"))
            throw new InvalidDataException(L.Get("project.invalid"));
        WorkspaceProject.RejectLinks(version.Path);
        if (WorkspaceHistoryFile.Exists(version.Path)) return ProjectSerializer.ReadProject(WorkspaceHistoryFile.ReadText(version.Path), version.Path);
        string directory = System.IO.Path.GetDirectoryName(version.Path)!;
        string marker = System.IO.Path.Combine(directory, version.WorkingCopy ? "current.snapshot" : "saved.snapshot");
        WorkspaceProject.RejectLinks(marker);
        string origin = File.ReadAllText(marker);
        string manifestPath = System.IO.Path.Combine(directory, version.WorkingCopy ? "current" : "", WorkspaceProject.ManifestName);
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(WorkspaceHistoryFile.ReadText(manifestPath))
            ?? throw new InvalidDataException(L.Get("project.invalid"));
        var project = new BeatmapProject { Name = manifest.Name, Notes = manifest.Notes };
        foreach (var entry in manifest.Difficulties)
        {
            if (System.IO.Path.GetFileName(entry.File) != entry.File) throw new InvalidDataException(L.Get("project.invalid"));
            string working = System.IO.Path.Combine(directory, "current", entry.File);
            string source = version.WorkingCopy && WorkspaceHistoryFile.Exists(working) ? working : System.IO.Path.Combine(directory, entry.File);
            project.Difficulties.Add(new() { Id = entry.Id, Name = entry.Name,
                Document = ProjectSerializer.Read(WorkspaceHistoryFile.ReadText(source), System.IO.Path.Combine(origin, entry.File)) });
        }
        project.Validate();
        return project;
    }

    internal static string ReferenceDirectory(string path)
    {
        string directory = System.IO.Path.GetDirectoryName(WorkspaceHistoryFile.LogicalPath(path))!;
        string marker = System.IO.Path.Combine(directory, "saved.snapshot");
        if (!File.Exists(marker)) marker = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory)!, "current.snapshot");
        if (!File.Exists(marker)) return directory;
        WorkspaceProject.RejectLinks(marker);
        return File.ReadAllText(marker);
    }

    public static void ArchiveCurrentDifficulty(WorkspaceSession session, BeatmapProject project, Guid id)
    {
        lock (WorkspaceProject.Gate)
        {
            string directory = WorkspaceSynchronization.Archive(session, "before-sync", deferProject: true);
            string working = System.IO.Path.Combine(directory, "current");
            Directory.CreateDirectory(working);
            var manifest = WorkspaceProject.SnapshotManifest(session.Manifest);
            var entry = manifest.Difficulties.Single(d => d.Id == id);
            var difficulty = project.Difficulties.Single(d => d.Id == id);
            entry.Name = difficulty.Name;
            foreach (var item in manifest.Difficulties) item.Sync = null;
            AtomicFile.Write(System.IO.Path.Combine(working, entry.File), ProjectSerializer.Serialize(difficulty.Document, System.IO.Path.Combine(session.Directory, entry.File)));
            AtomicFile.Write(System.IO.Path.Combine(working, WorkspaceProject.ManifestName), JsonSerializer.Serialize(manifest));
            AtomicFile.Write(System.IO.Path.Combine(directory, "current.snapshot"), System.IO.Path.GetFullPath(session.Directory));
        }
    }

    public static void ArchiveCurrent(WorkspaceSession session, BeatmapProject project)
    {
        lock (WorkspaceProject.Gate)
        {
            string directory = WorkspaceSynchronization.Archive(session, "restore");
            WorkspaceHistoryFile.WriteProject(project, System.IO.Path.Combine(directory, "current.catchproj"));
        }
    }

    internal static void ArchiveBeforeSave(WorkspaceSession session, BeatmapProject project)
    {
        if (!File.Exists(System.IO.Path.Combine(session.Directory, WorkspaceProject.ManifestName))) return;
        var manifest = WorkspaceProject.ReadManifest(session.Directory, includeSync: false);
        bool changed = manifest.Name != project.Name || manifest.Difficulties.Count != project.Difficulties.Count;
        if (!changed)
            for (int i = 0; i < project.Difficulties.Count; i++)
            {
                var current = project.Difficulties[i]; var saved = manifest.Difficulties[i];
                if (System.IO.Path.GetFileName(saved.File) != saved.File) throw new InvalidDataException(L.Get("project.invalid"));
                string path = System.IO.Path.Combine(session.Directory, saved.File);
                WorkspaceProject.RejectLinks(path);
                if (current.Id != saved.Id || current.Name != saved.Name || !current.Document.ContentEquals(ProjectSerializer.ReadFile(path)))
                { changed = true; break; }
            }
        if (changed) WorkspaceSynchronization.Archive(session, "save");
    }

    private static string HistoryRoot(WorkspaceSession session) => System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(session.Directory)!, ".sync-history", session.Manifest.Id.ToString("N"));
}
