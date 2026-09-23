using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;
using NAudio.Wave.SampleProviders;
using NAudio.Wave;

static class HitsoundMixerTests
{
    public static void Run()
    {
        ByteAdapterBuffers();
        ScheduledMusic();
        IndependentVolume();
        using var mixer = new HitsoundPlayer();
        var buffer = new float[4096];
        var sound = new Hitsound(CatchObjectKind.Fruit, null, .5f);
        var expected = HitsoundSamples.Create(sound);
        mixer.Queue(sound); mixer.Queue(sound);
        mixer.Read(buffer, 3, buffer.Length - 3);
        for (int i = 0; i < expected.Length; i++)
            if (Math.Abs(buffer[3 + i] - expected[i]) > .00001) throw new Exception("Concurrent samples or volume differ from reference PCM");
        if (buffer.Take(3).Any(v => v != 0) || buffer.Skip(3 + expected.Length).Any(v => v != 0)) throw new Exception("Mixer writes outside active sample boundaries");
        mixer.Queue(sound); mixer.Stop(); mixer.Read(buffer, 0, buffer.Length);
        if (buffer.Any(v => v != 0)) throw new Exception("Stopped voices still emit PCM");
        for (int i = 0; i < 64; i++) mixer.Queue(sound with { Volume = 1 });
        mixer.Read(buffer, 0, buffer.Length);
        if (buffer.Any(v => !float.IsFinite(v) || Math.Abs(v) > 1)) throw new Exception("Polyphonic mix clips outside valid PCM range");
        string path = Path.Combine(Path.GetTempPath(), "hitsound-" + Guid.NewGuid() + ".wav");
        try
        {
            File.WriteAllBytes(path, HitsoundSamples.CreateWave(CatchObjectKind.Droplet));
            mixer.Stop(); mixer.Queue(new(CatchObjectKind.Fruit, path, 1)); mixer.Read(buffer, 0, buffer.Length);
            var custom = HitsoundSamples.Create(CatchObjectKind.Droplet);
            for (int i = 0; i < custom.Length; i++)
                if (Math.Abs(buffer[i] - custom[i]) > .0001) throw new Exception("Custom WAV was not decoded as its own PCM");
        }
        finally { File.Delete(path); }
        mixer.Stop();
        var normal = HitsoundDefaults.Find(1, "hitnormal") ?? throw new Exception("Default hitnormal asset missing from build output");
        mixer.Queue(new(CatchObjectKind.Fruit, normal, 1)); mixer.Read(buffer, 0, buffer.Length);
        if (!buffer.Any(v => Math.Abs(v) > .001)) throw new Exception("Default normal-only note emitted silent PCM");
        Console.WriteLine("PASS Hitsound PCM mixing, volume, overlap, stop, clipping and custom WAV decoding");
    }

    private static void IndependentVolume()
    {
        using var mixer = new HitsoundPlayer();
        var sound = new Hitsound(CatchObjectKind.Fruit, null, .5f);
        var reference = HitsoundSamples.Create(sound);
        var buffer = new float[64];
        mixer.Queue(sound); mixer.Volume = .2f; mixer.Read(buffer, 0, buffer.Length);
        for (int i = 0; i < buffer.Length; i++)
            if (Math.Abs(buffer[i] - reference[i] * .1f) > .00001) throw new Exception("Live gain does not scale queued voices");
        mixer.Volume = 0; mixer.Read(buffer, 0, buffer.Length);
        if (buffer.Any(x => x != 0)) throw new Exception("Live gain did not mute active voices");
        mixer.Stop(); mixer.Schedule(sound, 0); mixer.Volume = .4f;
        float songGain = 0;
        var mixed = mixer.MixWithMusic(new PlaybackGain(new ConstantMusic(44100, 1), () => songGain), 0);
        mixed.Read(buffer, 0, buffer.Length);
        for (int i = 0; i < buffer.Length; i++)
            if (Math.Abs(buffer[i] - reference[i] * .2f) > .00001) throw new Exception("Song mute incorrectly mutes scheduled hitsounds");
        mixer.Stop(); songGain = .5f; mixer.Volume = 0;
        mixed.Read(buffer, 0, buffer.Length);
        if (buffer.Any(x => Math.Abs(x - .05f) > .00001)) throw new Exception("Hitsound mute or dynamic song gain changes music incorrectly");
        Console.WriteLine("PASS Independent live, scheduled and song gain updates without clock changes");
    }

