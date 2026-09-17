using L = FruitsAtelier.Localization.Strings;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FruitsAtelier.Core;

public static partial class ProjectSerializer
{
    private sealed class ProjectFile
    {
        public int SchemaVersion { get; set; }
        public MapDocument? Document { get; set; }
    }

    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    public static string Serialize(MapDocument document, string? projectPath = null)
    {
        OsuBeatmapReader.Validate(document);
        RejectNetworkPath(document.AudioPath);
        RejectNetworkPath(document.SourcePath);
        var copy = document.DeepClone();
        if (projectPath is not null)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            if (copy.AudioPath is not null && Path.IsPathFullyQualified(copy.AudioPath)) copy.AudioPath = Path.GetRelativePath(directory, copy.AudioPath);
            if (copy.SourcePath is not null && Path.IsPathFullyQualified(copy.SourcePath)) copy.SourcePath = Path.GetRelativePath(directory, copy.SourcePath);
        }
        string text = JsonSerializer.Serialize(new ProjectFile { SchemaVersion = HasControlCurves(copy) ? 3 : 1, Document = copy }, options);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > OsuBeatmapReader.MaximumFileBytes)
            throw new InvalidDataException(L.Get("core.project.writeLimit"));
        return text;
    }

    public static MapDocument Read(string text, string? projectPath = null)
    {
        if (text.Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.project.readLimit"));
        ProjectFile? file;
        try { file = JsonSerializer.Deserialize<ProjectFile>(text, options); }
        catch (JsonException error) { throw new InvalidDataException(L.Get("core.project.invalidJson"), error); }
        if (file?.SchemaVersion is not (1 or 3) || file.Document is null) throw new InvalidDataException(L.Get("core.project.schema"));
        var document = file.Document;
        RejectNetworkPath(document.AudioPath);
        RejectNetworkPath(document.SourcePath);
        if (projectPath is not null)
        {
            if (document.AudioPath is not null) document.AudioPath = OsuBeatmapReader.ResolveResource(projectPath, document.AudioPath);
            if (document.SourcePath is not null) document.SourcePath = OsuBeatmapReader.ResolveResource(projectPath, document.SourcePath);
        }
        OsuBeatmapReader.Validate(document);
        return document;
    }

    public static MapDocument ReadFile(string path)
    {
        if (new FileInfo(path).Length > OsuBeatmapReader.MaximumFileBytes) throw new InvalidDataException(L.Get("core.project.readLimit"));
        return Read(File.ReadAllText(path), path);
    }

    public static void WriteFile(MapDocument document, string path)
    {
        if (document.SourcePath is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(document.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.Get("core.project.sourceOverwrite"));
        AtomicFile.Write(path, Serialize(document, path));
    }

    private static bool HasControlCurves(MapDocument document) => document.Tracks.Any(t => t.Nodes.Any(n => n.OutgoingCurve is not null));

    private static void RejectNetworkPath(string? path)
    {
        if (path?.Replace('\\', '/').StartsWith("//", StringComparison.Ordinal) == true)
            throw new InvalidDataException(L.Get("core.project.networkPath"));
    }
}

internal static class AtomicFile
{
    internal static void Write(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, text, new System.Text.UTF8Encoding(false));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
