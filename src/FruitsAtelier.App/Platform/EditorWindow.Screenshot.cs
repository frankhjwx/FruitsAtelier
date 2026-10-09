using L = FruitsAtelier.Localization.Strings;
using System.Runtime.InteropServices;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private bool screenshotPending;

    private void CopyScreenshot()
    {
        screenshotPending = false;
        try
        {
            var frame = canvas!.CapturePixels();
            Native.WriteClipboardImage(hwnd, frame.Width, frame.Height, frame.Pixels);
            view.SetNotice(L.Get("screenshot.copied"));
        }
        catch (Exception error)
        {
            AppLog.Write(error.ToString());
            view.SetNotice(L.Get("screenshot.failed", error.Message));
        }
        Invalidate();
    }

    private void CheckScreenshot()
    {
        string folder = Path.Combine(Artifacts, "tests", "screenshot-native");
        Directory.CreateDirectory(folder);
        view.LoadDocument(DemoMap.Create()); view.CloseLibrary();
        var cases = new List<object>();
        foreach (int testDpi in new[] { 96, 144, 192 })
        foreach (bool paused in new[] { false, true })
        {
            int width = 980 * testDpi / 96, height = 620 * testDpi / 96;
            canvas!.Resize(width, height, testDpi);
            if (paused) { view.StartTestplay(); view.KeyDown(27, false, false); view.KeyUp(27); }
            view.KeyDown(123, false, false);
            if (!screenshotPending) throw new InvalidOperationException("F12 did not request screenshot capture.");
            canvas.Begin(); view.Render(canvas, 980, 620);
            canvas.Fill(new(0, 0, 4, 4), 0xFF0000);
            canvas.Fill(new(976, 616, 4, 4), 0x0000FF);
            canvas.End(capture: CopyScreenshot);
            view.KeyUp(123);
            if (screenshotPending || view.StatusMessage != L.Get("screenshot.copied"))
                throw new InvalidOperationException("Screenshot capture did not reach the clipboard.");
            if (!Native.OpenClipboard(hwnd)) throw new InvalidOperationException("Cannot inspect screenshot clipboard.");
            byte[] dib = new byte[40 + width * height * 4];
            try
            {
                nint memory = Native.GetClipboardData(8);
                if (memory == 0) throw new InvalidOperationException("Screenshot clipboard has no CF_DIB image.");
                nint pointer = Native.GlobalLock(memory);
                if (pointer == 0) throw new InvalidOperationException("Cannot read clipboard image.");
                try { Marshal.Copy(pointer, dib, 0, dib.Length); }
                finally { Native.GlobalUnlock(memory); }
            }
            finally { Native.CloseClipboard(); }
            if (BitConverter.ToInt32(dib, 4) != width || BitConverter.ToInt32(dib, 8) != -height
                || dib[40] != 0 || dib[42] != 255 || dib[^4] != 255 || dib[^2] != 0)
                throw new InvalidOperationException("Screenshot dimensions, colour or row order differ from the rendered frame.");
            string path = Path.Combine(folder, $"clipboard-{testDpi}-{(paused ? "paused" : "editor")}.bmp");
            using (var output = new BinaryWriter(File.Create(path)))
            {
                output.Write((ushort)0x4D42); output.Write(14 + dib.Length);
                output.Write(0); output.Write(54); output.Write(dib);
            }
            cases.Add(new { dpi = testDpi, width, height, paused, clipboardImage = true });
            if (paused) view.StopTestplay();
        }
        File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(cases));
        AppLog.Write("Screenshot clipboard native checks passed.");
    }
}
