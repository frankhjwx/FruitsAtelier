using L = FruitsAtelier.Localization.Strings;
using System.Security.Cryptography;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

public static class BeatmapResources
{
    private static readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".ogg", ".wav", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    public static void Copy(MapDocument document, string destinationDirectory, MapDocument exportedDocument)
    {
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        RejectLinks(destination);
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? audioName = exportedDocument.OriginalSections.Where(s => s.Name == "General")
            .SelectMany(s => s.Lines).Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim() == "AudioFilename")
            .Select(parts => parts[1].Trim()).LastOrDefault();
        string? audioTarget = null;
        if (!string.IsNullOrWhiteSpace(document.AudioPath) && !string.IsNullOrWhiteSpace(audioName))
        {
            audioTarget = BeatmapArchive.Within(destination, BeatmapArchive.ValidateRelativePath(audioName));
            plan.Add(audioTarget, Path.GetFullPath(document.AudioPath));
        }

        string? sourceDirectory = document.SourcePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(document.SourcePath));
        if (sourceDirectory is not null && Directory.Exists(sourceDirectory))
        {
            RejectLinks(sourceDirectory);
            var pending = new Stack<string>();
            pending.Push(sourceDirectory);
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            while (pending.TryPop(out string? directory))
            {
                // Never traverse an existing export subtree or discover files created by this export.
                if (string.Equals(Path.TrimEndingDirectorySeparator(directory), destination, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string child in Directory.EnumerateDirectories(directory, "*", options)) pending.Push(child);
                foreach (string source in Directory.EnumerateFiles(directory, "*", options))
                {
                    if (!extensions.Contains(Path.GetExtension(source))) continue;
                    string relative = BeatmapArchive.ValidateRelativePath(Path.GetRelativePath(sourceDirectory, source));
                    string target = BeatmapArchive.Within(destination, relative);
                    // The written AudioFilename owns its target even when an old resource has the same name.
                    if (string.Equals(target, audioTarget, StringComparison.OrdinalIgnoreCase)) continue;
                    plan.Add(target, source);
                }
            }
        }

        long bytes = 0;
        foreach (var (target, source) in plan)
        {
            RejectLinks(source);
            if (!File.Exists(source)) throw new FileNotFoundException(L.Get("resource.missing", source), source);
            if ((bytes += new FileInfo(source).Length) > 512L * 1024 * 1024)
                throw new IOException(L.Get("resource.limit"));
            CheckTarget(source, target);
        }
        foreach (var (target, source) in plan)
        {
            if (CheckTarget(source, target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            RejectLinks(target);
            File.Copy(source, target, overwrite: false);
        }
    }

    private static bool CheckTarget(string source, string target)
    {
        RejectLinks(target);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return true;
        if (Directory.Exists(target)) throw new IOException(L.Get("resource.targetDirectory", target));
        for (string? parent = Path.GetDirectoryName(target); parent is not null; parent = Path.GetDirectoryName(parent))
            if (File.Exists(parent)) throw new IOException(L.Get("resource.targetFile", parent));
        if (!File.Exists(target)) return false;
        using var a = File.OpenRead(source);
        using var b = File.OpenRead(target);
        if (a.Length != b.Length || !SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))
            throw new IOException(L.Get("resource.conflict", target));
        return true;
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("resource.link", current));
    }
}
