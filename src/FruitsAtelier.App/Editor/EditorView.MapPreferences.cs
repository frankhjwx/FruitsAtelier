using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapEditingPreferences EditingPreferences => difficulties[activeDifficulty].EditingPreferences;

    private void RestoreMapEditingPreferences()
    {
        foreach (var difficulty in difficulties)
            difficulty.EditingPreferences = LibrarySettings.MapEditingPreferences.GetValueOrDefault(difficulty.Id) ?? new();
        RestoreSnapPreferences();
        RestoreMapPanel();
    }

    private void RestoreMapPanel()
    {
        TimingPageVisible = EditingPreferences.ActivePanel == "Timing";
        HitsoundingPageVisible = EditingPreferences.ActivePanel == "Hitsounding";
    }
    private void SaveMapPanel()
    {
        string panel = HitsoundingPageVisible ? "Hitsounding" : TimingPageVisible ? "Timing" : "Mapping";
        if (EditingPreferences.ActivePanel == panel) return;
        EditingPreferences.ActivePanel = panel;
        SaveMapEditingPreferences();
    }

    private void RestoreSnapPreferences()
        => currentSnapDivisor = LibrarySettings.ExperimentalSnapDivisions
            ? EditingPreferences.BeatSnapDivisor : Math.Min(EditingPreferences.BeatSnapDivisor, 16);

    private void SaveMapEditingPreferences()
    {
        LibrarySettings.MapEditingPreferences[difficulties[activeDifficulty].Id] = EditingPreferences;
        RequestViewPreference?.Invoke();
    }
}
