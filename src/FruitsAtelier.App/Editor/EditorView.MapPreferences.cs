using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private MapEditingPreferences EditingPreferences => difficulties[activeDifficulty].EditingPreferences;

    private void RestoreMapEditingPreferences()
    {
        foreach (var difficulty in difficulties)
            difficulty.EditingPreferences = LibrarySettings.MapEditingPreferences.GetValueOrDefault(difficulty.Id) ?? new();
    }

    private void SaveMapEditingPreferences()
    {
        LibrarySettings.MapEditingPreferences[difficulties[activeDifficulty].Id] = EditingPreferences;
        RequestViewPreference?.Invoke();
    }
}
