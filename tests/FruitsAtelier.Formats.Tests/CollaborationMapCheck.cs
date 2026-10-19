using System.Text.Json;
using FruitsAtelier.Core;

internal static class CollaborationMapCheck
{
    internal static void Sequence(string directory)
    {
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(Path.Combine(directory, WorkspaceProject.ManifestName)))!;
        var project = new BeatmapProject { Name = manifest.Name, Difficulties = manifest.Difficulties.Select(d => new ProjectDifficulty
        {
            Id = d.Id, Name = d.Name, Document = ProjectSerializer.ReadFile(Path.Combine(directory, d.File))
        }).ToList() };
        foreach (var difficulty in project.Difficulties) WorkspaceTimingSynchronization.NormalizeDocument(difficulty.Document);
        var template = project.Difficulties.First(d => d.Name == "Rain").Document.DeepClone();
        template.Fruits.Clear(); template.Tracks.Clear(); template.ImportedSliders.Clear(); template.BananaShowers.Clear(); template.HitsoundOverrides.Clear();
        template.CollaborationReferences = null; template.SliderTickRate = 2;
        var target = new ProjectDifficulty { Name = "Merged sequence", Document = template }; project.Difficulties.Add(target);
        foreach (var source in project.Difficulties.Where(d => d.Id != target.Id).ToArray())
        {
            target.Document.CollaborationReferences = null;
            var refs = new[] { new CollaborationReference(Guid.NewGuid(), source.Id, 0, Math.Ceiling(source.Document.DurationMs)) };
            target.Document = CollaborationMapping.Synchronize(project, target.Id, refs).Document;
            Console.WriteLine("PASS read-only replace references with " + source.Name);
        }
    }
    internal static void Run(string targetPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(Path.Combine(directory, WorkspaceProject.ManifestName)))!;
        var project = new BeatmapProject { Name = manifest.Name, Difficulties = manifest.Difficulties.Select(d => new ProjectDifficulty
        {
            Id = d.Id, Name = d.Name, Document = ProjectSerializer.ReadFile(Path.Combine(directory, d.File))
        }).ToList() };
        foreach (var difficulty in project.Difficulties) WorkspaceTimingSynchronization.NormalizeDocument(difficulty.Document);
        var target = project.Difficulties.Single(d => manifest.Difficulties.Single(m => m.Id == d.Id).File == Path.GetFileName(targetPath));
        var output = CollaborationMapping.Synchronize(project, target.Id, target.Document.CollaborationReferences ?? []).Document;
        Console.WriteLine($"PASS read-only collaboration {target.Name}: {output.Fruits.Count} fruits, {output.Tracks.Count} tracks, {output.ImportedSliders.Count} Legacy sliders");
    }
}
