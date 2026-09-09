using System.Runtime.InteropServices;
using FruitsAtelier.Core;
using NVorbis;

namespace FruitsAtelier.Mac;

/// <summary>Project PCM bank feeding one persistent native mixer. The UI only queues timestamps.</summary>
public sealed class MacHitsoundPlayer(bool muted = false) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, nint> samples = new();
    private readonly HashSet<string> pending = new();
    private Task preparation = Task.CompletedTask;
    private nint engine;
    private bool disposed;
    private int generation;
    private long bytes;
    private const long MemoryLimit = 256 * 1024 * 1024;
    public Task Preparation { get { lock (gate) return preparation; } }
    internal int NativePlayerCreations { get; private set; }
    internal int SampleLoads { get; private set; }
    internal long DecodedBytes { get { lock (gate) return bytes; } }
    internal double LastVoicePositionMs { get { lock (gate) return engine == 0 ? 0 : Position(engine) * 1000; } }
    internal double LastRenderedStart { get { lock (gate) return engine == 0 ? 0 : RenderedStart(engine); } }
    public int ActiveVoices { get { lock (gate) return engine == 0 ? 0 : (int)Active(engine); } }

    public void PreloadProject(IReadOnlyList<MapDocument> documents)
    {
        lock (gate)
        {
            if (disposed) return;
            int version = ++generation;
            Stop();
            preparation = preparation.ContinueWith(_ =>
            {
                lock (gate)
                {
                    if (disposed || version != generation) return;
                    ReleaseBank(); pending.Clear();
                }
                // Snapshots are taken by the editor before dispatch; workers never inspect live edits.
                foreach (var document in documents)
                {
                    if (Retired(version)) return;
                    document.Tracks.RemoveAll(t => t.Nodes.Count < 2);
                    var objects = CatchStreamConverter.Convert(document).Objects;
                    var resolver = new HitsoundResolver(document, objects);
                    foreach (var sound in objects.SelectMany(resolver.Resolve).DistinctBy(Key))
                    {
                        if (Retired(version)) return;
                        LoadSample(sound, version);
                    }
                }
            }, TaskScheduler.Default);
        }
    }
    private bool Retired(int version) { lock (gate) return disposed || version != generation; }
    public void Prepare(Hitsound sound)
    {
        lock (gate)
        {
            if (disposed || samples.ContainsKey(Key(sound)) || !pending.Add(Key(sound))) return;
            int version = generation;
            preparation = preparation.ContinueWith(_ => LoadSample(sound, version), TaskScheduler.Default);
        }
    }
    private void LoadSample(Hitsound sound, int version)
    {
        lock (gate) if (disposed || version != generation || samples.ContainsKey(Key(sound))) return;
        nint sample = 0;
        string? temporary = null;
        try
        {
            // Native WAV/MP3 decoding and managed OGG conversion happen only on this loader worker.
            string? path = sound.FilePath;
            if (path is null || Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024)
            {
                temporary = Path.Combine(Path.GetTempPath(), "fa-hitsound-" + Guid.NewGuid() + ".wav");
                File.WriteAllBytes(temporary, Load(sound)); path = temporary;
            }
            sample = OpenSample(path);
            if (sample == 0)
            {
                temporary ??= Path.Combine(Path.GetTempPath(), "fa-hitsound-" + Guid.NewGuid() + ".wav");
                File.WriteAllBytes(temporary, HitsoundSamples.CreateWave(sound)); sample = OpenSample(temporary);
            }
            lock (gate)
            {
                if (disposed || version != generation || sample == 0) return;
                long size = SampleBytes(sample);
                if (bytes + size > MemoryLimit) { MacPaths.Log("Hitsound project PCM bank exceeds 256 MiB; sample skipped: " + Key(sound)); return; }
                if (engine == 0) { engine = Open(muted ? 1 : 0); NativePlayerCreations++; }
                samples[Key(sound)] = sample; sample = 0; bytes += size; SampleLoads++;
                pending.Remove(Key(sound));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { MacPaths.Log(ex.ToString()); }
        finally
        {
            if (sample != 0) CloseSample(sample);
            if (temporary is not null) File.Delete(temporary);
            lock (gate) if (!disposed && version == generation)
            {
                // Remember rejected samples too; playback preparation must not repeatedly decode them.
                samples.TryAdd(Key(sound), 0); pending.Remove(Key(sound));
            }
        }
    }
    private static string Key(Hitsound sound) => sound.FilePath ?? $"{sound.Kind}/{sound.SampleSet}/{sound.Name}";
    public void Play(Hitsound sound) => Schedule(sound, HostTime());
    public void Schedule(Hitsound sound, double hostTime)
    {
        if (!double.IsFinite(hostTime)) return;
        lock (gate)
        {
            // Never fall back to disk I/O or decoding on a playback miss.
            if (!disposed && engine != 0 && samples.TryGetValue(Key(sound), out nint sample) && sample != 0)
                ScheduleNative(engine, sample, hostTime, sound.Volume);
        }
    }
    public void Stop() { lock (gate) if (engine != 0) StopNative(engine); }
    private void ReleaseBank()
    {
        // Stop/join the render callback before releasing sample memory referenced by queued voices.
        if (engine != 0) Close(engine); engine = 0;
        foreach (nint sample in samples.Values) if (sample != 0) CloseSample(sample);
        samples.Clear(); bytes = 0;
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; generation++; ReleaseBank(); } }
    private static byte[] Load(Hitsound sound)
    {
        if (sound.FilePath is not null)
        {
            try
            {
                if (new FileInfo(sound.FilePath).Length > 16 * 1024 * 1024) return HitsoundSamples.CreateWave(sound);
                if (!Path.GetExtension(sound.FilePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase)) return File.ReadAllBytes(sound.FilePath);
                using var reader = new VorbisReader(sound.FilePath);
                if (reader.Channels is < 1 or > 8 || reader.SampleRate is < 8000 or > 192000) return HitsoundSamples.CreateWave(sound);
                using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)reader.Channels); writer.Write(reader.SampleRate);
                writer.Write(reader.SampleRate * reader.Channels * 2); writer.Write((short)(reader.Channels * 2)); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(0);
                var buffer = new float[4096]; int count;
                while ((count = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
                {
                    if (stream.Length + count * 2 > 16 * 1024 * 1024) return HitsoundSamples.CreateWave(sound);
                    for (int i = 0; i < count; i++) writer.Write((short)Math.Clamp(buffer[i] * 32767, -32768, 32767));
                }
                int length = (int)stream.Length; stream.Position = 4; writer.Write(length - 8); stream.Position = 40; writer.Write(length - 44);
                return stream.ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { MacPaths.Log(ex.ToString()); }
        }
        return HitsoundSamples.CreateWave(sound);
    }
    [DllImport("FruitsAtelierAudio", EntryPoint="fa_hitsounds_rendered_start")] private static extern double RenderedStart(nint engine);
    private const string Library = "FruitsAtelierAudio";
    [DllImport(Library, EntryPoint="fa_hitsounds_open")] private static extern nint Open(int muted);
    [DllImport(Library, EntryPoint="fa_hitsounds_close")] private static extern void Close(nint engine);
    [DllImport(Library, EntryPoint="fa_hitsounds_sample")] private static extern nint OpenSample([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library, EntryPoint="fa_hitsounds_sample_close")] private static extern void CloseSample(nint sample);
    [DllImport(Library, EntryPoint="fa_hitsounds_sample_bytes")] private static extern uint SampleBytes(nint sample);
    [DllImport(Library, EntryPoint="fa_hitsounds_schedule")] private static extern int ScheduleNative(nint engine, nint sample, double start, float volume);
    [DllImport(Library, EntryPoint="fa_hitsounds_stop")] private static extern void StopNative(nint engine);
    [DllImport(Library, EntryPoint="fa_hitsounds_position")] private static extern double Position(nint engine);
    [DllImport(Library, EntryPoint="fa_hitsounds_active")] private static extern uint Active(nint engine);
    [DllImport(Library, EntryPoint="fa_audio_host_time")] private static extern double HostTime();
}
