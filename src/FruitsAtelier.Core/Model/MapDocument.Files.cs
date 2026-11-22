namespace FruitsAtelier.Core;

public sealed class OsuSection
{
    public string Name { get; set; } = "";
    public OsuSectionLines Lines { get; private init; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RemoveWhenEmpty { get; set; }

    internal OsuSection DeepClone() => new() { Name = Name, Lines = Lines.DeepClone(), RemoveWhenEmpty = RemoveWhenEmpty };
}

public sealed partial class MapDocument
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? ImportedContentHash { get; set; }
    public string? SourcePath { get; set; }
    public string? AudioPath { get; set; }
    public bool IsDemo { get; set; } = true;
    public List<OsuSection> OriginalSections { get; } = [];

    private void CopyFileStateTo(MapDocument copy)
    {
        copy.SourcePath = SourcePath;
        copy.ImportedContentHash = ImportedContentHash;
        copy.AudioPath = AudioPath;
        copy.IsDemo = IsDemo;
        foreach (var section in OriginalSections)
            copy.OriginalSections.Add(section.DeepClone());
    }

    // A generated audio resource root does not change a map's authored source.
    private string? ContentSourcePath => StoryboardAudio.Count > 0 && Path.GetFileName(SourcePath) == WorkspaceProject.ResourceRootMarker ? null : SourcePath;

    private bool FileStateEquals(MapDocument other) => ContentSourcePath == other.ContentSourcePath
        && AudioPath == other.AudioPath && IsDemo == other.IsDemo
        && OriginalSections.Count == other.OriginalSections.Count
        && OriginalSections.Zip(other.OriginalSections).All(pair => pair.First.Name == pair.Second.Name
            && pair.First.Lines.ContentEquals(pair.Second.Lines));
}
