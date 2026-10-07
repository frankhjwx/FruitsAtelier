using FruitsAtelier.App.Audio;
using NAudio.Wave;

internal static class PreRollTests
{
    public static async Task Run(string file)
    {
        foreach (int channels in new[] { 1, 2 })
        foreach (double speed in new[] { .1, .75, 1, 1.5 })
        {
            var source = new Constant(channels);
            var samples = new PreRollSampleProvider(source, 300 / speed);
            int silentSamples = (int)Math.Round(300 / speed * 48) * channels;
            var output = new List<float>();
            float[] buffer = new float[257];
            while (output.Count < silentSamples + 100)
            {
                Array.Fill(buffer, -1);
                int read = samples.Read(buffer, 3, 251);
                if (buffer[0] != -1 || buffer[^1] != -1) throw new Exception("Pre-roll wrote outside its read range");
                output.AddRange(buffer.AsSpan(3, read).ToArray());
            }
            if (output.Take(silentSamples).Any(v => v != 0) || output.Skip(silentSamples).Any(v => v != .25f))
                throw new Exception("Pre-roll changed the silence length or first music samples");
        }
        foreach (double speed in new[] { .75, 1, 1.5 })
        {
            var players = new List<PausePositionTests.BufferedPlayer>();
            using var audio = new AudioTransport(1, () => { var player = new PausePositionTests.BufferedPlayer(); players.Add(player); return player; });
            if (!await audio.LoadAsync(file)) throw new Exception(audio.Error);
            audio.SetPlaybackSpeed(speed); audio.Seek(-300); audio.Play(); await audio.WaitForCommandsAsync();
            if (Math.Abs(audio.PositionMs + 300) > .1 || players[^1].FirstBuffer.Any(v => v != 0))
                throw new Exception("Negative playback did not begin with silence at its map time");
            var active = players[^1];
            active.Advance(100); await audio.WaitForCommandsAsync();
            double position = audio.PositionMs;
            if (Math.Abs(position - (-300 + 100 * speed)) > .1) throw new Exception("Pre-roll device clock used the wrong rate");
            audio.SetPlaybackSpeed(.5); await audio.WaitForCommandsAsync();
            if (Math.Abs(audio.PositionMs - position) > .1) throw new Exception("Speed change lost the remaining preparation");
            audio.Pause(); await audio.WaitForCommandsAsync();
            if (Math.Abs(audio.PositionMs - position) > .1) throw new Exception("Pause lost negative map time");
            audio.Play(); await audio.WaitForCommandsAsync();
            active = players[^1]; int sessions = players.Count;
            active.Advance((int)Math.Ceiling(-position / .5) + 20); await audio.WaitForCommandsAsync();
            if (audio.PositionMs <= 0 || !audio.IsPlaying || players.Count != sessions)
                throw new Exception("Crossing zero rebuilt or stopped audio playback");
        }
    }

    private sealed class Constant(int channels) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        public int Read(float[] buffer, int offset, int count) { Array.Fill(buffer, .25f, offset, count); return count; }
    }
}
