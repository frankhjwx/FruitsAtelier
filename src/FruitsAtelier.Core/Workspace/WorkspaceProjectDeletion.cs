namespace FruitsAtelier.Core;

internal static class WorkspaceProjectDeletion
{
    internal static void Delete(WorkspaceSession session, Action<IReadOnlyList<string>> recycle)
    {
        string workspace = Path.GetDirectoryName(session.Directory)!;
        string resources = Path.Combine(workspace, "Resources");
        string history = Path.Combine(workspace, ".sync-history", session.Manifest.Id.ToString("N"));
        var candidates = new HashSet<string>(WorkspaceSynchronization.Paths);
        foreach (var diff in session.Project.Difficulties)
            foreach (string? path in new[] { diff.Document.SourcePath, diff.Document.AudioPath })
                if (path is not null && WorkspaceProject.Within(resources, path))
                {
                    string name = Path.GetRelativePath(resources, path).Split(Path.DirectorySeparatorChar)[0];
                    if (name.StartsWith("FAZ-", StringComparison.Ordinal) || name.StartsWith("Audio-", StringComparison.Ordinal))
                        candidates.Add(Path.Combine(resources, name));
                }
        string owned = Path.Combine(resources, "FAZ-" + session.Manifest.Id.ToString("N"));
        if (Directory.Exists(owned)) candidates.Add(owned);

        // Retained snapshots can still depend on a shared resource directory.
        try
        {
            if (candidates.Count > 0)
            foreach (string file in Directory.EnumerateFiles(workspace, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
            {
                if (WorkspaceProject.Within(session.Directory, file) || WorkspaceProject.Within(history, file)) continue;
                string logical = WorkspaceHistoryFile.LogicalPath(file);
                if (Path.GetExtension(logical) is not (".catchdiff" or ".catchproj")) continue;
                using var json = System.Text.Json.JsonDocument.Parse(WorkspaceHistoryFile.ReadText(file));
                PreserveReferences(json.RootElement, logical);
                if (candidates.Count == 0) break;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            candidates.Clear();
        }
        var paths = new List<string>();
        Add(session.Directory, workspace);
        Add(history, Path.Combine(workspace, ".sync-history"));
        foreach (string resource in candidates) Add(resource, resources);
        recycle(paths);

        void Add(string path, string root)
        {
            if (!WorkspaceProject.Within(root, path) || WorkspaceSynchronization.Paths.Equals(root, path))
                throw new IOException(FruitsAtelier.Localization.Strings.Get("library.deleteUnavailable"));
            if (!Directory.Exists(path)) return;
            ValidateTree(path);
            paths.Add(path);
        }

        void PreserveReferences(System.Text.Json.JsonElement value, string file)
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) PreserveReferences(item, file);
            if (value.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "SourcePath" or "AudioPath" or "Source" or "ExportTarget"
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.String && property.Value.GetString() is { Length: > 0 } path)
                {
                    string absolute = Path.GetFullPath(path, Path.GetDirectoryName(file)!);
                    candidates.RemoveWhere(root => WorkspaceProject.Within(root, absolute));
                }
                else PreserveReferences(property.Value, file);
            }
        }
    }

    private static void ValidateTree(string path)
    {
        WorkspaceProject.RejectLinks(path);
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            WorkspaceProject.RejectLinks(entry);
            if (Directory.Exists(entry)) ValidateTree(entry);
        }
    }
}
