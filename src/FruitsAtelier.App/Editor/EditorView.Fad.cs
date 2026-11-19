using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action<int>? RequestFadExport { get; set; }
    public Action? RequestFadImport { get; set; }
    private (ProjectDifficulty Difficulty, HitsoundResourcePlan Resources, int UnassignedSections)? fadPending;

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
                if (comparison is null)
                {
                    if (plan.Resources.DisplayNames.Count > 0)
                        ApplyFad(snapshot.Difficulties.Single(d => d.Id == plan.Difficulty.Id), plan.Resources, plan.UnassignedSections);
                    else SetFadNotice(L.Get("sync.state.Current"), plan.UnassignedSections);
                    return;
                }
                fadPending = plan;
                syncMerges[plan.Difficulty.Id] = merge; syncComparisons[plan.Difficulty.Id] = comparison;
                BeginSyncReview(difficulties.FindIndex(d => d.Id == plan.Difficulty.Id));
                return;
            }
            ApplyFad(plan.Difficulty, plan.Resources, plan.UnassignedSections);
        });
    }

    private void ResolveFad(bool? useIncoming)
    {
        if (fadPending is not { } pending || !syncMerges.TryGetValue(pending.Difficulty.Id, out var merge)) return;
        var choices = merge.Conflicts.ToDictionary(c => c.Key, c => useIncoming ?? syncChoices.GetValueOrDefault(c.Key));
        if (!choices.Values.Any(v => v)) { ClearFadReview(); return; }
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
            pending.Difficulty.Document = task.GetAwaiter().GetResult();
            ApplyFad(pending.Difficulty, pending.Resources,
                choices.GetValueOrDefault("$objects:native") ? pending.UnassignedSections : 0);
            ClearFadReview();
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

    private void ApplyFad(ProjectDifficulty incoming, HitsoundResourcePlan resources, int unassignedSections)
    {
        var check = CaptureProject();
        check.Difficulties.RemoveAll(d => d.Id == incoming.Id); check.Difficulties.Add(incoming);
        check.Validate();
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
        activeDifficulty = index; ResetDifficultyView(preserveMapsetState: true); RevealDifficultyTab();
        syncStatuses.Remove(incoming.Id); syncMerges.Remove(incoming.Id); syncComparisons.Remove(incoming.Id);
        resourceSnapshot = null; resourceReferences = null;
        ResetHitsounds(); PreloadProjectHitsounds(); RequestDifficultyChanged?.Invoke();
        SetFadNotice(L.Get("fad.imported", difficulties[index].Name), unassignedSections);
    }

    private void SetFadNotice(string message, int unassignedSections)
    {
        SetNotice(unassignedSections > 0 ? L.Get("fad.unassignedSections", message, unassignedSections) : message);
    }
}
