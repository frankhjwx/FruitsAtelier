using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool timingAlignmentChecksEnabled;
    private readonly HashSet<Guid> timingAlignmentChecked = [];
    private TimingAlignmentPlan? timingAlignmentPlan;
    private MapDocument? timingAlignmentSource;
    public bool TimingAlignmentPromptVisible => timingAlignmentPlan is { HasChanges: true };
    public int TimingAlignmentObjectCount => timingAlignmentPlan?.ObjectCount ?? 0;
    public int TimingAlignmentPointCount => timingAlignmentPlan?.TimingPointCount ?? 0;

    public void CheckTimingAlignmentOnOpen() => timingAlignmentChecksEnabled = true;

    private void PumpTimingAlignment()
    {
        if (!timingAlignmentChecksEnabled || LibraryVisible || ExportVisible || SynchronizationVisible
            || SliderImportPromptVisible || SliderImportSourcePromptVisible || SliderConversionBusy
            || sliderBatchErrors.Length > 0 || history.HasActiveTransaction) return;
        if (timingAlignmentPlan is not null && ReferenceEquals(timingAlignmentSource, Document)) return;
        if (timingAlignmentChecked.Contains(difficulties[activeDifficulty].Id)) return;
        timingAlignmentSource = Document;
        timingAlignmentPlan = TimingAlignment.Analyze(Document);
        if (!timingAlignmentPlan.HasChanges) timingAlignmentChecked.Add(difficulties[activeDifficulty].Id);
    }

    public void AnswerTimingAlignment(bool align)
    {
        if (!TimingAlignmentPromptVisible) return;
        var plan = timingAlignmentPlan!;
        if (!ReferenceEquals(timingAlignmentSource, Document))
        { timingAlignmentPlan = null; PumpTimingAlignment(); return; }
        timingAlignmentChecked.Add(difficulties[activeDifficulty].Id);
        timingAlignmentPlan = null; timingAlignmentSource = null;
        sliderDialogHits.Clear();
        if (!align) return;
        history.Begin(L.Get("timingAlignment.undo"));
        try
        {
            TimingAlignment.Apply(Document, plan);
            CommitContentHistory();
            convertedSnapshot = null;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        { history.Cancel(); StatusMessage = fieldError = error.Message; }
    }

    private void DrawTimingAlignment(ICanvas c, Rect rect)
    {
        c.Text(L.Get("timingAlignment.title"), rect.X + 22, rect.Y + 20, 18, Foreground, rect.Width - 44, true);
        c.Text(CurrentDifficultyName, rect.X + 22, rect.Y + 62, 14, Foreground, rect.Width - 44);
        c.Text(L.Get("timingAlignment.objects", TimingAlignmentObjectCount), rect.X + 22, rect.Y + 104, 14, Foreground, rect.Width - 44);
        c.Text(L.Get("timingAlignment.points", TimingAlignmentPointCount), rect.X + 22, rect.Y + 134, 14, Foreground, rect.Width - 44);
        c.Text(L.Get("timingAlignment.question"), rect.X + 22, rect.Y + 180, 12, Muted, rect.Width - 44);
        Button(c, new(rect.Right - 330, rect.Bottom - 58, 144, 36), L.Get("timingAlignment.keep"), () => AnswerTimingAlignment(false));
        Button(c, new(rect.Right - 174, rect.Bottom - 58, 152, 36), L.Get("timingAlignment.align"), () => AnswerTimingAlignment(true), true);
        sliderDialogHits.AddRange(hits);
    }
}
