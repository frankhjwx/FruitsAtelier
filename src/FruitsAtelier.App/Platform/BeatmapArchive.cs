using L = FruitsAtelier.Localization.Strings;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FruitsAtelier.App.Platform;

public static class BeatmapArchive
{
    public static FruitsAtelier.Core.BeatmapProject OpenProject(string path, string cacheRoot)
    {
        if (Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase))
            return FruitsAtelier.Core.ProjectSerializer.ReadProjectFile(path);
        if (!Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase))
            return FruitsAtelier.Core.BeatmapProject.FromDocuments([FruitsAtelier.Core.OsuBeatmapReader.ReadFile(path)]);
        var documents = new List<FruitsAtelier.Core.MapDocument>();
        foreach (string map in Import(path, cacheRoot))
        {
            // Mixed-mode beatmap sets may contain unsupported rulesets. Only inspect General/Mode here;
            // malformed Catch maps still fail the entire open operation before replacing the editor.
            bool general = false;
            string? mode = null;
            foreach (string line in File.ReadLines(map))
            {
                string value = line.Trim();
                if (value.StartsWith('[')) general = value == "[General]";
                else if (general && value.Split(':', 2) is [var key, var setting] && key.Trim() == "Mode") mode = setting.Trim();
            }
            if (mode != "2") continue;
            documents.Add(FruitsAtelier.Core.OsuBeatmapReader.ReadFile(map));
        }
        if (documents.Count == 0) throw new InvalidDataException(L.Get("project.noCatch"));
        return FruitsAtelier.Core.BeatmapProject.FromDocuments(documents);
    }

    private static readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".osu", ".mp3", ".ogg", ".wav", ".jpg", ".jpeg", ".png" };

    public static IReadOnlyList<string> Import(string archivePath, string cacheRoot)
    {
        string root = Path.GetFullPath(cacheRoot);
        RejectLinks(root);
        using var input = File.OpenRead(archivePath);
        if (input.Length > 1024L * 1024 * 1024) throw new InvalidDataException(L.Get("archive.limit"));
        string key = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count > 20000) throw new InvalidDataException(L.Get("archive.fileCount"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<(ZipArchiveEntry Entry, string Path)>();
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            string path = ValidateRelativePath(entry.FullName.TrimEnd('/', '\\'));
            if (!seen.Add(path)) throw new InvalidDataException(L.Get("archive.duplicate"));
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(L.Get("archive.link"));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            if (!extensions.Contains(Path.GetExtension(path))) continue;
            long maximum = path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) ? 16L * 1024 * 1024 : 256L * 1024 * 1024;
            if (entry.Length > maximum || (total += entry.Length) > 512L * 1024 * 1024)
                throw new InvalidDataException(L.Get("archive.expansionLimit"));
            selected.Add((entry, path));
        }
        if (!selected.Any(e => Path.GetExtension(e.Path).Equals(".osu", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(L.Get("archive.noMaps"));
        string destination = Within(root, key);
        if (Directory.Exists(destination))
        {
            ValidateCache(destination, key, selected);
            return Maps(destination, selected);
        }
        Directory.CreateDirectory(root);
        RejectLinks(root);
        string staging = Within(root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (entry, path) in selected)
            {
                string target = Within(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                RejectLinks(Path.GetDirectoryName(target)!);
                using var source = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                byte[] buffer = new byte[81920];
                long written = 0;
                int count;
                while ((count = source.Read(buffer)) > 0)
                {
                    written += count;
                    if (written > entry.Length) throw new InvalidDataException(L.Get("archive.sizeMismatch"));
                    output.Write(buffer, 0, count);
                }
                if (written != entry.Length) throw new InvalidDataException(L.Get("archive.incompleteFile"));
            }
            File.WriteAllText(Within(staging, ".complete"), key);
            Directory.Move(staging, destination);
            return Maps(destination, selected);
        }
        finally
        {
            // Only importer-owned staging data is removed; a failed import never replaces a completed cache.
            if (Directory.Exists(staging)) RemoveStaging(staging, root);
        }
    }

    public static string ValidateRelativePath(string value)
    {
        string path = value.Replace('\\', '/');
        if (path.Length is 0 or > 2048 || path.StartsWith('/') || Path.IsPathRooted(path))
            throw new InvalidDataException(L.Get("archive.relativePath"));
        foreach (string part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(c => c < 32 || ":<>\"|?*".Contains(c)))
                throw new InvalidDataException(L.Get("archive.invalidPath"));
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException(L.Get("archive.devicePath"));
        }
        return path;
    }

    public static string Within(string root, string relative)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L.Get("archive.outsideDirectory"));
        return target;
    }

    private static IReadOnlyList<string> Maps(string folder, List<(ZipArchiveEntry Entry, string Path)> entries)
        => entries.Where(e => e.Path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)).Select(e => Within(folder, e.Path))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

    private static void ValidateCache(string folder, string key, List<(ZipArchiveEntry Entry, string Path)> entries)
    {
        RejectLinks(folder);
        string marker = Within(folder, ".complete");
        RejectLinks(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length != key.Length || File.ReadAllText(marker) != key)
            throw new InvalidDataException(L.Get("archive.cacheIncomplete"));
        foreach (var (entry, path) in entries)
        {
            string file = Within(folder, path);
            RejectLinks(file);
            if (!File.Exists(file) || new FileInfo(file).Length != entry.Length)
                throw new InvalidDataException(L.Get("archive.cacheModified"));
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("archive.cacheLink"));
    }

    private static void RemoveStaging(string staging, string root)
    {
        _ = Within(root, Path.GetRelativePath(root, staging));
        RejectLinks(staging);
        foreach (string child in Directory.EnumerateDirectories(staging)) RemoveStaging(child, root);
        foreach (string file in Directory.EnumerateFiles(staging)) { RejectLinks(file); File.Delete(file); }
        Directory.Delete(staging, false);
    }
}
