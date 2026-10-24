using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SliderBatchItem(DifficultySession Session, MapDocument Before, MapDocument After, SliderBatchConversionResult Result);
    private Task<SliderBatchItem[]>? sliderBatchTask;
    private CancellationTokenSource? sliderBatchCancellation;
    private int[] sliderImportTargets = [];
    private string[] sliderBatchErrors = [];
    private int sliderErrorPage;
    private readonly List<HitArea> sliderDialogHits = [];
    public bool SliderImportPromptVisible => sliderImportTargets.Length > 0;
    public bool SliderConversionBusy => sliderBatchTask is not null || sliderImportTask is not null || LegacyDropletRefreshBusy;
    private bool SliderDialogVisible => TimingAlignmentPromptVisible || SliderImportPromptVisible || SliderImportSourcePromptVisible || SliderConversionBusy || sliderBatchErrors.Length > 0;
    private bool rememberSliderImportChoice;

    public void OfferSliderConversion(bool allDifficulties)
    {
        sliderImportWizard = false;
        sliderDialogHits.Clear();
        sliderImportTargets = (allDifficulties ? Enumerable.Range(0, difficulties.Count) : new[] { activeDifficulty })
            .Where(i => difficulties[i].History.Document.ImportedSliders.Count > 0).ToArray();
    }
    public void AnswerSliderImport(bool convert)
    {
        if (!SliderImportPromptVisible) return;
        if (sliderImportWizard && rememberSliderImportChoice)
        {
            LibrarySettings.ImportSliderConversion = convert ? ImportSliderConversionMode.ConvertToFSlider : ImportSliderConversionMode.KeepLegacy;
            RequestViewPreference?.Invoke();
        }
        sliderImportWizard = false;
        rememberSliderImportChoice = false;
        sliderDialogHits.Clear();
        var targets = sliderImportTargets; sliderImportTargets = [];
        if (!convert) return;
        StartSliderBatch(targets);
    }
    public void ConvertAllSliders()
    {
        if (notesLocked) { StatusMessage = L.Get("assist.locked"); return; }
        if (!PrepareFileOperation()) return;
        OfferSliderConversion(false);
    }
    public void CancelSliderConversion()
    {
        sliderBatchCancellation?.Cancel();
        CancelLegacyDropletRefresh();
    }
    private void StartSliderBatch(int[] targets)
    {
        if (notesLocked) { StatusMessage = L.Get("assist.locked"); return; }
        if (SliderDialogVisible || !PrepareFileOperation()) return;
        sliderDialogHits.Clear();
        if (AudioPlaying) RequestTogglePlayback?.Invoke();
        var inputs = targets.Distinct().Select(i => difficulties[i]).Where(d => d.History.Document.ImportedSliders.Count > 0)
            .Select(d => (Session: d, Before: d.History.Document.DeepClone())).ToArray();
        if (inputs.Length == 0) return;
        sliderBatchCancellation = new(); var token = sliderBatchCancellation.Token;
        sliderBatchTask = Task.Run(() => inputs.Select(input =>
        {
            token.ThrowIfCancellationRequested();
            var edited = input.Before.DeepClone();
            var result = SliderImportEditing.ConvertFollowingMap(edited, edited.ImportedSliders.Select(s => s.Id).ToArray(), token);
            return new SliderBatchItem(input.Session, input.Before, edited, result);
        }).ToArray(), token);
    }
    private void PumpSliderBatch()
    {
        PumpSliderImport();
        if (sliderBatchTask is not { IsCompleted: true } task) return;
        sliderBatchTask = null;
        bool cancelled = sliderBatchCancellation!.IsCancellationRequested;
        sliderBatchCancellation.Dispose(); sliderBatchCancellation = null;
        if (cancelled || task.IsCanceled) { SetNotice(L.Get("sliderBatch.cancelled")); return; }
        if (task.IsFaulted) { sliderBatchErrors = [task.Exception!.GetBaseException().Message]; sliderErrorPage = 0; return; }
        var results = task.Result;
        if (results.Any(r => !difficulties.Contains(r.Session) || !r.Before.ContentEquals(r.Session.History.Document)))
        { SetNotice(L.Get("sliderBatch.changed")); return; }
        if (results.Any(r => CollaborationSectionEditing.ChangesLockedContent(r.Before, r.After)))
        { SetNotice(L.Get("collab.locked")); return; }
        int count = 0;
        foreach (var result in results)
        {
            if (result.Result.Tracks.Count == 0) continue;
            var history = result.Session.History;
            history.RestoreVersion(L.Get("sliderBatch.command"), result.After);
            count += result.Result.Tracks.Count;
        }
        sliderBatchErrors = results.SelectMany(r => r.Result.Failures.Select(f =>
            L.Get("sliderBatch.failure", r.Session.Name, Time(f.TimeMs), f.Reason))).ToArray();
        sliderErrorPage = 0; convertedSnapshot = null;
        SetNotice(L.Get("sliderBatch.finished", count, sliderBatchErrors.Length));
    }
    private void DrawSliderDialog(ICanvas c)
    {
        PumpTimingAlignment();
        if (!SliderDialogVisible || LegacyDropletRefreshBusy) return;
        hits.Clear(); sliderDialogHits.Clear(); contextItems.Clear(); menu = -1;
        var rect = new Rect((width - Math.Min(700, width - 40)) / 2, (height - 300) / 2, Math.Min(700, width - 40), 300);
        c.Fill(new(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8), Background, 10);
        c.Fill(rect, Panel, 8); c.Stroke(rect, Accent, 1, 8);
        if (TimingAlignmentPromptVisible) { DrawTimingAlignment(c, rect); return; }
        c.Text(L.Get(sliderImportWizard ? "sliderImport.title" : "sliderBatch.title"), rect.X + 22, rect.Y + 20, 18, Foreground, rect.Width - 200, true);
        if (sliderImportWizard && sliderSourceQuestions is { } questions)
        {
            int page = SliderImportPromptVisible ? questions.Length + 1 : questions.Count(i => i < sliderSourceIndex) + 1;
            string progress = L.Get("sliderImport.page", page, Math.Max(1, questions.Length + (LibrarySettings.ImportSliderConversion == ImportSliderConversionMode.Ask ? 1 : 0)));
            c.Text(progress, rect.Right - 22 - c.MeasureText(progress, 12), rect.Y + 24, 12, Muted, 170);
        }
        if (SliderImportSourcePromptVisible)
        {
            c.Text(difficulties[sliderSourceTargets[sliderSourceIndex]].Name, rect.X + 22, rect.Y + 62, 14, Foreground, rect.Width - 44);
            c.Text(L.Get("sliderImport.question"), rect.X + 22, rect.Y + 105, 14, Foreground, rect.Width - 44);
            c.Text(L.Get("sliderImport.detected"), rect.X + 22, rect.Y + 150, 12, Muted, rect.Width - 44);
            if (sliderBatchErrors.Length > 0)
                c.Text(sliderBatchErrors[0], rect.X + 22, rect.Y + 185, 11, Error, rect.Width - 44);
            if (sliderImportTask is not null) DrawRatingSpinner(c, rect.X + 30, rect.Bottom - 40);
            Button(c, new(rect.Right - 330, rect.Bottom - 58, 144, 36), L.Get("sliderImport.no"), () => AnswerSliderImportSource(false), enabled: sliderImportTask is null);
            Button(c, new(rect.Right - 174, rect.Bottom - 58, 152, 36), L.Get("sliderImport.yes"), () => AnswerSliderImportSource(true), true, enabled: sliderImportTask is null);
        }
        else if (SliderImportPromptVisible)
        {
            int count = sliderImportTargets.Sum(i => difficulties[i].History.Document.ImportedSliders.Count);
            c.Text(L.Get("sliderBatch.prompt", count, sliderImportTargets.Length), rect.X + 22, rect.Y + 65, 14, Foreground, rect.Width - 44);
            c.Text(L.Get("sliderImport.conversionMode"), rect.X + 22, rect.Y + 150, 12, Foreground, rect.Width - 44);
            if (sliderImportWizard)
            {
                TimingCheck(c, new(rect.X + 22, rect.Y + 180, rect.Width - 44, 28), "sliderImport.remember", rememberSliderImportChoice,
                    () => rememberSliderImportChoice = !rememberSliderImportChoice);
                c.Text(L.Get("sliderImport.rememberHint"), rect.X + 22, rect.Y + 214, 11, Muted, rect.Width - 44);
            }
            Button(c, new(rect.Right - 330, rect.Bottom - 58, 144, 36), L.Get("sliderBatch.keep"), () => AnswerSliderImport(false));
            Button(c, new(rect.Right - 174, rect.Bottom - 58, 152, 36), L.Get("sliderBatch.convert"), () => AnswerSliderImport(true), true);
        }
        else if (SliderConversionBusy)
        {
            DrawRatingSpinner(c, rect.X + 30, rect.Y + 80);
            c.Text(L.Get("sliderBatch.working"), rect.X + 48, rect.Y + 72, 14, Foreground, rect.Width - 70);
            if (sliderImportTask is null)
                Button(c, new(rect.Right - 174, rect.Bottom - 58, 152, 36), L.Get("sliderBatch.cancel"), CancelSliderConversion);
        }
        else
        {
            c.Text(L.Get("sliderBatch.preserved", sliderBatchErrors.Length), rect.X + 22, rect.Y + 60, 13, Gold, rect.Width - 44);
            var lines = new List<string>();
            foreach (string error in sliderBatchErrors)
            {
                string remaining = error;
                while (remaining.Length > 0)
                {
                    int length = remaining.Length;
                    while (length > 1 && c.MeasureText(remaining[..length], 11) > rect.Width - 44) length--;
                    lines.Add(remaining[..length]); remaining = remaining[length..];
                }
            }
            foreach (var (line, i) in lines.Skip(sliderErrorPage * 6).Take(6).Select((line, i) => (line, i)))
                c.Text(line, rect.X + 22, rect.Y + 90 + i * 22, 11, Foreground, rect.Width - 44);
            Button(c, new(rect.X + 22, rect.Bottom - 58, 44, 36), "‹", () => sliderErrorPage--, enabled: sliderErrorPage > 0);
            Button(c, new(rect.X + 76, rect.Bottom - 58, 44, 36), "›", () => sliderErrorPage++, enabled: (sliderErrorPage + 1) * 6 < lines.Count);
            Button(c, new(rect.Right - 174, rect.Bottom - 58, 152, 36), L.Get("sliderBatch.close"), () => sliderBatchErrors = []);
        }
        sliderDialogHits.AddRange(hits);
    }
}
