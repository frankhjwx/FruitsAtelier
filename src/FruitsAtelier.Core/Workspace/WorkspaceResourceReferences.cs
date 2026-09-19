namespace FruitsAtelier.Core;

public sealed class WorkspaceResourceReferences
{
    private readonly string[] paths;
    internal WorkspaceResourceReferences(string[] paths) { this.paths = paths; }

    public IReadOnlyList<string> FindMissing()
        => paths.Where(path => !File.Exists(path)).ToArray();
}
