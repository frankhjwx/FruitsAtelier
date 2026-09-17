using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class DiscardConfirmationTests
{
    public static void Run()
    {
        var view = new EditorView();
        view.ChangeAudioPath("unsaved.ogg");
        var canvas = new RecordingCanvas();
        int answer = 0;
        view.ShowDiscardConfirmation(a => answer = a);
        view.Render(canvas, 980, 620);
        if (!canvas.Texts.Any(t => t.Value == L.Get("window.unsavedTitle"))) throw new Exception("Confirmation missing");
        view.KeyDown(90, true, false);
        view.PointerDown(700, 450, 0, false, false);
        if (!view.IsDirty || answer != 0) throw new Exception("Background input escaped confirmation");
        view.KeyDown(27, false, false);
        if (answer != 2 || view.DiscardConfirmationVisible || !view.IsDirty) throw new Exception("Cancel must preserve changes");
        foreach (var (key, expected) in new[] { ("mac.save", 6), ("mac.discard", 7), ("mac.cancel", 2) })
        {
            answer = 0;
            view.ShowDiscardConfirmation(a => answer = a);
            canvas.Clear(); view.Render(canvas, 980, 620);
            var text = canvas.Texts.Single(t => t.Value == L.Get(key));
            view.PointerDown(text.X + 2, text.Y + 2, 0, false, false);
            if (answer != expected || view.DiscardConfirmationVisible) throw new Exception("Incorrect confirmation action");
        }
    }
}
