using NAudio.Wave;
using FruitsAtelier.App.Audio;

internal static class OutputRecoveryTests
{
    public static async Task DelayedStop(string file)
    {
        foreach (bool playing in new[] { false, true })
        {
            var players = new List<ControlledPlayer>();
            using var audio = new AudioTransport(0, () =>
            {
                var player = new ControlledPlayer();
                players.Add(player);
                return player;
            }, TimeSpan.FromMilliseconds(30));
            try
            {
                Check(await audio.LoadAsync(file), "Test file failed to load");
                audio.Play();
                await Flush(audio);
                if (!playing) { audio.Pause(); await Flush(audio); }
                var retired = players[^1];
                retired.DelayStop = true;
                int previousCount = players.Count;
                audio.Seek(1500);
                await Flush(audio);
                Check(audio.Error is null && audio.CanPlay && audio.IsPlaying == playing && Math.Abs(audio.PositionMs - 1500) < 1,
                    audio.Error ?? "Seek did not rebuild a timed-out session at the requested position");
                Check(players.Count == previousCount + 1, "Output was not rebuilt");
                if (playing)
                {
                    Check(!retired.Disposed && retired.ReadSamples() > 0, "A retired session lost its reader before it stopped");
                    retired.CompleteStop(new IOException("Delayed error from retired output"));
                    await retired.DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                Check(players.All(player => player.OnlySilence), "Automatic tests sent non-zero samples to output");
                await Flush(audio);
                Check(audio.CanPlay && audio.Error is null && audio.IsPlaying == playing && audio.PositionMs < audio.DurationMs,
                    "Late callback from a retired session changed the replacement clock or error");
                audio.Seek(500); audio.Seek(2500);
                await Flush(audio);
                Check(Math.Abs(audio.PositionMs - 2500) < 1 && audio.CanPlay && audio.Error is null, "Subsequent seeks reused the timed-out session");
            }
            finally { foreach (var player in players) player.CompleteStop(); }
        }
    }

    public static async Task RepeatedFailure(string file)
    {
        var players = new List<ControlledPlayer>();
        bool fail = true;
        using var audio = new AudioTransport(0, () =>
        {
            var player = new ControlledPlayer { DelayStop = fail };
            players.Add(player);
            return player;
        }, TimeSpan.FromMilliseconds(30));
        try
        {
            Check(await audio.LoadAsync(file), "Test file failed to load");
            audio.Play(); await Flush(audio);
            audio.Seek(1000); await Flush(audio);
            Check(audio.CanPlay && audio.Error is null, "First timeout was not recovered");
            audio.Seek(2000); await Flush(audio);
            Check(!audio.CanPlay && audio.Error is not null && !audio.IsPlaying, "Repeated failure did not retain an error");
            int count = players.Count;
            audio.Seek(2500); audio.Play(); await Flush(audio);
            Check(players.Count == count && count == 2 && !audio.CanPlay && audio.Error is not null,
                "Failed session became usable again or recovery created unbounded sessions");
            foreach (var player in players) player.CompleteStop();
            fail = false;
            Check(await audio.LoadAsync(file), "Explicit reload could not recover after output failure");
            audio.Play(); await Flush(audio);
            Check(audio.CanPlay && audio.IsPlaying && audio.Error is null, "Reload retained an old session error");
        }
        finally { foreach (var player in players) player.CompleteStop(); }
    }

    public static async Task EndBeforeCallback(string file)
    {
        var players = new List<ControlledPlayer>();
        using var audio = new AudioTransport(0, () =>
        {
            var player = new ControlledPlayer { DelayStop = players.Count == 0 };
            players.Add(player);
            return player;
        }, TimeSpan.FromMilliseconds(30));
        try
        {
            Check(await audio.LoadAsync(file), "Test file failed to load");
            audio.Play(); await Flush(audio);
            players[0].ReachEndBeforeCallback();
            audio.Play(); await Flush(audio);
            Check(players.Count == 2 && players[0].PlayCount == 1 && audio.IsPlaying && audio.PositionMs < 1,
                "Replay restarted a session whose previous playback thread has not exited");
            Check(!players[0].Disposed && players[0].ReadSamples() > 0, "EOF callback delay released an active reader");
            players[0].CompleteStop();
            await players[0].DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
            await Flush(audio);
            Check(audio.CanPlay && audio.Error is null && audio.IsPlaying && audio.PositionMs < 1,
                "Late EOF marked replacement playback as ended");
        }
        finally { foreach (var player in players) player.CompleteStop(); }
    }

    private static Task Flush(AudioTransport audio) => audio.WaitForCommandsAsync().WaitAsync(TimeSpan.FromSeconds(2));
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }

    private sealed class ControlledPlayer : IWavePlayer, IWavePosition
    {
        private IWaveProvider? source;
        private readonly TaskCompletionSource<bool> disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DelayStop { get; set; }
        public bool Disposed => disposed.Task.IsCompleted;
        public Task DisposedTask => disposed.Task;
        public bool OnlySilence { get; private set; } = true;
        public int PlayCount { get; private set; }
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat => source!.WaveFormat;
        public float Volume { get; set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider) => source = provider;
        public void Play() { PlayCount++; ReadSamples(); PlaybackState = PlaybackState.Playing; }
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() { PlaybackState = PlaybackState.Stopped; if (!DelayStop) CompleteStop(); }
        public void ReachEndBeforeCallback() => PlaybackState = PlaybackState.Stopped;
        public void CompleteStop(Exception? error = null) => PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
        public long GetPosition() => 0;
        public int ReadSamples()
        {
            byte[] buffer = new byte[128];
            int read = source!.Read(buffer, 0, buffer.Length);
            OnlySilence &= buffer.Take(read).All(value => value == 0);
            return read;
        }
        public void Dispose() => disposed.TrySetResult(true);
    }
}
