using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal static class FazStaging
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FileStream> Leases = new(WorkspaceSynchronization.Paths);

    internal static string Create(string workspace)
    {
        lock (Gate)
        {
            string root = Path.GetFullPath(Path.Combine(workspace, ".faz-staging"));
            WorkspaceProject.RejectLinks(root);
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var lease = new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try { Directory.CreateDirectory(path); Leases.Add(path, lease); return path; }
            catch { lease.Dispose(); File.Delete(path + ".lock"); throw; }
        }
    }

    internal static void Release(string path)
    {
        lock (Gate)
        {
            path = Path.GetFullPath(path);
            try { FazArchive.RemoveDirectory(path, Path.GetDirectoryName(path)!); }
            finally
            {
                if (Leases.Remove(path, out var lease)) lease.Dispose();
                File.Delete(path + ".lock");
            }
        }
    }

    internal static void Cleanup(string workspace)
    {
        lock (Gate)
        {
            string root = Path.GetFullPath(Path.Combine(workspace, ".faz-staging"));
            try
            {
                WorkspaceProject.RejectLinks(root);
                if (!Directory.Exists(root)) return;
                var paths = Directory.EnumerateDirectories(root).Concat(Directory.EnumerateFiles(root, "*.lock").Select(p => p[..^5])).Distinct(WorkspaceSynchronization.Paths).ToArray();
                foreach (string path in paths)
                {
                    if (!Guid.TryParseExact(Path.GetFileName(path), "N", out _) || Leases.ContainsKey(path)) continue;
                    try
                    {
                        WorkspaceProject.RejectLinks(path + ".lock");
                        // An exclusive lease protects staging owned by another running instance.
                        using (var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                            FazArchive.RemoveDirectory(path, root);
                        File.Delete(path + ".lock");
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
                if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
