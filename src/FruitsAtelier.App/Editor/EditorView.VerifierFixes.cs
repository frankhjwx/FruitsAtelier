using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    [Flags]
    private enum VerifierFixCategory { Objects = 1, Timing = 2, Kiai = 4, All = 7 }
    private sealed record VerifierFixPrompt(EditorHistory Owner, long Revision, Guid Difficulty,
        VerifierFixCategory Category, int Objects, int Timing, int Kiai);
    private VerifierFixPrompt? verifierFixPrompt;
    public bool VerifierFixPromptVisible => verifierFixPrompt is not null;

    private void OfferVerifierFix(Guid difficulty, VerifierFixCategory category)
    {
        if (difficulty != difficulties[activeDifficulty].Id || !ClipboardInteractionReady || MapsetVerifierBusy
            || IsTestplaying || history.HasActiveTransaction || LegacyDropletRefreshBusy) return;
        int objects = TimingAlignment.Analyze(Document, alignTimingPoints: false).ObjectCount;
        int timing = TimingAlignment.Analyze(Document, alignObjects: false).TimingPointCount;
        int kiai = KiaiTimingEditing.CountOffsets(Document);
        if (category != VerifierFixCategory.Objects && objects > 0
            || category != VerifierFixCategory.Timing && timing > 0
            || category != VerifierFixCategory.Kiai && kiai > 0)
        {
            verifierFixPrompt = new(history, history.ContentRevision, difficulty, category, objects, timing, kiai);
            sliderDialogHits.Clear();
        }
        else StartVerifierFix(difficulty, category);
    }

    public void AnswerVerifierFix(bool includeOtherSections)
    {
        if (verifierFixPrompt is not { } prompt) return;
        verifierFixPrompt = null; sliderDialogHits.Clear();
        if (!ReferenceEquals(prompt.Owner, history) || prompt.Revision != history.ContentRevision
            || prompt.Difficulty != difficulties[activeDifficulty].Id) return;
        StartVerifierFix(prompt.Difficulty, includeOtherSections ? VerifierFixCategory.All : prompt.Category);
    }

    private void CancelVerifierFixPrompt()
    {
        verifierFixPrompt = null; sliderDialogHits.Clear();
    }

    private void StartVerifierFix(Guid difficulty, VerifierFixCategory category)
    {
        var snapshot = Document.DeepClone();
        bool compensation = compensateTinyDroplets;
        verifierCleanupHistory = history; verifierCleanupRevision = history.ContentRevision;
        verifierCleanupDifficulty = difficulty; verifierCleanupCompensation = compensation;
        verifierCleanupTask = Task.Run(() =>
        {
            bool changed = false;
            if ((category & (VerifierFixCategory.Objects | VerifierFixCategory.Timing)) != 0)
            {
                var plan = TimingAlignment.Analyze(snapshot,
                    alignTimingPoints: category.HasFlag(VerifierFixCategory.Timing),
                    alignObjects: category.HasFlag(VerifierFixCategory.Objects));
                if (plan.HasChanges) { TimingAlignment.Apply(snapshot, plan); changed = true; }
            }
            if (category.HasFlag(VerifierFixCategory.Kiai) && KiaiTimingEditing.Prepare(snapshot, compensation) is { } aligned)
            { snapshot = aligned; changed = true; }
            return changed ? snapshot : null;
        });
        _ = verifierCleanupTask.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
    }

    private void PumpVerifierFix()
    {
        if (verifierCleanupTask is not { IsCompleted: true } task) return;
        if (!ReferenceEquals(verifierCleanupHistory, history) || verifierCleanupRevision != history.ContentRevision
            || verifierCleanupDifficulty != difficulties[activeDifficulty].Id || verifierCleanupCompensation != compensateTinyDroplets)
        { verifierCleanupTask = null; verifierCleanupHistory = null; return; }
        if (!ClipboardInteractionReady || history.HasActiveTransaction || LegacyDropletRefreshBusy || IsTestplaying) return;
        verifierCleanupTask = null; verifierCleanupHistory = null;
        if (!task.IsCompletedSuccessfully)
        { StatusMessage = fieldError = task.Exception?.GetBaseException().Message ?? L.Get("mv.timingUnsafe"); return; }
        if (task.Result is not { } candidate) { StatusMessage = L.Get("mv.noOffsets"); return; }
        history.Begin(L.Get("timingAlignment.undo"));
        try
        {
            Document.Fruits.Clear(); Document.Fruits.AddRange(candidate.Fruits);
            Document.Tracks.Clear(); Document.Tracks.AddRange(candidate.Tracks);
            Document.ImportedSliders.Clear(); Document.ImportedSliders.AddRange(candidate.ImportedSliders);
            Document.BananaShowers.Clear(); Document.BananaShowers.AddRange(candidate.BananaShowers);
            Document.TimingPoints.Clear(); Document.TimingPoints.AddRange(candidate.TimingPoints);
            Document.DurationMs = candidate.DurationMs;
            CommitContentHistory();
            convertedSnapshot = null;
            verifierReport = null; verifierLayout = null; verifierRuns.Clear();
            RefreshMapsetVerifier();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        { history.Cancel(); StatusMessage = fieldError = error.Message; }
    }

    private void DrawVerifierFixPrompt(ICanvas c, Rect rect)
    {
        var prompt = verifierFixPrompt!;
        c.Text(L.Get("mv.relatedOffsetsTitle"), rect.X + 22, rect.Y + 20, 18, Foreground, rect.Width - 44, true);
        c.Text(L.Get("mv.relatedOffsetsQuestion"), rect.X + 22, rect.Y + 63, 13, Muted, rect.Width - 44);
        c.Text(L.Get("timingAlignment.objects", prompt.Objects), rect.X + 22, rect.Y + 104, 13, Foreground, rect.Width - 44);
        c.Text(L.Get("timingAlignment.points", prompt.Timing), rect.X + 22, rect.Y + 134, 13, Foreground, rect.Width - 44);
        c.Text(L.Get("mv.kiaiOffsets", prompt.Kiai), rect.X + 22, rect.Y + 164, 13, Foreground, rect.Width - 44);
        Button(c, new(rect.X + 22, rect.Bottom - 58, 100, 36), L.Get("sliderBatch.cancel"), CancelVerifierFixPrompt);
        Button(c, new(rect.Right - 350, rect.Bottom - 58, 158, 36), L.Get("mv.fixThisSection"), () => AnswerVerifierFix(false));
        Button(c, new(rect.Right - 180, rect.Bottom - 58, 158, 36), L.Get("mv.fixAllSections"), () => AnswerVerifierFix(true), true);
        sliderDialogHits.AddRange(hits);
    }
}
