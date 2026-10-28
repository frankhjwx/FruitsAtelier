namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private (bool Light, uint Background, uint Foreground)? titleBarTheme;
    private bool titleBarColoursApplied;

    private void RefreshTitleBarTheme()
    {
        var theme = (view.LightEditorTheme, view.TitleBarBackground, view.TitleBarForeground);
        if (titleBarTheme == theme) return;
        int dark = theme.LightEditorTheme ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        int background = TitleBarColorRef(theme.TitleBarBackground);
        int foreground = TitleBarColorRef(theme.TitleBarForeground);
        int backgroundResult = Native.DwmSetWindowAttribute(hwnd, 35, ref background, 4);
        int foregroundResult = Native.DwmSetWindowAttribute(hwnd, 36, ref foreground, 4);
        titleBarColoursApplied = backgroundResult == 0 && foregroundResult == 0;
        titleBarTheme = theme;
    }

    private static int TitleBarColorRef(uint rgb) => (int)((rgb & 255) << 16 | rgb & 0xFF00 | rgb >> 16 & 255);
}
