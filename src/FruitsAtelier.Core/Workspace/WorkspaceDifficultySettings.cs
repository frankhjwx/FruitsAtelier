using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class WorkspaceDifficultySettings
{
    public static WorkspaceSession SetFaOnly(WorkspaceSession session, BeatmapProject project, Guid id, bool faOnly,
        bool deleteOsu = false, string? expectedHash = null)
    {
        lock (WorkspaceProject.Gate)
        {
            var entry = session.Manifest.Difficulties.Single(d => d.Id == id);
            var difficulty = project.Difficulties.Single(d => d.Id == id);
            var previous = WorkspaceProject.SnapshotManifest(session.Manifest);
            bool previousFaOnly = difficulty.FaOnly;
            string? source = WorkspaceSynchronization.Target(entry);
            if (deleteOsu)
            {
                if (!faOnly || source is null || !Path.GetExtension(source).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.Get("project.invalid"));
                WorkspaceProject.RejectLinks(source);
                WorkspaceAssociations.EnsureOwner(session, id, source);
                if (expectedHash is null || !File.Exists(source) || WorkspaceProject.Hash(source) != expectedHash)
                    throw new IOException(L.Get("library.exportConflict", source));
            }
            string backup = WorkspaceSynchronization.Archive(session, "fa-settings");
            WorkspaceHistoryFile.WriteProject(project, Path.Combine(backup, "current.catchproj"));
            if (deleteOsu)
            {
                string copy = Path.Combine(backup, "external.osu");
                WorkspaceHistoryFile.Write(copy, File.ReadAllBytes(source!));
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(WorkspaceHistoryFile.Read(copy))) != expectedHash)
                    throw new IOException(L.Get("library.exportConflict", source));
            }
            entry.FaOnly = difficulty.FaOnly = faOnly;
            if (deleteOsu)
            {
                entry.Source = entry.SourceHash = entry.ExportTarget = entry.ExportHash = null;
                entry.ExportConfirmed = false;
            }
            bool published = false;
            try
            {
                // Publish FA-only state before removing its external file so an
                // interrupted deletion cannot cause the missing file to be restored by sync.
                WorkspaceProject.Save(session, project, archiveBeforeSave: false);
                published = true;
                if (deleteOsu)
                {
                    if (WorkspaceProject.Hash(source!) != expectedHash) throw new IOException(L.Get("library.exportConflict", source));
                    File.Delete(source!);
                }
            }
            catch
            {
                difficulty.FaOnly = previousFaOnly;
                session.Manifest.Difficulties = previous.Difficulties;
                if (published) WorkspaceProject.Save(session with { Manifest = previous }, project, archiveBeforeSave: false);
                throw;
            }
            return session with { Project = project };
        }
    }
}
