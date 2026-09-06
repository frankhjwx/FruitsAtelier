using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed class WorkspaceManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? SongsRoot { get; set; }
    public string? SourceDirectory { get; set; }
    public List<WorkspaceDifficulty> Difficulties { get; set; } = [];
}

public sealed class WorkspaceDifficulty
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string? Source { get; set; }
    public string? SourceHash { get; set; }
    public string? ExportTarget { get; set; }
    public string? ExportHash { get; set; }
}

public sealed record WorkspaceSession(string Directory, WorkspaceManifest Manifest, BeatmapProject Project);

public static class WorkspaceProject
{
    internal static readonly object Gate = new();
    public const string ManifestName = "project.catchdiff";
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static string SafeName(string value)
    {
        var clean = new string(value.Where(c => !char.IsControl(c) && !"<>:\"/\\|?*".Contains(c)).ToArray()).Trim().TrimEnd('.');
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(clean);
        if (elements.Length > 160) clean = clean[..elements[160]];
        if (string.IsNullOrEmpty(clean)) return "Untitled";
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(clean.Split('.')[0], StringComparer.OrdinalIgnoreCase)) clean = "_" + clean;
        return clean;
    }

    public static string DifficultyFileName(MapDocument document, string difficulty, string extension = ".catchdiff")
        => SafeName($"{Metadata(document, "Artist")} - {Metadata(document, "Title", document.Name)} ({Metadata(document, "Creator")}) [{difficulty}]") + extension;
    private static string Metadata(MapDocument d, string key, string fallback = "Unknown") => OsuBeatmapReader.Setting(d, "Metadata", key) is { Length: > 0 } v ? v : fallback;
    public static string Hash(string path) { using var file = System.IO.File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    public static bool Within(string root, string path)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(fullPath, fullRoot, comparison) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }
    public static void RejectLinks(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            if ((System.IO.File.Exists(part) || System.IO.Directory.Exists(part)) && (System.IO.File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(L.Get("resource.link", part));
    }
    public static void ValidateRoots(string workspace, string songs, bool requireSongs = true)
    {
        RejectLinks(workspace);
        if (string.IsNullOrWhiteSpace(songs)) return;
        RejectLinks(songs);
        if (requireSongs && !System.IO.Directory.Exists(songs)) throw new DirectoryNotFoundException(songs);
        if (Within(songs, workspace) || Within(workspace, songs)) throw new InvalidOperationException(L.Get("library.rootsOverlap"));
    }

    public static WorkspaceSession Create(string workspace, BeatmapProject project, string songsRoot)
    {
        ValidateRoots(workspace, songsRoot);
        var manifest = new WorkspaceManifest { Name = project.Name, SongsRoot = string.IsNullOrWhiteSpace(songsRoot) ? null : Path.GetFullPath(songsRoot) };
        var source = project.Difficulties.Select(d => d.Document.SourcePath).FirstOrDefault(p => p is not null && manifest.SongsRoot is not null && Within(manifest.SongsRoot, p));
        if (source is not null) manifest.SourceDirectory = Path.GetRelativePath(songsRoot, Path.GetDirectoryName(source)!);
        string directory = Path.Combine(workspace, SafeName(project.Name) + " [" + manifest.Id.ToString("N")[..8] + "]");
        var session = new WorkspaceSession(directory, manifest, project);
        Save(session, project);
        return session;
    }

    public static WorkspaceSession Open(string directory) { lock (Gate) { return OpenLocked(directory); } }
    private static WorkspaceSession OpenLocked(string directory)
    {
        directory = Path.GetFullPath(directory);
        Recover(directory);
        RejectLinks(directory);
        var manifest = ReadManifest(directory);
        var project = new BeatmapProject { Name = manifest.Name };
        foreach (var entry in manifest.Difficulties)
        {
            if (Path.GetFileName(entry.File) != entry.File || entry.File == ManifestName || !entry.File.EndsWith(".catchdiff", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.Get("project.invalid"));
            string file = Path.Combine(directory, entry.File); RejectLinks(file);
            project.Difficulties.Add(new ProjectDifficulty { Id = entry.Id, Name = entry.Name, Document = ProjectSerializer.ReadFile(file) });
        }
        project.Validate();
        return new(directory, manifest, project);
    }

    public static WorkspaceManifest ReadManifest(string directory)
    {
        string path = Path.Combine(directory, ManifestName);
        RejectLinks(path);
        if (new FileInfo(path).Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.project.readLimit"));
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(System.IO.File.ReadAllText(path), json);
        if (manifest is null || manifest.SchemaVersion != 1 || manifest.Id == Guid.Empty || string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Difficulties is null || manifest.Difficulties.Count is < 1 or > 256
            || manifest.Difficulties.Any(d => d is null || d.Id == Guid.Empty || string.IsNullOrWhiteSpace(d.Name))
            || manifest.Difficulties.Select(d => d.Id).Distinct().Count() != manifest.Difficulties.Count
            || manifest.Difficulties.Select(d => d.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Difficulties.Count)
            throw new InvalidDataException(L.Get("project.invalid"));
        return manifest;
    }

    // A complete sibling snapshot is published by directory rename. A retained previous
    // directory makes an interrupted rename recoverable without mixing difficulty versions.
    public static void Save(WorkspaceSession session, BeatmapProject project) { lock (Gate) { SaveLocked(session, project); } }
    private static void SaveLocked(WorkspaceSession session, BeatmapProject project)
    {
        project.Validate();
        string directory = Path.GetFullPath(session.Directory);
        if (session.Manifest.SongsRoot is { } songs) ValidateRoots(Path.GetDirectoryName(directory)!, songs, false);
        RejectLinks(directory); Recover(directory);
        string staging = directory + ".saving", previous = directory + ".previous";
        RejectLinks(staging); RejectLinks(previous);
        if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true);
        System.IO.Directory.CreateDirectory(staging);
        var entries = new List<WorkspaceDifficulty>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestName };
        try
        {
            foreach (var diff in project.Difficulties)
            {
                var old = session.Manifest.Difficulties.FirstOrDefault(e => e.Id == diff.Id);
                string name = DifficultyFileName(diff.Document, diff.Name);
                if (!names.Add(name))
                {
                    string stem = Path.GetFileNameWithoutExtension(name); int suffix = 2;
                    do { name = stem + " (" + suffix++ + ").catchdiff"; } while (!names.Add(name));
                }
                string? source = diff.Document.SourcePath;
                if (old is null && source is not null && System.IO.File.Exists(source)
                    && LibraryDatabase.ReadMetadata(source)?.Difficulty != diff.Name) source = null;
                var entry = new WorkspaceDifficulty { Id = diff.Id, Name = diff.Name, File = name, Source = old is not null ? old.Source : source,
                    SourceHash = old is not null ? old.SourceHash : (source is not null && System.IO.File.Exists(source) ? Hash(source) : null), ExportTarget = old?.ExportTarget, ExportHash = old?.ExportHash };
                entries.Add(entry);
                // Encode relative paths against the final location, not the staging folder.
                AtomicFile.Write(Path.Combine(staging, name), ProjectSerializer.Serialize(diff.Document, Path.Combine(directory, name)));
            }
            var manifest = new WorkspaceManifest { Id = session.Manifest.Id, Name = project.Name, SongsRoot = session.Manifest.SongsRoot,
                SourceDirectory = session.Manifest.SourceDirectory, Difficulties = entries };
            AtomicFile.Write(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(manifest, json));
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Move(directory, previous);
            try { System.IO.Directory.Move(staging, directory); }
            catch { if (System.IO.Directory.Exists(previous)) System.IO.Directory.Move(previous, directory); throw; }
            session.Manifest.Name = project.Name; session.Manifest.Difficulties = entries;
            if (System.IO.Directory.Exists(previous)) System.IO.Directory.Delete(previous, true);
        }
        finally { if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, true); }
    }

    public static void Recover(string directory) { lock (Gate) { RecoverLocked(directory); } }
    private static void RecoverLocked(string directory)
    {
        string previous = directory + ".previous";
        RejectLinks(directory); RejectLinks(previous);
        if (!System.IO.Directory.Exists(previous)) return;
        if (!System.IO.Directory.Exists(directory)) System.IO.Directory.Move(previous, directory);
        else if (System.IO.File.Exists(Path.Combine(directory, ManifestName))) System.IO.Directory.Delete(previous, true);
    }

    public static IReadOnlyList<string> MissingResources(BeatmapProject project)
    {
        var missing = new HashSet<string>();
        foreach (var diff in project.Difficulties)
        {
            var d = diff.Document;
            Check(d.SourcePath); Check(d.AudioPath);
            if (d.SourcePath is null) continue;
            foreach (string line in d.OriginalSections.Where(s => s.Name == "Events").SelectMany(s => s.Lines))
            {
                var parts = Csv(line);
                if (parts.Length < 3) continue;
                int index = parts[0] is "Sprite" or "Animation" or "Sample" ? 3 : parts[0] is "0" or "1" or "Video" ? 2 : -1;
                if (index < 0 || parts.Length <= index) continue;
                if (parts[0] == "Animation" && parts.Length > 6 && int.TryParse(parts[6], out int frames) && frames is > 0 and <= 10000)
                {
                    string name = parts[index];
                    for (int i = 0; i < frames; i++) Reference(Path.Combine(Path.GetDirectoryName(name) ?? "", Path.GetFileNameWithoutExtension(name) + i + Path.GetExtension(name)));
                }
                else Reference(parts[index]);
            }
            foreach (string line in d.OriginalSections.Where(s => s.Name == "HitObjects").SelectMany(s => s.Lines))
            {
                var fields = line.Split(',');
                if (fields.Length < 6) continue;
                string sample = fields[^1];
                if (sample.Count(c => c == ':') >= 4) Reference(sample.Split(':', 5)[4]);
            }
            void Reference(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                try { Check(OsuBeatmapReader.ResolveResource(d.SourcePath, name.Replace('\\', '/'))); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException) { missing.Add(name); }
            }
        }
        return missing.ToArray();
        void Check(string? path) { if (!string.IsNullOrWhiteSpace(path) && !System.IO.File.Exists(path)) missing.Add(path); }
    }
    internal static string[] Csv(string line)
    {
        var fields = new List<string>(); var value = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append(c); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(value.ToString().Trim()); value.Clear(); }
            else value.Append(c);
        }
        fields.Add(value.ToString().Trim()); return fields.ToArray();
    }

}
