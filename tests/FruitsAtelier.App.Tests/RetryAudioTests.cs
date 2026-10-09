#if WINDOWS
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave;

internal static class RetryAudioTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int ms) => ticks += ms;
    }
    private sealed class Output : IWavePlayer
    {
        public IWaveProvider? Source;
        public WaveFormat OutputWaveFormat => Source?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; } = 1;
        public event EventHandler<StoppedEventArgs>? PlaybackStopped { add { } remove { } }
        public void Init(IWaveProvider source) => Source = source;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Dispose() { }
    }

    public static void Run()
    {
        var output = new Output();
        using var mixer = new HitsoundPlayer(createAuditionOutput: () => output);
        var clock = new Clock();
        var ui = new Ui(false, clock);
        var map = new MapDocument { IsDemo = false, DurationMs = 20000 };
        map.Fruits.Add(new() { TimeMs = 15000, X = 256 });
        ui.LoadDocument(map);
        ui.View.RequestPrepareHitsound = mixer.Prepare;
        ui.View.RequestAuditionHitsound = mixer.PlayAudition;
        ui.View.RequestTestplayMenuLoop = mixer.SetMenuLoop;
        ui.View.RequestStopHitsounds = mixer.Stop;
        ui.View.StartTestplay();
        ui.Key(192);
        CheckPcm(150, "Held retry has no audible pause-loop PCM in its first 150 ms.");
        clock.Advance(150); ui.Paint();
        CheckPcm(150, "Held retry stopped its loop before the 300 ms deadline.");
        clock.Advance(150); ui.Paint();
        CheckPcm(1000, "Restart cleared the retry click before it could play.");
        ui.View.KeyUp(192); ui.View.StopTestplay();

        void CheckPcm(int ms, string message)
        {
            var bytes = new byte[44100 * 4 * ms / 1000];
            if (output.Source is null) throw new Exception(message);
            output.Source.Read(bytes, 0, bytes.Length);
            if (!bytes.Any(b => b != 0)) throw new Exception(message);
        }
    }
}
#endif
