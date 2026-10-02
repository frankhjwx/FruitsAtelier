using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed record WorkspaceExportPlan(Guid DifficultyId, MapDocument Document, string Target, string? ExpectedHash, OsuWriteResult Output, string? PreviousTarget = null)
{
    public string ExistingTarget => PreviousTarget ?? Target;
}

public static class WorkspaceExport
{
    public static WorkspaceExportPlan Plan(WorkspaceSession session, ProjectDifficulty difficulty, string songs, bool overwrite, string newName, bool compensate)
    {
        if (string.IsNullOrWhiteSpace(songs)) throw new InvalidOperationException(L.Get("library.bindForExport"));
        WorkspaceProject.ValidateRoots(Path.GetDirectoryName(session.Directory)!, songs);
        var missing = WorkspaceProject.MissingResources(BeatmapProject.FromDocuments([difficulty.Document]));
        if (missing.Count > 0) throw new IOException(L.Get("library.missingResources", string.Join("\n", missing)));
        var entry = session.Manifest.Difficulties.FirstOrDefault(d => d.Id == difficulty.Id);
        string? target = overwrite ? entry?.ExportTarget ?? entry?.Source : null;
        string? expected = overwrite ? entry?.ExportHash ?? entry?.SourceHash : null;
        var document = difficulty.Document.DeepClone();
        SetMetadata(document, "Version", difficulty.Name);
        if (overwrite)
        {
            if (target is null || expected is null || !File.Exists(target) || !WorkspaceProject.Within(songs, target))
                throw new IOException(L.Get("library.noExportTarget"));
            if (WorkspaceProject.Hash(target) != expected) throw new IOException(L.Get("library.exportConflict", target));
            WorkspaceAssociations.EnsureOwner(session, difficulty.Id, target);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new InvalidDataException(L.Get("project.invalid"));
            SetMetadata(document, "Version", newName); SetMetadata(document, "BeatmapID", "0");
            string directory = session.Manifest.SourceDirectory is { } relative ? Path.GetFullPath(Path.Combine(songs, relative))
                : Path.Combine(songs, "FruitsAtelier " + WorkspaceProject.SafeName(session.Manifest.Name) + " " + session.Manifest.Id.ToString("N")[..8]);
            if (!WorkspaceProject.Within(songs, directory) || directory == Path.GetFullPath(songs)) throw new IOException(L.Get("library.noExportTarget"));
            target = Path.Combine(directory, WorkspaceProject.DifficultyFileName(document, newName, ".osu"));
            if (File.Exists(target)) throw new IOException(L.Get("library.exportExists", target));
        }
        WorkspaceProject.RejectLinks(target!);
        var plan = new WorkspaceExportPlan(difficulty.Id, document, target!, expected, OsuBeatmapWriter.Serialize(document, compensate));
        return overwrite ? WithMetadataFileName(plan, OsuBeatmapReader.ReadFile(target!)) : plan;
    }

    internal static WorkspaceExportPlan WithMetadataFileName(WorkspaceExportPlan plan, MapDocument previous)
    {
        string[] keys = ["Artist", "Title", "Creator", "Version"];
        if (!keys.Any(key => OsuBeatmapReader.Setting(previous, "Metadata", key)
            != OsuBeatmapReader.Setting(plan.Document, "Metadata", key))) return plan;
        string version = OsuBeatmapReader.Setting(plan.Document, "Metadata", "Version") ?? plan.Document.Name;
        string target = Path.Combine(Path.GetDirectoryName(plan.Target)!, WorkspaceProject.DifficultyFileName(plan.Document, version, ".osu"));
        if (string.Equals(target, plan.Target, StringComparison.Ordinal)) return plan;
        if (!WorkspaceSynchronization.Paths.Equals(target, plan.Target) && File.Exists(target))
            throw new IOException(L.Get("library.exportExists", target));
        return plan with { Target = target, PreviousTarget = plan.Target };
    }

