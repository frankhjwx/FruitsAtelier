using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(nint window, nint dc, uint flags);

    private void CheckEditorThemes()
    {
        string folder = Path.Combine(Artifacts, "tests", "editor-themes-native");
        Directory.CreateDirectory(folder);
        string language = L.Language;
        var original = view.LibrarySettings.EditorTheme;
        var preference = view.RequestViewPreference;
        view.RequestViewPreference = null;
        try
        {
            view.LoadDocument(DemoMap.Create()); view.CloseLibrary();
            Native.SetWindowPos(hwnd, 0, 0, 0, 1920, 1080, 0x0004 | 0x0010);
            Native.ShowWindow(hwnd, 4);
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var theme in Enum.GetValues<EditorThemeId>())
            {
                L.SetLanguage(locale);
                view.OpenSettings(); Paint();
                var r = view.SettingsBounds;
                Click(r.X + 40, r.Y + 192); Paint();
                var selector = view.EditorThemeSelectorBounds;
                Click(selector.X + 12, selector.Y + 12); Paint();
                Native.GetClientRect(hwnd, out var size);
                float top = Math.Clamp(selector.Bottom + 4, 0, Math.Max(0, size.Bottom * 96 / uiDpi - (12 + Enum.GetValues<EditorThemeId>().Length * 32)));
                Click(selector.X + 12, top + 6 + (int)theme * 32 + 16); Paint();
                CheckTitleBar();
                Capture($"{theme}-{locale}-settings");
                view.ApplySettings(Path.Combine(folder, "settings.json"), force: true); Paint();
                if (view.LibrarySettings.EditorTheme != theme || failed)
                    throw new InvalidOperationException($"Native theme selection failed: {theme}.");
                view.KeyDown(27, false, false); Paint(); Capture($"{theme}-{locale}-editor");
                CheckTitleBar();
            }
            AppLog.Write("Editor theme native checks passed: eight palettes, settings input and persistence in English and Chinese.");
        }
        finally { Native.ShowWindow(hwnd, 0); view.LibrarySettings.EditorTheme = original; view.RequestViewPreference = preference; L.SetLanguage(language); }

        void Paint() => WndProc(hwnd, 0x000F, 0, 0);
        void CheckTitleBar()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            if (!titleBarColoursApplied || titleBarTheme != (view.LightEditorTheme, view.TitleBarBackground, view.TitleBarForeground))
                throw new InvalidOperationException("Windows did not accept the current titlebar palette.");
        }
        void Click(float x, float y)
        {
            int px = (int)Math.Round(x * uiDpi / 96), py = (int)Math.Round(y * uiDpi / 96);
            nint point = (nint)((long)(ushort)py << 16 | (ushort)px);
            HandleMessage(hwnd, 0x0201, 0, point); HandleMessage(hwnd, 0x0202, 0, point);
        }
        void Capture(string name)
        {
            canvas!.Begin(); Native.GetClientRect(hwnd, out var size);
            view.Render(canvas, size.Right * 96 / uiDpi, size.Bottom * 96 / uiDpi);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                SaveFrame(frame.Width, frame.Height, frame.Pixels, Path.Combine(folder, name + ".bmp"));
            });
            Native.GetWindowRect(hwnd, out var bounds);
            using var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            nint dc = graphics.GetHdc();
            bool captured;
            try { captured = PrintWindow(hwnd, dc, 2); }
            finally { graphics.ReleaseHdc(dc); }
            if (captured) bitmap.Save(Path.Combine(folder, name + "-window.png"), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
