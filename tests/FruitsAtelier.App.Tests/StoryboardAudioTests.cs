#if WINDOWS
using System.Diagnostics;
using System.Reflection;
using FruitsAtelier.App.Audio;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

internal static class StoryboardAudioTests
{
    public static int Run(Action<LibrarySettings>? configure = null, bool toggleDuringPlayback = false)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { CheckWithoutMusic(configure); foreach (double speed in new[] { .5, 1, 1.5 }) CheckPlatformPcm(speed, configure, toggleDuringPlayback); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Storyboard audio integration failed", failure);
        return 0;
    }

    private static void CheckWithoutMusic(Action<LibrarySettings>? configure)
    {
        string directory = Path.GetFullPath("artifacts/tests/video-storyboard/audio-without-music"); Directory.CreateDirectory(directory);
        string sample = Path.Combine(directory, "sample.wav"); File.WriteAllBytes(sample, HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        var map = new MapDocument { SourcePath = Path.Combine(directory, "map.osu"), DurationMs = 20000 };
        map.Fruits.AddRange([new() { TimeMs = 1000, X = 256 }, new() { TimeMs = 10000, X = 256 }]);
        map.OriginalSections.Add(new() { Name = "Events", Lines = { "Sample,500,Foreground,\"sample.wav\",25" } });
        var clock = new Clock(); var view = new EditorView(false, timeProvider: clock); var canvas = new RecordingCanvas();
        configure?.Invoke(view.LibrarySettings);
        view.LibrarySettings.TestplayStartupDelaySeconds = 0; view.LoadDocument(map); view.CloseLibrary();
        using var mixer = new HitsoundPlayer(); int emitted = 0;
        view.RequestPrepareHitsound = mixer.Prepare; view.RequestStopHitsounds = mixer.Stop;
        view.RequestHitsound = sound => { if (sound.Name == "storyboard") { emitted++; mixer.PlayImmediate(sound); } };
        var timer = Stopwatch.StartNew();
        do { view.Render(canvas, 1440, 900); Thread.Sleep(5); }
        while (view.LibraryOpening && timer.Elapsed.TotalSeconds < 5);
        foreach (int expected in new[] { 1, 2 })
        {
            view.UpdateTransport(0, 20000, true, false, false, null, null);
            view.UpdateTransport(0, 20000, false, false, false, null, null);
            view.StartTestplay(); view.Render(canvas, 1440, 900);
            double start = view.PlayheadMs; clock.Advance((long)(500 - start)); view.Render(canvas, 1440, 900);
            if (emitted != expected) throw new Exception("Actual testplay without music omitted or duplicated the storyboard Sample.");
            var pcm = new float[4096]; mixer.Read(pcm, 0, 64);
            if (!pcm.Any(p => Math.Abs(p) > .01)) throw new Exception("Storyboard Sample without music had no decoded PCM.");
            view.KeyDown(80, true, false); view.KeyUp(80); clock.Advance(100); view.Render(canvas, 1440, 900);
            mixer.Read(pcm, 0, pcm.Length);
            if (pcm.Any(p => p != 0)) throw new Exception("Paused no-music testplay retained a storyboard voice.");
            view.StopTestplay(); view.Render(canvas, 1440, 900);
        }
        view.UpdateTransport(500, 20000, true, false, false, null, null);
        view.UpdateTransport(500, 20000, false, false, false, null, null);
        view.StartTestplay(); view.Render(canvas, 1440, 900);
        if (emitted != 3) throw new Exception("A storyboard Sample exactly at testplay start was omitted.");
        view.StopTestplay(); view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
        Console.WriteLine("PASS actual no-music testplay storyboard PCM, pause, replay and exact-start Sample");
    }

    private static void CheckPlatformPcm(double speed, Action<LibrarySettings>? configure, bool toggleDuringPlayback)
    {
        string directory = Path.GetFullPath("artifacts/tests/video-storyboard/audio-" + speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        string wave = Path.Combine(directory, "silence.wav"), soundPath = Path.Combine(directory, "sample.wav"), mapPath = Path.Combine(directory, "sample.osu");
        using (var writer = new WaveFileWriter(wave, new WaveFormat(44100, 16, 1))) writer.Write(new byte[44100 * 2 * 3]);
        File.WriteAllBytes(soundPath, HitsoundSamples.CreateWave(CatchObjectKind.Fruit));
        File.WriteAllText(mapPath, "osu file format v14\n[General]\nAudioFilename:silence.wav\nMode:2\n[Metadata]\nTitle:Storyboard PCM fixture\nArtist:Fixture\nCreator:Fixture\nVersion:Fixture\n[Difficulty]\nHPDrainRate:5\nCircleSize:5\nOverallDifficulty:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[Events]\nSample,500,Foreground,\"sample.wav\",25\nSample,500,Fail,\"sample.wav\",100\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n");
        var document = OsuBeatmapReader.ReadFile(mapPath);
        var view = new EditorView(false); var canvas = new RecordingCanvas();
        configure?.Invoke(view.LibrarySettings);
        view.LibrarySettings.Workspace = Path.Combine(directory, "workspace");
        view.LibrarySettings.BackgroundDim = view.LibrarySettings.MappingCanvasBackgroundDim = 0;
        view.LoadDocument(document); view.CloseLibrary();
        var loading = Stopwatch.StartNew();
        do { view.Render(canvas, 1440, 900); Thread.Sleep(5); }
        while ((typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view) is Task) && loading.Elapsed.TotalSeconds < 5);
        using var mixer = new HitsoundPlayer();
        var captures = new List<MutedWasapi>();
        using var audio = new AudioTransport(1, () => { var player = new MutedWasapi(); captures.Add(player); return player; }) { Hitsounds = mixer, SongVolume = 0 };
        int scheduled = 0;
        view.RequestPrepareHitsound = mixer.Prepare;
        view.RequestScheduleHitsound = (sound, time) => { scheduled++; mixer.Schedule(sound, time); };
        view.RequestStopHitsounds = mixer.Stop;
        if (!audio.LoadAsync(wave).GetAwaiter().GetResult()) throw new Exception(audio.Error);
        audio.SetPlaybackSpeed(speed); audio.WaitForCommandsAsync().GetAwaiter().GetResult(); view.SetPlaybackSpeed(speed);
        view.UpdateTransport(0, 3000, true, false, false, null, wave);
        audio.Play(); audio.WaitForCommandsAsync().GetAwaiter().GetResult();
        view.RequestTogglePlayback = () => throw new Exception("Media preference requested a music pause.");
        bool toggled = false;
        var timer = Stopwatch.StartNew();
        while (audio.PositionMs < 900 && timer.Elapsed.TotalSeconds < 5)
        {
            var state = audio.State;
            view.UpdateTransport(state.PositionMs, state.DurationMs, state.CanPlay, state.IsPlaying, state.IsLoading, state.Error, state.FilePath, state.PositionTimestampMs, state.OutputBufferAheadMs);
            view.Render(canvas, 1440, 900); Thread.Sleep(2);
            if (toggleDuringPlayback && !toggled && state.PositionMs >= 250)
            {
                typeof(EditorView).GetMethod("ToggleVideoStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
                view.Render(canvas, 1440, 900);
                if (view.LibraryOpening || !audio.IsPlaying || !view.AudioPlaying) throw new Exception("Media toggle interrupted the running WASAPI transport.");
                toggled = true;
            }
        }
        if (audio.PositionMs < 900 || audio.Error is not null) throw new Exception(audio.Error ?? "The real WASAPI clock did not advance.");
        audio.Pause(); audio.WaitForCommandsAsync().GetAwaiter().GetResult();
        double paused = audio.PositionMs; Thread.Sleep(70);
        if (Math.Abs(audio.PositionMs - paused) > 3) throw new Exception("Paused device clock kept advancing.");
        if (scheduled != 1) throw new Exception($"Expected one Foreground sample and no Fail sample; scheduled {scheduled}.");
        if (toggleDuringPlayback && !toggled) throw new Exception("Music clock did not reach the media-toggle boundary.");
        var bytes = captures.SelectMany(c => c.Capture.ToArray()).ToArray();
        var pcm = new short[bytes.Length / 2]; Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 2);
        int first = Array.FindIndex(pcm, x => x != 0);
        double firstMs = first * 1000d / 44100;
        if (first < 0 || Math.Abs(firstMs - 500 / speed) > 10) throw new Exception($"Storyboard sample PCM begins at {firstMs:F3}ms, expected {500 / speed:F3}ms.");
        var reference = HitsoundSamples.Create(CatchObjectKind.Fruit);
        double expectedPeak = reference.Max(x => Math.Abs(x)) * .25 * short.MaxValue;
        double actualPeak = pcm.Max(x => Math.Abs((int)x));
        if (Math.Abs(actualPeak - expectedPeak) > 4) throw new Exception($"Sample volume changed: peak {actualPeak}, expected {expectedPeak}.");
        File.WriteAllBytes(Path.Combine(directory, "pre-mute-pcm.s16le"), bytes);
        File.WriteAllText(Path.Combine(directory, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { speed, scheduled, firstMs, actualPeak, expectedPeak, pausedPositionMs = paused,
            note = "Real WASAPI device clock and AudioTransport mixing. PCM captured before the final zeroing wrapper; every byte submitted to the device is silent." }));
        view.StopFileMonitoring(); view.CompleteLibraryCacheWrites();
        Console.WriteLine($"PASS storyboard Sample→Editor→HitsoundPlayer→AudioTransport→WASAPI, speed {speed}, mid-playback toggle={toggled}: PCM onset {firstMs:F3}ms, peak {actualPeak}, final device PCM silent");
    }

    private sealed class MutedWasapi : IWavePlayer, IWavePosition
    {
        private readonly WasapiOut output = new(AudioClientShareMode.Shared, true, 10);
        internal MemoryStream Capture { get; } = new();
        public WaveFormat OutputWaveFormat => output.OutputWaveFormat;
        public PlaybackState PlaybackState => output.PlaybackState;
        public float Volume { get => output.Volume; set => output.Volume = value; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add => output.PlaybackStopped += value; remove => output.PlaybackStopped -= value; }
        public void Init(IWaveProvider provider) => output.Init(new MutedCapture(provider, Capture));
        public void Play() => output.Play(); public void Pause() => output.Pause(); public void Stop() => output.Stop();
        public long GetPosition() => output.GetPosition(); public void Dispose() => output.Dispose();
    }

    private sealed class MutedCapture(IWaveProvider source, MemoryStream capture) : IWaveProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;
        public int Read(byte[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count); lock (capture) capture.Write(buffer, offset, read);
            Array.Clear(buffer, offset, read); return read;
        }
    }
    private sealed class Clock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(long amount) => milliseconds += amount;
    }
}
#else
internal static class StoryboardAudioTests
{
    public static int Run(Action<FruitsAtelier.Core.LibrarySettings>? configure = null, bool toggleDuringPlayback = false) => throw new PlatformNotSupportedException("Storyboard platform PCM checks require Windows.");
}
#endif
