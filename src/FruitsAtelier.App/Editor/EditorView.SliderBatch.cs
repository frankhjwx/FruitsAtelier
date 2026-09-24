using FruitsAtelier.Core;
using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;
namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SliderBatchItem(DifficultySession Session, MapDocument Before, SliderBatchConversionResult Result);
    private Task<SliderBatchItem[]>? sliderBatchTask;
    private CancellationTokenSource? sliderBatchCancellation;
    private int[] sliderImportTargets = [];
    private Guid sliderSingleTarget;
    private bool sliderDerandomizeDroplets = true;
    private string[] sliderBatchErrors = [];
    private int sliderErrorPage;
    private readonly List<HitArea> sliderDialogHits = [];
    public bool SliderImportPromptVisible => sliderImportTargets.Length > 0 || sliderSingleTarget != Guid.Empty;
    public bool SliderConversionBusy => sliderBatchTask is not null;
    private bool SliderDialogVisible => SliderImportPromptVisible || SliderConversionBusy || sliderBatchErrors.Length > 0;

    public void OfferSliderConversion(bool allDifficulties)
    {
        sliderDialogHits.Clear();
        sliderImportTargets = (allDifficulties ? Enumerable.Range(0, difficulties.Count) : new[] { activeDifficulty })
            .Where(i => difficulties[i].History.Document.ImportedSliders.Count > 0).ToArray();
        sliderDerandomizeDroplets = Document.DerandomizeDroplets ?? LibrarySettings.DerandomizeDroplets;
    }
    public void AnswerSliderImport(bool convert)
    {
        sliderDialogHits.Clear();
        var targets = sliderImportTargets; sliderImportTargets = [];
        var single = sliderSingleTarget; sliderSingleTarget = Guid.Empty;
        if (!convert) return;
        if (single != Guid.Empty) ConvertSelectedImportedSlider(single, sliderDerandomizeDroplets);
        else StartSliderBatch(targets, sliderDerandomizeDroplets);
    }
    public void ConvertAllSliders()
    {
        if (notesLocked) { StatusMessage = L.Get("assist.locked"); return; }
        if (!PrepareFileOperation()) return;
        OfferSliderConversion(false);
    }
    private void OfferSingleSliderConversion(Guid id)
    {
        sliderSingleTarget = id;
        sliderDerandomizeDroplets = Document.DerandomizeDroplets ?? LibrarySettings.DerandomizeDroplets;
    }
    public void CancelSliderConversion() => sliderBatchCancellation?.Cancel();
    private void StartSliderBatch(int[] targets, bool derandomizeDroplets)
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
            return new SliderBatchItem(input.Session, input.Before, ImportedSliderEditing.ConvertAll(edited, token, derandomizeDroplets));
        }).ToArray(), token);
    }
    private void PumpSliderBatch()
    {
        if (sliderBatchTask is not { IsCompleted: true } task) return;
        sliderBatchTask = null;
        bool cancelled = sliderBatchCancellation!.IsCancellationRequested;
        sliderBatchCancellation.Dispose(); sliderBatchCancellation = null;
        if (cancelled || task.IsCanceled) { SetNotice(L.Get("sliderBatch.cancelled")); return; }
        if (task.IsFaulted) { sliderBatchErrors = [task.Exception!.GetBaseException().Message]; sliderErrorPage = 0; return; }
        var results = task.Result;
        if (results.Any(r => !difficulties.Contains(r.Session) || !r.Before.ContentEquals(r.Session.History.Document)))
        { SetNotice(L.Get("sliderBatch.changed")); return; }
        int count = 0;
        foreach (var result in results)
        {
            if (result.Result.Tracks.Count == 0) continue;
            var history = result.Session.History;
            history.Begin(L.Get("sliderBatch.command"));
            history.Document.DerandomizeDroplets = sliderDerandomizeDroplets;
            var ids = result.Result.Tracks.Select(t => t.Id).ToHashSet();
            history.Document.ImportedSliders.RemoveAll(s => ids.Contains(s.Id));
            history.Document.Tracks.AddRange(result.Result.Tracks); history.Commit(); count += ids.Count;
        }
        sliderBatchErrors = results.SelectMany(r => r.Result.Failures.Select(f =>
            L.Get("sliderBatch.failure", r.Session.Name, Time(f.TimeMs), f.Reason))).ToArray();
        sliderErrorPage = 0; convertedSnapshot = null;
        SetNotice(L.Get("sliderBatch.finished", count, sliderBatchErrors.Length));
    }
    private void DrawSliderDialog(ICanvas c)
    {
        if (!SliderDialogVisible) return;
        hits.Clear(); sliderDialogHits.Clear(); contextItems.Clear(); menu = -1;
        var rect = new Rect((width - Math.Min(700, width - 40)) / 2, (height - 300) / 2, Math.Min(700, width - 40), 300);
        c.Fill(new(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8), Background, 10);
        c.Fill(rect, Panel, 8); c.Stroke(rect, Accent, 1, 8);
        c.Text(L.Get("sliderBatch.title"), rect.X + 22, rect.Y + 20, 18, Foreground, rect.Width - 44, true);
        if (SliderImportPromptVisible)
        {
            int count = sliderImportTargets.Sum(i => difficulties[i].History.Document.ImportedSliders.Count);
            c.Text(sliderSingleTarget != Guid.Empty ? L.Get("sliderBatch.singlePrompt") : L.Get("sliderBatch.prompt", count, sliderImportTargets.Length), rect.X + 22, rect.Y + 65, 14, Foreground, rect.Width - 44);
            c.Text(L.Get("sliderBatch.help"), rect.X + 22, rect.Y + 104, 12, Muted, rect.Width - 44);
            Button(c, new(rect.X + 22, rect.Y + 145, rect.Width - 44, 36),
                L.Get(sliderDerandomizeDroplets ? "sliderBatch.derandomizeOn" : "sliderBatch.derandomizeOff"),
                () => sliderDerandomizeDroplets = !sliderDerandomizeDroplets, sliderDerandomizeDroplets);
            c.Text(L.Get("sliderBatch.later"), rect.X + 22, rect.Y + 195, 12, Muted, rect.Width - 44);
            Button(c, new(rect.Right - 330, rect.Bottom - 58, 144, 36), L.Get("sliderBatch.keep"), () => AnswerSliderImport(false));
            Button(c, new(rect.Right - 174, rect.Bottom - 58, 152, 36), L.Get("sliderBatch.convert"), () => AnswerSliderImport(true), true);
        }
        else if (SliderConversionBusy)
        {
            DrawRatingSpinner(c, rect.X + 30, rect.Y + 80);
            c.Text(L.Get("sliderBatch.working"), rect.X + 48, rect.Y + 72, 14, Foreground, rect.Width - 70);
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
