using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FruitsAtelier.App.Audio;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckStoryboardSeek(string path)
    {
        string directory = Path.Combine(Artifacts, "tests", "storyboard-seek-native");
        Directory.CreateDirectory(directory);
        using var progress = new StreamWriter(Path.Combine(directory, "progress.txt")) { AutoFlush = true };
        void Log(string message) { progress.WriteLine(message); Console.WriteLine(message); }
        view.InitializeLibrary(false, new Core.LibrarySettings { Workspace = Path.Combine(directory, "workspace"), FirstRunSetupCompleted = true, EnableVideoStoryboard = true, BackgroundDim = 0, MappingCanvasBackgroundDim = 0, ForceBackgroundDim = false });
        view.RequestViewPreference = null;
        var document = Core.OsuBeatmapReader.ReadFile(path);
        view.LibrarySettings.EnableVideoStoryboard = true;
        view.LoadProject(Core.BeatmapProject.FromDocuments([document]));
        view.CloseLibrary();
        audio.Dispose(); audio = new AudioTransport(0) { Hitsounds = hitsounds };
        var loading = audio.LoadAsync(view.Document.AudioPath!);
        Native.ShowWindow(hwnd, 5);
        var results = new List<object>();
        double maxFrame = 0, maxInput = 0;
        void Draw()
        {
            var clock = Stopwatch.StartNew();
            Invalidate(); WndProc(hwnd, 0x000F, 0, 0);
            maxFrame = Math.Max(maxFrame, clock.Elapsed.TotalMilliseconds);
            for (int i = 0; i < 32 && Native.PeekMessage(out var message, 0, 0, 0, 1); i++)
            { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
        }
        void Input(uint message, double time)
        {
            var bar = (Rect)typeof(Editor.EditorView).GetField("overview", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            int x = (int)((bar.X + bar.Width * time / view.AudioDurationMs) * uiDpi / 96);
            int y = (int)((bar.Y + bar.Height / 2) * uiDpi / 96);
            var clock = Stopwatch.StartNew();
            WndProc(hwnd, message, message == 0x0202 ? 0u : 1u, (nint)((y << 16) | (x & 0xffff)));
            maxInput = Math.Max(maxInput, clock.Elapsed.TotalMilliseconds);
        }
        Log("Opening");
        var setup = Stopwatch.StartNew();
        do { Draw(); Thread.Sleep(2); }
        while ((!loading.IsCompleted || view.LibraryOpening || view.StarRatingsRefreshing || canvas!.MediaNeedsRedraw) && setup.Elapsed.TotalSeconds < 30);
        if (!loading.GetAwaiter().GetResult() || view.LibraryOpening) throw new InvalidOperationException("Storyboard seek setup failed.");
        if (view.TimingAlignmentPromptVisible) { view.AnswerTimingAlignment(false); Draw(); }
        Log($"Source: {view.Document.SourcePath}");
        double[] targets = [38555, 260000, 79200, 320000, 29000, 180000, 110000, 38555];
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                double target = Math.Min(targets[i], view.AudioDurationMs - 1000);
                maxFrame = maxInput = 0;
                Log($"Seek round={round} target={target}");
                var elapsed = Stopwatch.StartNew();
                Input(0x0201, target);
                if (round == 1)
                {
                    for (int move = 0; move < 12; move++)
                    { Input(0x0200, targets[(i + move) % targets.Length]); Draw(); }
                    Input(0x0200, target);
                }
                Input(0x0202, target);
                if (view.PlayheadMs == 0) throw new InvalidOperationException($"Progress-bar input did not seek: {view.StatusMessage}");
                do { Draw(); Thread.Sleep(2); } while (elapsed.Elapsed.TotalMilliseconds < 700 || canvas!.MediaNeedsRedraw && elapsed.Elapsed.TotalSeconds < 5);
                var targetFrame = (List<Core.StoryboardSpriteState>)typeof(Editor.EditorView).GetField("storyboardFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
                var displayFrame = (List<Core.StoryboardSpriteState>)typeof(Editor.EditorView).GetField("storyboardDisplayFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
                if (!targetFrame.SequenceEqual(displayFrame)) throw new InvalidOperationException("Latest seek did not publish its complete storyboard frame.");
                using var process = Process.GetCurrentProcess();
                var row = new { round, target, elapsedMs = elapsed.Elapsed.TotalMilliseconds, maxFrame, maxInput,
                    playhead = view.PlayheadMs, sceneSprites = (typeof(Editor.EditorView).GetField("storyboardScene", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view) as Core.StoryboardScene)?.SpriteCount,
                    status = view.StatusMessage, sb = view.LibrarySettings.EnableVideoStoryboard,
                    decodes = canvas!.StoryboardDecodeCount, uploads = canvas.StoryboardUploadCount,
                    textureBytes = canvas.StoryboardTextureBytes, privateBytes = process.PrivateMemorySize64,
                    managedBytes = GC.GetTotalMemory(false), gen2 = GC.CollectionCount(2), pending = canvas.MediaNeedsRedraw };
                results.Add(row); Log(JsonSerializer.Serialize(row));
                File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        if (canvas!.StoryboardUploadCount == 0) throw new InvalidOperationException("Fixture did not render any storyboard images.");
        Log("Completed");
    }
}
