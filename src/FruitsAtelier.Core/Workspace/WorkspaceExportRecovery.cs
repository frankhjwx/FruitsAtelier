using System.Text.Json;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public static class WorkspaceExportRecovery
{
    private sealed record Receipt(string Directory, string Project, string Manifest);
    private static string ReceiptPath(string directory) => Path.Combine(Path.GetDirectoryName(directory)!, ".sync-history", "pending",
        WorkspaceSynchronization.Digest(Path.GetFullPath(directory)) + ".json");

    public static string Prepare(WorkspaceSession session, BeatmapProject project, WorkspaceExportPlan plan, Guid owner)
    {
        var manifest = WorkspaceProject.SnapshotManifest(session.Manifest);
        var entry = manifest.Difficulties.FirstOrDefault(d => d.Id == owner);
        if (entry is null) { entry = new WorkspaceDifficulty { Id = owner }; manifest.Difficulties.Add(entry); }
        var difficulty = project.Difficulties.Single(d => d.Id == owner);
        entry.Name = difficulty.Name; entry.Source = entry.ExportTarget = plan.Target;
        entry.SourceHash = entry.ExportHash = WorkspaceSynchronization.Digest(plan.Output.Text);
        entry.Sync = WorkspaceSynchronization.Capture(plan.Target, difficulty.Document, session.Directory, plan.Output.Text, plan.Output.ObjectSources, entry.Sync);
        string path = ReceiptPath(session.Directory);
        WorkspaceProject.RejectLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.Write(path, JsonSerializer.Serialize(new Receipt(session.Directory,
            ProjectSerializer.Serialize(project, Path.Combine(session.Directory, "recovery.catchproj")), JsonSerializer.Serialize(manifest))));
        return path;
    }

    public static void Complete(string receipt) => File.Delete(receipt);

    public static void Recover(string directory)
    {
        string path = ReceiptPath(directory);
        if (!File.Exists(path)) return;
        WorkspaceProject.RejectLinks(path);
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)) ?? throw new InvalidDataException(L.Get("project.invalid"));
        if (!WorkspaceSynchronization.Paths.Equals(Path.GetFullPath(directory), receipt.Directory)) throw new InvalidDataException(L.Get("project.invalid"));
        var project = ProjectSerializer.ReadProject(receipt.Project, Path.Combine(directory, "recovery.catchproj"));
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(receipt.Manifest) ?? throw new InvalidDataException(L.Get("project.invalid"));
        // Recovery publishes authoring only. The external file may have changed again after the interrupted write.
        WorkspaceProject.Save(new(directory, manifest, project), project);
        File.Delete(path);
    }
}
