using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

public sealed partial class PatternGalleryStore
{
    private static PatternFolder RequireFolder(PatternArchive.Contents contents, Guid id)
        => contents.Folders!.FirstOrDefault(f => f.Id == id) ?? throw new InvalidOperationException(L.Get("core.pattern.missing"));

    private void SaveContents(PatternArchive.Contents contents)
    {
        Directory.CreateDirectory(directory);
        PatternArchive.WriteFile(contents.Patterns, ArchivePath, contents.Tags, contents.Folders);
    }

    private static string ExportFolderName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).Trim();
        return name.Length == 0 ? PatternFolder.DefaultName : name[..Math.Min(name.Length, 128)];
    }

    private static string UniqueFolderName(string name, IEnumerable<PatternFolder> folders)
    {
        name = name.Trim();
        var names = folders.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name)) return name;
        for (int i = 1; ; i++)
        {
            string suffix = $" ({i})";
            string candidate = name[..Math.Min(name.Length, 128 - suffix.Length)] + suffix;
            if (!names.Contains(candidate)) return candidate;
        }
    }

    public Guid CreateFolder(string name)
    {
        var contents = LoadContents();
        var folder = new PatternFolder(Guid.NewGuid(), name.Trim(), []);
        SaveContents(contents with { Folders = contents.Folders!.Append(folder).ToArray() });
        return folder.Id;
    }

    public void RenameFolder(Guid id, string name)
    {
        var contents = LoadContents();
        RequireFolder(contents, id);
        if (id == PatternFolder.DefaultId) throw new InvalidOperationException(L.Get("gallery.defaultPermanent"));
        SaveContents(contents with { Folders = contents.Folders!.Select(f => f.Id == id ? f with { Name = name.Trim() } : f).ToArray() });
    }

    public void MergeFolders(IEnumerable<Guid> ids, Guid destination)
    {
        var contents = LoadContents();
        var selected = ids.ToHashSet();
        selected.Add(destination);
        foreach (var id in selected) RequireFolder(contents, id);
        var members = contents.Folders!.Where(f => selected.Contains(f.Id)).SelectMany(f => f.PatternIds).ToArray();
        SaveContents(contents with
        {
            Folders = contents.Folders!.Where(f => !selected.Contains(f.Id) || f.Id == destination || f.Id == PatternFolder.DefaultId)
                .Select(f => f.Id == destination ? f with { PatternIds = members }
                    : selected.Contains(f.Id) ? f with { PatternIds = [] } : f).ToArray()
        });
    }

    public void DeletePatterns(IEnumerable<Guid> ids)
    {
        var contents = LoadContents();
        var selected = ids.ToHashSet();
        if (selected.Except(contents.Patterns.Select(p => p.Id)).Any()) throw new InvalidOperationException(L.Get("core.pattern.missing"));
        SaveContents(contents with
        {
            Patterns = contents.Patterns.Where(p => !selected.Contains(p.Id)).ToArray(),
            Folders = contents.Folders!.Select(f => f with { PatternIds = f.PatternIds.Where(id => !selected.Contains(id)).ToArray() }).ToArray()
        });
    }

    public void TransferPatterns(IEnumerable<Guid> ids, Guid destination, bool copy)
    {
        var contents = LoadContents();
        RequireFolder(contents, destination);
        var selected = ids.ToHashSet();
        var originals = contents.Patterns.Where(p => selected.Contains(p.Id)).ToArray();
        if (originals.Length != selected.Count) throw new InvalidOperationException(L.Get("core.pattern.missing"));
        var additions = copy ? originals.Select(p => new PatternSnapshot(Guid.NewGuid(), p.Name, p.Source, p.CreateDocument(), p.Tags)).ToArray() : originals;
        var newIds = additions.Select(p => p.Id).ToArray();
        SaveContents(contents with
        {
            Patterns = copy ? contents.Patterns.Concat(additions).ToArray() : contents.Patterns,
            Folders = contents.Folders!.Select(f => f with
            {
                PatternIds = (copy ? f.PatternIds : f.PatternIds.Where(id => !selected.Contains(id)))
                    .Concat(f.Id == destination ? newIds : []).ToArray()
            }).ToArray()
        });
        if (copy)
            for (int i = 0; i < originals.Length; i++) RememberSource(additions[i].Id, SourcePath(originals[i].Id));
    }

    private int ImportFolders(string path, Guid? destination)
    {
        var incoming = PatternArchive.ReadContentsFile(path);
        var contents = LoadContents();
        if (destination is { } target) RequireFolder(contents, target);
        var folders = contents.Folders!.ToList();
        var patterns = contents.Patterns.ToList();
        var ids = patterns.Select(p => p.Id).ToHashSet();
        var tags = contents.Tags.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in incoming.Tags) tags.TryAdd(tag.Name, tag);
        var remap = new Dictionary<Guid, Guid>();
        foreach (var pattern in incoming.Patterns)
        {
            Guid id = ids.Add(pattern.Id) ? pattern.Id : Guid.NewGuid();
            ids.Add(id); remap.Add(pattern.Id, id);
            patterns.Add(new(id, pattern.Name, pattern.Source, pattern.CreateDocument(),
                pattern.Tags.Select(t => tags.TryGetValue(t.Name, out var local) ? local : t)));
        }
        if (destination is { } folderId)
        {
            int index = folders.FindIndex(f => f.Id == folderId);
            folders[index] = folders[index] with { PatternIds = folders[index].PatternIds.Concat(remap.Values).ToArray() };
        }
        else
        {
            var importedFolders = incoming.Folders ?? [new(Guid.NewGuid(), ExportFolderName(path), incoming.Patterns.Select(p => p.Id).ToArray())];
            foreach (var folder in importedFolders)
                folders.Add(new(Guid.NewGuid(), UniqueFolderName(folder.Name, folders), folder.PatternIds.Select(id => remap[id]).ToArray()));
        }
        SaveContents(new(patterns, tags.Values.ToArray(), folders));
        return incoming.Patterns.Count;
    }
}
