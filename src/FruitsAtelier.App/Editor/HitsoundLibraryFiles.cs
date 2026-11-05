using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

internal static class HitsoundLibraryFiles
{
    internal static void Validate(string root, string path, bool allowRoot = false)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if ((!allowRoot && WorkspaceSynchronization.Paths.Equals(root, path))
            || !WorkspaceSynchronization.Paths.Equals(root, path) && !WorkspaceProject.Within(root, path))
            throw new IOException(L.Get("hitsounding.invalidDestination"));
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("hitsounding.invalidDestination"));
            if (WorkspaceSynchronization.Paths.Equals(current, root)) return;
        }
        throw new IOException(L.Get("hitsounding.invalidDestination"));
    }

    internal static string NamedPath(string root, string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.Any(ch => char.IsControl(ch) || "<>:\"/\\|?*".Contains(ch)))
            throw new IOException(L.Get("hitsounding.invalidName"));
        string path = Path.Combine(directory, name);
        Validate(root, path);
        if (Path.Exists(path)) throw new IOException(L.Get("hitsounding.nameExists"));
        return path;
    }

    internal static string[] Transfer(string root, IReadOnlyList<string> sources, string destination, bool move)
    {
        Validate(root, destination, allowRoot: true);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException(destination);
        foreach (string source in sources)
        {
            Validate(root, source);
            if (!Path.Exists(source)) throw new FileNotFoundException(L.Get("hitsounding.missingFile"), source);
            if (Directory.Exists(source) && (WorkspaceSynchronization.Paths.Equals(source, destination) || WorkspaceProject.Within(source, destination)))
                throw new IOException(L.Get("hitsounding.invalidDestination"));
        }
        var targets = new List<string>();
        foreach (string source in sources)
        {
            if (move && WorkspaceSynchronization.Paths.Equals(Path.GetDirectoryName(source), destination)) { targets.Add(source); continue; }
            string target = UniquePath(destination, Path.GetFileName(source), Directory.Exists(source));
            Validate(root, target);
            if (move)
            {
                if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
            }
            else if (Directory.Exists(source))
            {
                var entries = Directory.EnumerateFileSystemEntries(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }).Take(20001).ToArray();
                if (entries.Length > 20000) throw new IOException(L.Get("resource.limit"));
                foreach (string entry in entries) Validate(root, entry);
                Directory.CreateDirectory(target);
                foreach (string entry in entries)
                {
                    string copy = Path.Combine(target, Path.GetRelativePath(source, entry));
                    if (Directory.Exists(entry)) Directory.CreateDirectory(copy);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(entry, copy); }
                }
            }
            else File.Copy(source, target);
            targets.Add(target);
        }
        return targets.ToArray();
    }

    private static string UniquePath(string directory, string name, bool folder)
    {
        string target = Path.Combine(directory, name);
        string stem = folder ? name : Path.GetFileNameWithoutExtension(name), extension = folder ? "" : Path.GetExtension(name);
        for (int suffix = 2; Path.Exists(target); suffix++) target = Path.Combine(directory, $"{stem} ({suffix}){extension}");
        return target;
    }
}
