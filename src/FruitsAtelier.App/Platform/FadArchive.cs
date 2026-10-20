using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

public static class FadArchive
{
    private const string Format = "FruitsAtelierDifficulty";
    public static bool IsPackage(string path) => Path.GetExtension(path).Equals(".fad", StringComparison.OrdinalIgnoreCase);

    public static void Export(ProjectDifficulty difficulty, string destination, bool compensate, string workspace)
        => FazArchive.ExportPackage(new BeatmapProject { Name = difficulty.Document.Name, Difficulties = [difficulty] },
            destination, false, compensate, workspace, Format);

    public static FazPackage Prepare(string path, LibrarySettings settings)
    {
        var package = FazArchive.PreparePackage(path, settings, false, Format);
        if (!package.Manifest.Complete && package.Project.Difficulties.Count == 1) return package;
        package.Dispose();
        throw new InvalidDataException(L.Get("fad.invalid"));
    }

    public static ProjectDifficulty ResolveTarget(FazPackage package, BeatmapProject project)
    {
        var incoming = package.Project.Difficulties.Single();
        var target = project.Difficulties.FirstOrDefault(d => d.Id == incoming.Id);
        if (target is not null) return target;
        return project.Difficulties.FirstOrDefault(d => SameSong(incoming.Document, d.Document))
            ?? throw new InvalidDataException(L.Get("fad.wrongProject"));
    }

    private static bool SameSong(MapDocument incoming, MapDocument local)
    {
        string Setting(MapDocument d, string key) => OsuBeatmapReader.Setting(d, "Metadata", key)?.Trim() ?? "";
        string set = Setting(incoming, "BeatmapSetID");
        if (long.TryParse(set, out long id) && id > 0 && set == Setting(local, "BeatmapSetID")) return true;
        return Setting(local, "Title").Length > 0 && Setting(local, "Artist").Length > 0
            && string.Equals(Setting(local, "Title"), Setting(incoming, "Title"), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Setting(local, "Artist"), Setting(incoming, "Artist"), StringComparison.OrdinalIgnoreCase);
    }

    public static (ProjectDifficulty Difficulty, HitsoundResourcePlan Resources) PlanImport(FazPackage package, BeatmapProject project)
    {
        var incoming = package.Project.Difficulties.Single();
        var target = ResolveTarget(package, project);
        bool replacing = target.Id == incoming.Id;
        if (!replacing)
        {
            string? beatmap = OsuBeatmapReader.Setting(incoming.Document, "Metadata", "BeatmapID");
            var eligible = project.Difficulties.Where(d => SameSong(incoming.Document, d.Document)).ToArray();
            var matches = eligible.Where(d => long.TryParse(beatmap, out long id) && id > 0
                && OsuBeatmapReader.Setting(d.Document, "Metadata", "BeatmapID") == beatmap).ToArray();
            if (matches.Length == 0) matches = eligible.Where(d => d.Name == incoming.Name).ToArray();
            if (matches.Length > 1) throw new InvalidDataException(L.Get("fad.nameConflict"));
            if (matches.Length == 1) { target = matches[0]; replacing = true; }
        }
        if (!replacing && (project.Difficulties.Count >= 256 || project.Difficulties.Any(d => d.Name == incoming.Name)))
            throw new InvalidDataException(L.Get("fad.nameConflict"));
        var candidate = incoming.Document.DeepClone();
        candidate.SourcePath = target.Document.SourcePath;
        candidate.AudioPath = target.Document.AudioPath;
        if (OsuBeatmapReader.Setting(target.Document, "General", "AudioFilename") is { } audio)
            SongSetup.Set(candidate, "General", "AudioFilename", audio);
        string destination = Path.GetDirectoryName(target.Document.SourcePath ?? target.Document.AudioPath)
            ?? throw new InvalidOperationException(L.Get("copier.saveFirst"));
        if (!replacing) candidate.SourcePath = Path.Combine(destination, ".fa-resource");
        string root = Path.GetDirectoryName(incoming.Document.SourcePath)!;
        var paths = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !WorkspaceSynchronization.Paths.Equals(p, incoming.Document.AudioPath))
            .Select(p => Path.GetRelativePath(root, p));
        var resources = HitsoundResourcePlan.CopyFiles(root, destination, paths);
        var result = new ProjectDifficulty { Id = replacing ? target.Id : incoming.Id, Name = incoming.Name, Document = candidate, FaOnly = replacing ? target.FaOnly : incoming.FaOnly };
        var check = new BeatmapProject { Name = project.Name, Difficulties = project.Difficulties.Where(d => d.Id != result.Id).Append(result).ToList() };
        check.Validate();
        foreach (var d in check.Difficulties)
            if (d.Document.CollaborationReferences is { Count: > 0 } references) CollaborationMapping.ValidateReferences(check, d.Id, references);
        return (result, resources);
    }
}
