using FruitsAtelier.Core;
namespace FruitsAtelier.App.Platform;

public static class LibraryOperations
{
    public static MapDocument StandaloneExportDocument(ProjectDifficulty difficulty, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException(FruitsAtelier.Localization.Strings.Get("project.invalid"));
        var document = difficulty.Document.DeepClone();
        var metadata = document.OriginalSections.FirstOrDefault(s => s.Name == "Metadata");
        if (metadata is null) { metadata = new OsuSection { Name = "Metadata" }; document.OriginalSections.Add(metadata); }
        metadata.Lines.RemoveAll(line => line.Split(':', 2)[0].Trim() is "Version" or "BeatmapID");
        metadata.Lines.Add("Version:" + name.Trim());
        metadata.Lines.Add("BeatmapID:0");
        return document;
    }

    public static WorkspaceSession ImportPath(string path, LibrarySettings settings)
    {
        path = Path.GetFullPath(path);
        WorkspaceProject.ValidateRoots(settings.Workspace, settings.Songs, false);
        string resources = Path.Combine(settings.Workspace, "Resources");
        var project = BeatmapArchive.OpenProject(path, resources, completeArchive: true);
        var db = new LibraryDatabase(settings.Workspace, settings.Songs);
        string? sourceDirectory = project.Difficulties.Select(d => d.Document.SourcePath).FirstOrDefault(p => p is not null) is { } source
            ? Path.GetDirectoryName(source) : null;
        if (Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase))
        {
            string root = Path.Combine(resources, Path.GetRelativePath(resources, sourceDirectory!).Split(Path.DirectorySeparatorChar)[0]);
            db.RegisterSource(root, path);
        }
        else if (sourceDirectory is not null) db.RegisterSource(sourceDirectory);
        // Source association survives restarts and re-imports without replacing authored edits.
        if (!Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase)
            && sourceDirectory is not null && db.ProjectForSource(sourceDirectory) is { } existing)
            return WorkspaceProject.Open(existing);
        return WorkspaceProject.Create(settings.Workspace, project, settings.Songs) with { IsNewImport = !Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase) };
    }
    public static void ImportFolder(string directory, LibrarySettings settings)
        => new LibraryDatabase(settings.Workspace, settings.Songs).RegisterSource(directory);

    public static WorkspaceSession Open(LibraryMap map, LibrarySettings settings)
    {
        if (map.ProjectPath is not null) return WorkspaceProject.Open(map.ProjectPath);
        // A library card may predate the project's creation or the next database scan.
        if (new LibraryDatabase(settings.Workspace, settings.Songs).ProjectForSource(map.Directory) is { } existing)
            return WorkspaceProject.Open(existing);
        var documents = Directory.EnumerateFiles(map.Directory).Where(p => Path.GetExtension(p).Equals(".osu", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)
            .Where(p => LibraryDatabase.ReadMetadata(p) is not null).Select(OsuBeatmapReader.ReadFile).ToArray();
        return WorkspaceProject.Create(settings.Workspace, BeatmapProject.FromDocuments(documents), settings.Songs) with { IsNewImport = true };
    }
    public static void Export(WorkspaceSession session, BeatmapProject project, WorkspaceExportPlan plan)
    {
        // All conversion and conflict checks have completed before touching Songs.
        string folder = Path.GetDirectoryName(plan.Target)!;
        Directory.CreateDirectory(folder);
        BeatmapResources.Copy(plan.Document, folder, plan.Output.ReadBack);
        WorkspaceExport.Commit(session, plan);
        WorkspaceProject.Save(session, project);
    }
}
