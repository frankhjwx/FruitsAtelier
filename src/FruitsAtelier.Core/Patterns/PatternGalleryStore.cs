using L = FruitsAtelier.Localization.Strings;
using System.Text.Json;

namespace FruitsAtelier.Core.Patterns;

/// <summary>The complete gallery is atomically replaced, including multi-pattern imports.</summary>
public sealed class PatternGalleryStore
{
    private readonly string directory;
    private string ArchivePath => Path.Combine(directory, "gallery.fap");
    public PatternGalleryStore(string directory) => this.directory = Path.GetFullPath(directory);

    public IReadOnlyList<PatternSnapshot> Load() => File.Exists(ArchivePath) ? PatternArchive.ReadFile(ArchivePath) : [];

    public bool Add(PatternSnapshot pattern) => Merge([pattern]) > 0;

    public int Import(string path) => Merge(PatternArchive.ReadFile(path));

    public string? SourcePath(Guid id) => ReadSourceHints().GetValueOrDefault(id);

    public void RememberSource(Guid id, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.Replace('\\', '/').StartsWith("//", StringComparison.Ordinal)) return;
        var hints = ReadSourceHints();
        if (!hints.ContainsKey(id) && hints.Count >= PatternArchive.MaximumPatterns) return;
        hints[id] = Path.GetFullPath(path);
        Directory.CreateDirectory(directory);
        AtomicFile.Write(Path.Combine(directory, "sources.local.json"), JsonSerializer.Serialize(hints));
    }

    private Dictionary<Guid, string> ReadSourceHints()
    {
        string path = Path.Combine(directory, "sources.local.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 2 * 1024 * 1024) return [];
        try
        {
            var hints = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(path)) ?? [];
            return hints.Where(p => !string.IsNullOrWhiteSpace(p.Value) && Path.IsPathFullyQualified(p.Value)
                && !p.Value.Replace('\\', '/').StartsWith("//", StringComparison.Ordinal))
                .Take(PatternArchive.MaximumPatterns).ToDictionary(p => p.Key, p => p.Value);
        }
        catch (JsonException) { return []; }
    }

    public void Replace(PatternSnapshot pattern)
    {
        var patterns = Load().ToList();
        int index = patterns.FindIndex(p => p.Id == pattern.Id);
        if (index < 0) throw new InvalidOperationException(L.Get("core.pattern.missing"));
        patterns[index] = pattern;
        PatternArchive.WriteFile(patterns, ArchivePath);
    }

    public void Export(string path, IEnumerable<Guid>? ids = null)
    {
        var all = Load();
        var selected = ids?.ToHashSet();
        if (selected is not null && selected.Except(all.Select(p => p.Id)).Any())
            throw new InvalidOperationException(L.Get("core.pattern.missing"));
        PatternArchive.WriteFile(selected is null ? all : all.Where(p => selected.Contains(p.Id)), path);
    }

    public void Delete(Guid id)
    {
        var patterns = Load().ToList();
        if (patterns.RemoveAll(p => p.Id == id) == 0)
            throw new InvalidOperationException(L.Get("core.pattern.missing"));
        PatternArchive.WriteFile(patterns, ArchivePath);
    }

    public void EditTag(string name, PatternTag? replacement)
    {
        var patterns = Load();
        if (replacement is not null && !string.Equals(name, replacement.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            && patterns.Any(p => p.Tags.Any(t => string.Equals(t.Name, replacement.Name.Trim(), StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException(L.Get("gallery.tagExists"));
        var updated = patterns.Select(p => p.Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ? p.WithTags(p.Tags.Where(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                .Concat(replacement is null ? [] : new[] { replacement })) : p).ToArray();
        PatternArchive.WriteFile(updated, ArchivePath);
    }

    private int Merge(IEnumerable<PatternSnapshot> incoming)
    {
        var patterns = Load().ToList();
        var byId = patterns.ToDictionary(p => p.Id);
        var tags = patterns.SelectMany(p => p.Tags).DistinctBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (var pattern in incoming)
        {
            var imported = pattern.WithTags(pattern.Tags.Select(t => tags.TryGetValue(t.Name, out var tag) ? tag : t));
            if (byId.TryGetValue(pattern.Id, out var existing))
            {
                if (existing.Name != pattern.Name || existing.Source != pattern.Source || !existing.Tags.SequenceEqual(imported.Tags)
                    || !existing.CreateDocument().ContentEquals(pattern.CreateDocument()))
                    throw new InvalidDataException(L.Get("core.pattern.conflict"));
                continue;
            }
            foreach (var tag in imported.Tags) tags.TryAdd(tag.Name, tag);
            patterns.Add(imported); byId.Add(imported.Id, imported); added++;
        }
        if (added > 0)
        {
            Directory.CreateDirectory(directory);
            PatternArchive.WriteFile(patterns, ArchivePath);
        }
        return added;
    }
}
