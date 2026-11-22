using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

internal static class WorkspaceStoryboardAudioHistory
{
    internal static void CaptureSaved(WorkspaceSession session, string directory)
    {
        var manifest = WorkspaceProject.ReadManifest(session.Directory, includeSync: false);
        var project = new BeatmapProject();
        foreach (var entry in manifest.Difficulties)
        {
            if (Path.GetFileName(entry.File) != entry.File) throw new InvalidDataException(L.Get("project.invalid"));
            string path = Path.Combine(session.Directory, entry.File);
            WorkspaceProject.RejectLinks(path);
            if (!ContainsAudioRegistration(path)) continue;
            project.Difficulties.Add(new() { Id = entry.Id, Name = entry.Name, Document = ProjectSerializer.ReadFile(path) });
        }
        Capture(project, directory);
    }

    internal static void Capture(BeatmapProject project, string directory)
    {
        lock (WorkspaceProject.Gate)
        {
            foreach (var difficulty in project.Difficulties)
            {
                var document = difficulty.Document;
                if (document.StoryboardAudio.Count == 0) continue;
                string? root = Path.GetDirectoryName(document.SourcePath ?? document.AudioPath);
                if (root is null) continue;
                string text = string.Join("\n", document.OriginalSections.Where(s => s.Name is "Events" or "Variables")
                    .SelectMany(s => new[] { "[" + s.Name + "]" }.Concat(s.Lines)));
                var used = StoryboardReader.Read(text, null, root, samplesOnly: true).Samples
                    .Select(s => Path.GetRelativePath(root, s.Path).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var (relative, audio) in document.StoryboardAudio.Where(p => used.Contains(p.Key)))
                {
                    string saved = Path.Combine(root, relative);
                    byte[]? data = audio.Data;
                    if (data is null)
                    {
                        string source = File.Exists(saved) ? saved
                            : audio.HistoryPath is { } history && File.Exists(history) ? history : audio.SourcePath;
                        WorkspaceProject.RejectLinks(source);
                        audio.Cache(saved);
                        data = audio.Data;
                    }
                    if (data is not { Length: > 0 }) continue;
                    string target = ResourcePath(directory, difficulty.Id, relative);
                    if (File.Exists(target))
                    {
                        if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(data)) throw new IOException(L.Get("resource.conflict", target));
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllBytes(temporary, data); File.Move(temporary, target); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
        }
    }

    internal static void Restore(BeatmapProject project, string directory)
    {
        lock (WorkspaceProject.Gate)
            foreach (var difficulty in project.Difficulties)
                foreach (var (relative, audio) in difficulty.Document.StoryboardAudio.ToArray())
                {
                    string stored = ResourcePath(directory, difficulty.Id, relative);
                    if (File.Exists(stored)) difficulty.Document.StoryboardAudio[relative] = audio with { HistoryPath = stored };
                }
    }

    private static string ResourcePath(string directory, Guid id, string relative)
    {
        if (!StoryboardAudio.ValidRelativePath(relative)) throw new InvalidDataException(L.Get("project.invalid"));
        string path = Path.GetFullPath(Path.Combine(directory, "storyboard-audio", id.ToString("N"), relative));
        if (!WorkspaceProject.Within(directory, path)) throw new InvalidDataException(L.Get("project.invalid"));
        WorkspaceProject.RejectLinks(path);
        return path;
    }

    private static bool ContainsAudioRegistration(string path)
    {
        const string property = "\"StoryboardAudio\"";
        using var reader = new StreamReader(path);
        char[] buffer = new char[4096 + property.Length];
        int retained = 0, count;
        while ((count = reader.Read(buffer, retained, 4096)) > 0)
        {
            int length = retained + count;
            if (((ReadOnlySpan<char>)buffer.AsSpan(0, length)).Contains(property, StringComparison.Ordinal)) return true;
            retained = Math.Min(property.Length - 1, length);
            buffer.AsSpan(length - retained, retained).CopyTo(buffer);
        }
        return false;
    }
}
