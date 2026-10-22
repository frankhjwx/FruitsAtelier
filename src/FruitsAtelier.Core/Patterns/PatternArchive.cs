using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

/// <summary>The versioned .fap package contains metadata and complete project document encodings.</summary>
public static class PatternArchive
{
    public const string Extension = ".fap";
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
    private sealed record Manifest(int Version, Entry[] Patterns);

    public static IReadOnlyList<PatternSnapshot> ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static IReadOnlyList<PatternSnapshot> Read(Stream stream)
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
            if (manifest?.Version is not (1 or 2) || manifest.Patterns is null || manifest.Patterns.Length > MaximumPatterns)
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
            return patterns.AsReadOnly();
        }
        catch (JsonException ex) { throw new InvalidDataException(L.Get("core.pattern.invalid"), ex); }
    }

    public static void WriteFile(IEnumerable<PatternSnapshot> patterns, string path)
    {
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                Write(patterns, stream);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Write(IEnumerable<PatternSnapshot> patterns, Stream stream)
    {
        var items = patterns.Take(MaximumPatterns + 1).ToArray();
        if (items.Length > MaximumPatterns) throw Invalid("limit");
        if (items.Select(p => p.Id).Distinct().Count() != items.Length) throw Invalid("invalid");
        long expanded = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            int version = items.Any(p => p.Tags.Count > 0) ? 2 : 1;
            string manifest = JsonSerializer.Serialize(new Manifest(version, items.Select(p => new Entry(p.Id, p.Name, p.Source,
                p.Tags.Count == 0 ? null : p.Tags.ToArray())).ToArray()), Options);
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
