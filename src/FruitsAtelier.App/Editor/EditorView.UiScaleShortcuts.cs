namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool UiScaleKey(int key, bool ctrl)
    {
        if (!ctrl || altHeld || key is not (187 or 189 or 107 or 109)) return false;
        if (drag != DragKind.None || draftTrack != Guid.Empty || draftBanana != Guid.Empty
            || osuDraftSlider is not null || osuSpinnerStart is not null || menu >= 0 || contextItems.Count > 0) return false;
        int previous = LibrarySettings.UiScalePercent;
        LibrarySettings.UiScalePercent += key is 187 or 107
            ? FruitsAtelier.Core.LibrarySettings.UiScaleStep : -FruitsAtelier.Core.LibrarySettings.UiScaleStep;
        if (previous != LibrarySettings.UiScalePercent) RequestViewPreference?.Invoke();
        return true;
    }
}
