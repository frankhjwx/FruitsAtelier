using L = FruitsAtelier.Localization.Strings;
using System.Text.Json;

namespace FruitsAtelier.Core.Patterns;

/// <summary>The complete gallery is atomically replaced, including multi-pattern imports.</summary>
public sealed partial class PatternGalleryStore
{
    private readonly string directory;
    private string ArchivePath => Path.Combine(directory, "gallery.fap");
    public PatternGalleryStore(string directory) => this.directory = Path.GetFullPath(directory);

    public PatternArchive.Contents LoadContents()
    {
        var contents = File.Exists(ArchivePath) ? PatternArchive.ReadContentsFile(ArchivePath) : new([], []);
        var folders = contents.Folders?.ToList() ?? [new(PatternFolder.DefaultId, PatternFolder.DefaultName, contents.Patterns.Select(p => p.Id).ToArray())];
        if (folders.All(f => f.Id != PatternFolder.DefaultId)) folders.Insert(0, new(PatternFolder.DefaultId, PatternFolder.DefaultName, []));
        if (folders.Any(f => f.Id == PatternFolder.DefaultId && f.Name != PatternFolder.DefaultName
            || f.Id != PatternFolder.DefaultId && string.Equals(f.Name, PatternFolder.DefaultName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(L.Get("core.pattern.invalid"));
        return contents with { Folders = folders };
    }
    public IReadOnlyList<PatternSnapshot> Load() => LoadContents().Patterns;

    public void AddTag(PatternTag tag, Guid assignTo = default)
    {
        var contents = LoadContents();
        if (contents.Tags.Any(t => string.Equals(t.Name, tag.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.Get("gallery.tagExists"));
        Directory.CreateDirectory(directory);
        var patterns = contents.Patterns;
        if (assignTo != Guid.Empty)
        {
            if (!patterns.Any(p => p.Id == assignTo)) throw new InvalidOperationException(L.Get("core.pattern.missing"));
            patterns = patterns.Select(p => p.Id == assignTo ? p.WithTags(p.Tags.Append(tag)) : p).ToArray();
        }
        PatternArchive.WriteFile(patterns, ArchivePath, contents.Tags.Append(tag), contents.Folders);
    }

    public bool Add(PatternSnapshot pattern, Guid? folderId = null) => Merge([pattern], folderId: folderId) > 0;

    public int Import(string path, Guid? folderId = null) => ImportFolders(path, folderId);

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
        var contents = LoadContents();
        var patterns = contents.Patterns.ToList();
        int index = patterns.FindIndex(p => p.Id == pattern.Id);
        if (index < 0) throw new InvalidOperationException(L.Get("core.pattern.missing"));
        patterns[index] = pattern;
        PatternArchive.WriteFile(patterns, ArchivePath, contents.Tags, contents.Folders);
    }

    public void Export(string path, IEnumerable<Guid>? ids = null, IEnumerable<Guid>? folderIds = null)
    {
        var contents = LoadContents();
        var all = contents.Patterns;
        var selectedFolders = folderIds?.ToHashSet();
        if (selectedFolders is not null && selectedFolders.Except(contents.Folders!.Select(f => f.Id)).Any())
            throw new InvalidOperationException(L.Get("core.pattern.missing"));
        var selected = ids?.ToHashSet() ?? (selectedFolders is null ? null : contents.Folders!
            .Where(f => selectedFolders.Contains(f.Id)).SelectMany(f => f.PatternIds).ToHashSet());
        if (selected is not null && selected.Except(all.Select(p => p.Id)).Any())
            throw new InvalidOperationException(L.Get("core.pattern.missing"));
        var exporting = selected is null ? all : all.Where(p => selected.Contains(p.Id)).ToArray();
        var usedTags = exporting.SelectMany(p => p.Tags).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exportingIds = exporting.Select(p => p.Id).ToHashSet();
        var folders = contents.Folders!.Where(f => selectedFolders is not null ? selectedFolders.Contains(f.Id)
            : selected is null || f.PatternIds.Any(exportingIds.Contains))
            .Select(f => f with { PatternIds = f.PatternIds.Where(exportingIds.Contains).ToArray() }).ToArray();
        if (selectedFolders is null && folders.All(f => f.Id == PatternFolder.DefaultId))
            folders = [new(Guid.NewGuid(), ExportFolderName(path), exportingIds.ToArray())];
        PatternArchive.WriteFile(exporting, path, selected is null ? contents.Tags : contents.Tags.Where(t => usedTags.Contains(t.Name)), folders);
    }

    public void Delete(Guid id)
    {
        var contents = LoadContents();
        var patterns = contents.Patterns.ToList();
        if (patterns.RemoveAll(p => p.Id == id) == 0)
            throw new InvalidOperationException(L.Get("core.pattern.missing"));
        PatternArchive.WriteFile(patterns, ArchivePath, contents.Tags,
            contents.Folders!.Select(f => f with { PatternIds = f.PatternIds.Where(member => member != id).ToArray() }));
    }

    public void EditTag(string name, PatternTag? replacement)
    {
        var contents = LoadContents();
        var patterns = contents.Patterns;
        if (replacement is not null && !string.Equals(name, replacement.Name.Trim(), StringComparison.OrdinalIgnoreCase)
            && contents.Tags.Any(t => string.Equals(t.Name, replacement.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.Get("gallery.tagExists"));
        var updated = patterns.Select(p => p.Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ? p.WithTags(p.Tags.Where(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                .Concat(replacement is null ? [] : new[] { replacement })) : p).ToArray();
        var tags = contents.Tags.SelectMany(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)
            ? replacement is null ? Array.Empty<PatternTag>() : new[] { replacement } : new[] { t });
        PatternArchive.WriteFile(updated, ArchivePath, tags, contents.Folders);
    }

    public void ReorderTags(IEnumerable<string> names)
    {
        var contents = LoadContents();
        var order = names.ToArray();
        var tags = contents.Tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        if (order.Length != tags.Count || order.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tags.Count
            || order.Any(name => !tags.ContainsKey(name)))
            throw new InvalidDataException(L.Get("core.pattern.invalid"));
        PatternArchive.WriteFile(contents.Patterns, ArchivePath, order.Select(name => tags[name]), contents.Folders);
    }

    private int Merge(IEnumerable<PatternSnapshot> incoming, IEnumerable<PatternTag>? importedTags = null, Guid? folderId = null)
    {
        var contents = LoadContents();
        Guid destination = RequireFolder(contents, folderId ?? PatternFolder.DefaultId).Id;
        var patterns = contents.Patterns.ToList();
        var byId = patterns.ToDictionary(p => p.Id);
        var tags = contents.Tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in importedTags ?? []) tags.TryAdd(tag.Name, tag);
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
        if (added > 0 || tags.Count != contents.Tags.Count)
        {
            Directory.CreateDirectory(directory);
            var newIds = patterns.Select(p => p.Id).Except(contents.Patterns.Select(p => p.Id)).ToArray();
            PatternArchive.WriteFile(patterns, ArchivePath, tags.Values, contents.Folders!.Select(f => f.Id == destination
                ? f with { PatternIds = f.PatternIds.Concat(newIds).ToArray() } : f));
        }
        return added;
    }
}
