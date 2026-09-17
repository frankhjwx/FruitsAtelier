using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private Action<int>? discardConfirmation;
    public bool DiscardConfirmationVisible => discardConfirmation is not null;

    public void ShowDiscardConfirmation(Action<int> answer)
    {
        if (DiscardConfirmationVisible) return;
        CancelInteraction(); menu = -1; contextItems.Clear();
        discardConfirmation = answer;
        hits.Clear(); fields.Clear();
    }

    private void AnswerDiscard(int answer)
    {
        var callback = discardConfirmation;
        discardConfirmation = null;
        hits.Clear();
        callback?.Invoke(answer);
    }

    private void DrawDiscardConfirmation(ICanvas c)
    {
        if (!DiscardConfirmationVisible) return;
        hits.Clear(); fields.Clear();
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
