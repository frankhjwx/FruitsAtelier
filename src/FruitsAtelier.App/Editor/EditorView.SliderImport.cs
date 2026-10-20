using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SliderImportPrepared(DifficultySession Owner, MapDocument Before, MapDocument? After, bool Ask, string[] Errors, int[] Questions, bool[] Compensated,
        WorkspaceSession? SavedWorkspace = null);
    private Task<SliderImportPrepared>? sliderImportTask;
    private int[] sliderSourceTargets = [];
    private int sliderSourceIndex;
    private int[]? sliderSourceQuestions;
    private bool[]? sliderSourceCompensated;
    private bool sliderImportWizard;
    public bool SliderImportSourcePromptVisible { get; private set; }

    private void PrepareImportedSliders(bool allDifficulties)
    {
        sliderSourceTargets = (allDifficulties ? Enumerable.Range(0, difficulties.Count) : [activeDifficulty])
            .Where(i => difficulties[i].History.Document.SliderImportState == SliderImportState.Pending).ToArray();
        sliderSourceIndex = 0;
        sliderSourceQuestions = null;
        sliderSourceCompensated = null;
        rememberSliderImportChoice = false;
        sliderImportWizard = sliderSourceTargets.Length > 0;
        StartSliderImport();
    }

    public void AnswerSliderImportSource(bool compensated)
    {
        if (!SliderImportSourcePromptVisible || sliderImportTask is not null) return;
        sliderBatchErrors = [];
        sliderDialogHits.Clear();
        StartSliderImport(compensated);
    }

    private void StartSliderImport(bool? compensated = null)
    {
        if (sliderSourceIndex >= sliderSourceTargets.Length)
        {
            sliderImportTargets = sliderSourceTargets.Where(i => difficulties[i].History.Document.ImportedSliders.Count > 0).ToArray();
            if (LibrarySettings.ImportSliderConversion != ImportSliderConversionMode.Ask && sliderImportTargets.Length > 0)
                AnswerSliderImport(LibrarySettings.ImportSliderConversion == ImportSliderConversionMode.ConvertToFSlider);
            return;
        }
        var owner = difficulties[sliderSourceTargets[sliderSourceIndex]];
        var before = owner.History.Document.DeepClone();
        int sourceIndex = sliderSourceIndex;
        var workspace = WorkspaceSession is { } session && session.Project.Difficulties.Any(d =>
            d.Id == owner.Id && d.Document.SliderImportState == SliderImportState.Pending) ? session : null;
        var questions = sliderSourceQuestions;
        var classifications = sliderSourceCompensated;
        bool confirmCompensated = LibrarySettings.ConfirmCompensatedImports;
        var inputs = questions is null ? sliderSourceTargets.Select(i => difficulties[i].History.Document.DeepClone()).ToArray() : [];
        sliderImportTask = Task.Run(() =>
        {
            try
            {
                if (questions is null)
                {
                    var ratios = inputs.Select(SliderImportEditing.CompensatedSliderRatio).ToArray();
                    classifications = ratios.Select(ratio => !confirmCompensated && ratio >= .95).ToArray();
                    questions = Enumerable.Range(0, inputs.Length).Where(i => ratios[i] > .8 && !classifications[i]).ToArray();
                }
                if (compensated is null && questions.Contains(sourceIndex))
                    return new SliderImportPrepared(owner, before, null, true, [], questions, classifications!);
                var edited = before.DeepClone();
                var result = SliderImportEditing.Initialize(edited, compensated ?? classifications![sourceIndex]);
                // Persist the resolved initial state before offering edits, so reopening or exporting
                // a sibling difficulty cannot restore an unanswered import. Existing projects never enter here.
                var saved = result.Success && workspace is not null
                    ? WorkspaceProject.SaveDifficulty(workspace, owner.Id, edited, owner.Name, archiveBeforeSave: false) : null;
                return new SliderImportPrepared(owner, before, result.Success ? edited : null, false, result.Diagnostics.ToArray(), questions, classifications!, saved);
            }
            catch (Exception error) { return new SliderImportPrepared(owner, before, null, false, [error.Message], questions ?? [], classifications ?? []); }
        });
    }

    private void PumpSliderImport()
    {
        if (sliderImportTask is not { IsCompleted: true } task) return;
        sliderImportTask = null;
        var result = task.GetAwaiter().GetResult();
        if (!difficulties.Contains(result.Owner) || !result.Owner.History.Document.ContentEquals(result.Before)) return;
        sliderSourceQuestions = result.Questions;
        sliderSourceCompensated = result.Compensated;
        if (result.Ask) { SliderImportSourcePromptVisible = true; return; }
        if (result.After is null)
        {
            sliderBatchErrors = result.Errors;
            SliderImportSourcePromptVisible = true;
            return;
        }
        // Import preparation is the initial editable state, not a repeatable content effect.
        result.Owner.History.Reset(result.After);
        result.Owner.History.MarkSaved(result.SavedWorkspace is null ? result.Before : result.After);
        if (result.SavedWorkspace is { } saved) WorkspaceSession = saved;
        SliderImportSourcePromptVisible = false;
        convertedSnapshot = null;
        sliderSourceIndex++;
        StartSliderImport();
    }
}
