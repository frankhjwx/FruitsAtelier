using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

public static class FadArchive
{
    private const string Format = "FruitsAtelierDifficulty";
    public static bool IsPackage(string path) => Path.GetExtension(path).Equals(".fad", StringComparison.OrdinalIgnoreCase);

    public static void Export(ProjectDifficulty difficulty, string destination, bool compensate, string workspace)
        => FazArchive.ExportPackage(new BeatmapProject { Name = difficulty.Document.Name, Difficulties = [difficulty] },
            destination, false, compensate, workspace, Format, includeResources: false);

    public static FazPackage Prepare(string path, LibrarySettings settings)
    {
        var package = FazArchive.PreparePackage(path, settings, false, Format);
        if (!package.Manifest.Complete && package.Project.Difficulties.Count == 1) return package;
        package.Dispose();
        throw new InvalidDataException(L.Get("fad.invalid"));
    }

    private static (ProjectDifficulty Target, bool Replacing) ResolveTarget(ProjectDifficulty incoming, BeatmapProject project)
    {
        var target = project.Difficulties.FirstOrDefault(d => d.Id == incoming.Id);
        if (target is not null) return (target, true);
        if (OnlineId(incoming.Document, "BeatmapID") is { } beatmapId)
        {
            target = SingleMatch(project.Difficulties.Where(d => OnlineId(d.Document, "BeatmapID") == beatmapId));
            if (target is not null) return (target, true);
        }
        var sameSong = project.Difficulties.Where(d => SameSong(incoming.Document, d.Document)).ToArray();
        if (sameSong.Length == 0) throw new InvalidDataException(L.Get("fad.wrongProject"));
        target = SingleMatch(sameSong.Where(d => d.Name == incoming.Name));
        if (target is not null) return (target, true);
        target = sameSong.FirstOrDefault(d => Directory.Exists(ResourceDirectory(d.Document)))
            ?? sameSong.FirstOrDefault(d => ResourceDirectory(d.Document) is not null)
            ?? sameSong[0];
        return (target, false);
    }

    private static ProjectDifficulty? SingleMatch(IEnumerable<ProjectDifficulty> candidates)
    {
        var matches = candidates.Take(2).ToArray();
        if (matches.Length > 1) throw new InvalidDataException(L.Get("fad.nameConflict"));
        return matches.FirstOrDefault();
    }

    private static long? OnlineId(MapDocument document, string key)
        => long.TryParse(OsuBeatmapReader.Setting(document, "Metadata", key), out long id) && id > 0 ? id : null;

    private static bool SameSong(MapDocument incoming, MapDocument local)
    {
        string Setting(MapDocument d, string key) => OsuBeatmapReader.Setting(d, "Metadata", key)?.Trim() ?? "";
        if (OnlineId(incoming, "BeatmapSetID") is { } setId && OnlineId(local, "BeatmapSetID") == setId) return true;
        return Setting(local, "Title").Length > 0 && Setting(local, "Artist").Length > 0
            && string.Equals(Setting(local, "Title"), Setting(incoming, "Title"), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Setting(local, "Artist"), Setting(incoming, "Artist"), StringComparison.OrdinalIgnoreCase);
    }

    public static (ProjectDifficulty Difficulty, HitsoundResourcePlan Resources, int UnassignedSections) PlanImport(FazPackage package, BeatmapProject project)
    {
        var incoming = package.Project.Difficulties.Single();
        var (target, replacing) = ResolveTarget(incoming, project);
        if (!replacing && (project.Difficulties.Count >= 256 || project.Difficulties.Any(d => d.Name == incoming.Name)))
            throw new InvalidDataException(L.Get("fad.nameConflict"));
        Guid resultId = replacing ? target.Id : incoming.Id;
        var candidate = incoming.Document.DeepClone();
        var availableReferences = project.Difficulties.Where(d => d.Id != resultId).Select(d => d.Id).ToHashSet();
        int unassignedSections = UnassignUnavailableSections(candidate, availableReferences);
        candidate.SourcePath = target.Document.SourcePath;
        candidate.AudioPath = target.Document.AudioPath;
        if (OsuBeatmapReader.Setting(target.Document, "General", "AudioFilename") is { } audio)
            SongSetup.Set(candidate, "General", "AudioFilename", audio);
        string? destination = ResourceDirectory(target.Document);
        if (!replacing) candidate.SourcePath = destination is null ? null : Path.Combine(destination, WorkspaceProject.ResourceRootMarker);
        string root = Path.GetDirectoryName(incoming.Document.SourcePath)!;
        var paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !WorkspaceSynchronization.Paths.Equals(p, incoming.Document.AudioPath))
            .Select(p => Path.GetRelativePath(root, p))
            .Where(p => p != WorkspaceProject.ResourceRootMarker).ToArray();
        var resources = paths.Length == 0 ? HitsoundResourcePlan.Empty : HitsoundResourcePlan.CopyFiles(root,
            destination ?? throw new InvalidOperationException(L.Get("copier.saveFirst")), paths);
        var result = new ProjectDifficulty { Id = resultId, Name = incoming.Name, Document = candidate, FaOnly = replacing ? target.FaOnly : incoming.FaOnly };
        var check = new BeatmapProject { Name = project.Name, Difficulties = project.Difficulties.Where(d => d.Id != result.Id).Append(result).ToList() };
        check.Validate();
        return (result, resources, unassignedSections);
    }

    internal static string? ResourceDirectory(MapDocument document)
        => Path.GetDirectoryName(document.SourcePath ?? document.AudioPath) is { Length: > 0 } directory ? directory : null;

    internal static int UnassignUnavailableSections(MapDocument document, HashSet<Guid> availableReferences)
    {
        if (document.CollaborationReferences is not { } references) return 0;
        int count = 0;
        for (int i = 0; i < references.Count; i++)
        {
            var reference = references[i];
            if (reference.DifficultyId == Guid.Empty || availableReferences.Contains(reference.DifficultyId)) continue;
            references[i] = reference with { DifficultyId = Guid.Empty };
            count++;
        }
        return count;
    }
}
