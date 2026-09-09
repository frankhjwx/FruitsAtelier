using FruitsAtelier.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

/// <summary>A bounded polyphonic mixer, independent of the music transport clock.</summary>
internal sealed class HitsoundPlayer(Action<string>? log = null) : ISampleProvider, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, float[]> cache = new();
    private long cacheBytes;
    private readonly List<(float[] Samples, int Position, float Volume)> voices = new();
    private WasapiOut? output;
    private bool unavailable;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(HitsoundSamples.SampleRate, 1);
    public void Prepare(Hitsound sound) { if (!unavailable) GetSamples(sound); }
    public void Play(Hitsound sound)
    {
        if (unavailable) return;
        try
        {
            if (output is null)
            {
                output = new WasapiOut(AudioClientShareMode.Shared, true, 20);
                output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(this));
                output.Play();
            }
            Queue(sound);
        }
        catch (Exception ex) { unavailable = true; output?.Dispose(); output = null; log?.Invoke(ex.ToString()); }
    }
    internal void Queue(Hitsound sound)
    {
        var decoded = GetSamples(sound);
        lock (gate)
        {
            if (voices.Count == 32) voices.RemoveAt(0);
            voices.Add((decoded, 0, sound.Volume));
        }
    }
    private float[] GetSamples(Hitsound sound)
    {
        string key = sound.FilePath ?? $"{sound.Kind}/{sound.SampleSet}/{sound.Name}";
        if (cache.TryGetValue(key, out var cached)) return cached;
        float[] samples = HitsoundSamples.Create(sound);
        if (sound.FilePath is not null)
        {
            try
            {
                if (new FileInfo(sound.FilePath).Length <= 16 * 1024 * 1024)
                {
                    using WaveStream reader = Path.GetExtension(sound.FilePath).ToLowerInvariant() switch
                    {
                        ".ogg" => new NAudio.Vorbis.VorbisWaveReader(sound.FilePath),
                        ".wav" => new WaveFileReader(sound.FilePath),
                        _ => new MediaFoundationReader(sound.FilePath)
                    };
                    ISampleProvider source = reader.ToSampleProvider();
                    if (source.WaveFormat.Channels == 2) source = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(source);
                    if (source.WaveFormat.Channels != 1) throw new InvalidDataException("Hitsound must be mono or stereo.");
                    if (source.WaveFormat.SampleRate != HitsoundSamples.SampleRate)
                        source = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(source, HitsoundSamples.SampleRate);
                    var decoded = new List<float>(); var buffer = new float[4096]; int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (decoded.Count + count > 4 * 1024 * 1024) throw new InvalidDataException("Hitsound exceeds decoded size limit.");
                        decoded.AddRange(buffer.Take(count));
                    }
                    samples = decoded.ToArray();
                }
            }
            catch (Exception ex) { log?.Invoke(ex.ToString()); }
        }
        if (cacheBytes + samples.LongLength * 4 > 64 * 1024 * 1024) { cache.Clear(); cacheBytes = 0; }
        cache[key] = samples; cacheBytes += samples.LongLength * 4;
        return samples;
    }
    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        lock (gate)
        {
            for (int v = voices.Count - 1; v >= 0; v--)
            {
                var voice = voices[v];
                int length = Math.Min(count, voice.Samples.Length - voice.Position);
                for (int i = 0; i < length; i++) buffer[offset + i] += voice.Samples[voice.Position + i] * voice.Volume;
                voice.Position += length;
                if (voice.Position == voice.Samples.Length) voices.RemoveAt(v); else voices[v] = voice;
            }
        }
        for (int i = offset; i < offset + count; i++) buffer[i] = Math.Clamp(buffer[i], -1, 1);
        return count;
    }
    public void Stop() { lock (gate) voices.Clear(); }
    public void Dispose() { unavailable = true; output?.Dispose(); output = null; Stop(); }
}
