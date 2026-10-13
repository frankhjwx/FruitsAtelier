using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckDpiScaling()
    {
        string folder = Path.Combine(Artifacts, "tests", "dpi-native");
        Directory.CreateDirectory(folder);
        string language = L.Language;
        var preference = view.RequestViewPreference;
        float originalDpi = dpi;
        nint originalStyle = Native.GetWindowLongPtr(hwnd, -16);
        nint memory = Marshal.AllocHGlobal(Marshal.SizeOf<Native.Rectangle>());
        var results = new List<object>();
        Rect? referencePlot = null;
        view.RequestViewPreference = null;
        try
        {
            CheckFrameSize();
            Native.SetWindowLongPtr(hwnd, -16, unchecked((nint)0x80000000u));
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (var size in new[] { (2520, 1575), (3840, 2160), (2560, 1440), (1920, 1080), (1920, 1200) })
            {
                L.SetLanguage(locale);
                var document = DemoMap.Create();
                view.LibrarySettings.CanvasZoom = .6;
                view.LibrarySettings.CanvasTimeZoom = .6;
                view.LoadDocument(document); view.CloseLibrary();
                Native.SetWindowPos(hwnd, 0, 0, 0, size.Item1, size.Item2, 0x0004 | 0x0010 | 0x0020);
                Native.GetClientRect(hwnd, out var client);
                if (client.Right != size.Item1 || client.Bottom != size.Item2)
                    throw new InvalidOperationException("DPI check did not obtain its requested client pixel dimensions.");
                float expectedDpi = size switch
                {
                    (2520, 1575) => 168, (3840, 2160) => 230.4f,
                    (2560, 1440) => 153.6f, (1920, 1080) => 115.2f, _ => 128
                };
                string? referencePixels = null;
                Rect? referenceBounds = null;
                if (size == (2520, 1575))
                {
                    canvas!.Resize(2520, 1575, 168);
                    canvas.Begin(); view.Render(canvas, 1440, 900);
                    referenceBounds = view.CanvasScaleLockBounds;
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        referencePixels = MenuHash(frame.Width, frame.Pixels, 168);
                    });
                }
                foreach (int monitorDpi in new[] { 168, 96, 120, 144, 192, 240 })
                {
                    Native.GetWindowRect(hwnd, out var suggested);
                    Marshal.StructureToPtr(suggested, memory, false);
                    HandleMessage(hwnd, 0x02E0, (nuint)(monitorDpi | monitorDpi << 16), memory);
                    WndProc(hwnd, 0x000F, 0, 0);
                    if (failed || Math.Abs(uiDpi - expectedDpi) > .001)
                        throw new InvalidOperationException("Client scaling must depend on pixel dimensions, not monitor DPI.");
                    var bounds = view.CanvasScaleLockBounds;
                    if (size.Item2 * 16 == size.Item1 * 9)
                    {
                        var plot = view.CanvasPlotBounds;
                        referencePlot ??= plot;
                        var expected = referencePlot.Value;
                        if (Math.Abs(plot.X - expected.X) > .001 || Math.Abs(plot.Y - expected.Y) > .001
                            || Math.Abs(plot.Width - expected.Width) > .001 || Math.Abs(plot.Height - expected.Height) > .001)
                            throw new InvalidOperationException("Equal aspect ratios must retain the same logical editing area.");
                    }
                    referenceBounds ??= bounds;
                    if (bounds != referenceBounds.Value)
                        throw new InvalidOperationException("Monitor DPI changed the logical toolbar layout.");
                    // Compare the static menu strip to the 175% monitor case at this client resolution.
                    canvas!.Begin(); view.Render(canvas, client.Right * 96 / uiDpi, client.Bottom * 96 / uiDpi);
                    string pixelHash = "";
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        pixelHash = MenuHash(frame.Width, frame.Pixels, uiDpi);
                        if (monitorDpi == 168) SaveFrame(frame.Width, frame.Height, frame.Pixels,
                            Path.Combine(folder, $"{locale}-{size.Item1}x{size.Item2}.bmp"));
                    });
                    referencePixels ??= pixelHash;
                    if (pixelHash != referencePixels)
                        throw new InvalidOperationException("Monitor DPI changed the rendered menu pixels.");

                    bool uniform = view.UniformCanvasScale;
                    Click(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                    if (view.UniformCanvasScale == uniform) throw new InvalidOperationException("Scaled button hit testing missed the lock.");
                    Click(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                    WndProc(hwnd, 0x000F, 0, 0);
                    var slider = view.ZoomSliderBounds;
                    SendPointer(0x0201, slider.X + slider.Width / 2, slider.Y + slider.Height / 2);
                    SendPointer(0x0200, slider.Right, slider.Y + slider.Height / 2);
                    SendPointer(0x0202, slider.Right, slider.Y + slider.Height / 2);
                    if (Math.Abs(view.CanvasZoom - 1) > .001 || view.WantsCapture)
                        throw new InvalidOperationException("Scaled slider dragging missed its endpoint or retained capture.");
                    int snap = view.SnapDivisor;
                    Wheel(120);
                    if (view.SnapDivisor == snap) throw new InvalidOperationException("Scaled wheel dispatch missed the canvas.");
                    Wheel(-120);
                    HandleMessage(hwnd, 0x02A3, 0, 0);
                    if (!document.ContentEquals(view.Document)) throw new InvalidOperationException("DPI input checks changed beatmap content.");
                    results.Add(new { locale, width = client.Right, height = client.Bottom, monitorDpi, uiDpi = uiDpi, pixelHash });
                }
                if (size == (2520, 1575))
                {
                    view.ShowLibrary();
                    WndProc(hwnd, 0x000F, 0, 0);
                    canvas!.Begin(); view.Render(canvas, 1440, 900);
                    canvas.End(capture: () =>
                    {
                        var frame = canvas.CapturePixels();
                        SaveFrame(frame.Width, frame.Height, frame.Pixels, Path.Combine(folder, $"{locale}-library-reference.bmp"));
                    });
                    view.CloseLibrary();
                }
            }
            File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(results));
            AppLog.Write("Client-resolution DPI checks passed: rendering, click, drag and wheel at 1080p, 1440p, 4K and 16:10 across six monitor DPI values.");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            dpi = originalDpi;
            L.SetLanguage(language);
            view.RequestViewPreference = preference;
            Native.SetWindowLongPtr(hwnd, -16, originalStyle);
        }

        void Click(float x, float y) { SendPointer(0x0201, x, y); SendPointer(0x0202, x, y); }
        void SendPointer(uint message, float x, float y)
        {
            int px = (int)Math.Round(x * uiDpi / 96), py = (int)Math.Round(y * uiDpi / 96);
            HandleMessage(hwnd, message, 0, (nint)((long)(ushort)py << 16 | (ushort)px));
        }
        void Wheel(int delta)
        {
            var plot = view.CanvasPlotBounds;
            var point = new Native.Point { X = (int)((plot.X + plot.Width / 2) * uiDpi / 96),
                Y = (int)((plot.Y + plot.Height / 2) * uiDpi / 96) };
            Native.GetWindowRect(hwnd, out var bounds);
            point.X += bounds.Left; point.Y += bounds.Top;
            HandleMessage(hwnd, 0x020A, (nuint)((uint)(ushort)delta << 16 | 8),
                (nint)((long)(ushort)point.Y << 16 | (ushort)point.X));
        }
        void CheckFrameSize()
        {
            uint style = (uint)originalStyle;
            foreach (int nextDpi in new[] { 96, 168, 240 })
            {
                var before = new Native.Rectangle { Right = 1920, Bottom = 1080 };
                var after = before;
                Native.AdjustWindowRectExForDpi(ref before, style, false, 0, (uint)dpi);
                Native.AdjustWindowRectExForDpi(ref after, style, false, 0, (uint)nextDpi);
                Marshal.StructureToPtr(new Native.Point { X = before.Right - before.Left, Y = before.Bottom - before.Top }, memory, false);
                if (HandleMessage(hwnd, 0x02E4, (nuint)nextDpi, memory) != 1)
                    throw new InvalidOperationException("Window did not override automatic DPI sizing.");
                var actual = Marshal.PtrToStructure<Native.Point>(memory);
                if (actual.X != after.Right - after.Left || actual.Y != after.Bottom - after.Top)
                    throw new InvalidOperationException("Window DPI sizing changed the pending client dimensions.");
            }
        }
    }

    private static string MenuHash(int width, byte[] pixels, float renderDpi)
        => Convert.ToHexString(SHA256.HashData(pixels.AsSpan(0, width * 4 * (int)(36 * renderDpi / 96))));

    private static void SaveFrame(int width, int height, byte[] pixels, string path)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0x4D42); output.Write(54 + pixels.Length); output.Write(0); output.Write(54);
        output.Write(40); output.Write(width); output.Write(-height);
        output.Write((ushort)1); output.Write((ushort)32); output.Write(0); output.Write(pixels.Length);
        output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(pixels);
    }
}
