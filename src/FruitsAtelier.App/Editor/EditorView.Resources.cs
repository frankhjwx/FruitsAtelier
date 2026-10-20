using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private BeatmapProject? resourceSnapshot;
    private WorkspaceResourceReferences? resourceReferences;
    private Task<ResourceCheckResult>? resourceCheckTask;
    private sealed record ResourceCheckResult(WorkspaceSession Session, BeatmapProject Snapshot,
        WorkspaceResourceReferences References, IReadOnlyList<string> Missing);

    private bool ResourceSnapshotMatches(BeatmapProject snapshot)
        => snapshot.Difficulties.Count == difficulties.Count && snapshot.Difficulties
            .Zip(difficulties).All(pair => pair.First.Id == pair.Second.Id
                && pair.First.FaOnly == pair.Second.FaOnly && pair.First.Document.ContentEquals(pair.Second.History.Document));

    private BeatmapProject ResourceSnapshot()
    {
        if (resourceSnapshot is null || !ResourceSnapshotMatches(resourceSnapshot))
        {
            resourceSnapshot = CaptureProject();
            resourceReferences = null;
        }
        return resourceSnapshot;
    }

    private void PumpWorkspaceResources()
    {
        if (resourceCheckTask is { IsCompleted: true } completed)
        {
            resourceCheckTask = null;
            if (completed.IsCompletedSuccessfully)
            {
                var result = completed.Result;
                if (ReferenceEquals(result.Session, WorkspaceSession) && ReferenceEquals(result.Snapshot, resourceSnapshot)
                    && ResourceSnapshotMatches(result.Snapshot))
                {
                    resourceReferences = result.References;
                    resourceErrors = result.Missing;
                }
                else nextResourceCheck = DateTime.MinValue;
            }
            else if (completed.Exception is { } error)
                SetNotice(L.Get("library.resourceCheckFailed", error.GetBaseException().Message));
        }
        if (resourceCheckTask is not null || LibraryVisible || WorkspaceSession is not { } session || DateTime.UtcNow < nextResourceCheck) return;
        var snapshot = ResourceSnapshot();
        var references = resourceReferences;
        var contextSources = snapshot.Difficulties.Where(d => session.Manifest.Difficulties
            .Any(e => e.Id == d.Id && WorkspaceSynchronization.Target(e) is null))
            .Select(d => d.Document.SourcePath).OfType<string>().ToHashSet(WorkspaceSynchronization.Paths);
        // The worker owns a frozen document snapshot. Filesystem checks and storyboard
        // parsing must not run in the playback paint loop or read live editing collections.
        resourceCheckTask = Task.Run(() =>
        {
            var resolved = references ?? WorkspaceProject.ResourceReferences(snapshot);
            return new ResourceCheckResult(session, snapshot, resolved, resolved.FindMissing().Where(path => !contextSources.Contains(path)).ToArray());
        });
        nextResourceCheck = DateTime.UtcNow.AddSeconds(3);
    }
}
