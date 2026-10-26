#if WINDOWS
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Media;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class StoryboardFirstPlaybackTests
{
    public static int Run(string path, string output, int seed)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Check(path, output, seed); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Cold storyboard playback failed", failure);
        return 0;
    }

    private static void Check(string path, string output, int seed)
    {
        Directory.CreateDirectory(output);
        Native.WindowProc procedure = Native.DefWindowProc;
        string className = "FruitsAtelier.HiddenFirstPlayback." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        nint hwnd = Native.CreateWindowEx(0, className, "Hidden cold storyboard playback", Native.WindowStyle, -32000, -32000, 1000, 680, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        var view = new EditorView(false);
        var frames = new List<object>();
        try
        {
            using var canvas = new D2DCanvas(hwnd, 980, 620, 96);
            var random = new Random(seed);
            var decodeRandom = new Random(seed ^ 0x1729);
            bool delayed = seed % 2 != 0;
            var decodeMethod = typeof(D2DCanvas).GetMethod("DecodeStoryboardImage", BindingFlags.Static | BindingFlags.NonPublic)!;
            var cache = new StoryboardImageCache(p =>
            {
                if (delayed) Thread.Sleep(decodeRandom.Next(10, 65));
                return (StoryboardImageCache.Pixels?)decodeMethod.Invoke(null, [p]);
            });

            view.LibrarySettings.BackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim = 0;
            var document = OsuBeatmapReader.ReadFile(path);
            var project = BeatmapProject.FromDocuments([document]);
            view.InitializeLibrary(false, new LibrarySettings { Workspace = Path.Combine(Path.GetFullPath(output), "workspace"), FirstRunSetupCompleted = true, BackgroundDim = 0, MappingCanvasBackgroundDim = 0, ForceBackgroundDim = false });
            view.LoadWorkspace(new(Path.GetDirectoryName(Path.GetFullPath(path))!, new WorkspaceManifest { Name = project.Name }, project)); view.CloseLibrary();
            canvas.Begin(); view.Render(canvas, 980, 620); canvas.End();
            (Field(canvas, "storyboardPixels") as StoryboardImageCache)?.Dispose();
            typeof(D2DCanvas).GetField("storyboardPixels", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(canvas, cache);
            void Draw() { canvas.Begin(); view.Render(canvas, 980, 620); canvas.End(); }
            var opening = Stopwatch.StartNew();
            do { Draw(); Thread.Sleep(1); } while ((view.LibraryOpening || Field(view, "storyboardLoad") is Task) && opening.Elapsed.TotalSeconds < 15);
            if (view.LibraryOpening) throw new TimeoutException("Opening failed to settle.");
            view.UpdateTransport(0, 190000, true, false, false, null, document.AudioPath);
            for (int i = 0; i < 32; i++)
            {
                view.UpdateTransport(random.NextDouble() * 185000, 190000, true, true, false, null, document.AudioPath); Draw(); Thread.Sleep(random.Next(1, 12));
            }
            var clock = Stopwatch.StartNew(); int samples = 0, missed = 0;
            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (clock.Elapsed.TotalMilliseconds < 6500)
            {
                double position = 126000 + clock.Elapsed.TotalMilliseconds;
                view.UpdateTransport(position, 190000, true, true, false, null, document.AudioPath); Draw();
                var target = (List<StoryboardSpriteState>)Field(view, "storyboardFrame")!;
                var display = (List<StoryboardSpriteState>)Field(view, "storyboardDisplayFrame")!;
                var textures = (IDictionary)Field(canvas, "storyboardTextures")!;
                string[] absent = display.Select(s => s.Path).Distinct().Where(p => !textures.Contains(p)).ToArray();
                if (absent.Length > 0) throw new Exception("Complete displayed frame lost textures: " + string.Join(", ", absent));
                bool ready = (bool)Field(view, "storyboardTargetReady")!;
                if (!ready) missed++;
                if (ready)
                    foreach (var sprite in display.Where(s => s.Path.Contains(Path.Combine("SB", "s15"), StringComparison.OrdinalIgnoreCase))) observed.Add(Path.GetFileName(sprite.Path));
                if (++samples % 4 == 0)
                    lock (Field(cache, "gate")!)
                        frames.Add(new { position, ready, playhead = view.PlayheadMs, target = target.Select(s => Path.GetFileName(s.Path)).Distinct().ToArray(), display = display.Select(s => Path.GetFileName(s.Path)).Distinct().ToArray(), loading = Field(cache, "loadingPath"), pending = ((IEnumerable<string>)Field(cache, "pending")!).ToArray(), textures = canvas.StoryboardTextureBytes, decoded = cache.Bytes });
                Thread.Sleep(7);
            }
            File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(new { seed, delayed, samples, missed, observed, frames }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (string image in new[] { "4.jpg", "5.jpg", "6.jpg", "7.jpg" })
                if (!observed.Contains(image)) throw new Exception("First playback omitted " + image);
            using var process = Process.GetCurrentProcess(); process.Refresh();
            if (process.PrivateMemorySize64 > 3L * 1024 * 1024 * 1024) throw new Exception("Memory exceeded 3 GiB.");
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { seed, delayed, samples, missed, observed, cache.DecodeCount, cache.Error, privateBytes = process.PrivateMemorySize64, frames }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS cold seed={seed}, delayed={delayed}, frames={samples}, retained={missed}, displayed={string.Join(',', observed)}, privateMiB={process.PrivateMemorySize64 / 1048576d:F1}");
        }
        finally { view.StopFileMonitoring(); view.StopMapsetVerifier(); view.StopDiscussions(); view.CompleteLibraryCacheWrites(); Native.DestroyWindow(hwnd); GC.KeepAlive(procedure); }
    }
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
}
#else
internal static class StoryboardFirstPlaybackTests
{
    public static int Run(string path, string output, int seed) => throw new PlatformNotSupportedException();
}
#endif
