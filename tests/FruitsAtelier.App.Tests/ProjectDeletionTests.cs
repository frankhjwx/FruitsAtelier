using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;

internal static class ProjectDeletionTests
{
    internal static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp/atelier-project-deletion" : "artifacts/tests/project-deletion", Guid.NewGuid().ToString("N")));
        try { RunFixture(root); }
        finally
        {
            if (OperatingSystem.IsMacOS() && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void RunFixture(string root)
    {
        string workspace = Path.Combine(root, "workspace");
        var project = BeatmapProject.FromDocuments([new MapDocument()]);
        var session = WorkspaceProject.Create(workspace, project, "");
        WorkspaceSynchronization.Archive(session, "fixture");
        string history = Path.Combine(workspace, ".sync-history", session.Manifest.Id.ToString("N"));
        string resources = Path.Combine(workspace, "Resources", "FAZ-" + session.Manifest.Id.ToString("N"));
        Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "audio.wav"), "resource fixture");
        string[] expected = [session.Directory, history, resources];
        var contents = expected.SelectMany(path => Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            .ToDictionary(path => path, File.ReadAllBytes);
        try
        {
            WorkspaceAssociations.DeleteLocalProject(session.Directory, recycle: paths =>
            {
                Check(paths.SequenceEqual(expected), "project, history and exclusive resources are submitted together");
                throw new IOException("fixture: recycle unavailable");
            });
            throw new Exception("Recycling failure was ignored");
        }
        catch (IOException) { }
        Check(contents.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "recycling failure preserves all content");
        try
        {
            WorkspaceAssociations.DeleteLocalProject(session.Directory);
            throw new Exception("Missing recycling service was ignored");
        }
        catch (IOException) { }
        Check(contents.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "no service cannot permanently delete content");

        string trash = Path.Combine(root, "simulated-trash"); Directory.CreateDirectory(trash);
        var moved = new List<(string Original, string Recycled)>();
        WorkspaceAssociations.DeleteLocalProject(session.Directory, recycle: paths =>
        {
            foreach (string path in paths)
            {
                string target = Path.Combine(trash, moved.Count.ToString());
                Directory.Move(path, target); moved.Add((path, target));
            }
        });
        Check(expected.All(path => !Directory.Exists(path)), "recycled folders leave the workspace");
        foreach (var item in moved) Directory.Move(item.Recycled, item.Original);
        Check(contents.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "restoring all recycled folders preserves exact bytes");
        Check(WorkspaceProject.Open(session.Directory).Manifest.Id == session.Manifest.Id, "restored project opens with its original identity");
        WorkspaceAssociations.DeleteLocalProject(session.Directory, keepBackup: true, recycle: _ => throw new Exception("Backup must not use trash"));
        Check(Directory.EnumerateDirectories(history, "retired-project", SearchOption.AllDirectories).Any(), "backup retains the complete project");
        Check(Directory.Exists(resources), "backup retains managed resources");

        string native = Path.Combine(root, "native-trash-fixture");
        Directory.CreateDirectory(Path.Combine(native, "nested"));
        File.WriteAllText(Path.Combine(native, "nested", "中文 fixture.catchdiff"), "recycle fixture");
        SystemTrash.Recycle([native]);
        Check(!Directory.Exists(native), "native system trash accepts complete directories");
        Console.WriteLine("Native recycle fixture: " + native);
        Console.WriteLine("PASS project deletion failure, backup, content restoration and native recycling");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
