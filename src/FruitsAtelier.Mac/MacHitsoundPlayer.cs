using System.Runtime.InteropServices;
using FruitsAtelier.Core;
using NVorbis;

namespace FruitsAtelier.Mac;

public sealed class MacHitsoundPlayer(bool muted = false) : IDisposable
{
    private sealed class Voice(string key, nint handle, int bytes)
    {
        public string Key { get; } = key;
        public nint Handle { get; } = handle;
        public int Bytes { get; } = bytes;
        public double BusyUntil { get; set; }
    }
    private readonly Dictionary<string, byte[]> cache = new();
    private readonly List<Voice> voices = new();
    private bool disposed;
    private long cacheBytes, voiceBytes;
    private const long MemoryLimit = 64 * 1024 * 1024;
    internal int NativePlayerCreations { get; private set; }
    private Voice? lastVoice;
    internal double LastVoicePositionMs => lastVoice is null ? 0 : Position(lastVoice.Handle) * 1000;
    public int ActiveVoices => voices.Count(v => v.BusyUntil > DeviceTime(v.Handle));

    public void Prepare(Hitsound sound)
    {
        if (disposed) return;
        string key = Key(sound);
        byte[] data = GetData(sound);
        // Retain prepared native players, not just compressed file bytes.
        if (!voices.Any(v => v.Key == key)) CreateVoice(key, data, sound);
    }
    public void Play(Hitsound sound) => PlayInternal(sound, null);
    public void Schedule(Hitsound sound, double deviceTime)
    {
        if (double.IsFinite(deviceTime)) PlayInternal(sound, deviceTime);
    }
    private void PlayInternal(Hitsound sound, double? deviceTime)
    {
        if (disposed) return;
        try
        {
            string key = Key(sound);
            var voice = voices.FirstOrDefault(v => v.Key == key && v.BusyUntil <= DeviceTime(v.Handle));
            if (voice is null && voices.Count >= 32)
            {
                // Preserve new attacks under load by reusing the oldest matching tail.
                voice = voices.Where(v => v.Key == key).MinBy(v => v.BusyUntil);
            }
            if (voice is null) voice = CreateVoice(key, GetData(sound), sound);
            if (voice is null) return;
            // A completed voice retains its output resources. Reset before reusing it.
            if (voice.BusyUntil != 0) { Pause(voice.Handle); Seek(voice.Handle, 0); PrepareNative(voice.Handle); }
            Volume(voice.Handle, muted ? 0 : sound.Volume);
            double now = DeviceTime(voice.Handle);
            double start = Math.Max(now, deviceTime ?? now);
            int played = deviceTime.HasValue ? PlayAt(voice.Handle, start) : PlayNative(voice.Handle);
            if (played != 0) { voice.BusyUntil = start + Duration(voice.Handle) + .01; lastVoice = voice; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { MacPaths.Log(ex.ToString()); }
    }
    private Voice? CreateVoice(string key, byte[] data, Hitsound sound)
    {
        // Warming samples must never interrupt an active or future scheduled voice.
        while (voices.Count >= 32 || voiceBytes + data.Length > MemoryLimit)
        {
            var idle = voices.FirstOrDefault(v => v.BusyUntil <= DeviceTime(v.Handle));
            if (idle is null) return null;
            if (lastVoice == idle) lastVoice = null;
            Close(idle.Handle); voices.Remove(idle); voiceBytes -= idle.Bytes;
        }
        nint player = Open(data, data.Length);
        if (player == 0)
        {
            var fallback = HitsoundSamples.CreateWave(sound);
            cacheBytes += fallback.Length - data.Length; cache[key] = fallback; data = fallback;
            player = Open(data, data.Length);
            if (player == 0) return null;
        }
        var voice = new Voice(key, player, data.Length);
        voices.Add(voice); voiceBytes += data.Length; NativePlayerCreations++;
        return voice;
    }
    private static string Key(Hitsound sound) => sound.FilePath ?? $"{sound.Kind}/{sound.SampleSet}/{sound.Name}";
    private byte[] GetData(Hitsound sound)
    {
        string key = Key(sound);
        if (cache.TryGetValue(key, out var data)) return data;
        data = Load(sound);
        if (cacheBytes + data.Length > MemoryLimit) { cache.Clear(); cacheBytes = 0; }
        cache[key] = data; cacheBytes += data.Length;
        return data;
    }
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
    public void Stop()
    {
        foreach (var voice in voices)
        {
            if (voice.BusyUntil == 0) continue;
            Pause(voice.Handle); Seek(voice.Handle, 0); PrepareNative(voice.Handle); voice.BusyUntil = 0;
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var voice in voices) Close(voice.Handle);
        voices.Clear(); cache.Clear(); lastVoice = null; voiceBytes = cacheBytes = 0;
    }
    private const string Library = "FruitsAtelierAudio";
    [DllImport(Library, EntryPoint = "fa_audio_open_data")] private static extern nint Open(byte[] data, int length);
    [DllImport(Library, EntryPoint = "fa_audio_close")] private static extern void Close(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_play")] private static extern int PlayNative(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_play_at")] private static extern int PlayAt(nint handle, double time);
    [DllImport(Library, EntryPoint = "fa_audio_device_time")] private static extern double DeviceTime(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_position")] private static extern double Position(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_duration")] private static extern double Duration(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_prepare")] private static extern int PrepareNative(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_pause")] private static extern void Pause(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_seek")] private static extern void Seek(nint handle, double position);
    [DllImport(Library, EntryPoint = "fa_audio_volume")] private static extern void Volume(nint handle, float volume);
}
