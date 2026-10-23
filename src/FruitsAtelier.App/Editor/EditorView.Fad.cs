using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<int>? RequestFadExport { get; set; }
    public Action? RequestFadImport { get; set; }
    private (ProjectDifficulty Difficulty, HitsoundResourcePlan Resources)? fadPending;

    public ProjectDifficulty CaptureDifficulty(int index)
    {
        var d = difficulties[index];
        return new ProjectDifficulty { Id = d.Id, Name = d.Name, FaOnly = d.FaOnly, Document = d.History.Document.DeepClone() };
    }

    public void BeginFadExport(string path, int index)
    {
        if (RejectPatternProjectOperation()) return;
        if (!PrepareFileOperation() || index < 0 || index >= difficulties.Count) return;
        var difficulty = CaptureDifficulty(index);
        bool compensate = CompensateTinyDroplets; string workspace = LibrarySettings.Workspace;
        StartFazTask(Task.Run(() => FadArchive.Export(difficulty, path, compensate, workspace)),
            () => SetNotice(L.Get("library.exported", path)));
    }

    public void BeginFadImport(string path)
    {
        if (RejectPatternProjectOperation()) return;
        if (!HasEditorProject || LibraryVisible) { ShowError(L.Get("fad.openProjectFirst")); return; }
        if (!PrepareFileOperation()) return;
        RequestPausePlayback?.Invoke();
        var snapshot = CaptureProject(); var owner = history;
        var settings = new LibrarySettings { Workspace = LibrarySettings.Workspace, Songs = LibrarySettings.Songs };
        bool compensate = CompensateTinyDroplets;
        var task = Task.Run(() =>
        {
            using var package = FadArchive.Prepare(path, settings);
            var plan = FadArchive.PlanImport(package, snapshot);
            var local = snapshot.Difficulties.FirstOrDefault(d => d.Id == plan.Difficulty.Id);
            var merge = local is null ? null : WorkspaceSynchronization.CompareNativeDifficulty(local.Document, plan.Difficulty.Document, path, compensate);
            var comparison = merge is { Conflicts.Count: > 0 } ? PrepareSyncComparison(merge, compensate) : null;
            return (plan, merge, comparison);
        });
        StartFazTask(task, () =>
        {
            if (!ReferenceEquals(owner, history)) return;
            var (plan, merge, comparison) = task.GetAwaiter().GetResult();
            if (merge is not null)
            {
                if (comparison is null) { SetNotice(L.Get("sync.state.Current")); return; }
                fadPending = plan;
                syncMerges[plan.Difficulty.Id] = merge; syncComparisons[plan.Difficulty.Id] = comparison;
                BeginSyncReview(difficulties.FindIndex(d => d.Id == plan.Difficulty.Id));
                return;
            }
            ApplyFad(plan.Difficulty, plan.Resources);
        });
    }

    private void ResolveFad(bool? useIncoming)
    {
        if (fadPending is not { } pending || !syncMerges.TryGetValue(pending.Difficulty.Id, out var merge)) return;
        var choices = merge.Conflicts.ToDictionary(c => c.Key, c => useIncoming ?? syncChoices.GetValueOrDefault(c.Key));
        bool compensate = CompensateTinyDroplets;
        var task = Task.Run(() =>
        {
            var result = WorkspaceSynchronization.Resolve(merge, choices);
            OsuBeatmapWriter.Serialize(result, compensate);
            return result;
        });
        syncPage = null;
        StartFazTask(task, () =>
        {
            var result = task.GetAwaiter().GetResult();
            bool importing = choices.Values.Any(v => v);
            ClearFadReview();
            if (importing)
            {
                pending.Difficulty.Document = result;
                ApplyFad(pending.Difficulty, pending.Resources);
            }
        }, () => { if (fadPending is not null) syncPage = "resolve"; });
    }

    private void ClearFadReview()
    {
        if (fadPending is { } pending)
        { syncMerges.Remove(pending.Difficulty.Id); syncComparisons.Remove(pending.Difficulty.Id); }
        fadPending = null; syncPage = null; syncDifficulty = Guid.Empty;
        syncVisualMerge = null; syncResultPane = null; syncPreviewRevision++;
        hits.Clear(); fields.Clear();
    }

    private void ApplyFad(ProjectDifficulty incoming, HitsoundResourcePlan resources)
    {
        var check = CaptureProject();
        check.Difficulties.RemoveAll(d => d.Id == incoming.Id); check.Difficulties.Add(incoming);
        check.Validate();
        foreach (var d in check.Difficulties)
            if (d.Document.CollaborationReferences is { Count: > 0 } references) CollaborationMapping.ValidateReferences(check, d.Id, references);
        int index = difficulties.FindIndex(d => d.Id == incoming.Id);
        resources.Apply();
        try
        {
            if (index >= 0)
                difficulties[index].History.RestoreVersion(L.Get("fad.import"), incoming.Document);
            else
            {
                difficulties.Add(new DifficultySession(incoming)); index = difficulties.Count - 1;
                WorkspaceSession?.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = incoming.Id, Name = incoming.Name, FaOnly = incoming.FaOnly });
                projectStructureDirty = true;
            }
        }
        catch { resources.Restore(); throw; }
        activeDifficulty = index; ResetDifficultyView(); RevealDifficultyTab();
        syncStatuses.Remove(incoming.Id); syncMerges.Remove(incoming.Id); syncComparisons.Remove(incoming.Id);
        resourceSnapshot = null; resourceReferences = null;
        ResetHitsounds(); PreloadProjectHitsounds(); RequestDifficultyChanged?.Invoke();
        SetNotice(L.Get("fad.imported", incoming.Name));
    }
}
