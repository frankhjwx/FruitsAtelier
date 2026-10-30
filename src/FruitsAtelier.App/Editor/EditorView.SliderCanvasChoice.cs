using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Guid[]? sliderCanvasChoice;
    internal bool SliderCanvasChoiceVisible => sliderCanvasChoice is not null;

    private void ShowSliderCanvasChoice(IEnumerable<Guid> targets)
    {
        if (notesLocked || DiscardConfirmationVisible) return;
        var ids = targets.Where(IsReadOnlySlider).Distinct().ToArray();
        if (ids.Length == 0) return;
        var selectionIds = ClipboardSelectedParentIds().Concat(ids).Distinct().ToArray();
        ShowDiscardConfirmation(answer =>
        {
            sliderCanvasChoice = null;
            if (answer == 2) return;
            if (answer == 7)
            {
                ToggleOsuCanvas();
                SelectObjects(selectionIds, ids[0]);
                SeekObjectBoundary(ClipboardParents(Document).First(p => p.Id == ids[0]).TimeMs);
                return;
            }
            if (!EditSliderRepresentation(L.Get("editor.command.editImportedSlider"), () =>
            {
                foreach (var id in ids)
                {
                    var result = IsOsuCanvas
                        ? SliderRepresentationEditing.ConvertToLegacy(Document, id, compensateTinyDroplets)
                        : SliderRepresentationEditing.ConvertToTrack(Document, id);
                    if (!result.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), result.Diagnostics));
                }
            })) return;
            SelectObjects(selectionIds, ids[0]);
            tool = Tool.Select;
            if (!IsOsuCanvas && selectionIds.Length == 1 && SelectedTrack is { } track) SelectAnchors(track, []);
        });
        sliderCanvasChoice = ids;
    }

    private void DrawSliderCanvasChoice(ICanvas c)
    {
        float w = Math.Min(620, width - 32);
        var body = WrapSyncText(c, L.Get(IsOsuCanvas ? "osu.canvasChoiceFSlider" : "osu.canvasChoiceLegacy", sliderCanvasChoice!.Length), w - 48);
        var help = WrapSyncText(c, L.Get("osu.canvasChoiceHelp"), w - 48);
        float h = 216 + 22 * (body.Length + help.Length);
        float x = (width - w) / 2, y = (height - h) / 2;
        c.Fill(new(0, 0, width, height), Background, opacity: .65f);
        c.Fill(new(x, y, w, h), Panel, 8);
        c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("osu.canvasChoiceTitle"), x + 24, y + 24, 20, Foreground, w - 48, true);
        float row = y + 65;
        foreach (var line in body) { c.Text(line.Text, x + 24, row, 15, Foreground, w - 48); row += 22; }
        row += 8;
        foreach (var line in help) { c.Text(line.Text, x + 24, row, 15, Muted, w - 48); row += 22; }
        row += 15;
        Button(c, new(x + 24, row, w - 48, 32), L.Get(IsOsuCanvas ? "osu.convertLegacy" : "osu.convertFSlider"), () => AnswerDiscard(6));
        Button(c, new(x + 24, row + 41, w - 48, 32), L.Get(IsOsuCanvas ? "osu.switchCatch" : "osu.switchOsu"), () => AnswerDiscard(7));
        Button(c, new(x + 24, row + 82, w - 48, 30), L.Get("mac.cancel"), () => AnswerDiscard(2), true);
    }
}
