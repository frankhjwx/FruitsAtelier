namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool UiScaleKey(int key, bool ctrl)
    {
        if (!ctrl || altHeld || key is not (187 or 189 or 107 or 109)) return false;
        if (drag != DragKind.None || draftTrack != Guid.Empty || draftBanana != Guid.Empty
            || osuDraftSlider is not null || osuSpinnerStart is not null || menu >= 0 || contextItems.Count > 0) return false;
        var ids = ClipboardSelectedParentIds();
        var tracks = Document.Tracks.Where(t => ids.Contains(t.Id)).ToArray();
        var sliders = Document.ImportedSliders.Where(s => ids.Contains(s.Id)).ToArray();
        if (!LibraryVisible && (tracks.Length > 0 || sliders.Length > 0))
        {
            if (TimingPageVisible || notesLocked || RejectReadOnlySelection()) return true;
            int delta = key is 187 or 107 ? 1 : -1;
            Edit(FruitsAtelier.Localization.Strings.Get(delta > 0 ? "editor.command.addReverse" : "editor.command.removeReverse"), () =>
            {
                foreach (var track in tracks) track.SpanCount = Math.Clamp(track.SpanCount + delta, 1, 9000);
                foreach (var slider in sliders) ChangeOsuSpanCount(slider, Math.Clamp(slider.SpanCount + delta, 1, 9000));
            });
            return true;
        }
        int previous = LibrarySettings.UiScalePercent;
        LibrarySettings.UiScalePercent += key is 187 or 107
            ? FruitsAtelier.Core.LibrarySettings.UiScaleStep : -FruitsAtelier.Core.LibrarySettings.UiScaleStep;
        if (previous != LibrarySettings.UiScalePercent) RequestViewPreference?.Invoke();
        return true;
    }
}
