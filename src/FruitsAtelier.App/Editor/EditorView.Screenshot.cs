namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool screenshotHeld;
    public Action? RequestScreenshot { get; set; }

    private bool ScreenshotKeyDown(int key, bool ctrl, bool shift)
    {
        if (key != 123 || ctrl || shift || altHeld) return false;
        if (!screenshotHeld)
        {
            screenshotHeld = true;
            RequestScreenshot?.Invoke();
        }
        return true;
    }
}
