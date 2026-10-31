using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private static void CheckAudioShutdown()
    {
        string directory = Path.Combine(Artifacts, "tests", "audio-shutdown-native");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "shutdown.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2)))
            writer.Write(new byte[44100 * 4], 0, 44100 * 4);
        foreach (bool playing in new[] { false, true })
        {
            using var window = new EditorWindow();
            window.audio.Dispose();
            var music = new AudioResetCheckOutput();
            var setup = new AudioResetCheckOutput();
            window.audio = new AudioTransport(0, () => music);
            window.setupAudio = new AudioTransport(0, () => setup);
            foreach (var transport in new[] { window.audio, window.setupAudio })
            {
                if (!transport.LoadAsync(path).GetAwaiter().GetResult()) throw new InvalidOperationException(transport.Error);
                if (playing) transport.Play();
                transport.WaitForCommandsAsync().GetAwaiter().GetResult();
            }
            var cacheWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            typeof(Editor.EditorView).GetField("libraryPreviewSave",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(window.view, cacheWrite.Task);
            var observer = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(music.Released.Task, setup.Released.Task).WaitAsync(TimeSpan.FromSeconds(2));
                    return music.PlaybackState == PlaybackState.Stopped && setup.PlaybackState == PlaybackState.Stopped;
                }
                catch (TimeoutException) { return false; }
                finally { cacheWrite.TrySetResult(true); }
            });
            window.Dispose();
            if (!observer.GetAwaiter().GetResult())
                throw new InvalidOperationException("Window shutdown waited for cache writes before releasing audio.");
        }
        AppLog.Write("Audio shutdown check passed: paused and playing outputs released before blocked cache writes.");
    }

    private void CheckDifficultyAudioReset()
    {
        string directory = Path.Combine(Artifacts, "tests", "audio-switch-native");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "audio-switch-check.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2)))
            writer.Write(new byte[44100 * 4 * 4], 0, 44100 * 4 * 4);
        var previousAudio = audio;
        var previousDisplayedAudio = displayedAudioState;
        var project = view.CaptureProject();
        var difficultyChanged = view.RequestDifficultyChanged;
        var canReuseAudio = view.CanReuseDifficultyAudio;
        var stopHitsounds = view.RequestStopHitsounds;
        var seek = view.RequestSeek;
        try
        {
            void FinishSwitch(int previousIndex)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                do
                {
                    Native.GetClientRect(hwnd, out var client);
                    bool previousPainting = painting;
                    painting = true;
                    try { canvas!.Begin(); view.Render(canvas, client.Right * 96 / uiDpi, client.Bottom * 96 / uiDpi); canvas.End(); }
                    finally { canvas?.AbortDraw(); painting = previousPainting; }
                    Thread.Sleep(1);
                } while (view.ActiveDifficultyIndex == previousIndex && DateTime.UtcNow < deadline);
                if (view.ActiveDifficultyIndex == previousIndex) throw new InvalidOperationException("Difficulty pause barrier did not complete.");
            }
            var map = new MapDocument { AudioPath = path, DurationMs = 4000, IsDemo = false };
            view.LoadProject(BeatmapProject.FromDocuments([map, map.DeepClone()]));
            foreach (bool playing in new[] { false, true })
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                do
                {
                    Native.GetClientRect(hwnd, out var client);
                    canvas!.Begin(); view.Render(canvas, client.Right * 96 / uiDpi, client.Bottom * 96 / uiDpi); canvas.End();
                    Thread.Sleep(1);
                } while (view.LibraryOpening && DateTime.UtcNow < deadline);
                int outputCreations = 0;
                audio = new AudioTransport(0, () => { Interlocked.Increment(ref outputCreations); return new AudioResetCheckOutput(); });
                if (!audio.LoadAsync(path).GetAwaiter().GetResult()) throw new InvalidOperationException(audio.Error);
                PollAudio(); audio.Seek(1250);
                if (playing) audio.Play();
                audio.WaitForCommandsAsync().GetAwaiter().GetResult(); PollAudio();
                var retained = audio; long generation = audio.LoadGeneration;
                int retainedOutputCreations = outputCreations;
                double duration = view.AudioDurationMs;
                view.RequestStopHitsounds = () =>
                {
                    if (!view.AudioReady || view.AudioLoading || view.AudioDurationMs != duration)
                        throw new InvalidOperationException("Unchanged difficulty audio exposed transient loading metadata during reentrant input.");
                };
                int previousIndex = view.ActiveDifficultyIndex;
                if (!view.SwitchDifficulty(1 - previousIndex)) throw new InvalidOperationException("Audio reuse switch failed.");
                FinishSwitch(previousIndex);
                audio.WaitForCommandsAsync().GetAwaiter().GetResult(); PollAudio();
                if (!ReferenceEquals(retained, audio) || audio.LoadGeneration != generation || view.AudioLoading || !view.AudioReady
                    || view.AudioDurationMs != duration || view.AudioPlaying || audio.IsPlaying || Math.Abs(view.PlayheadMs - 1250) > 20)
                    throw new InvalidOperationException("Identical difficulty audio reopened or lost its paused transport metadata.");
                if (!playing && outputCreations != retainedOutputCreations)
                    throw new InvalidOperationException("Paused unchanged difficulty audio rebuilt its native output.");
                var stamp = File.GetLastWriteTimeUtc(path);
                File.SetLastWriteTimeUtc(path, stamp.AddSeconds(2));
                if (audio.CanReuse(path)) throw new InvalidOperationException("Changed audio version was accepted for decoder reuse.");
                File.SetLastWriteTimeUtc(path, stamp);
                view.RequestStopHitsounds = stopHitsounds; audio.Dispose();
            }
            AppLog.Write("Difficulty audio continuity check passed: actual host callback retains decoder/load generation/readiness/duration, pauses both states and rejects changed versions.");
            view.CanReuseDifficultyAudio = _ => false;
            view.RequestDifficultyChanged = ResetAudio;
            foreach (bool playing in new[] { false, true })
            {
                var openingDeadline = DateTime.UtcNow.AddSeconds(10);
                while (view.LibraryOpening && DateTime.UtcNow < openingDeadline)
                {
                    Native.GetClientRect(hwnd, out var client);
                    canvas!.Begin(); view.Render(canvas, client.Right * 96 / uiDpi, client.Bottom * 96 / uiDpi); canvas.End();
                    Thread.Sleep(1);
                }
                if (view.LibraryOpening) throw new InvalidOperationException("Audio switch fixture media did not finish preparing.");
                audio = new AudioTransport(0, () => new AudioResetCheckOutput());
                if (!audio.LoadAsync(path).GetAwaiter().GetResult()) throw new InvalidOperationException(audio.Error);
                PollAudio();
                audio.Seek(1250);
                if (playing) audio.Play();
                audio.WaitForCommandsAsync().GetAwaiter().GetResult();
                PollAudio();
                double position = view.PlayheadMs;
                if (Math.Abs(position - 1250) > 1) throw new InvalidOperationException("Audio switch fixture did not seek.");
                bool nestedMessages = false;
                double requestedSeek = -1;
                view.RequestSeek = time => requestedSeek = time;
                view.RequestStopHitsounds = () =>
                {
                    if (nestedMessages) return;
                    nestedMessages = true;
                    // Reproduce messages pumped while ResetAudio still owns the retiring transport.
                    WndProc(hwnd, 0x0113, 1, 0);
                    WndProc(hwnd, 0x000F, 0, 0);
                };
                int previousIndex = view.ActiveDifficultyIndex;
                if (!view.SwitchDifficulty(1 - previousIndex)) throw new InvalidOperationException("Audio reset switch failed.");
                FinishSwitch(previousIndex);
                if (!nestedMessages)
                    throw new InvalidOperationException("Audio reset did not exercise nested window messages.");
                view.UpdateTransport(0, 4000, true, false, false, null, path);
                if (Math.Abs(view.PlayheadMs - position) > .01 || Math.Abs(requestedSeek - position) > .01 || view.IsDirty)
                    throw new InvalidOperationException("Retiring audio reset the difficulty position during nested messages.");
                view.RequestStopHitsounds = stopHitsounds;
                audio.Dispose();
            }
            AppLog.Write("Difficulty audio reset check passed: nested timer/paint preserve paused and playing positions without a device.");
        }
        finally
        {
            audio.Dispose(); audio = previousAudio;
            displayedAudioState = previousDisplayedAudio;
            view.RequestDifficultyChanged = difficultyChanged;
            view.CanReuseDifficultyAudio = canReuseAudio;
            view.RequestStopHitsounds = stopHitsounds;
            view.RequestSeek = seek;
            if (project.Difficulties.Count > 0 && !string.IsNullOrWhiteSpace(project.Name)) view.LoadProject(project);
            else view.NewProject();
        }
    }

    private sealed class AudioResetCheckOutput : IWavePlayer, IWavePosition
    {
        public TaskCompletionSource<bool> Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WaveFormat OutputWaveFormat { get; private set; } = new(44100, 16, 2);
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider source) => OutputWaveFormat = source.WaveFormat;
        public long GetPosition() => 0;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new()); }
        public void Dispose() => Released.TrySetResult(true);
    }
}
