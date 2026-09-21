using FruitsAtelier.App.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

internal static class PausePositionTests
{
    public static async Task Run(string file)
    {
        await PendingPause(file);
        var players = new List<BufferedPlayer>();
        using var audio = new AudioTransport(1, () => { var player = new BufferedPlayer(); players.Add(player); return player; });
        if (!await audio.LoadAsync(file)) throw new Exception(audio.Error);
        foreach (int delay in new[] { 0, 100, 400 })
        {
            audio.Play(); await audio.WaitForCommandsAsync();
            players[^1].Advance(200);
            await audio.WaitForCommandsAsync();
            double before = audio.PositionMs;
            audio.Pause(); await audio.WaitForCommandsAsync();
            double paused = audio.PositionMs;
            if (Math.Abs(paused - before) > 1000.0 / 44100) throw new Exception("Pause moved the displayed playhead past consumed audio");
            await Task.Delay(delay);
            await audio.WaitForCommandsAsync();
            if (audio.PositionMs != paused) throw new Exception("Paused playhead continued advancing");
            audio.Play(); await audio.WaitForCommandsAsync();
            if (Math.Abs(audio.PositionMs - paused) > 1000.0 / 44100) throw new Exception("Resume jumped the playhead");
            using var reader = new WaveFileReader(file);
            reader.CurrentTime = TimeSpan.FromMilliseconds(paused);
            var expected = new byte[players[^1].FirstBuffer.Length];
            new SampleToWaveProvider16(reader.ToSampleProvider()).Read(expected, 0, expected.Length);
            if (!players[^1].FirstBuffer.SequenceEqual(expected)) throw new Exception("Resume used buffered read-ahead audio instead of the paused frame");
        }
    }

    private static async Task PendingPause(string file)
    {
        foreach (string scenario in new[] { "pause", "seek-pause", "pause-seek", "pause-play", "pause-pause" })
        {
            var players = new List<BufferedPlayer>();
            using var audio = new AudioTransport(1, () => { var player = new BufferedPlayer(); players.Add(player); return player; });
            if (!await audio.LoadAsync(file)) throw new Exception(audio.Error);
            audio.Play(); await audio.WaitForCommandsAsync();
            var active = players[^1];
            active.Advance(200); await audio.WaitForCommandsAsync();
            double expected = audio.PositionMs;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            active.BeforeGetPosition = () => { entered.TrySetResult(true); release.Wait(); };
            Task inFlight = audio.WaitForCommandsAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (scenario == "seek-pause") { audio.Seek(1000); expected = 1000; }
                audio.Pause();
                if (scenario == "pause-seek") { audio.Seek(1200); expected = 1200; }
                if (scenario == "pause-play") audio.Play();
                if (scenario == "pause-pause") audio.Pause();
                active.Advance(50);
                if (audio.PositionMs != expected) throw new Exception($"{scenario}: pending pause moved the playhead.");
            }
            finally { release.Set(); }
            await inFlight;
            await audio.WaitForCommandsAsync();
            if (Math.Abs(audio.PositionMs - expected) > 1000d / 44100)
                throw new Exception($"{scenario}: a stale clock replaced the requested position ({audio.PositionMs} vs {expected}).");
            if (audio.IsPlaying != (scenario == "pause-play")) throw new Exception($"{scenario}: latest play intent was lost.");
            audio.Play(); await audio.WaitForCommandsAsync();
            using var reader = new WaveFileReader(file);
            reader.CurrentTime = TimeSpan.FromMilliseconds(expected);
            var pcm = new byte[players[^1].FirstBuffer.Length];
            new SampleToWaveProvider16(reader.ToSampleProvider()).Read(pcm, 0, pcm.Length);
            if (!players[^1].FirstBuffer.SequenceEqual(pcm)) throw new Exception($"{scenario}: resumed PCM starts at the wrong frame.");
        }
    }

    internal sealed class BufferedPlayer : IWavePlayer, IWavePosition
    {
        private IWaveProvider source = null!;
        private long position;
        public Action? BeforeGetPosition;
        public byte[] FirstBuffer { get; private set; } = [];
        public WaveFormat OutputWaveFormat => source.WaveFormat;
        public PlaybackState PlaybackState { get; private set; }
        public float Volume { get; set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider) => source = provider;
        public void Play()
        {
            if (PlaybackState == PlaybackState.Stopped)
            {
                FirstBuffer = new byte[OutputWaveFormat.AverageBytesPerSecond * 80 / 1000];
                source.Read(FirstBuffer, 0, FirstBuffer.Length);
            }
            PlaybackState = PlaybackState.Playing;
        }
        public void Advance(int ms)
        {
            var buffer = new byte[OutputWaveFormat.AverageBytesPerSecond * ms / 1000];
            source.Read(buffer, 0, buffer.Length);
            position += buffer.Length;
        }
        public void Pause() { PlaybackState = PlaybackState.Paused; position += FirstBuffer.Length; }
        public void Stop() { PlaybackState = PlaybackState.Stopped; position = 0; PlaybackStopped?.Invoke(this, new()); }
        public long GetPosition() { Interlocked.Exchange(ref BeforeGetPosition, null)?.Invoke(); return position; }
        public void Dispose() { }
    }
}
