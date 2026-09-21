using FruitsAtelier.Core;
using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

/// <summary>A bounded polyphonic mixer, independent of the music transport clock.</summary>
internal sealed class HitsoundPlayer(Action<string>? log = null, string? diagnosticDirectory = null) : ISampleProvider, IDisposable
{
    private readonly AudioDiagnosticLog diagnostics = new(diagnosticDirectory);
    private readonly object gate = new();
    private readonly Dictionary<string, float[]> cache = new();
    private long cacheBytes;
    private readonly List<(float[] Samples, int Position, float Volume)> voices = new();
    private readonly List<(float[] Samples, double TimeMs, float Volume)> scheduled = new();
    private bool unavailable;
    private IWavePlayer? liveOutput;
    private float volume = 1;
    public float Volume { get => Volatile.Read(ref volume); set => Volatile.Write(ref volume, float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1); }
    public void PrepareLiveOutput(float outputGain = 1)
    {
        if (liveOutput is not null || unavailable) return;
        var output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 10);
        try
        {
            output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider16(this) { Volume = outputGain });
            output.Play();
            liveOutput = output;
        }
        catch { output.Dispose(); throw; }
    }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(HitsoundSamples.SampleRate, 1);
    public void PreloadProject(IReadOnlyList<MapDocument> documents, IEnumerable<string>? skinFolders = null)
    {
        Stop(); cache.Clear(); cacheBytes = 0;
        foreach (var document in documents)
        {
            document.Tracks.RemoveAll(t => t.Nodes.Count < 2);
            var objects = CatchStreamConverter.Convert(document).Objects;
            var resolver = new HitsoundResolver(document, objects, skinFolders);
            foreach (var sound in objects.SelectMany(resolver.Resolve).DistinctBy(s => s.FilePath ?? $"{s.Kind}/{s.SampleSet}/{s.Name}"))
                Prepare(sound);
        }
    }
    public void Prepare(Hitsound sound) { if (!unavailable) GetSamples(sound); }
    public void Schedule(Hitsound sound, double timeMs)
    {
        if (unavailable || !double.IsFinite(timeMs)) return;
        var samples = GetSamples(sound);
        lock (gate)
        {
            if (scheduled.Count == 2048) scheduled.RemoveAt(0);
            scheduled.Add((samples, timeMs, sound.Volume));
        }
    }
    public void PlayImmediate(Hitsound sound)
    {
        if (unavailable) return;
        // Live judgements cannot be inserted into music frames already submitted to WASAPI.
        Queue(sound);
    }
    internal ISampleProvider MixWithMusic(ISampleProvider music, double startMs, double speed = 1) => new MusicMixer(this, music, startMs, speed);

    private sealed class MusicMixer(HitsoundPlayer owner, ISampleProvider music, double startMs, double speed) : ISampleProvider
    {
        private long framesRead;
        public WaveFormat WaveFormat => music.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = music.Read(buffer, offset, count);
            int channels = WaveFormat.Channels, rate = WaveFormat.SampleRate;
            int frames = read / channels;
            lock (owner.gate)
            {
                for (int v = owner.scheduled.Count - 1; v >= 0; v--)
                {
                    var voice = owner.scheduled[v];
                    long firstFrame = (long)Math.Round((voice.TimeMs - startMs) * rate / (1000 * speed));
                    int begin = (int)Math.Clamp(firstFrame - framesRead, 0, frames);
                    for (int frame = begin; frame < frames; frame++)
                    {
                        double position = (framesRead + frame - firstFrame) * (double)HitsoundSamples.SampleRate / rate;
                        if (position >= voice.Samples.Length) break;
                        int index = (int)position;
                        float a = voice.Samples[index], b = voice.Samples[Math.Min(index + 1, voice.Samples.Length - 1)];
                        float sample = (a + (b - a) * (float)(position - index)) * voice.Volume * owner.Volume;
                        for (int channel = 0; channel < channels; channel++)
                            buffer[offset + frame * channels + channel] += sample;
                    }
                }
            }
            framesRead += frames;
            for (int i = offset; i < offset + read; i++) buffer[i] = Math.Clamp(buffer[i], -1, 1);
            return read;
        }
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
            double beganMs = AudioDiagnosticLog.NowMs;
            string? sourceFormat = null, decoder = null;
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
                    sourceFormat = reader.WaveFormat.ToString();
                    decoder = reader.GetType().Name;
                    if (diagnostics.Enabled) diagnostics.Write("hitsoundDecodeBegin", new { fileName = Path.GetFileName(sound.FilePath),
                        decoder, sourceFormat, encoding = reader.WaveFormat.Encoding.ToString(),
                        bitsPerSample = reader.WaveFormat.BitsPerSample, channels = reader.WaveFormat.Channels,
                        sampleRate = reader.WaveFormat.SampleRate });
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
                    if (diagnostics.Enabled) diagnostics.Write("hitsoundDecoded", new { fileName = Path.GetFileName(sound.FilePath),
                        samples = samples.Length, elapsedMs = AudioDiagnosticLog.NowMs - beganMs });
                }
            }
            catch (Exception ex)
            {
                if (diagnostics.Enabled) diagnostics.Write("hitsoundDecodeFailed", new { fileName = Path.GetFileName(sound.FilePath),
                    decoder, sourceFormat, errorType = ex.GetType().FullName, ex.HResult, ex.StackTrace,
                    fallback = "generatedSample", elapsedMs = AudioDiagnosticLog.NowMs - beganMs });
                log?.Invoke(ex.ToString());
            }
        }
        if (cacheBytes + samples.LongLength * 4 > 256 * 1024 * 1024) { log?.Invoke("Hitsound project PCM bank exceeds 256 MiB; sample skipped: " + key); samples = []; }
        cache[key] = samples; cacheBytes += samples.LongLength * 4;
        return samples;
    }
    public int Read(float[] buffer, int offset, int count)
    {
        // NAudio's SampleToWaveProvider aliases a byte[] as float[]; Array.Clear would clear byte counts.
        for (int i = offset; i < offset + count; i++) buffer[i] = 0;
        lock (gate)
        {
            for (int v = voices.Count - 1; v >= 0; v--)
            {
                var voice = voices[v];
                int length = Math.Min(count, voice.Samples.Length - voice.Position);
                for (int i = 0; i < length; i++) buffer[offset + i] += voice.Samples[voice.Position + i] * voice.Volume * Volume;
                voice.Position += length;
                if (voice.Position == voice.Samples.Length) voices.RemoveAt(v); else voices[v] = voice;
            }
        }
        for (int i = offset; i < offset + count; i++) buffer[i] = Math.Clamp(buffer[i], -1, 1);
        return count;
    }
    public void Stop() { lock (gate) { voices.Clear(); scheduled.Clear(); } }
    public void Dispose()
    {
        unavailable = true;
        try { liveOutput?.Dispose(); liveOutput = null; Stop(); }
        finally { diagnostics.Dispose(); }
    }
}
