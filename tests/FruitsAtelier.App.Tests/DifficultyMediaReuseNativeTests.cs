#if WINDOWS
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Media;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class DifficultyMediaReuseNativeTests
{
    internal static int RunMap(string mapPath)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { CheckMap(mapPath); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Native difficulty media continuity failed", failure);
        return 0;
    }

    private static void CheckMap(string mapPath)
    {
        string directory = Path.GetFullPath("artifacts/diff-media-continuity-20261031/real-map"); Directory.CreateDirectory(directory);
        var map = OsuBeatmapReader.ReadFile(mapPath); var second = map.DeepClone(); second.Name += " (switch)";
        Native.WindowProc procedure = Native.DefWindowProc;
        string className = "FruitsAtelier.MediaContinuity." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var wc = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className };
        if (Native.RegisterClassEx(ref wc) == 0) throw new System.ComponentModel.Win32Exception();
        nint hwnd = Native.CreateWindowEx(0, className, "Media continuity", Native.WindowStyle, -32000, -32000, 1000, 680, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        var view = new EditorView(false);
        try
        {
            using var canvas = new D2DCanvas(hwnd, 980, 620, 96);
            view.LibrarySettings.Workspace = Path.Combine(directory, "workspace");
            view.LibrarySettings.FirstRunSetupCompleted = true; view.LibrarySettings.MappingCanvasBackgroundDim = 0;
            view.LoadProject(BeatmapProject.FromDocuments([map, second])); view.CloseLibrary();
            view.UpdateTransport(0, 180000, true, false, false, null, map.AudioPath);
            view.UpdateTransport(72324, 180000, true, false, false, null, map.AudioPath);
            (int Width, int Height, byte[] Pixels) captured = (0, 0, []);
            void Draw(bool capture = false) { canvas.Begin(); view.Render(canvas, 980, 620); canvas.End(capture: capture ? () => captured = canvas.CapturePixels() : null); }
            void Settle()
            {
                var timer = Stopwatch.StartNew();
                do { Draw(); Thread.Sleep(1); } while ((view.LibraryOpening || canvas.MediaNeedsRedraw) && timer.Elapsed.TotalSeconds < 20);
                Assert(!view.LibraryOpening, "Real-map media did not settle.");
            }
            Settle();
            Draw(capture: true); var before = captured; SavePixels(Path.Combine(directory, "before.bmp"), before.Width, before.Height, before.Pixels);
            var bounds = view.CanvasRenderBounds;
            string Hash(byte[] pixels, int width, int height)
            {
                int left = Math.Clamp((int)bounds.X + 16, 0, width), right = Math.Clamp((int)bounds.Right - 16, 0, width);
                int top = Math.Clamp((int)bounds.Y + 16, 0, height), bottom = Math.Clamp((int)bounds.Bottom - 16, 0, height);
                Assert(right > left && bottom > top, "Real-map comparison region is empty.");
                using var bytes = new MemoryStream();
                for (int y = top; y < bottom; y++) bytes.Write(pixels, (y * width + left) * 4, (right - left) * 4);
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
            }
            string expected = Hash(before.Pixels, before.Width, before.Height);
            int decodes = canvas.StoryboardDecodeCount, uploads = canvas.StoryboardUploadCount;
            view.CanReuseDifficultyAudio = _ => true;
            Assert(view.SwitchDifficulty(1), "Real-map switch was rejected.");
            typeof(EditorView).GetMethod("RefreshStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
            var task = (Task<StoryboardScene>)Field(view, "storyboardLoad")!;
            Assert(task.Wait(TimeSpan.FromSeconds(20)), "Real-map source parse did not finish.");
            var delayed = new TaskCompletionSource<StoryboardScene>();
            typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, delayed.Task);
            for (int i = 0; i < 6; i++)
            {
                Draw(capture: true); var frame = captured;
                if (i == 0) SavePixels(Path.Combine(directory, "pending.bmp"), frame.Width, frame.Height, frame.Pixels);
                Assert(Hash(frame.Pixels, frame.Width, frame.Height) == expected && canvas.StoryboardSprites > 0
                    && view.AudioReady && !view.AudioLoading && view.AudioDurationMs == 180000 && Math.Abs(view.PlayheadMs - 72324) < .01,
                    $"A pending real-map switch changed media pixels or flashed audio loading metadata: pixels={Hash(frame.Pixels, frame.Width, frame.Height) == expected}, sprites={canvas.StoryboardSprites}, audioReady={view.AudioReady}, loading={view.AudioLoading}, duration={view.AudioDurationMs}, playhead={view.PlayheadMs}, transition={Field(view, "storyboardTransition")}, presented={Field(view, "storyboardPresented")}.");
            }
            delayed.SetResult(task.Result); Settle();
            Draw(capture: true); var after = captured; SavePixels(Path.Combine(directory, "after.bmp"), after.Width, after.Height, after.Pixels);
            Assert(Hash(after.Pixels, after.Width, after.Height) == expected && canvas.StoryboardDecodeCount == decodes && canvas.StoryboardUploadCount == uploads,
                $"Identical real-map target changed presented media or decoded/uploaded unchanged images: pixels={Hash(after.Pixels, after.Width, after.Height) == expected}, decodes={decodes}->{canvas.StoryboardDecodeCount}, uploads={uploads}->{canvas.StoryboardUploadCount}.");
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, mapPath, positionMs = 72324, frozenFrames = 6, region = bounds,
                pixelHash = expected, decodes, uploads, sameAfterPixels = true, nativeAudioCoveredSeparately = true }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS real-map difficulty continuity at72.324s: six delayed frames and final target preserve exact media-region pixels, audio metadata and decode/upload counts");
        }
        finally { view.StopFileMonitoring(); view.CompleteLibraryCacheWrites(); Native.DestroyWindow(hwnd); GC.KeepAlive(procedure); }
    }

    private static void SavePixels(string path, int width, int height, byte[] pixels)
    {
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0x4D42); output.Write(54 + pixels.Length); output.Write(0); output.Write(54);
        output.Write(40); output.Write(width); output.Write(-height); output.Write((ushort)1); output.Write((ushort)32);
        output.Write(0); output.Write(pixels.Length); output.Write(2835); output.Write(2835); output.Write(0); output.Write(0); output.Write(pixels);
    }

    internal static int Run(string videoPath)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Check(videoPath); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Native difficulty media reuse failed", failure);
        return 0;
    }

    private static void Check(string videoPath)
    {
        using (var probe = new VideoPlayback(Path.GetFullPath(videoPath)))
        {
            if (!SpinWait.SpinUntil(() => probe.DurationMs > 0 || probe.Completion.IsCompleted, TimeSpan.FromSeconds(5)) || probe.DurationMs < 2000)
                throw new ArgumentException($"Native media reuse needs a video at least two seconds long; observed {probe.DurationMs:F1} ms. {probe.Error}", nameof(videoPath));
        }
        string directory = Path.GetFullPath("artifacts/diff-media-continuity-20261031/native"); Directory.CreateDirectory(directory);
        string common = Path.Combine(directory, "common.bmp"), background = Path.Combine(directory, "background.bmp");
        Save(common, 2048, 2048, 40); Save(background, 1024, 768, 80);
        Save(Path.Combine(directory, "first.bmp"), 64, 64, 120); Save(Path.Combine(directory, "second.bmp"), 64, 64, 200);
        string video = Path.Combine(directory, "video" + Path.GetExtension(videoPath)); File.Copy(videoPath, video, true);
        File.WriteAllText(Path.Combine(directory, "shared.osb"), "[Events]\nSprite,Foreground,Centre,\"common.bmp\",320,240\n F,0,0,180000,1\n S,0,0,,0.025\n");
        Native.WindowProc procedure = Native.DefWindowProc;
        string className = "FruitsAtelier.DifficultyMediaReuse." + Environment.ProcessId;
        nint instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className };
        if (Native.RegisterClassEx(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        nint hwnd = Native.CreateWindowEx(0, className, "Difficulty media reuse", Native.WindowStyle, -32000, -32000, 1000, 680, 0, 0, instance, 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        var view = new EditorView(false);
        try
        {
            using var canvas = new D2DCanvas(hwnd, 980, 620, 96);
            view.LibrarySettings.Workspace = Path.Combine(directory, "workspace");
            view.LibrarySettings.FirstRunSetupCompleted = true;
            view.LibrarySettings.EnableVideoStoryboard = true;
            view.LibrarySettings.MappingCanvasBackgroundDim = 0;
            string otherDirectory = Path.Combine(directory, "other"); Directory.CreateDirectory(otherDirectory);
            var maps = new[] { Map("first", 0, 100), Map("second", 0, 500), Map("offset", 400, 200), Map("none", null, 0),
                new MapDocument { SourcePath = Path.Combine(otherDirectory, "empty.osu"), DurationMs = 180000 } };
            view.LoadProject(BeatmapProject.FromDocuments(maps)); view.CloseLibrary(); Settle();
            var decoder = (VideoPlayback)Field(canvas, "video")!;
            var pixels = Field(canvas, "storyboardPixels");
            var texture = Texture(common); var bg = Background(); var videoBitmap = Field(canvas, "videoBitmap");
            int seeks = decoder.SeekCount; long serial = (long)Field(canvas, "videoSerial")!;
            Switch(1, delayPublication: true);
            Assert(ReferenceEquals(decoder, Field(canvas, "video")) && decoder.SeekCount == seeks
                && ReferenceEquals(videoBitmap, Field(canvas, "videoBitmap")) && (long)Field(canvas, "videoSerial")! == serial,
                "Identical video and offset reopened, sought or uploaded a replacement current frame.");
            Assert(ReferenceEquals(pixels, Field(canvas, "storyboardPixels")) && ReferenceEquals(texture, Texture(common)) && ReferenceEquals(bg, Background()),
                "Same-directory switch replaced common decoded pixels, GPU texture or resident background.");
            int warmedDecodes = canvas.StoryboardDecodeCount, warmedUploads = canvas.StoryboardUploadCount;
            var timings = new List<double>();
            for (int i = 0; i < 12; i++)
            {
                var timer = Stopwatch.StartNew(); Switch(i % 2, delayPublication: true); timings.Add(timer.Elapsed.TotalMilliseconds);
                Assert(ReferenceEquals(decoder, Field(canvas, "video")) && ReferenceEquals(texture, Texture(common)) && ReferenceEquals(bg, Background()), "Repeated switch replaced reusable media.");
                Assert(canvas.StoryboardDecodeCount == warmedDecodes && canvas.StoryboardUploadCount == warmedUploads, "Warm switch decoded or uploaded an unchanged image.");
            }
            Switch(2, delayPublication: true);
            Assert(ReferenceEquals(decoder, Field(canvas, "video")), "Changed offset reopened the identical video file.");
            bool offsetFrame = false;
            decoder.WithFrame(600, false, 1, frame => offsetFrame = frame.Time <= 600 && frame.EndTime > 600 && frame.Serial == (long)Field(canvas, "videoSerial")!);
            Assert(offsetFrame, "Offset change displayed a frame from the previous difficulty's video time.");
            Switch(3);
            Assert(Field(canvas, "video") is null && (Field(view, "storyboardDisplayFrame") as List<StoryboardSpriteState>)?.Count == 1,
                "No-video difficulty retained its decoder or previous difficulty sprites.");
            Assert(decoder.Completion.Wait(TimeSpan.FromSeconds(5)), "Unused decoder failed to retire.");
            Switch(4);
            Assert(canvas.StoryboardTextureBytes == 0 && Field(canvas, "beatmapBackground") is null && (Field(view, "storyboardScene") as StoryboardScene)?.SpriteCount == 0,
                "Different-directory resource-free difficulty retained the previous mapset's visuals.");

            Switch(0); decoder = (VideoPlayback)Field(canvas, "video")!;
            texture = Texture(common); bg = Background();
            Save(common, 2048, 2048, 220);
            File.SetLastWriteTimeUtc(common, DateTime.UtcNow.AddSeconds(2)); File.SetLastWriteTimeUtc(background, DateTime.UtcNow.AddSeconds(2));
            File.SetLastWriteTimeUtc(video, DateTime.UtcNow.AddSeconds(2));
            Switch(1);
            Assert(!ReferenceEquals(texture, Texture(common)) && !ReferenceEquals(bg, Background()) && !ReferenceEquals(decoder, Field(canvas, "video")),
                "Changed same-path image, background or video reused stale resources.");
            Assert(decoder.Completion.Wait(TimeSpan.FromSeconds(5)), "Changed-file decoder failed to retire.");
            decoder = (VideoPlayback)Field(canvas, "video")!;
            view.LoadProject(BeatmapProject.FromDocuments(maps)); view.CloseLibrary();
            Assert(view.SwitchDifficulty(1), "Project replacement switch failed."); Settle();
            Assert(!ReferenceEquals(decoder, Field(canvas, "video")), "A queued full retirement was overridden by difficulty reuse.");
            Assert(decoder.Completion.Wait(TimeSpan.FromSeconds(5)), "Replaced project's decoder failed to retire.");
            decoder = (VideoPlayback)Field(canvas, "video")!;
            view.ShowLibrary(); Draw();
            Assert(Field(canvas, "video") is null && canvas.StoryboardTextureBytes == 0 && Field(canvas, "beatmapBackground") is null,
                "Library retirement retained native media.");
            Assert(decoder.Completion.Wait(TimeSpan.FromSeconds(5)), "Library decoder failed to retire.");
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
            {
                passed = true, sharedImageSize = "2048x2048", sceneDurationMs = 180000, warmSwitches = timings.Count,
                warmSwitchMs = timings, warmedDecodes, warmedUploads, finalTextureBytes = canvas.StoryboardTextureBytes,
                note = "Real hidden Direct2D/FFmpeg resource identity and frame checks; no physical display timing claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS native difficulty media reuse: equal-offset decoder/frame/bitmap, changed-offset frames, bounded warm image decode/upload counts, file invalidation and full retirement");

            object? Texture(string path) => ((ITuple)((IDictionary)Field(canvas, "storyboardTextures")!)[path]!)[0];
            object? Background() => Field(canvas, "beatmapBackground") is ITuple tuple ? tuple[1] : null;
            void Draw() { canvas.Begin(); view.Render(canvas, 980, 620); canvas.End(); }
            void Settle()
            {
                view.UpdateTransport(1000, 180000, true, false, false, null, null);
                var timer = Stopwatch.StartNew();
                do { Draw(); Thread.Sleep(2); }
                while ((view.LibraryOpening || Field(view, "storyboardLoad") is Task
                    || (Field(view, "storyboardScene") as StoryboardScene)?.Videos.Count > 0
                        && (Field(canvas, "videoPresentationPending") is true || Field(canvas, "videoBitmap") is null)) && timer.Elapsed.TotalSeconds < 10);
                Assert(!view.LibraryOpening && Field(view, "storyboardLoad") is null, "Difficulty preparation did not settle.");
                Assert(Field(canvas, "videoPresentationPending") is false,
                    $"Video never produced a current-difficulty frame: playhead={view.PlayheadMs}, sceneVideo={(Field(view, "storyboardScene") as StoryboardScene)?.Videos.FirstOrDefault()}, requested={Field(view, "videoRequestedThisFrame")}, decoder={(Field(canvas, "video") as VideoPlayback)?.DiagnosticState}, error={canvas.MediaError}.");
            }
            void Switch(int index, bool delayPublication = false)
            {
                Assert(view.SwitchDifficulty(index), "Difficulty switch was rejected.");
                if (delayPublication)
                {
                    typeof(EditorView).GetMethod("RefreshStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
                    var load = (Task<StoryboardScene>)Field(view, "storyboardLoad")!;
                    Assert(load.Wait(TimeSpan.FromSeconds(5)), "Storyboard parse did not complete.");
                    var delayed = new TaskCompletionSource<StoryboardScene>();
                    typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, delayed.Task);
                    var before = Field(canvas, "video"); Draw();
                    Assert(ReferenceEquals(before, Field(canvas, "video")), "An in-editor preparation frame retired the reusable video.");
                    Assert((Field(view, "storyboardDisplayFrame") as List<StoryboardSpriteState>)?.Count > 0 && canvas.StoryboardSprites > 0
                        && Field(canvas, "videoBitmap") is not null && Field(canvas, "beatmapBackground") is not null,
                        "Preparation blanked the complete previous storyboard/video/background presentation.");
                    delayed.SetResult(load.Result);
                }
                Settle();
                var scene = (StoryboardScene)Field(view, "storyboardScene")!;
                var states = new List<StoryboardSpriteState>(); scene.Evaluate(1000, states);
                string expected = index == 1 ? "second.bmp" : "first.bmp";
                Assert(index == 4 ? states.Count == 0 : index == 3 ? states.Count == 1 : states.Any(s => s.Path.EndsWith(expected, StringComparison.Ordinal)), "Difficulty-specific sprite state did not rebuild.");
            }
            MapDocument Map(string name, int? offset, int x)
            {
                var map = new MapDocument { SourcePath = Path.Combine(directory, name + ".osu"), DurationMs = 180000 };
                map.Fruits.Add(new() { TimeMs = 9000, X = 256 });
                var events = new OsuSection { Name = "Events" };
                if (offset.HasValue)
                {
                    events.Lines.Add(name == "second" ? "0,0,\"BACKGROUND.BMP\",0,0" : "0,0,\"background.bmp\",0,0");
                    events.Lines.Add($"Video,{offset},\"{(name == "second" ? Path.GetFileName(video).ToUpperInvariant() : Path.GetFileName(video))}\"");
                    events.Lines.Add($"Sprite,Foreground,Centre,\"{(name == "second" ? "second" : "first")}.bmp\",{x},240");
                    events.Lines.Add(" F,0,0,180000,1");
                }
                map.OriginalSections.Add(events); return map;
            }
        }
        finally { view.StopFileMonitoring(); view.CompleteLibraryCacheWrites(); Native.DestroyWindow(hwnd); GC.KeepAlive(procedure); }
    }

    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Save(string path, int width, int height, byte blue)
    {
        using var output = new BinaryWriter(File.Create(path));
        int length = width * height * 4;
        output.Write((ushort)0x4D42); output.Write(54 + length); output.Write(0); output.Write(54);
        output.Write(40); output.Write(width); output.Write(-height); output.Write((ushort)1); output.Write((ushort)32);
        output.Write(0); output.Write(length); output.Write(2835); output.Write(2835); output.Write(0); output.Write(0);
        byte[] row = new byte[width * 4];
        for (int i = 0; i < row.Length; i += 4) { row[i] = blue; row[i + 3] = 255; }
        for (int y = 0; y < height; y++) output.Write(row);
    }
}
#else
internal static class DifficultyMediaReuseNativeTests
{
    internal static int Run(string videoPath) => throw new PlatformNotSupportedException("Native Direct2D check requires Windows.");
    internal static int RunMap(string mapPath) => throw new PlatformNotSupportedException("Native Direct2D check requires Windows.");
}
#endif
