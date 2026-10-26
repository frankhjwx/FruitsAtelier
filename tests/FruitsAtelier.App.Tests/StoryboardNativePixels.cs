#if WINDOWS
using System.Diagnostics;
using System.Runtime.InteropServices;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using Vortice.WIC;

internal static class StoryboardNativePixels
{
    public static int Run(string directory, string characterDirectory)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Check(Path.GetFullPath(directory), Path.GetFullPath(characterDirectory)); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Storyboard native pixels failed", failure);
        return 0;
    }

    private static void Check(string directory, string characterDirectory)
    {
        Directory.CreateDirectory(directory);
        string white = Path.Combine(directory, "white.bmp");
        SaveBitmap(1, 1, [255, 255, 255, 255], white);
        string first = Path.Combine(characterDirectory, "1.png");
        string second = Path.Combine(Path.GetDirectoryName(first)!, "1_2.png");
        Native.WindowProc procedure = Native.DefWindowProc;
        string className = "FruitsAtelier.HiddenPixels." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        nint hwnd = Native.CreateWindowEx(0, className, "Hidden native pixel check", Native.WindowStyle, -32000, -32000, 800, 750, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        try
        {
            using var canvas = new D2DCanvas(hwnd, 739, 672, 96);
            var paths = new[] { white, first, second };
            canvas.PrepareStoryboardImages(paths);
            var warm = Stopwatch.StartNew();
            do { Draw(1, true); Thread.Sleep(5); } while (canvas.MediaNeedsRedraw && warm.Elapsed.TotalSeconds < 5);
            var whiteReference = Draw(0, true); var whiteOverlay = Draw(.5f, true);
            int whiteIndex = (40 * 739 + 40) * 4;
            for (int channel = 0; channel < 3; channel++)
                if (Math.Abs(whiteReference[whiteIndex + channel] - whiteOverlay[whiteIndex + channel]) > 1 || whiteOverlay[whiteIndex + channel] < 254)
                    throw new Exception("Half-opacity white over opaque white darkened: " + whiteOverlay[whiteIndex + channel]);
            var a = Decode(first); var b = Decode(second);
            var baseline = Draw(0, false);
            int compared = 0, maximumDelta = 0;
            foreach (float opacity in new[] { .25f, (34024f - 33931) / 187, .75f, 1f })
            {
                byte[] actual = Draw(opacity, false);
                SaveBitmap(739, 672, actual, Path.Combine(directory, "asuka-" + opacity.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + ".bmp"));
                for (int i = 0; i < a.Length; i += 4)
                {
                    if (a[i + 3] != 255 || b[i + 3] != 255 || a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) continue;
                    for (int channel = 0; channel < 3; channel++) maximumDelta = Math.Max(maximumDelta, Math.Abs(baseline[i + channel] - actual[i + channel]));
                    compared++;
                }
            }
            if (compared < 10000 || maximumDelta > 2) throw new Exception($"Asuka common opaque pixels changed during expression crossfade: count={compared}, maximum delta={maximumDelta}.");
            File.WriteAllText(Path.Combine(directory, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { visible = false, compared, maximumDelta,
                timeRange = new[] { 33931, 34024, 34118 }, whiteOverlay = .5, asukaOverlay = (34024d - 33931) / 187,
                note = "Hidden Direct2D SpriteBatch readback. Source opacity and tint retained; identical opaque source regions must keep their brightness under crossfade." }));
            Console.WriteLine($"PASS native white alpha and Asuka 33931–34118 expression crossfade: {compared} opaque comparisons, maximum channel delta {maximumDelta}");

            byte[] Draw(float opacity, bool testWhite)
            {
                byte[]? pixels = null;
                canvas.Begin(); canvas.Fill(new(0, 0, 739, 672), 0);
                canvas.BeginStoryboard();
                if (testWhite)
                {
                    canvas.StoryboardSprite(white, 32, 32, 32, 32, 0xFFFFFF, 1, 0, false, false, false, 0, 0);
                    canvas.StoryboardSprite(white, 32, 32, 32, 32, 0xFFFFFF, opacity, 0, false, false, false, 0, 0);
                }
                else
                {
                    canvas.StoryboardSprite(first, 0, 0, 1, 1, 0xFFFFFF, 1, 0, false, false, false, 0, 0);
                    canvas.StoryboardSprite(second, 0, 0, 1, 1, 0xFFFFFF, opacity, 0, false, false, false, 0, 0);
                }
                canvas.EndStoryboard(); canvas.End(capture: () => pixels = canvas.CapturePixels().Pixels);
                return pixels!;
            }
        }
        finally { Native.DestroyWindow(hwnd); GC.KeepAlive(procedure); }
    }

    private static byte[] Decode(string path)
    {
        using var factory = new IWICImagingFactory(); using var decoder = factory.CreateDecoderFromFileName(path); using var frame = decoder.GetFrame(0);
        frame.GetSize(out uint width, out uint height);
        using var converter = factory.CreateFormatConverter(); converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();
        var pixels = new byte[width * height * 4]; converter.CopyPixels(width * 4, pixels); return pixels;
    }
    private static void SaveBitmap(int width, int height, byte[] pixels, string path)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0x4D42); output.Write(54 + pixels.Length); output.Write(0); output.Write(54);
        output.Write(40); output.Write(width); output.Write(-height); output.Write((ushort)1); output.Write((ushort)32);
        output.Write(0); output.Write(pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(pixels);
    }
}
#else
internal static class StoryboardNativePixels
{
    public static int Run(string directory, string characterDirectory) => throw new PlatformNotSupportedException("Native pixel validation requires Windows.");
}
#endif
