using System.Text.Json;
using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.Core;

public static partial class ProjectSerializer
{
    private sealed class MultiProjectFile
    {
        public int SchemaVersion { get; set; }
        public BeatmapProject? Project { get; set; }
    }

    public static string Serialize(BeatmapProject project, string? projectPath = null)
    {
        project.Validate();
        var copy = new BeatmapProject { Name = project.Name };
        foreach (var diff in project.Difficulties)
        {
            // Reuse schema 1's validation and relative resource path handling.
            var document = Read(Serialize(diff.Document, projectPath));
            copy.Difficulties.Add(new ProjectDifficulty { Id = diff.Id, Name = diff.Name, Document = document });
        }
        string text = JsonSerializer.Serialize(new MultiProjectFile { SchemaVersion = 2, Project = copy }, options);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > OsuBeatmapReader.MaximumFileBytes)
            throw new InvalidDataException(L.Get("core.project.writeLimit"));
        return text;
    }

    public static BeatmapProject ReadProject(string text, string? projectPath = null)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > OsuBeatmapReader.MaximumFileBytes)
            throw new InvalidDataException(L.Get("core.project.readLimit"));
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException(L.Get("core.project.schema"));
            if (json.RootElement.TryGetProperty("SchemaVersion", out var version) && version.TryGetInt32(out int schema) && schema == 1)
                return BeatmapProject.FromDocuments([Read(text, projectPath)]);
            var file = JsonSerializer.Deserialize<MultiProjectFile>(text, options);
            if (file?.SchemaVersion != 2 || file.Project is null) throw new InvalidDataException(L.Get("core.project.schema"));
            file.Project.Validate();
            foreach (var diff in file.Project.Difficulties)
                diff.Document = Read(Serialize(diff.Document), projectPath);
            return file.Project;
        }
        catch (JsonException error) { throw new InvalidDataException(L.Get("core.project.invalidJson"), error); }
    }

    public static BeatmapProject ReadProjectFile(string path)
    {
        if (new FileInfo(path).Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.project.readLimit"));
        return ReadProject(File.ReadAllText(path), path);
    }

    public static void WriteFile(BeatmapProject project, string path)
    {
        project.Validate();
        foreach (var diff in project.Difficulties)
            if (diff.Document.SourcePath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(diff.Document.SourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.Get("core.project.sourceOverwrite"));
        AtomicFile.Write(path, Serialize(project, path));
    }
}
