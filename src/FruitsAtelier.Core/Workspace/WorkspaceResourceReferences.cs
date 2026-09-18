namespace FruitsAtelier.Core;

public sealed class WorkspaceResourceReferences
{
    private readonly string[] paths, invalid;
    internal WorkspaceResourceReferences(string[] paths, string[] invalid) { this.paths = paths; this.invalid = invalid; }

    public IReadOnlyList<string> FindMissing()
        => invalid.Concat(paths.Where(path => !File.Exists(path))).Distinct().ToArray();
}
