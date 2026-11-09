using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckKeybindings()
    {
        string folder = Path.Combine(Artifacts, "tests", "keybindings-native"); Directory.CreateDirectory(folder);
        string language = L.Language;
        try
        {
            view.LoadDocument(DemoMap.Create()); view.CloseLibrary();
            Native.SetWindowPos(hwnd, 0, 0, 0, 1920, 1080, 0x0004 | 0x0010); Native.ShowWindow(hwnd, 4);
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale); view.OpenSettings(); Paint();
                var r = view.SettingsBounds; Click(r.X + 40, r.Y + 78 + 6 * 48 + 16); Paint();
                r = view.SettingsBounds;
                Capture(locale + "-list");
                float keyX = r.X + 230 + (r.Width - 262) * .59f;
                Click(keyX + 14, r.Y + 326); Paint();
                if (!view.CapturingShortcut) throw new InvalidOperationException("Native keycap did not start capture.");
                view.KeyDown('D', true, true); view.KeyUp('D'); Paint(); Capture(locale + "-capture");
                view.KeyDown('D', true, false); view.KeyUp('D'); Paint();
                Native.GetClientRect(hwnd, out var size); float w = size.Right * 96 / uiDpi, h = size.Bottom * 96 / uiDpi;
                Click((w + 550) / 2 - 100, (h + 310) / 2 - 40); Paint(); Capture(locale + "-conflict");
                Click((w + 550) / 2 - 240, (h + 360) / 2 - 40); Paint();
                view.KeyDown(27, false, false); view.KeyUp(27);
                var command = Editor.EditorView.ShortcutCommands.Single(c => c.Id == "select" && c.Scope == Editor.EditorView.ShortcutScope.Compose && !c.Lazer);
                view.LibrarySettings.ShortcutBindings[command.StorageKey] = [ShortcutBinding.From(17, 16, 'D')];
                view.OpenSettings(); Paint(); r = view.SettingsBounds;
                Click(r.X + 40, r.Y + 78 + 6 * 48 + 16); Paint(); Capture(locale + "-modified");
                r = view.SettingsBounds;
                Click(r.X + 230 + (r.Width - 262) / 9 * 5.5f, r.Y + 190); Paint();
                view.Wheel(r.X + 400, r.Y + 400, -12000, false); Paint(); Capture(locale + "-hitsounding");
                view.LibrarySettings.ShortcutBindings.Remove(command.StorageKey);
                view.KeyDown(27, false, false); view.KeyUp(27);
            }
            L.SetLanguage("en"); view.OpenSettings(); Paint();
            view.PointerMove(140, 20, false, false); Capture("settings-editor-blocked", true);
            view.KeyDown(27, false, false); view.KeyUp(27);
            view.LibrarySettings.Workspace = Path.Combine(folder, "library-workspace"); view.LibrarySettings.OsuRoot = "";
            view.MarkSaved(); view.ShowLibrary(); Paint(); view.OpenSettings(); Paint();
            view.PointerMove(70, 214, false, false); Capture("settings-library-blocked", true);
            AppLog.Write("Shortcut native render checks passed: list, chord capture and conflict modal in English and Chinese.");
        }
        finally { L.SetLanguage(language); Native.ShowWindow(hwnd, 0); }
        void Paint() => WndProc(hwnd, 0x000F, 0, 0);
        void Click(float x, float y)
        {
            nint point = (nint)((long)(ushort)(y * uiDpi / 96) << 16 | (ushort)(x * uiDpi / 96));
            HandleMessage(hwnd, 0x0201, 0, point); HandleMessage(hwnd, 0x0202, 0, point);
        }
        void Capture(string name, bool keepPointer = false)
        {
            if (!keepPointer) view.PointerMove(0, 0, false, false);
            canvas!.Begin(); Native.GetClientRect(hwnd, out var size);
            view.Render(canvas, size.Right * 96 / uiDpi, size.Bottom * 96 / uiDpi);
            canvas.End(capture: () =>
            {
                var frame = canvas.CapturePixels();
                using var bitmap = new System.Drawing.Bitmap(frame.Width, frame.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var data = bitmap.LockBits(new(0, 0, frame.Width, frame.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
                try { System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, 0, data.Scan0, frame.Pixels.Length); }
                finally { bitmap.UnlockBits(data); }
                bitmap.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            });
        }
    }
}
