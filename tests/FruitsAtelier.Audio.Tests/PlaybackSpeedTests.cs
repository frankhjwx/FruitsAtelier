using FruitsAtelier.App.Audio;
using NAudio.Wave;

internal static class PlaybackSpeedTests
{
    public static void PitchAndDuration()
    {
        foreach (int sampleRate in new[] { 44100, 48000 })
        foreach (int channels in new[] { 1, 2 })
        foreach (double speed in new[] { .1, .25, .5, .75, 1.5 })
        {
            var source = new Tone(sampleRate, channels);
            var tempo = new TempoSampleProvider(source, speed);
            var result = new List<float>();
            float[] buffer = new float[257 * channels + 6];
            int read;
            while ((read = tempo.Read(buffer, 3, buffer.Length - 6)) > 0)
            {
                result.AddRange(buffer.AsSpan(3, read).ToArray());
                if (result.Count > sampleRate * channels * (2 / speed + 1)) throw new Exception("Tempo output did not reach EOF");
            }
            double duration = result.Count / (double)(sampleRate * channels);
            if (Math.Abs(duration - 2 / speed) > .002) throw new Exception($"Wrong stretched duration: {duration} at {speed}");
            for (int channel = 0; channel < channels; channel++)
            {
                double minimumRms = double.MaxValue;
                int window = sampleRate / 50;
                // Keep this continuity check in the sustained middle of the tone, before EOF flushing.
                for (int first = sampleRate / 4; first + window < result.Count / channels / 2; first += window)
                {
                    double energy = 0;
                    for (int frame = first; frame < first + window; frame++)
                        energy += result[frame * channels + channel] * result[frame * channels + channel];
                    double rms = Math.Sqrt(energy / window);
                    minimumRms = Math.Min(minimumRms, rms);
                }
                if (minimumRms < .05) throw new Exception($"Continuous tone has a dropout at {speed}, {sampleRate} Hz, channel {channel}: RMS {minimumRms}");
                int crossings = 0;
                for (int i = sampleRate / 4; i < sampleRate * 3 / 4; i++)
                    if (result[i * channels + channel] <= 0 && result[(i + 1) * channels + channel] > 0) crossings++;
                if (Math.Abs(crossings * 2 - (440 + 220 * channel)) > 3)
                    throw new Exception($"Pitch changed at {speed}: {crossings} Hz");
            }
            if (buffer.Take(3).Concat(buffer.TakeLast(3)).Any(v => v != 0)) throw new Exception("Tempo overwrote the buffer boundary");
        }
    }

    public static async Task Clock(string file)
    {
        var players = new List<PausePositionTests.BufferedPlayer>();
        using var audio = new AudioTransport(0, () => { var player = new PausePositionTests.BufferedPlayer(); players.Add(player); return player; });
        audio.SetPlaybackSpeed(.25);
        if (!await audio.LoadAsync(file)) throw new Exception(audio.Error);
        audio.Play(); await audio.WaitForCommandsAsync();
        foreach (double speed in new[] { .1, .25, .5, .75, 1, 1.5, .1 })
        {
            double before = audio.PositionMs;
            audio.SetPlaybackSpeed(speed); await audio.WaitForCommandsAsync();
            Near(before, audio.PositionMs);
            if (!audio.IsPlaying) throw new Exception("Changing speed paused playback");
            players[^1].Advance(200); await audio.WaitForCommandsAsync();
            Near(before + 200 * speed, audio.PositionMs);
            audio.Pause(); await audio.WaitForCommandsAsync();
            before = audio.PositionMs;
            audio.Play(); await audio.WaitForCommandsAsync(); Near(before, audio.PositionMs);
        }
        audio.Pause(); await audio.WaitForCommandsAsync();
        audio.Seek(2300); audio.SetPlaybackSpeed(.5); await audio.WaitForCommandsAsync();
        Near(2300, audio.PositionMs);
        if (audio.IsPlaying) throw new Exception("Changing paused speed started music");
        audio.Play(); await audio.WaitForCommandsAsync();
        players[^1].Advance(200); await audio.WaitForCommandsAsync(); Near(2400, audio.PositionMs);
        static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > .15) throw new Exception($"Clock: {expected} != {actual}"); }
    }

    private sealed class Tone(int sampleRate, int channels) : ISampleProvider
    {
        private int sample;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            count = Math.Min(count, sampleRate * channels * 2 - sample);
            for (int i = 0; i < count; i++, sample++)
                buffer[offset + i] = (float)(.25 * Math.Sin(2 * Math.PI * (440 + 220 * (sample % channels)) * (sample / channels) / sampleRate));
            return count;
        }
    }
}
