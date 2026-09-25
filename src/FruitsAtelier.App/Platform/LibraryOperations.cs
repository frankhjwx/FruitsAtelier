using FruitsAtelier.Core;
using System.IO.Compression;
using System.Text;
namespace FruitsAtelier.App.Platform;

public static class LibraryOperations
{
    public static void ExportOsz(BeatmapProject project, string destination, bool compensate)
    {
        // macOS's default /var temp path traverses a symlink rejected by resource copying.
        string temporaryRoot = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        string staging = Path.Combine(temporaryRoot, "FruitsAtelier-osz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var difficulty in project.Difficulties)
            {
                var document = StandaloneExportDocument(difficulty, difficulty.Name);
                var output = OsuBeatmapWriter.Serialize(document, compensate);
                string name = WorkspaceProject.DifficultyFileName(document, difficulty.Name, ".osu");
                if (!names.Add(name)) throw new IOException(FruitsAtelier.Localization.Strings.Get("library.exportExists", name));
                BeatmapResources.Copy(document, staging, output.ReadBack);
                CopyArchiveExtras(document, staging);
                File.WriteAllText(Path.Combine(staging, name), output.Text, new UTF8Encoding(false));
            }
            long total = 0;
            int count = 0;
            foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                if (++count > 20000) throw new InvalidDataException(FruitsAtelier.Localization.Strings.Get("archive.fileCount"));
                long size = new FileInfo(file).Length;
                long maximum = Path.GetExtension(file).Equals(".osu", StringComparison.OrdinalIgnoreCase)
                    ? 16L * 1024 * 1024 : 256L * 1024 * 1024;
                if (size > maximum || (total += size) > 512L * 1024 * 1024)
                    throw new InvalidDataException(FruitsAtelier.Localization.Strings.Get("archive.expansionLimit"));
            }
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ZipFile.CreateFromDirectory(staging, temporary, CompressionLevel.Optimal, false);
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { Directory.Delete(staging, true); }
    }

    private static void CopyArchiveExtras(MapDocument document, string staging)
    {
        if (document.SourcePath is not { } source) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(source))!;
        if (!Directory.Exists(root)) return;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".osb", ".mp4", ".avi", ".mkv", ".webm", ".mov", ".flv", ".mpg", ".mpeg" };
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string file in Directory.EnumerateFiles(root, "*", options))
        {
            if (!extensions.Contains(Path.GetExtension(file))) continue;
            string relative = BeatmapArchive.ValidateRelativePath(Path.GetRelativePath(root, file));
            string target = BeatmapArchive.Within(staging, relative);
            if (File.Exists(target))
            {
                if (WorkspaceProject.Hash(file) != WorkspaceProject.Hash(target))
                    throw new IOException(FruitsAtelier.Localization.Strings.Get("resource.conflict", target));
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public static BeatmapProject ExportProject(LibraryMap map)
    {
        if (map.ProjectPath is { } project) return WorkspaceProject.Open(project).Project;
        var documents = Directory.EnumerateFiles(map.Directory, "*.osu")
            .Where(path =>
            {
                bool general = false;
                foreach (string line in File.ReadLines(path))
                {
                    string value = line.Trim();
                    if (value.StartsWith('[')) general = value == "[General]";
                    else if (general && value.Split(':', 2) is [var key, var setting] && key.Trim() == "Mode")
                        return setting.Trim() == "2";
                }
                return false;
            })
            .Select(OsuBeatmapReader.ReadFile).ToArray();
        if (documents.Length == 0) throw new InvalidDataException(FruitsAtelier.Localization.Strings.Get("project.noCatch"));
        return BeatmapProject.FromDocuments(documents);
    }

    public static void DeleteProject(string projectPath, LibrarySettings settings)
    {
        string projects = settings.Workspace;
        string project = Path.GetFullPath(projectPath);
        if (!WorkspaceProject.Within(projects, project) || project == Path.GetFullPath(projects))
            throw new IOException(FruitsAtelier.Localization.Strings.Get("library.deleteUnavailable"));
        WorkspaceProject.RejectLinks(project);
        var manifest = WorkspaceProject.ReadManifest(project);
        if (WorkspaceProject.HasExistingSongsFile(manifest, settings.Songs))
            throw new IOException(FruitsAtelier.Localization.Strings.Get("library.deleteUnavailable"));
        Directory.Delete(project, true);
    }
    public static void OpenExternalPath(string path)
    {
        if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }
        if (!File.Exists(path)) throw new FileNotFoundException(null, path);
        var start = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "notepad.exe") { UseShellExecute = false };
        if (OperatingSystem.IsMacOS()) start.ArgumentList.Add("-t");
        start.ArgumentList.Add(Path.GetFullPath(path));
        System.Diagnostics.Process.Start(start);
    }
    public static IReadOnlyList<LibraryMap> MissingDifficulties(WorkspaceSession session, BeatmapProject project)
    {
        var known = session.Manifest.Difficulties.Select(d => d.Source)
            .Concat(project.Difficulties.Select(d => d.Document.SourcePath)).Where(p => p is not null)
            .Select(p => Path.GetFullPath(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = session.Manifest.Difficulties.SelectMany(d => new[] { d.Source, d.ExportTarget }).Where(p => p is not null)
            .Select(p => Path.GetDirectoryName(p!)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (session.Manifest.ExternalSourceDirectory is { } external) folders.Add(external);
        if (session.Manifest.SongsRoot is { } root && session.Manifest.SourceDirectory is { } relative)
            folders.Add(Path.Combine(root, relative));
        return folders.Where(Directory.Exists).SelectMany(Directory.EnumerateFiles)
            .Where(p => Path.GetExtension(p).Equals(".osu", StringComparison.OrdinalIgnoreCase) && !known.Contains(Path.GetFullPath(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(LibraryDatabase.ReadMetadata).OfType<LibraryMap>().ToArray();
    }

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
        ProjectDifficulty? added = null;
        if (plan.ExpectedHash is null)
        {
            var document = plan.Document.DeepClone();
            document.SourcePath = plan.Target;
            added = BeatmapProject.FromDocuments([document]).Difficulties.Single();
            var saved = WorkspaceProject.Open(session.Directory).Project.Difficulties.FirstOrDefault(d => d.Id == plan.DifficultyId);
            int originalIndex = project.Difficulties.FindIndex(d => d.Id == plan.DifficultyId);
            if (saved is not null) project.Difficulties[originalIndex] = saved;
            else project.Difficulties.RemoveAt(originalIndex);
            project.Difficulties.Add(added);
            project.Validate();
        }
        // All conversion and conflict checks have completed before touching Songs.
        string folder = Path.GetDirectoryName(plan.Target)!;
        Directory.CreateDirectory(folder);
        BeatmapResources.Copy(plan.Document, folder, plan.Output.ReadBack);
        WorkspaceExport.Commit(session, plan, updateAssociation: added is null);
        if (added is not null)
        {
            string hash = WorkspaceProject.Hash(plan.Target);
            session.Manifest.Difficulties.Add(new WorkspaceDifficulty
            {
                Id = added.Id, Name = added.Name, Source = plan.Target, SourceHash = hash,
                ExportTarget = plan.Target, ExportHash = hash
            });
        }
        WorkspaceProject.Save(session, project);
    }
}
