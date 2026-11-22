using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool syncModePrompt, syncSimplified;
    internal Rect SyncModeSimpleBounds { get; private set; }
    internal Rect SyncModeDetailedBounds { get; private set; }

    private bool IsSyncReviewItem(WorkspaceSyncConflict conflict) => !syncSimplified
        || syncComparisons.TryGetValue(syncDifficulty, out var comparison) && comparison.ActualChanges.Contains(conflict.Key);

    private void SelectSyncMode(bool simplified)
    {
        if (!syncMerges.TryGetValue(syncDifficulty, out var merge)
            || !syncComparisons.TryGetValue(syncDifficulty, out var comparison)) return;
        syncModePrompt = false; syncSimplified = simplified;
        if (simplified)
        {
            foreach (var conflict in merge.Conflicts.Where(c => !comparison.ActualChanges.Contains(c.Key)))
            { syncChoices[conflict.Key] = true; syncRoundChoices.Add(conflict.Key); }
            syncTab = "Objects";
            syncRow = Math.Max(0, merge.Conflicts.FindIndex(c => comparison.ActualChanges.Contains(c.Key)));
        }
        syncVisualKey = null; syncPreviewRevision++; syncResultPane = null;
        hits.Clear(); fields.Clear();
        if (simplified && comparison.ActualChanges.Count == 0) ResolveSync(null);
    }

    private void DrawSyncModePrompt(ICanvas c)
    {
        c.Fill(new(0, 0, width, height), Background, opacity: .78f);
        hits.Clear(); fields.Clear();
        float w = Math.Min(540, width - 32), x = (width - w) / 2;
        float h = 222, y = (height - h) / 2;
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 1, 8);
        c.Text(L.Get("sync.mode.title"), x + 20, y + 18, 20, Foreground, w - 40, true);
        SyncModeSimpleBounds = new(x + 20, y + 64, w - 40, 42);
        SyncReviewButton(c, SyncModeSimpleBounds, L.Get("sync.mode.auto"), () => SelectSyncMode(true));
        SyncModeDetailedBounds = new(x + 20, y + 118, w - 40, 42);
        SyncReviewButton(c, SyncModeDetailedBounds, L.Get("sync.mode.detailed"), () => SelectSyncMode(false));
        SyncReviewButton(c, new(x + w - 132, y + h - 44, 112, 32), L.Get("mac.cancel"), CancelSynchronization);
    }
}
