using FruitsAtelier.Core;
namespace FruitsAtelier.App.Platform;

public static class LibraryOperations
{
    public static WorkspaceSession Open(LibraryMap map, LibrarySettings settings)
    {
        if (map.ProjectPath is not null) return WorkspaceProject.Open(map.ProjectPath);
        var documents = Directory.EnumerateFiles(map.Directory).Where(p => Path.GetExtension(p).Equals(".osu", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)
            .Where(p => LibraryDatabase.ReadMetadata(p) is not null).Select(OsuBeatmapReader.ReadFile).ToArray();
        return WorkspaceProject.Create(settings.Workspace, BeatmapProject.FromDocuments(documents), settings.Songs);
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
