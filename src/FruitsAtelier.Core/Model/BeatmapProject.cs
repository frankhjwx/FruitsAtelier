using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public sealed class BeatmapProject
{
    public string Name { get; set; } = L.Get("core.names.untitled");
    public List<ProjectDifficulty> Difficulties { get; set; } = [];

    public static BeatmapProject FromDocuments(IEnumerable<MapDocument> documents)
    {
        var project = new BeatmapProject();
        foreach (var document in documents)
        {
            string? version = OsuBeatmapReader.Setting(document, "Metadata", "Version");
            project.Difficulties.Add(new ProjectDifficulty { Name = string.IsNullOrWhiteSpace(version)
                ? L.Get("project.defaultDifficulty", project.Difficulties.Count + 1) : version, Document = document });
        }
        if (project.Difficulties.Count > 0 && !string.IsNullOrWhiteSpace(project.Difficulties[0].Document.Name))
            project.Name = project.Difficulties[0].Document.Name;
        project.Validate();
        return project;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Difficulties is null || Difficulties.Count is < 1 or > 256
            || Difficulties.Any(d => d is null || d.Id == Guid.Empty || string.IsNullOrWhiteSpace(d.Name) || d.Document is null)
            || Difficulties.Select(d => d.Id).Distinct().Count() != Difficulties.Count)
            throw new InvalidDataException(L.Get("project.invalid"));
        foreach (var difficulty in Difficulties) OsuBeatmapReader.Validate(difficulty.Document);
    }
}

public sealed class ProjectDifficulty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public MapDocument Document { get; set; } = new();
}
