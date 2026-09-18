using L = FruitsAtelier.Localization.Strings;

internal static class OperationErrorTests
{
    public static void Run()
    {
        foreach (bool library in new[] { false, true })
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(false); ui.Resize(980, 620);
            if (library)
            {
                ui.View.LibrarySettings.Workspace = Path.GetFullPath("artifacts/tests/error-dialog/" + Guid.NewGuid());
                ui.View.LibrarySettings.Songs = "";
                ui.View.ShowLibrary(); ui.Paint();
            }
            var before = ui.View.Document.DeepClone();
            double time = ui.View.PlayheadMs, zoom = ui.View.CanvasZoom;
            string message = "bad-map.osu\n" + string.Join('\n', Enumerable.Range(0, 30).Select(i => $"Error line {i}: 文件无法加载。"));
            ui.View.ShowError(message); ui.Paint();
            if (!ui.Canvas.Texts.Any(t => t.Value == "bad-map.osu") || !ui.Canvas.Texts.Any(t => t.Value == L.Get("files.incomplete")))
                throw new Exception("File errors must be visible inside either editor page.");
            ui.View.ShowError("secondary error"); ui.Paint();
            if (ui.Canvas.Texts.Any(t => t.Value == "secondary error")) throw new Exception("Repeated errors replaced the active message.");
            ui.Click(40, 40); ui.Key(46); ui.Key('Z', ctrl: true); ui.Type("x");
            ui.View.PasteLibraryText("paste"); ui.View.PointerDoubleClick(300, 200, false, false);
            ui.View.Wheel(400, 300, -120, false); ui.Paint();
            if (ui.Canvas.Texts.Any(t => t.Value == "bad-map.osu") || ui.View.PrepareFileOperation())
                throw new Exception("Error text did not scroll or file actions remained enabled.");
            if (!before.ContentEquals(ui.View.Document) || ui.View.PlayheadMs != time || ui.View.CanvasZoom != zoom || !ui.View.ErrorVisible)
                throw new Exception("Error input leaked through to the document.");
            ui.ClickText(L.Get("mac.ok"));
            if (ui.View.ErrorVisible || ui.View.LibraryVisible != library) throw new Exception("Dismissal did not restore the original page.");
            ui.View.ShowError("again"); ui.Key(27);
            if (ui.View.ErrorVisible) throw new Exception("Escape did not dismiss a later error.");
            ui.View.ShowError("again"); ui.Key(13);
            if (ui.View.ErrorVisible) throw new Exception("Enter did not dismiss the error.");
        }
    }
}