    private static void ScheduledMusic()
    {
        foreach (int rate in new[] { 44100, 48000 })
        foreach (int channels in new[] { 1, 2 })
        foreach (double speed in new[] { .25, .5, .75, 1 })
        {
            using var mixer = new HitsoundPlayer();
            var sound = new Hitsound(CatchObjectKind.Fruit, null, .5f);
            var sample = HitsoundSamples.Create(sound);
            mixer.Schedule(sound, 537.25);
            var combined = mixer.MixWithMusic(new ConstantMusic(rate, channels), 500, speed);
            long hitFrame = (long)Math.Round(37.25 * rate / (1000 * speed));
            var buffer = new float[257 * channels + 6];
            for (int block = 0; block < 70; block++)
            {
                Array.Fill(buffer, .75f);
                int count = 257 * channels;
                if (combined.Read(buffer, 3, count) != count) throw new Exception("Mixed music read length changed");
                for (int i = 0; i < 257; i++)
                {
                    double position = (block * 257 + i - hitFrame) * 44100.0 / rate;
                    float expected = .1f;
                    if (position >= 0 && position < sample.Length)
                    {
                        int index = (int)position;
                        expected += (sample[index] + (sample[Math.Min(index + 1, sample.Length - 1)] - sample[index]) * (float)(position - index)) * sound.Volume;
                    }
                    for (int c = 0; c < channels; c++)
                        if (Math.Abs(buffer[3 + i * channels + c] - expected) > .00001f)
                            throw new Exception($"Scheduled hit differs from its music frame at {rate} Hz / {channels} channels");
                }
                if (buffer.Take(3).Concat(buffer.TakeLast(3)).Any(v => v != .75f)) throw new Exception("Scheduled mixer overwrote buffer boundaries");
            }
            mixer.Schedule(sound, 1000);
            mixer.Stop();
            var resumed = mixer.MixWithMusic(new ConstantMusic(rate, channels), 1000);
            resumed.Read(buffer, 0, channels * 257);
            if (buffer.Take(channels * 257).Any(v => v != .1f)) throw new Exception("Canceled future hit survived a new music session");
            mixer.Schedule(sound, 999);
            resumed.Read(buffer, 0, channels * 257);
            for (int i = 0; i < 257; i++)
            {
                double position = i * 44100d / rate;
                int index = (int)position;
                float attack = (sample[index] + (sample[Math.Min(index + 1, sample.Length - 1)] - sample[index]) * (float)(position - index)) * sound.Volume;
                for (int c = 0; c < channels; c++)
                    if (Math.Abs(buffer[i * channels + c] - (.1f + attack)) > .00001f)
                        throw new Exception("Late catch lost the beginning of its sample");
            }
            mixer.Stop();
            mixer.PlayImmediate(sound);
            resumed.Read(buffer, 0, channels * 257);
            for (int i = 0; i < 257; i++)
            {
                double position = i * 44100d / rate;
                int index = (int)position;
                float attack = (sample[index] + (sample[Math.Min(index + 1, sample.Length - 1)] - sample[index]) * (float)(position - index)) * sound.Volume;
                for (int c = 0; c < channels; c++)
                    if (Math.Abs(buffer[i * channels + c] - (.1f + attack)) > .00001f)
                        throw new Exception("Live catch was not mixed into the next music frames");
            }
            mixer.Stop();
            resumed.Read(buffer, 0, channels * 257);
            if (buffer.Take(channels * 257).Any(v => v != .1f)) throw new Exception("Live catch survived cancellation");
        }
        Console.WriteLine("PASS Timestamped hitsounds share music frames across buffer boundaries, sample rates, channels and cancellation");
    }

    private sealed class ConstantMusic(int rate, int channels) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++) buffer[i] = .1f;
            return count;
        }
    }

    private static void ByteAdapterBuffers()
    {
        foreach (int sampleCount in new[] { 256, 4096 })
        foreach (int offset in new[] { 0, 12 })
        {
            using var mixer = new HitsoundPlayer();
            var adapter = new SampleToWaveProvider(mixer);
            var sound = new Hitsound(CatchObjectKind.Fruit, null, .5f);
            var samples = HitsoundSamples.Create(sound);
            int byteCount = sampleCount * sizeof(float);
            var buffer = Enumerable.Repeat((byte)0x5a, offset + byteCount + 12).ToArray();

            ReadAndCompare(null, 0);
            mixer.Queue(sound);
            for (int position = 0; position < samples.Length + sampleCount * 2; position += sampleCount)
                ReadAndCompare(samples, position);

            mixer.Queue(sound);
            ReadAndCompare(samples, 0);
            mixer.Stop();
            ReadAndCompare(null, 0);
            ReadAndCompare(null, 0);

            void ReadAndCompare(float[]? expected, int position)
            {
                if (adapter.Read(buffer, offset, byteCount) != byteCount)
                    throw new Exception("Hitsound byte adapter did not fill the requested buffer");
                for (int i = 0; i < sampleCount; i++)
                {
                    float value = expected is not null && position + i < expected.Length ? expected[position + i] * sound.Volume : 0;
                    float actual = BitConverter.ToSingle(buffer, offset + i * sizeof(float));
                    if (!float.IsFinite(actual) || Math.Abs(actual - value) > .00001f)
                        throw new Exception($"Hitsound byte adapter retained or altered PCM: count={sampleCount}, offset={offset}, position={position}, sample={i}, expected={value}, actual={actual}");
                }
                if (buffer.Take(offset).Concat(buffer.Skip(offset + byteCount)).Any(b => b != 0x5a))
                    throw new Exception("Hitsound byte adapter wrote outside the requested range");
            }
        }
        Console.WriteLine("PASS Hitsound byte adapter preserves PCM and clears reused buffers after voice completion and stop");
    }
}
