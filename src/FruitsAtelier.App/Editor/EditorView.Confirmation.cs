using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Action<int>? discardConfirmation;
    private bool deleteProjectConfirmation;
    private bool offerSongsExport;
    public bool DiscardConfirmationVisible => discardConfirmation is not null || SynchronizationVisible || SynchronizationBusy;

    public void ShowDiscardConfirmation(Action<int> answer)
    {
        if (DiscardConfirmationVisible) return;
        offerSongsExport = false;
        CancelInteraction(); menu = -1; contextItems.Clear();
        discardConfirmation = answer;
        hits.Clear(); fields.Clear();
    }

    public void ShowDeleteProjectConfirmation(Action<bool> answer)
    {
        ShowDiscardConfirmation(value => answer(value == 7));
        deleteProjectConfirmation = true;
    }

    private void AnswerDiscard(int answer)
    {
        if (SynchronizationBusy) return;
        if (SynchronizationVisible) { CancelSynchronization(); return; }
        var callback = discardConfirmation;
        discardConfirmation = null;
        deleteProjectConfirmation = false;
        offerSongsExport = false;
        hits.Clear();
        callback?.Invoke(answer);
    }

    private void DrawDiscardConfirmation(ICanvas c)
    {
        if (SynchronizationBusy) { DrawStatus(c); hits.Clear(); fields.Clear(); return; }
        if (SynchronizationVisible) { DrawSynchronization(c); return; }
        if (!DiscardConfirmationVisible) return;
        hits.Clear(); fields.Clear();
        if (deleteProjectConfirmation)
        {
            float left = (width - 500) / 2, top = (height - 160) / 2;
            c.Fill(new(left, top, 500, 160), Panel, 8);
            c.Stroke(new(left, top, 500, 160), Accent, 2, 8);
            c.Text(L.Get("library.deleteProjectConfirm"), left + 24, top + 28, 20, Foreground, 452, true);
            Button(c, new(left + 24, top + 96, 210, 40), L.Get("mac.cancel"), () => AnswerDiscard(2), true);
            Button(c, new(left + 266, top + 96, 210, 40), L.Get("library.deleteProject"), () => AnswerDiscard(7));
            return;
        }
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
