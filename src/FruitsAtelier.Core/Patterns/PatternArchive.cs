using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

/// <summary>The versioned .fpt package contains metadata and complete project document encodings.</summary>
public static class PatternArchive
{
    public const string Extension = ".fpt";
    public const int MaximumPatterns = 4096;
    public const long MaximumArchiveBytes = 64 * 1024 * 1024;
    public const long MaximumExpandedBytes = 256 * 1024 * 1024;
    private const int MaximumManifestBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record Entry(Guid Id, string Name, PatternSource Source,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PatternTag[]? Tags = null);
    private sealed record Manifest(int Version, Entry[] Patterns,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PatternTag[]? Tags = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PatternFolder[]? Folders = null);
    public sealed record Contents(IReadOnlyList<PatternSnapshot> Patterns, IReadOnlyList<PatternTag> Tags,
        IReadOnlyList<PatternFolder>? Folders = null);

    public static IReadOnlyList<PatternSnapshot> ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadContents(stream).Patterns;
    }

    public static Contents ReadContentsFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadContents(stream);
    }

    public static IReadOnlyList<PatternSnapshot> Read(Stream stream) => ReadContents(stream).Patterns;

    public static Contents ReadContents(Stream stream)
    {
        if (!stream.CanSeek || stream.Length > MaximumArchiveBytes) throw Invalid("limit");
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaximumPatterns + 1) throw Invalid("limit");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > MaximumExpandedBytes - expanded) throw Invalid("limit");
                expanded += entry.Length;
            }
            var manifestEntry = archive.GetEntry("manifest.json") ?? throw Invalid("invalid");
            var manifest = JsonSerializer.Deserialize<Manifest>(ReadEntry(manifestEntry, MaximumManifestBytes), Options);
            if (manifest?.Version is not (1 or 2 or 3 or 4) || manifest.Patterns is null || manifest.Patterns.Length > MaximumPatterns)
                throw Invalid("invalid");
            var ids = new HashSet<Guid>();
            var expected = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
            foreach (var item in manifest.Patterns)
            {
                if (item is null || !ids.Add(item.Id)) throw Invalid("invalid");
                expected.Add(DocumentPath(item.Id));
            }
            if (archive.Entries.Count != expected.Count || archive.Entries.Any(e => !expected.Remove(e.FullName)))
                throw Invalid("invalid");
            var patterns = new List<PatternSnapshot>(manifest.Patterns.Length);
            foreach (var item in manifest.Patterns)
            {
                var text = ReadEntry(archive.GetEntry(DocumentPath(item.Id))!, ProjectSerializer.MaximumFileBytes);
                var document = ProjectSerializer.Read(text);
                patterns.Add(new(item.Id, item.Name, item.Source, document, item.Tags));
            }
            var tags = ValidateTags(manifest.Tags ?? []);
            if (manifest.Version < 3 && manifest.Tags is not null) throw Invalid("invalid");
            var catalog = tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var tag in patterns.SelectMany(p => p.Tags))
                if (!catalog.TryAdd(tag.Name, tag) && manifest.Version >= 3 && catalog[tag.Name] != tag) throw Invalid("invalid");
            if (manifest.Version < 4 && manifest.Folders is not null || manifest.Version == 4 && manifest.Folders is null)
                throw Invalid("invalid");
            var folders = manifest.Folders is null ? null : ValidateFolders(manifest.Folders, ids);
            return new(patterns.AsReadOnly(), Array.AsReadOnly(catalog.Values.ToArray()), folders);
        }
        catch (JsonException ex) { throw new InvalidDataException(L.Get("core.pattern.invalid"), ex); }
    }

    public static void WriteFile(IEnumerable<PatternSnapshot> patterns, string path, IEnumerable<PatternTag>? tags = null,
        IEnumerable<PatternFolder>? folders = null)
    {
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                Write(patterns, stream, tags, folders);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Write(IEnumerable<PatternSnapshot> patterns, Stream stream, IEnumerable<PatternTag>? tags = null,
        IEnumerable<PatternFolder>? folders = null)
    {
        var items = patterns.Take(MaximumPatterns + 1).ToArray();
        if (items.Length > MaximumPatterns) throw Invalid("limit");
        if (items.Select(p => p.Id).Distinct().Count() != items.Length) throw Invalid("invalid");
        var catalog = ValidateTags(tags ?? []);
        var byName = catalog.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in items.SelectMany(p => p.Tags))
        {
            if (!byName.TryAdd(tag.Name, tag) && byName[tag.Name] != tag) throw Invalid("invalid");
        }
        bool hasCatalog = catalog.Count > 0;
        var folderList = folders is null ? null : ValidateFolders(folders, items.Select(p => p.Id).ToHashSet());
        long expanded = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            int version = folderList is not null ? 4 : hasCatalog ? 3 : items.Any(p => p.Tags.Count > 0) ? 2 : 1;
            string manifest = JsonSerializer.Serialize(new Manifest(version, items.Select(p => new Entry(p.Id, p.Name, p.Source,
                p.Tags.Count == 0 ? null : p.Tags.ToArray())).ToArray(), hasCatalog ? ValidateTags(byName.Values).ToArray() : null, folderList), Options);
            if (Encoding.UTF8.GetByteCount(manifest) > MaximumManifestBytes) throw Invalid("limit");
            WriteEntry("manifest.json", manifest);
            foreach (var item in items) WriteEntry(DocumentPath(item.Id), ProjectSerializer.Serialize(item.CreateDocument()));

            void WriteEntry(string path, string text)
            {
                int length = Encoding.UTF8.GetByteCount(text);
                if (length > MaximumExpandedBytes - expanded) throw Invalid("limit");
                expanded += length;
                using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        if (stream.CanSeek && stream.Length > MaximumArchiveBytes) throw Invalid("limit");
    }

    private static PatternFolder[] ValidateFolders(IEnumerable<PatternFolder> folders, HashSet<Guid> ids)
    {
        var items = folders.Take(MaximumPatterns + 1).ToArray();
        if (items.Length is 0 or > MaximumPatterns || items.Any(f => f is null || f.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(f.Name) || f.Name.Length > 128 || f.Name.Any(char.IsControl) || f.PatternIds is null)
            || items.Select(f => f.Id).Distinct().Count() != items.Length
            || items.Select(f => f.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
            throw Invalid("invalid");
        var members = new HashSet<Guid>();
        foreach (var folder in items)
            foreach (var id in folder.PatternIds)
                if (!ids.Contains(id) || !members.Add(id)) throw Invalid("invalid");
        if (!members.SetEquals(ids)) throw Invalid("invalid");
        return items.Select(f => f with { Name = f.Name.Trim(), PatternIds = f.PatternIds.ToArray() }).ToArray();
    }

    private static List<PatternTag> ValidateTags(IEnumerable<PatternTag> tags)
    {
        var items = tags.Take(MaximumPatterns + 1).ToList();
        if (items.Count > MaximumPatterns || items.Any(t => t is null || string.IsNullOrWhiteSpace(t.Name)
            || t.Name.Length > 64 || t.Name.Any(char.IsControl) || t.Colour > 0xFFFFFF)
            || items.Select(t => t.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw Invalid("invalid");
        return items.Select(t => t with { Name = t.Name.Trim() }).ToList();
    }

    private static string DocumentPath(Guid id) => $"patterns/{id:N}.catchproj";
    private static string ReadEntry(ZipArchiveEntry entry, int maximumBytes)
    {
        if (entry.Length > maximumBytes) throw Invalid("limit");
        using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        var buffer = new byte[81920];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (count > maximumBytes - output.Length || output.Length + count > entry.Length) throw Invalid("limit");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length) throw Invalid("invalid");
        return new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, (int)output.Length);
    }
    private static InvalidDataException Invalid(string reason) => new(L.Get("core.pattern." + reason));
}
