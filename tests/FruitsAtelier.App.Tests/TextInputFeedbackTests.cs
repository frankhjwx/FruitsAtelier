using FruitsAtelier.App.Editor;
using L = FruitsAtelier.Localization.Strings;

internal static class TextInputFeedbackTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "atelier-text-feedback-" + Guid.NewGuid());
        var view = new EditorView();
        view.LibrarySettings.Workspace = root; view.LibrarySettings.Songs = "";
        var canvas = new RecordingCanvas();
        void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
        view.ShowLibrary(); Paint();
        try
        {
        view.PointerDown(250, 100, 0, false, false); view.PointerUp(250, 100, 0);
        view.PasteLibraryText("himikos"); Paint();
            bool Caret() => canvas.Lines.Any(l => l.Y1 == 96 && l.Y2 == 114 && l.X1 == l.X2);
            Check(Caret(), "Typing must show the caret immediately");
            Check(canvas.Texts.Any(t => t.Value == "himikos") && !canvas.Texts.Any(t => t.Value.Contains('│')), "Caret must not add a text glyph or trailing space");
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!view.TextCaretNeedsRedraw && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Check(view.TextCaretNeedsRedraw, "Idle text input must request a blink repaint");
            Paint(); Check(!Caret(), "Blink repaint must hide the caret");
            view.KeyDown(65, true, false); Paint();
            Check(canvas.Fills.Any(f => f.Color == 0x365D77 && f.Bounds.Width > 0), "Ctrl+A must highlight selected text");
            Check(!Caret(), "Selected text should not have an end caret");
            view.TextInput('x'); Paint();
            Check(canvas.Texts.Any(t => t.Value == "x") && Caret(), "Typing replaces the selected text and resets blinking");
            view.SetTextInputFocus(false); Paint();
            Check(!Caret() && !view.TextCaretNeedsRedraw, "An unfocused window must not blink");
            view.NewProject(); view.CloseLibrary(); view.SetTextInputFocus(true); Paint();
            foreach (var (menu, last) in new[] { ("ui.file", "ui.exitMenu"), ("ui.view", "movement.analysis"), ("ui.edit", "sliderBatch.menu") })
            {
                var label = canvas.Texts.Single(t => t.Value == L.Get(menu));
                view.PointerDown(label.X + 2, label.Y + 2, 0, false, false); view.PointerUp(label.X + 2, label.Y + 2, 0); Paint();
                var bounds = canvas.Outlines.Single(o => o.Bounds.Width == 282 && o.Bounds.Y == 38).Bounds;
                var lastLabel = canvas.Texts.Single(t => t.Value == L.Get(last));
                Check(Math.Abs(bounds.Bottom - lastLabel.Y - 30.5) < .01, "Menu must end with seven pixels of padding after its last row");
                view.KeyDown(27, false, false); Paint();
            }
        }
        finally
        {
            var settle = DateTime.UtcNow.AddSeconds(10);
            while (view.LibraryLoading && DateTime.UtcNow < settle) { Paint(); Thread.Sleep(10); }
            if (Directory.Exists(root) && !view.LibraryLoading) Directory.Delete(root, true);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