    public static void Commit(WorkspaceSession session, WorkspaceExportPlan plan, bool updateAssociation = true)
    {
        WorkspaceProject.RejectLinks(plan.Target);
        if (plan.PreviousTarget is not null)
        {
            WorkspaceProject.RejectLinks(plan.ExistingTarget);
            WorkspaceAssociations.EnsureOwner(session, plan.DifficultyId, plan.ExistingTarget);
            if (!WorkspaceSynchronization.Paths.Equals(plan.Target, plan.ExistingTarget) && File.Exists(plan.Target))
                throw new IOException(L.Get("library.exportExists", plan.Target));
        }
        WorkspaceAssociations.EnsureOwner(session, plan.DifficultyId, plan.Target);
        if (plan.ExpectedHash is null)
        {
            // CreateNew prevents a race from turning a new-difficulty export into an overwrite.
            string temporary = plan.Target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, plan.Output.Text, new System.Text.UTF8Encoding(false)); File.Move(temporary, plan.Target, overwrite: false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        else
        {
            if (!File.Exists(plan.ExistingTarget) || WorkspaceProject.Hash(plan.ExistingTarget) != plan.ExpectedHash)
                throw new IOException(L.Get("library.exportConflict", plan.Target));
            byte[] original = File.ReadAllBytes(plan.ExistingTarget);
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(Path.GetDirectoryName(session.Directory)!, "library.db"), Pooling = false }.ToString()))
            {
                db.Open(); using var backup = db.CreateCommand();
                backup.CommandText = "CREATE TABLE IF NOT EXISTS export_backups(target TEXT NOT NULL, hash TEXT NOT NULL, content BLOB NOT NULL, created TEXT NOT NULL); INSERT INTO export_backups VALUES($p,$h,$b,$t)";
                backup.Parameters.AddWithValue("$p", plan.ExistingTarget); backup.Parameters.AddWithValue("$h", plan.ExpectedHash);
                backup.Parameters.AddWithValue("$b", original); backup.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                backup.ExecuteNonQuery();
            }
            AtomicFile.Write(plan.ExistingTarget, plan.Output.Text);
            if (plan.PreviousTarget is not null)
            {
                try { File.Move(plan.ExistingTarget, plan.Target); }
                catch
                {
                    if (File.Exists(plan.ExistingTarget) && WorkspaceProject.Hash(plan.ExistingTarget) == WorkspaceSynchronization.Digest(plan.Output.Text))
                    {
                        string rollback = plan.ExistingTarget + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try { File.WriteAllBytes(rollback, original); File.Move(rollback, plan.ExistingTarget, overwrite: true); }
                        finally { if (File.Exists(rollback)) File.Delete(rollback); }
                    }
                    throw;
                }
            }
        }
        if (updateAssociation)
        {
            plan.Document.SourcePath = plan.Target;
            var difficulty = session.Project.Difficulties.FirstOrDefault(d => d.Id == plan.DifficultyId);
            if (difficulty is not null) difficulty.Document.SourcePath = plan.Target;
            var entry = session.Manifest.Difficulties.Single(d => d.Id == plan.DifficultyId);
            entry.ExportTarget = plan.Target; entry.ExportHash = WorkspaceSynchronization.Digest(plan.Output.Text);
            entry.Source = plan.Target; entry.SourceHash = entry.ExportHash;
            entry.Sync = WorkspaceSynchronization.Capture(plan.Target, plan.Document, session.Directory, plan.Output.Text, plan.Output.ObjectSources, entry.Sync);
        }
    }
    private static void SetMetadata(MapDocument document, string key, string value)
    {
        var metadata = document.OriginalSections.FirstOrDefault(s => s.Name == "Metadata");
        if (metadata is null) { metadata = new OsuSection { Name = "Metadata" }; document.OriginalSections.Add(metadata); }
        metadata.Lines.RemoveAll(l => l.Split(':', 2)[0].Trim() == key); metadata.Lines.Add(key + ":" + value);
    }
}
