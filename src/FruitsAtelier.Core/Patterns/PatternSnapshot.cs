using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core.Patterns;

public sealed record PatternSource(string Artist, string Title, string Difficulty, string Creator, double StartTimeMs,
    long BeatmapId = 0, long BeatmapSetId = -1);

public sealed record PatternTag(string Name, uint Colour);

/// <summary>A detached authoring snapshot. Call CreateDocument to obtain an editable copy.</summary>
public sealed class PatternSnapshot
{
    private readonly MapDocument document;
    public Guid Id { get; }
    public string Name { get; }
    public PatternSource Source { get; }
    public IReadOnlyList<PatternTag> Tags { get; }
    public double StartTimeMs { get; }
    public int ObjectCount => document.Fruits.Count + document.Tracks.Count + document.ImportedSliders.Count + document.BananaShowers.Count;
    public MapDocument CreateDocument() => document.DeepClone();
    public PatternSnapshot WithDocument(MapDocument edited) => new(Id, Name, Source, edited, Tags);
    public PatternSnapshot WithName(string name) => new(Id, name, Source, document, Tags);
    public PatternSnapshot WithTags(IEnumerable<PatternTag> tags) => new(Id, Name, Source, document, tags);

    internal PatternSnapshot(Guid id, string name, PatternSource source, MapDocument document, IEnumerable<PatternTag>? tags = null)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 256 || source is null
            || new[] { source.Artist, source.Title, source.Difficulty, source.Creator }.Any(s => s is null || s.Length > 4096)
            || !double.IsFinite(source.StartTimeMs) || source.StartTimeMs < 0)
            throw new InvalidDataException(L.Get("core.pattern.invalid"));
        var parents = Parents(document).ToArray();
        if (parents.Length is 0 or > 100000 || parents.Select(p => p.Id).Distinct().Count() != parents.Length
            || parents.Any(p => p.Id == Guid.Empty))
            throw new InvalidDataException(L.Get("core.pattern.invalid"));
        Id = id; Name = name.Trim(); Source = source;
        var tagList = tags?.Take(65).ToArray() ?? [];
        if (tagList.Length > 64 || tagList.Any(t => t is null || string.IsNullOrWhiteSpace(t.Name)
            || t.Name.Length > 64 || t.Name.Any(char.IsControl) || t.Colour > 0xFFFFFF)
            || tagList.Select(t => t.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tagList.Length)
            throw new InvalidDataException(L.Get("core.pattern.invalid"));
        Tags = Array.AsReadOnly(tagList.Select(t => t with { Name = t.Name.Trim() }).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        StartTimeMs = parents.Min(p => p.TimeMs);
        this.document = document.DeepClone();
        DetachResources(this.document);
        OsuBeatmapReader.Validate(this.document);
    }

    public static PatternSnapshot Capture(MapDocument document, IEnumerable<Guid> selectedParentIds, string name)
    {
        var selected = selectedParentIds.ToHashSet();
        var parents = Parents(document).OrderBy(p => p.TimeMs).ThenBy(p => p.SourceOrder).ToArray();
        var indices = Enumerable.Range(0, parents.Length).Where(i => selected.Contains(parents[i].Id)).ToArray();
        if (indices.Length == 0 || indices.Length != selected.Count)
            throw new InvalidOperationException(L.Get("core.pattern.selectParents"));
        if (indices[^1] - indices[0] + 1 != indices.Length)
            throw new InvalidOperationException(L.Get("core.pattern.consecutive"));
        var source = new PatternSource(Metadata("Artist"), Metadata("Title", document.Name), Metadata("Version"),
            Metadata("Creator"), parents[indices[0]].TimeMs, MetadataId("BeatmapID", 0), MetadataId("BeatmapSetID", -1));
        var copy = document.DeepClone();
        copy.Fruits.RemoveAll(f => !selected.Contains(f.Id));
        copy.Tracks.RemoveAll(t => !selected.Contains(t.Id));
        copy.ImportedSliders.RemoveAll(s => !selected.Contains(s.Id));
        copy.BananaShowers.RemoveAll(s => !selected.Contains(s.Id));
        copy.HitsoundOverrides.RemoveAll(h => !selected.Contains(h.SourceId));
        copy.CollaborationReferences = null;
        copy.CollaborationEditableRanges = null;
        return new(Guid.NewGuid(), name, source, copy);

        string Metadata(string key, string fallback = "") => OsuBeatmapReader.Setting(document, "Metadata", key) ?? fallback;
        long MetadataId(string key, long fallback) => long.TryParse(Metadata(key), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long value) ? value : fallback;
    }

    private static IEnumerable<(Guid Id, double TimeMs, int SourceOrder)> Parents(MapDocument document)
        => document.Fruits.Select(f => (f.Id, f.TimeMs, f.SourceOrder))
            .Concat(document.Tracks.Where(t => t.Nodes.Count >= 2).Select(t => (t.Id, t.Nodes[0].TimeMs, t.SourceOrder)))
            .Concat(document.ImportedSliders.Select(s => (s.Id, s.TimeMs, s.SourceOrder)))
            .Concat(document.BananaShowers.Select(s => (s.Id, s.TimeMs, s.SourceOrder)));

    private static void DetachResources(MapDocument document)
    {
        document.SourcePath = null;
        document.AudioPath = null;
        document.IsDemo = false;
        document.CollaborationReferences = null;
        document.CollaborationEditableRanges = null;
        // Timing and selected object lines remain authoritative; unrelated map assets and objects do not.
        document.OriginalSections.RemoveAll(s => s.Name is not ("Metadata" or "Difficulty" or "General"));
        foreach (var section in document.OriginalSections.Where(s => s.Name == "General"))
            for (int i = section.Lines.Count - 1; i >= 0; i--)
                if (section.Lines[i].Split(':', 2)[0].Trim() is "AudioFilename" or "AudioLeadIn" or "PreviewTime")
                    section.Lines.RemoveAt(i);
    }
}
