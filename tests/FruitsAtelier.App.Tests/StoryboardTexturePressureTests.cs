#if WINDOWS
using System.Reflection;
using System.Runtime.InteropServices;
using FruitsAtelier.App.Media;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;

internal static class StoryboardTexturePressureTests
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Check(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Storyboard texture pressure regression", failure);
        return 0;
    }

    private static void Check()
    {
        Native.WindowProc procedure = Native.DefWindowProc;
        string name = "StoryboardTexturePressure." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = name };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        nint hwnd = Native.CreateWindowEx(0, name, name, Native.WindowStyle, -32000, -32000, 640, 480, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        try
        {
            using var canvas = new D2DCanvas(hwnd, 640, 480, 96);
            byte[] data = new byte[8 * 1024 * 1024];
            using var cache = new StoryboardImageCache(_ => new(2048, 1024, data));
            typeof(D2DCanvas).GetField("storyboardPixels", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(canvas, cache);
            string[] preload = Enumerable.Range(0, 40).Select(i => "pressure-" + i).ToArray();
            canvas.PrepareStoryboardImages(preload);
            Wait();
            string[] required = [preload[38], preload[39]];
            canvas.Begin();
            if (canvas.StoryboardImagesReady(required)) throw new Exception("Two 8 MiB textures bypassed the per-frame upload budget.");
            canvas.BeginStoryboard(); canvas.EndStoryboard(); canvas.End();
            if (!canvas.MediaNeedsRedraw) throw new Exception("Deferred required upload lost its redraw request.");
            canvas.Begin();
            if (!canvas.StoryboardImagesReady(required)) throw new Exception("Deferred required image never became ready.");
            canvas.RetainStoryboardImages(required); canvas.BeginStoryboard(); canvas.EndStoryboard(); canvas.End();
            int uploads = canvas.StoryboardUploadCount;
            for (int frame = 0; frame < 80; frame++)
            {
                canvas.Begin(); canvas.StoryboardImagesReady(required);
                canvas.BeginStoryboard(); canvas.EndStoryboard(); canvas.End();
            }
            if (uploads != 2 || canvas.StoryboardUploadCount != uploads || canvas.StoryboardTextureBytes != 16 * 1024 * 1024)
                throw new Exception("Oversized speculative lookahead repeatedly uploaded or evicted textures for an unchanged frame.");
            cache.Prepare(Enumerable.Range(40, 40).Select(i => "pressure-" + i).ToArray()); Wait();
            int decodes = cache.DecodeCount;
            canvas.PrepareStoryboardImages(required); Wait();
            if (cache.DecodeCount != decodes) throw new Exception("Resident native textures were unnecessarily decoded after CPU cache eviction.");
            Console.WriteLine("PASS 320 MiB lookahead leaves only 16 MiB required textures, stable warm uploads and no resident redecodes; deferred uploads request redraw");
            void Wait() { if (!SpinWait.SpinUntil(() => !cache.Loading, TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
        }
        finally { Native.DestroyWindow(hwnd); GC.KeepAlive(procedure); }
    }
}
#else
internal static class StoryboardTexturePressureTests
{
    internal static int Run() => throw new PlatformNotSupportedException("Native Direct2D check requires Windows.");
}
#endif
