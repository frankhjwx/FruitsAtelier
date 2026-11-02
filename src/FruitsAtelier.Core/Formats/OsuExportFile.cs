using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

internal static class OsuExportFile
{
    // osu! stable normalizes watcher events with legacy MAX_PATH rules, including temporary files.
    internal const int MaximumPathLength = 259;
    internal const int MaximumDirectoryLength = MaximumPathLength - 1 - 37;

    internal static void ValidatePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (fullPath.Length > MaximumPathLength || Path.GetDirectoryName(fullPath)!.Length > MaximumDirectoryLength)
            throw new IOException(L.Get("library.exportPathTooLong", fullPath));
    }

    internal static void Write(string target, byte[] content, bool overwrite)
    {
        ValidatePath(target);
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write);
        try
        {
            using (stream) stream.Write(content);
            File.Move(temporary, target, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
