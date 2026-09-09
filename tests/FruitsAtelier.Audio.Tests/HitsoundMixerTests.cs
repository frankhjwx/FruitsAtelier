using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;

static class HitsoundMixerTests
{
    public static void Run()
    {
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
}
