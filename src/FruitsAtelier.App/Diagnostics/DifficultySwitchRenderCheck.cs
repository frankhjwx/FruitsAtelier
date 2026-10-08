using System.Diagnostics;
using System.Text.Json;
using FruitsAtelier.App.Audio;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;

namespace FruitsAtelier.App.Diagnostics;

internal static class DifficultySwitchRenderCheck
{
    internal static void Run(D2DCanvas canvas, EditorView view, string path, float dpi)
    {
        using var process = Process.GetCurrentProcess();
        var project = BeatmapArchive.OpenProject(path, "artifacts/difficulty-switch/maps");
        view.RequestPreloadHitsounds = null;
        view.RequestPrepareHitsound = _ => { };
        view.RequestHitsound = _ => { };
        view.RequestScheduleHitsound = (_, _) => { };
        view.RequestAuditionHitsound = _ => { };
        view.RequestTestplayMenuLoop = _ => { };
        view.RequestStopHitsounds = null;
        view.RequestRunTestplay = null;
        view.RequestDifficultyChanged = null;
        view.LibrarySettings.TestplayStartupDelaySeconds = 0;
        var audio = new AudioTransport(0);
        view.RequestSeek = audio.Seek;
        view.RequestPausePlayback = audio.Pause;
        view.RequestTogglePlayback = () => { if (audio.IsPlaying) audio.Pause(); else audio.Play(); };
        canvas.Resize((int)(1440 * dpi / 96), (int)(900 * dpi / 96), dpi);
        var results = new List<object>();
        void Poll()
        {
            var state = audio.State;
            view.UpdateTransport(state.PositionMs, state.DurationMs, state.CanPlay, state.IsPlaying,
                state.IsLoading, state.Error, state.FilePath, state.PositionTimestampMs, state.OutputBufferAheadMs);
        }
        double Draw()
        {
            var timer = Stopwatch.StartNew();
            canvas.Begin(); view.Render(canvas, 1440, 900); canvas.End();
            return timer.Elapsed.TotalMilliseconds;
        }
        try
        {
            for (int round = 0; round < 3; round++)
            {
                view.LoadProject(project); view.CloseLibrary(); Draw();
                for (int i = 0; i < project.Difficulties.Count * 2; i++)
                {
                    int index = i < project.Difficulties.Count ? i : project.Difficulties.Count * 2 - 1 - i;
                    view.StopTestplay();
                    var timer = Stopwatch.StartNew();
                    if (!view.SwitchDifficulty(index)) throw new InvalidOperationException("Native difficulty switch rejected.");
                    double editorMs = Draw();
                    double switchMs = timer.Elapsed.TotalMilliseconds;
                    timer.Restart();
                    audio.Dispose(); audio = new AudioTransport(0);
                    if (!audio.LoadAsync(view.Document.AudioPath!).GetAwaiter().GetResult())
                        throw new InvalidOperationException(audio.State.Error);
                    double audioMs = timer.Elapsed.TotalMilliseconds;
                    Poll();
                    double startMs = Math.Clamp((view.Conversion.Objects.FirstOrDefault()?.TimeMs ?? 0) + 5000,
                        0, Math.Max(0, audio.DurationMs - 1000));
                    audio.Seek(startMs); audio.WaitForCommandsAsync().GetAwaiter().GetResult(); Poll();
                    view.StartTestplay();
                    if (!view.IsTestplaying) throw new InvalidOperationException("Native testplay failed: " + view.StatusMessage);
                    view.KeyDown(9, false, false); view.KeyUp(9);
                    var frames = new List<double>();
                    for (int frame = 0; frame < 30; frame++)
                    {
                        Poll(); frames.Add(Draw()); Thread.Sleep(2);
                        if (!view.IsTestplaying) throw new InvalidOperationException("Native testplay ended during sampling.");
                    }
                    if (audio.PositionMs <= startMs)
                        throw new InvalidOperationException("Native audio device clock did not advance.");
                    view.StopTestplay(); Draw();
                    frames.Sort();
                    process.Refresh();
                    long privateBytes = process.PrivateMemorySize64;
                    results.Add(new { round, index, switchMs, editorMs, audioMs,
                        frameMedianMs = frames[15], frameP95Ms = frames[28], frameMaxMs = frames[^1],
                        privateBytes, heapBytes = GC.GetTotalMemory(false), gen2 = GC.CollectionCount(2) });
                    Console.WriteLine($"Native round {round + 1}, diff {i + 1}/{project.Difficulties.Count * 2}: switch={switchMs:F1} ms; audio={audioMs:F1} ms; frame P95={frames[28]:F1} ms; private={privateBytes / 1048576} MiB");
                }
                var captured = view.CaptureProject();
                if (view.IsDirty || !project.Difficulties.Zip(captured.Difficulties).All(pair =>
                    pair.First.Id == pair.Second.Id && pair.First.Document.ContentEquals(pair.Second.Document)))
                    throw new InvalidOperationException("Native switching or sorting changed project content.");
            }
            Directory.CreateDirectory("artifacts/difficulty-switch");
            File.WriteAllText("artifacts/difficulty-switch/native.json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { audio.Dispose(); }
    }
}
