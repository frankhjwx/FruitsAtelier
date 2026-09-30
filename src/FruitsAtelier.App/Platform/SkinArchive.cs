using L = FruitsAtelier.Localization.Strings;
using System.IO.Compression;
using System.Security.Cryptography;
using FruitsAtelier.App.Skinning;

namespace FruitsAtelier.App.Platform;

public static class SkinArchive
{
    public const long MaxArchiveBytes = 256L * 1024 * 1024;
    public const long MaxFileBytes = 16L * 1024 * 1024;
    public const long MaxSelectedBytes = 64L * 1024 * 1024;
    private const string completionFile = ".complete";

    public static string Import(string archivePath, string cacheRoot)
    {
        string root = Path.GetFullPath(cacheRoot);
        RejectReparseAncestors(root);
        using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaxArchiveBytes) throw new InvalidDataException(L.Get("skinArchive.limit"));
        string key = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 20_000) throw new InvalidDataException(L.Get("skinArchive.fileCount"));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<(ZipArchiveEntry Entry, string Folder, string Name)>();
        long selectedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            string normalized = ValidateEntryPath(entry.FullName, out bool directory);
            if (!paths.Add(normalized)) throw new InvalidDataException(L.Get("skinArchive.duplicate"));
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(L.Get("skinArchive.link"));
            if (directory) continue;
            int separator = normalized.LastIndexOf('/');
            string name = normalized[(separator + 1)..];
            if (!IsSelected(name)) continue;
            if (entry.Length > MaxFileBytes) throw new InvalidDataException(L.Get("skinArchive.fileLimit"));
            selectedBytes += entry.Length;
            if (selectedBytes > MaxSelectedBytes) throw new InvalidDataException(L.Get("skinArchive.expansionLimit"));
            selected.Add((entry, separator < 0 ? "" : normalized[..separator], name));
        }
        if (selected.Count == 0) throw new InvalidDataException(L.Get("skinArchive.noFiles"));
        var configurations = selected.Where(e => e.Name.Equals("skin.ini", StringComparison.OrdinalIgnoreCase)).ToArray();
        string skinRoot = configurations.Length == 1 ? configurations[0].Folder : selected[0].Folder;
        if (configurations.Length > 1 || selected.Any(e => !e.Folder.Equals(skinRoot, StringComparison.OrdinalIgnoreCase)
            && (configurations.Length == 0 || (skinRoot.Length > 0 && !e.Folder.StartsWith(skinRoot + "/", StringComparison.OrdinalIgnoreCase)))))
            throw new InvalidDataException(L.Get("skinArchive.multipleFolders"));
        selected = selected.Select(e => (e.Entry, e.Folder,
            (e.Folder.Length == skinRoot.Length ? "" : e.Folder[(skinRoot.Length == 0 ? 0 : skinRoot.Length + 1)..] + "/") + e.Name)).ToList();

        // Version the cache when the extracted resource set changes.
        string destination = ChildPath(root, "v11-" + key);
        if (Directory.Exists(destination))
        {
            EnsureComplete(destination, key, selected);
            return destination;
        }
        if (File.Exists(destination)) throw new IOException(L.Get("skinArchive.cacheFile"));
        Directory.CreateDirectory(root);
        RejectReparseAncestors(root);
        string staging = ChildPath(root, ".import-" + key + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            RejectReparseAncestors(staging);
            foreach (var file in selected)
            {
                string path = ChildPath(staging, file.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RejectReparseAncestors(Path.GetDirectoryName(path)!);
                using var source = file.Entry.Open();
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = source.Read(buffer)) != 0)
                {
                    copied += read;
                    if (copied > MaxFileBytes || copied > file.Entry.Length)
                        throw new InvalidDataException(L.Get("skinArchive.sizeMismatch"));
                    output.Write(buffer, 0, read);
                }
                if (copied != file.Entry.Length) throw new InvalidDataException(L.Get("skinArchive.incompleteFile"));
            }
            if (!CatchSkin.TryLoad(staging, out _, out string message)) throw new InvalidDataException(message);
            File.WriteAllText(ChildPath(staging, completionFile), key);
            try { Directory.Move(staging, destination); }
            catch (IOException) when (Directory.Exists(destination))
            {
                // Another importer may have completed the same content-addressed cache first.
                EnsureComplete(destination, key, selected);
            }
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                RejectReparseAncestors(staging);
                foreach (string directory in Directory.EnumerateDirectories(staging, "*", SearchOption.AllDirectories))
                    RejectReparseAncestors(directory);
                foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    ChildPath(staging, Path.GetRelativePath(staging, file));
                    RejectReparseAncestors(Path.GetDirectoryName(file)!);
                    File.Delete(file);
                }
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void EnsureComplete(string folder, string key,
        IReadOnlyList<(ZipArchiveEntry Entry, string Folder, string Name)> selected)
    {
        RejectReparseAncestors(folder);
        string marker = ChildPath(folder, completionFile);
        if (!File.Exists(marker))
            throw new InvalidDataException(L.Get("skinArchive.incompleteCache"));
        RejectReparseFile(marker);
        if (new FileInfo(marker).Length != key.Length)
            throw new InvalidDataException(L.Get("skinArchive.invalidMarker"));
        if (File.ReadAllText(marker) != key) throw new InvalidDataException(L.Get("skinArchive.markerMismatch"));
        foreach (string directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
            RejectReparseAncestors(directory);
        if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count() != selected.Count + 1)
            throw new InvalidDataException(L.Get("skinArchive.modifiedFiles"));
        foreach (var entry in selected)
        {
            string file = ChildPath(folder, entry.Name);
            if (!File.Exists(file))
                throw new InvalidDataException(L.Get("skinArchive.modifiedSize"));
            RejectReparseFile(file);
            if (new FileInfo(file).Length != entry.Entry.Length)
                throw new InvalidDataException(L.Get("skinArchive.modifiedSize"));
        }
        if (!CatchSkin.TryLoad(folder, out _, out string message)) throw new InvalidDataException(message);
    }

    private static string ValidateEntryPath(string value, out bool directory)
    {
        string normalized = value.Replace('\\', '/');
        directory = normalized.EndsWith('/');
        if (directory) normalized = normalized[..^1];
        if (normalized.Length == 0 || normalized.Length > 4096 || normalized.StartsWith('/') || Path.IsPathRooted(normalized))
            throw new InvalidDataException(L.Get("skinArchive.invalidPath"));
        foreach (string component in normalized.Split('/'))
        {
            if (component.Length == 0 || component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ')
                || component.Any(c => c < 32 || ":<>\"|?*".Contains(c)))
                throw new InvalidDataException(L.Get("skinArchive.pathTraversal"));
            string stem = component.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException(L.Get("skinArchive.devicePath"));
        }
        return normalized;
    }

    private static bool IsSelected(string name) => name.Equals("skin.ini", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(name, @"^(cursor|cursormiddle|cursortrail|arrow-pause|arrow-warning|play-warningarrow|play-skip(-[0-9]+)?|pause-(overlay|continue|retry|back))(@2x)?\.png$|^pause-overlay\.jpg$|^(menuhit|menuclick|menuback|pause-(loop|hover|(continue|retry|back)-(click|hover)))\.(wav|ogg|mp3)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || FruitsAtelier.Core.HitsoundResolver.IsSkinSample(name)
        || System.Text.RegularExpressions.Regex.IsMatch(name, @"^.+-[0-9](@2x)?\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || name.Equals("reversearrow.png", StringComparison.OrdinalIgnoreCase)
        || name.Equals("reversearrow@2x.png", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(name, @"^(hitcircle|sliderstartcircle|sliderendcircle)(overlay)?(@2x)?\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || (name.StartsWith("fruit-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

    private static string ChildPath(string root, string name)
    {
        string child = Path.GetFullPath(Path.Combine(root, name));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Get("skinArchive.outsideCache"));
        return child;
    }

    private static void RejectReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("skinArchive.cacheLink"));
    }

    private static void RejectReparseFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(L.Get("skinArchive.fileLink"));
    }
}
