using FruitsAtelier.App.Audio;
using NAudio.Wave;

internal static class PlaybackTransitionTests
{
    public static async Task Run(string file)
    {
        foreach (int rate in new[] { 44100, 48000 })
        foreach (int channels in new[] { 1, 2 })
        foreach (int chunk in new[] { 1, 73, 1024 })
        {
            var source = new Constant(rate, channels);
            var transition = new PlaybackTransition(source, true);
            var samples = new List<float>();
            float[] buffer = new float[chunk + 6];
            void Read()
            {
                Array.Fill(buffer, 9);
                int read = transition.Read(buffer, 3, chunk);
                if (read != chunk || buffer.Take(3).Concat(buffer.TakeLast(3)).Any(v => v != 9))
                    throw new Exception("Transition changed read size or wrote outside the buffer.");
                samples.AddRange(buffer.AsSpan(3, read).ToArray());
            }
            while (samples.Count < rate * channels / 100) Read();
            if (samples[0] != 0 || Math.Abs(samples[^1]) != .25f)
                throw new Exception("Resume does not ramp from silence to unchanged PCM.");
            var completion = transition.FadeOut();
            while (!completion.IsCompleted) Read();
            int calls = source.Reads;
            Read(); Read();
            if (source.Reads != calls || samples.TakeLast(chunk * 2).Any(v => v != 0))
                throw new Exception("Completed pause fade continued consuming source PCM or emitted sound.");
            for (int i = channels; i < samples.Count; i++)
                if (Math.Abs(Math.Abs(samples[i]) - Math.Abs(samples[i - channels])) > .002)
                    throw new Exception("Pause/resume envelope contains a discontinuity.");
            for (int i = 0; i + channels <= samples.Count; i += channels)
                if (channels == 2 && samples[i] != -samples[i + 1])
                    throw new Exception("Transition used different gains for the stereo channels.");
        }

        var players = new List<PumpingPlayer>();
        using var audio = new AudioTransport(1, () => { var player = new PumpingPlayer(); players.Add(player); return player; });
        if (!await audio.LoadAsync(file)) throw new Exception(audio.Error);
        foreach (double speed in new[] { 1d, .75, 1.5, 1 })
        {
            audio.SetPlaybackSpeed(speed); await audio.WaitForCommandsAsync();
            audio.Play(); await audio.WaitForCommandsAsync();
            await Task.Delay(35); await audio.WaitForCommandsAsync();
            var active = players[^1];
            audio.Pause();
            double position = audio.PositionMs;
            if (audio.IsPlaying) throw new Exception("Pause did not immediately freeze its requested state.");
            await audio.WaitForCommandsAsync().WaitAsync(TimeSpan.FromSeconds(2));
            if (!active.StoppedAtSilence)
                throw new Exception("Stop cut off the generated fade before the buffered device consumed it.");
            if (Math.Abs(audio.PositionMs - position) > 1000d / 44100)
                throw new Exception("Fade-out moved the saved pause point.");
            audio.Seek(position + 100); await audio.WaitForCommandsAsync();
            audio.Play(); await audio.WaitForCommandsAsync();
            if (players[^1].FirstBuffer.Take(4).Any(v => v != 0)) throw new Exception("Resume starts with an abrupt nonzero PCM frame.");
            audio.Pause(); await audio.WaitForCommandsAsync();
        }
        audio.Play(); await audio.WaitForCommandsAsync();
        audio.Pause(); audio.Play(); audio.Seek(1000);
        await audio.WaitForCommandsAsync();
        if (!audio.IsPlaying || audio.PositionMs < 999 || audio.PositionMs > 1200)
            throw new Exception("Queued pause, play and seek lost the playback intent or seek point.");
        if (players[^1].FirstBuffer.Take(4).Any(v => v != 0))
            throw new Exception("Queued seek discarded the resume fade.");
        audio.Pause(); await audio.WaitForCommandsAsync();
    }

    private sealed class Constant(int rate, int channels) : ISampleProvider
    {
        public int Reads;
        private int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            Reads++;
            for (int i = 0; i < count; i++) buffer[offset + i] = channels == 2 && position++ % 2 == 1 ? -.25f : .25f;
            return count;
        }
    }

    private sealed class PumpingPlayer : IWavePlayer, IWavePosition
    {
        private IWaveProvider source = null!;
        private Task? pump;
        private volatile bool playing;
        private long position;
        public byte[] FirstBuffer { get; private set; } = [];
        public bool StoppedAtSilence { get; private set; }
        public long BytesConsumed => Interlocked.Read(ref position);
        public WaveFormat OutputWaveFormat => source.WaveFormat;
        public PlaybackState PlaybackState => playing ? PlaybackState.Playing : PlaybackState.Stopped;
        public float Volume { get; set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider provider) => source = provider;
        public void Play()
        {
            if (playing) return;
            playing = true;
            int bytes = OutputWaveFormat.SampleRate / 100 * OutputWaveFormat.BlockAlign;
            var queue = new Queue<byte[]>();
            for (int i = 0; i < 3; i++)
            {
                var block = new byte[bytes]; source.Read(block, 0, bytes); queue.Enqueue(block);
                if (i == 0) FirstBuffer = block;
            }
            pump = Task.Run(async () =>
            {
                try
                {
                    while (playing)
                    {
                        var block = new byte[bytes]; source.Read(block, 0, bytes); queue.Enqueue(block);
                        var consumed = queue.Dequeue();
                        StoppedAtSilence = consumed.TakeLast(OutputWaveFormat.BlockAlign).All(v => v == 0);
                        Interlocked.Add(ref position, consumed.Length);
                        await Task.Delay(10);
                    }
                }
                finally { PlaybackStopped?.Invoke(this, new()); }
            });
        }
        public void Pause() => Stop();
        public void Stop() { playing = false; pump?.GetAwaiter().GetResult(); }
        public long GetPosition() => BytesConsumed;
        public void Dispose() => Stop();
    }
}
