namespace FruitsAtelier.Core;

/// <summary>Original, procedurally generated preview samples; no external audio assets.</summary>
public static class HitsoundSamples
{
    public const int SampleRate = 44100;
    public static float[] Create(CatchObjectKind kind) => Create(new Hitsound(kind, null, 1));

    public static float[] Create(Hitsound sound)
    {
        var kind = sound.Kind;
        double frequency = kind switch { CatchObjectKind.Fruit => 1100, CatchObjectKind.Droplet => 1600, CatchObjectKind.TinyDroplet => 2200, _ => 850 };
        frequency *= sound.SampleSet switch { 2 => .8, 3 => .6, _ => 1 };
        frequency *= sound.Name switch { "hitwhistle" => 1.7, "hitfinish" => .55, "hitclap" => 2.3, _ => 1 };
        double gain = kind switch { CatchObjectKind.Fruit => .32, CatchObjectKind.Droplet => .20, CatchObjectKind.TinyDroplet => .10, _ => .18 };
        var samples = new float[SampleRate / 16];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double envelope = Math.Min(1, i / 32.0) * Math.Exp(-t * 90) * (1 - (double)i / samples.Length);
            samples[i] = (float)(gain * envelope * (Math.Sin(2 * Math.PI * frequency * t) + .25 * Math.Sin(2 * Math.PI * frequency * 2.7 * t)));
        }
        return samples;
    }

    public static byte[] CreateWave(CatchObjectKind kind) => CreateWave(new Hitsound(kind, null, 1));

    public static byte[] CreateWave(Hitsound sound)
    {
        var samples = Create(sound);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + samples.Length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
        writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(samples.Length * 2);
        foreach (float sample in samples) writer.Write((short)(sample * short.MaxValue));
        return stream.ToArray();
    }
}
