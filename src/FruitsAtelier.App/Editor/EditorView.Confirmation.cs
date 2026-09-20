using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Action<int>? discardConfirmation;
    private bool offerSongsExport;
    private IReadOnlyList<FruitsAtelier.Core.LibraryMap>? additionalDifficulties;
    public bool DiscardConfirmationVisible => discardConfirmation is not null;

    public void ShowDiscardConfirmation(Action<int> answer)
    {
        if (DiscardConfirmationVisible) return;
        offerSongsExport = false;
        additionalDifficulties = null;
        CancelInteraction(); menu = -1; contextItems.Clear();
        discardConfirmation = answer;
        hits.Clear(); fields.Clear();
    }

    private void AnswerDiscard(int answer)
    {
        var callback = discardConfirmation;
        discardConfirmation = null;
        offerSongsExport = false;
        additionalDifficulties = null;
        hits.Clear();
        callback?.Invoke(answer);
    }

    private void DrawDiscardConfirmation(ICanvas c)
    {
        if (!DiscardConfirmationVisible) return;
        hits.Clear(); fields.Clear();
        if (offerSongsExport)
        {
            float left = (width - 600) / 2, top = (height - 210) / 2;
            c.Fill(new(left, top, 600, 210), Panel, 8);
            c.Stroke(new(left, top, 600, 210), Accent, 2, 8);
            c.Text(L.Get("library.savedWorkspaceTitle"), left + 24, top + 24, 20, Foreground, 552, true);
            c.Text(L.Get("library.offerSongsExport"), left + 24, top + 68, 14, Foreground, 552);
            c.Text(L.Get("library.offerSongsExportHelp"), left + 24, top + 94, 14, Muted, 552);
            Button(c, new(left + 24, top + 146, 264, 40), L.Get("library.workspaceOnly"), () => AnswerDiscard(2));
            Button(c, new(left + 312, top + 146, 264, 40), L.Get("library.exportToSongs"), () => AnswerDiscard(6), true);
            return;
        }
        if (additionalDifficulties is { } additions)
        {
            float boxHeight = 154 + Math.Min(8, additions.Count) * 24;
            float left = (width - 560) / 2, top = (height - boxHeight) / 2;
            c.Fill(new(left, top, 560, boxHeight), Panel, 8);
            c.Stroke(new(left, top, 560, boxHeight), Accent, 2, 8);
            c.Text(L.Get("library.additionalTitle", additions.Count), left + 24, top + 24, 20, Foreground, 512, true);
            for (int i = 0; i < Math.Min(8, additions.Count); i++)
                c.Text(additions[i].Difficulty, left + 24, top + 62 + i * 24, 14, Foreground, 512);
            Button(c, new(left + 24, top + boxHeight - 64, 246, 40), L.Get("library.importAdditional"), () => AnswerDiscard(6));
            Button(c, new(left + 290, top + boxHeight - 64, 246, 40), L.Get("library.openExisting"), () => AnswerDiscard(2));
            return;
        }
        float x = (width - 500) / 2, y = (height - 180) / 2;
        c.Fill(new(x, y, 500, 180), Panel, 8);
        c.Stroke(new(x, y, 500, 180), Accent, 2, 8);
        c.Text(L.Get("window.unsavedTitle"), x + 24, y + 24, 20, Foreground, 452, true);
        c.Text(L.Get("window.unsavedPrompt"), x + 24, y + 62, 14, Foreground, 452);
        Button(c, new(x + 24, y + 116, 140, 40), L.Get("mac.save"), () => AnswerDiscard(6));
        Button(c, new(x + 180, y + 116, 140, 40), L.Get("mac.discard"), () => AnswerDiscard(7));
        Button(c, new(x + 336, y + 116, 140, 40), L.Get("mac.cancel"), () => AnswerDiscard(2), true);
    }
}
