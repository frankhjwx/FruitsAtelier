using System.Globalization;
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
                if (File.Exists(path)) versions.Add(new(path, time, name[23..], file == "current.catchproj"));
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
        return ProjectSerializer.ReadProjectFile(version.Path);
    }

    public static void ArchiveCurrent(WorkspaceSession session, BeatmapProject project)
    {
        lock (WorkspaceProject.Gate)
        {
            string directory = WorkspaceSynchronization.Archive(session, "restore");
            ProjectSerializer.WriteFile(project, System.IO.Path.Combine(directory, "current.catchproj"));
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
