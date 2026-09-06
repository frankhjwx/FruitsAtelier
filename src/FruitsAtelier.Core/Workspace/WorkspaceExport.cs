using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed record WorkspaceExportPlan(Guid DifficultyId, MapDocument Document, string Target, string? ExpectedHash, OsuWriteResult Output);

public static class WorkspaceExport
{
    public static WorkspaceExportPlan Plan(WorkspaceSession session, ProjectDifficulty difficulty, string songs, bool overwrite, string newName, bool compensate)
    {
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
        return new(difficulty.Id, document, target!, expected, OsuBeatmapWriter.Serialize(document, compensate));
    }

    public static void Commit(WorkspaceSession session, WorkspaceExportPlan plan)
    {
        WorkspaceProject.RejectLinks(plan.Target);
        if (plan.ExpectedHash is null)
        {
            // CreateNew prevents a race from turning a new-difficulty export into an overwrite.
            string temporary = plan.Target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, plan.Output.Text, new System.Text.UTF8Encoding(false)); File.Move(temporary, plan.Target, overwrite: false); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        else
        {
            if (!File.Exists(plan.Target) || WorkspaceProject.Hash(plan.Target) != plan.ExpectedHash)
                throw new IOException(L.Get("library.exportConflict", plan.Target));
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(Path.GetDirectoryName(session.Directory)!, "library.db"), Pooling = false }.ToString()))
            {
                db.Open(); using var backup = db.CreateCommand();
                backup.CommandText = "CREATE TABLE IF NOT EXISTS export_backups(target TEXT NOT NULL, hash TEXT NOT NULL, content BLOB NOT NULL, created TEXT NOT NULL); INSERT INTO export_backups VALUES($p,$h,$b,$t)";
                backup.Parameters.AddWithValue("$p", plan.Target); backup.Parameters.AddWithValue("$h", plan.ExpectedHash);
                backup.Parameters.AddWithValue("$b", File.ReadAllBytes(plan.Target)); backup.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                backup.ExecuteNonQuery();
            }
            AtomicFile.Write(plan.Target, plan.Output.Text);
        }
        var entry = session.Manifest.Difficulties.Single(d => d.Id == plan.DifficultyId);
        entry.ExportTarget = plan.Target; entry.ExportHash = WorkspaceProject.Hash(plan.Target);
    }
    private static void SetMetadata(MapDocument document, string key, string value)
    {
        var metadata = document.OriginalSections.FirstOrDefault(s => s.Name == "Metadata");
        if (metadata is null) { metadata = new OsuSection { Name = "Metadata" }; document.OriginalSections.Add(metadata); }
        metadata.Lines.RemoveAll(l => l.Split(':', 2)[0].Trim() == key); metadata.Lines.Add(key + ":" + value);
    }
}
