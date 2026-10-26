#if WINDOWS
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Media;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class MediaToggleNativeTests
{
    public static int Run(string videoPath)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { CheckNative(videoPath); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Hidden native media toggle failed", failure);
        return 0;
    }

    private static void CheckNative(string videoPath)
    {
        string directory = Path.GetFullPath("artifacts/tests/media-toggle/native"); Directory.CreateDirectory(directory);
        string imageA = Path.Combine(directory, "first.bmp"), imageB = Path.Combine(directory, "second.bmp");
        Save(64, 64, Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 64 * 64).SelectMany(p => p).ToArray(), imageA);
        Save(64, 64, Enumerable.Repeat(new byte[] { 0, 255, 0, 255 }, 64 * 64).SelectMany(p => p).ToArray(), imageB);
        string video = Path.Combine(directory, "video" + Path.GetExtension(videoPath));
        File.Copy(videoPath, video, overwrite: true);
        Native.WindowProc procedure = Native.DefWindowProc;
        string className = "FruitsAtelier.HiddenToggle." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        nint hwnd = Native.CreateWindowEx(0, className, "Hidden media toggle", Native.WindowStyle, -32000, -32000, 1000, 680, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        string originalLanguage = L.Language;
        try
        {
            using var canvas = new D2DCanvas(hwnd, 980, 620, 96);
            var view = new EditorView(false); view.LibrarySettings.EnableVideoStoryboard = false;
            view.LibrarySettings.BackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim = 0;
            view.LoadDocument(Map(imageA)); view.CloseLibrary(); Settle();
            Assert(Field(canvas, "storyboardPixels") is null && Field(canvas, "video") is null && canvas.StoryboardTextureBytes == 0,
                "Cold disabled native render opened visual decoders.");
            Toggle(); Settle();
            var ready = Stopwatch.StartNew();
            do { Draw(); Thread.Sleep(2); } while ((canvas.StoryboardTextureBytes == 0 || Field(canvas, "videoBitmap") is null) && ready.Elapsed.TotalSeconds < 5);
            Console.WriteLine($"Native enabled: textures={canvas.StoryboardTextureBytes}, video={Field(canvas, "video") is not null}, bitmap={Field(canvas, "videoBitmap") is not null}, error={canvas.MediaError}, scene={(Field(view, "storyboardScene") as StoryboardScene)?.SpriteCount}, videos={(Field(view, "storyboardScene") as StoryboardScene)?.Videos.Count}, diagnostics={string.Join(';', (Field(view, "storyboardScene") as StoryboardScene)?.Diagnostics ?? [])}, position={view.PlayheadMs}");
            Assert(canvas.StoryboardTextureBytes > 0 && Field(canvas, "videoBitmap") is not null, "Enabled native resources did not load.");
            CheckSharedVideo();
            var retiredPixels = (StoryboardImageCache)Field(canvas, "storyboardPixels")!;
            var retiredVideo = (VideoPlayback)Field(canvas, "video")!;
            Toggle(); Draw();
            Assert(!view.LibraryOpening && Field(canvas, "storyboardPixels") is null && Field(canvas, "video") is null
                && Field(canvas, "videoBitmap") is null && canvas.StoryboardTextureBytes == 0 && retiredPixels.Bytes == 0,
                "Disabled native resources retained images, video, textures or reopened loading.");
            Assert(retiredVideo.Completion.Wait(TimeSpan.FromSeconds(5)), "Retired decoder failed to complete.");
            for (int i = 0; i < 8; i++)
            {
                Toggle(); Draw(); Toggle(); view.LoadDocument(Map(i % 2 == 0 ? imageA : imageB)); Draw();
            }
            Settle();
            Assert(Field(canvas, "storyboardPixels") is null && Field(canvas, "video") is null && canvas.StoryboardTextureBytes == 0,
                "Rapid reopen republished retired native resources.");
            view.LoadDocument(Map(imageB)); Toggle(); Settle();
            var textures = (IDictionary)Field(canvas, "storyboardTextures")!;
            Assert(textures.Count > 0 && textures.Keys.Cast<string>().All(p => p == imageB), "Reenabled texture belongs to a retired map.");
            foreach (string language in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(language);
                ClickLabel(L.Get("ui.view")); Capture(language + "-view-enabled.bmp");
                ClickLabel(L.Get("settings.enableVideoStoryboard"));
                Capture(language + "-view-disabled.bmp");
                view.KeyDown(27, false, false); view.KeyUp(27); Draw();
                view.OpenSettings(); Draw(); ClickLabel(L.Get("settings.testplay")); Capture(language + "-settings-minimum.bmp");
                ClickLabel(L.Get("settings.enableVideoStoryboard")); Capture(language + "-settings-checked.bmp");
                view.Wheel(view.SettingsBounds.Right - 80, view.SettingsBounds.Bottom - 130, -120, false); Draw();
                Capture(language + "-settings-scrolled.bmp");
                var r = view.SettingsBounds; Click(r.Right - 32, r.Y + 24);
                var toggle = view.CanvasMediaToggleBounds; view.PointerMove(toggle.X + 12, toggle.Y + 12, false, false);
                Thread.Sleep(180); Draw(); Capture(language + "-canvas-disabled.bmp");
                Click(toggle.X + 12, toggle.Y + 12); Settle(); Capture(language + "-canvas-enabled.bmp");
                view.OpenSettings(); Draw(); ClickLabel(L.Get("settings.testplay")); ClickLabel(L.Get("settings.enableVideoStoryboard"));
                view.ApplySettings(Path.Combine(directory, language + "-applied.json")); Draw();
                Assert(canvas.StoryboardTextureBytes == 0 && Field(canvas, "video") is null && Field(canvas, "storyboardPixels") is null,
                    "Settings Apply did not release actual native visual resources.");
                r = view.SettingsBounds; Click(r.Right - 32, r.Y + 24); Toggle(); Settle();
            }
            var process = Process.GetCurrentProcess(); process.Refresh();
            File.WriteAllText(Path.Combine(directory, "result.json"), System.Text.Json.JsonSerializer.Serialize(new {
                passed = true, visible = false, width = 980, height = 620, dpi = 96,
                decoderRetired = retiredVideo.Completion.IsCompleted, retiredDecodedImageBytes = retiredPixels.Bytes,
                finalStoryboardTextureBytes = canvas.StoryboardTextureBytes, privateBytes = process.PrivateMemorySize64,
                note = "Hidden real Direct2D canvas with cold disabled open, actual decoder/cache/texture release, rapid pending reopen, current-map restoration and bilingual native checkbox captures. No physical-present FPS claim." }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
            Console.WriteLine("PASS hidden native zero cold visual decoders, resource release, completion, pending reopen and EN/ZH three-entry captures at 980×620");

            MapDocument Map(string image)
            {
                var map = new MapDocument { SourcePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(image) + ".osu"), DurationMs = 10000 };
                map.Fruits.Add(new() { TimeMs = 9000, X = 256 });
                map.OriginalSections.Add(new() { Name = "Events", Lines = { $"Video,0,\"{video}\"", $"Sprite,Foreground,Centre,\"{image}\",320,240", " F,0,0,10000,1,1" } });
                return map;
            }
            void Draw() { canvas.Begin(); view.Render(canvas, 980, 620); canvas.End(); }
            void CheckSharedVideo()
            {
                var toggle = view.PreviewToggleBounds;
                if (!view.CatchPreviewVisible) Click(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2);
                Assert(view.CatchPreviewVisible, "Native shared-video check requires the actual Preview dock.");
                var decoder = (VideoPlayback)Field(canvas, "video")!;
                foreach (bool force in new[] { false, true, false })
                {
                    view.LibrarySettings.ForceBackgroundDim = force;
                    for (int frame = 0; frame < 12; frame++)
                    {
                        Draw();
                        Assert(ReferenceEquals(decoder, Field(canvas, "video")),
                            $"Preview retired the Mapping decoder: force={force}, frame={frame}.");
                        Assert(Field(canvas, "videoBitmap") is not null, "Mapping lost its prepared video frame.");
                    }
                    Console.WriteLine($"PASS native shared video force={force}: 12 frames retain one decoder");
                }
                view.LibrarySettings.ForceBackgroundDim = true;
                view.UpdateTransport(2000, 10000, true, false, false, null, null);
                var sought = Stopwatch.StartNew();
                bool displayed = false;
                do
                {
                    Draw();
                    decoder.WithFrame(2000, false, 1, frame => displayed = frame.Time <= 2000 && frame.EndTime > 2000
                        && (long)Field(canvas, "videoSerial")! == frame.Serial);
                    if (!displayed) Thread.Sleep(2);
                }
                while (!displayed && sought.Elapsed.TotalSeconds < 5);
                Assert(ReferenceEquals(decoder, Field(canvas, "video")) && displayed,
                    "Paused seek did not reach the requested video time with force-dim Preview.");
                view.LibrarySettings.TestplayStartupDelaySeconds = 0;
                view.StartTestplay(); Draw();
                Assert(view.IsTestplaying && Field(canvas, "video") is null,
                    "Force-dim testplay retained a decoder without any visible video viewport.");
                Assert(decoder.Completion.Wait(TimeSpan.FromSeconds(5)), "Suppressed testplay decoder did not finish retiring.");
                view.StopTestplay();
                view.LibrarySettings.ForceBackgroundDim = false;
                Settle();
                var resumed = Stopwatch.StartNew();
                do { Draw(); Thread.Sleep(2); }
                while (Field(canvas, "videoBitmap") is null && resumed.Elapsed.TotalSeconds < 5);
                Assert(Field(canvas, "videoBitmap") is not null, "Returning from testplay did not restore Mapping video.");
                toggle = view.PreviewToggleBounds;
                if (view.CatchPreviewVisible) Click(toggle.X + toggle.Width / 2, toggle.Y + toggle.Height / 2);
            }
            void Settle()
            {
                view.UpdateTransport(1000, 10000, true, false, false, null, null);
                var timer = Stopwatch.StartNew();
                do { Draw(); Thread.Sleep(2); } while ((view.LibraryOpening || Field(view, "storyboardLoad") is Task) && timer.Elapsed.TotalSeconds < 10);
                Assert(!view.LibraryOpening && Field(view, "storyboardLoad") is null, "Native media preparation timed out.");
            }
            void Toggle() => typeof(EditorView).GetMethod("ToggleVideoStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
            void Click(float x, float y) { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); Draw(); }
            void ClickLabel(string label)
            {
                var recording = new RecordingCanvas(); view.Render(recording, 980, 620);
                var text = recording.Texts.First(t => t.Value == label || t.Value == "✓ " + label); Click(text.X + 3, text.Y + 3);
            }
            void Capture(string name) { canvas.Begin(); view.Render(canvas, 980, 620); canvas.End(capture: () => { var p = canvas.CapturePixels(); Save(p.Width, p.Height, p.Pixels, Path.Combine(directory, name)); }); }
        }
        finally { L.SetLanguage(originalLanguage); Native.DestroyWindow(hwnd); GC.KeepAlive(procedure); }
    }
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Save(int width, int height, byte[] pixels, string path)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0x4D42); output.Write(54 + pixels.Length); output.Write(0); output.Write(54);
        output.Write(40); output.Write(width); output.Write(-height); output.Write((ushort)1); output.Write((ushort)32);
        output.Write(0); output.Write(pixels.Length); output.Write(0); output.Write(0); output.Write(0); output.Write(0); output.Write(pixels);
    }
}
#else
internal static class MediaToggleNativeTests { public static int Run(string videoPath) => throw new PlatformNotSupportedException(); }
#endif
