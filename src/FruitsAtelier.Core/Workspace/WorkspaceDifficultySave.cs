using System.Text.Json;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static partial class WorkspaceProject
{
    private sealed record DifficultyPublication(string[] Files, string[] Existing);

    // session.Project supplies the last published snapshots for unselected difficulties.
    public static WorkspaceSession SaveDifficulty(WorkspaceSession session, Guid id, MapDocument document, string name,
        bool archiveBeforeSave = true)
    {
        lock (Gate)
        {
            Recover(session.Directory);
            OsuBeatmapReader.Validate(document);
            var project = new BeatmapProject
            {
                Name = session.Project.Name, Notes = session.Project.Notes,
                Difficulties = session.Project.Difficulties.Select(d => d.Id == id
                    ? new ProjectDifficulty { Id = id, Name = name, Document = document, FaOnly = session.Manifest.Difficulties.Single(d => d.Id == id).FaOnly } : d).ToList()
            };
            var manifest = SnapshotManifest(session.Manifest);
            var entry = manifest.Difficulties.Single(d => d.Id == id);
            ValidatePublicationName(entry.File);
            if (entry.File != DifficultyFileName(document, name))
            {
                // Directory publication removes the old filename as part of the same recoverable rename.
                var renamed = session with { Manifest = manifest, Project = project };
                Save(renamed, project, archiveBeforeSave);
                return renamed;
            }
            if (archiveBeforeSave && !document.ContentEquals(ProjectSerializer.ReadFile(Path.Combine(session.Directory, entry.File))))
                WorkspaceSynchronization.Archive(session, "save", deferProject: true);
            entry.Name = name;
            entry.SyncFile = entry.Sync is null ? null : id.ToString("N") + ".catchsync";
            var files = new Dictionary<string, string>
            {
                [entry.File] = ProjectSerializer.Serialize(document, Path.Combine(session.Directory, entry.File))
            };
            if (entry.SyncFile is { } syncFile)
            {
                string text = JsonSerializer.Serialize(entry.Sync, json);
                if (System.Text.Encoding.UTF8.GetByteCount(text) > 128L * 1024 * 1024)
                    throw new InvalidDataException(L.Get("core.project.writeLimit"));
                files[syncFile] = text;
            }
            var persisted = SnapshotManifest(manifest);
            foreach (var difficulty in persisted.Difficulties) difficulty.Sync = null;
            files[ManifestName] = JsonSerializer.Serialize(persisted, json);
            PublishDifficulty(session.Directory, files);
            return session with { Manifest = manifest, Project = project };
        }
    }

    private static void PublishDifficulty(string directory, Dictionary<string, string> files)
    {
        string journal = directory + ".updating";
        RejectLinks(directory); RejectLinks(journal);
        Directory.CreateDirectory(Path.Combine(journal, "before"));
        Directory.CreateDirectory(Path.Combine(journal, "after"));
        try
        {
            var existing = new List<string>();
            foreach (var (name, text) in files)
            {
                ValidatePublicationName(name);
                string target = Path.Combine(directory, name);
                RejectLinks(target);
                if (File.Exists(target))
                {
                    File.Copy(target, Path.Combine(journal, "before", name));
                    existing.Add(name);
                }
                AtomicFile.Write(Path.Combine(journal, "after", name), text);
            }
            // Backups and the complete replacement list exist before the first live file changes.
            AtomicFile.Write(Path.Combine(journal, "ready.json"), JsonSerializer.Serialize(new DifficultyPublication(files.Keys.ToArray(), existing.ToArray())));
            foreach (string name in files.Keys)
                File.Move(Path.Combine(journal, "after", name), Path.Combine(directory, name), overwrite: true);
            AtomicFile.Write(Path.Combine(journal, "committed"), "");
        }
        finally { RecoverDifficultyPublication(directory); }
    }

    private static void ValidatePublicationName(string name)
    {
        if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name
            || name != ManifestName && !name.EndsWith(".catchdiff", StringComparison.Ordinal) && !name.EndsWith(".catchsync", StringComparison.Ordinal))
            throw new InvalidDataException(L.Get("project.invalid"));
    }

    private static void RecoverDifficultyPublication(string directory)
    {
        string journal = directory + ".updating";
        RejectLinks(journal);
        if (!Directory.Exists(journal)) return;
        string ready = Path.Combine(journal, "ready.json");
        if (File.Exists(ready) && !File.Exists(Path.Combine(journal, "committed")))
        {
            RejectLinks(ready);
            if (new FileInfo(ready).Length > 65536) throw new InvalidDataException(L.Get("project.invalid"));
            var publication = JsonSerializer.Deserialize<DifficultyPublication>(File.ReadAllText(ready))
                ?? throw new InvalidDataException(L.Get("project.invalid"));
            if (publication.Files is null || publication.Existing is null || publication.Files.Length is < 1 or > 3
                || publication.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != publication.Files.Length
                || publication.Existing.Except(publication.Files).Any()) throw new InvalidDataException(L.Get("project.invalid"));
            foreach (string name in publication.Files)
            {
                ValidatePublicationName(name);
                string target = Path.Combine(directory, name), backup = Path.Combine(journal, "before", name);
                RejectLinks(target); RejectLinks(backup);
                if (publication.Existing.Contains(name)) File.Copy(backup, target, overwrite: true);
                else File.Delete(target);
            }
        }
        Directory.Delete(journal, recursive: true);
    }
}
