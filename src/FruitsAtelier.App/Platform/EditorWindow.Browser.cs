namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void UpdateBrowserViewport()
    {
        var viewport = view.BrowserViewport;
        if (viewport.Width <= 0 && osuBrowser is null) return;
        float scale = uiDpi / 96f;
        var bounds = new System.Drawing.Rectangle((int)(viewport.X * scale), (int)(viewport.Y * scale),
            (int)(viewport.Width * scale), (int)(viewport.Height * scale));
        if (osuBrowser is null)
        {
            osuBrowser = new OsuBrowserWindow();
            view.RequestBrowserCommand = command => osuBrowser.Execute(command, view.BrowserUrl);
        }
        osuBrowser.Update(hwnd, bounds, view.BrowserUrl);
    }
}
