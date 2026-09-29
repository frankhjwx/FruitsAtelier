using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckDifficultyAudioReset()
    {
        string path = Path.Combine(Artifacts, "audio-switch-check.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2)))
            writer.Write(new byte[44100 * 4 * 4], 0, 44100 * 4 * 4);
        var previousAudio = audio;
        var previousDisplayedAudio = displayedAudioState;
        var project = view.CaptureProject();
        var difficultyChanged = view.RequestDifficultyChanged;
        var stopHitsounds = view.RequestStopHitsounds;
        var seek = view.RequestSeek;
        try
        {
            var map = new MapDocument { AudioPath = path, DurationMs = 4000, IsDemo = false };
            view.LoadProject(BeatmapProject.FromDocuments([map, map.DeepClone()]));
            view.RequestDifficultyChanged = ResetAudio;
            foreach (bool playing in new[] { false, true })
            {
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
                if (!view.SwitchDifficulty(1 - view.ActiveDifficultyIndex) || !nestedMessages)
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
            view.RequestStopHitsounds = stopHitsounds;
            view.RequestSeek = seek;
            view.LoadProject(project);
        }
    }

    private sealed class AudioResetCheckOutput : IWavePlayer, IWavePosition
    {
        public WaveFormat OutputWaveFormat { get; private set; } = new(44100, 16, 2);
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider source) => OutputWaveFormat = source.WaveFormat;
        public long GetPosition() => 0;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; PlaybackStopped?.Invoke(this, new()); }
        public void Dispose() { }
    }
}
