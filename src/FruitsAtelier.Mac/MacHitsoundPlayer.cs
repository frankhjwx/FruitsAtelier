using System.Runtime.InteropServices;
using FruitsAtelier.Core;
using NVorbis;

namespace FruitsAtelier.Mac;

public sealed class MacHitsoundPlayer(bool muted = false) : IDisposable
{
    private readonly Dictionary<string, byte[]> cache = new();
    private readonly List<nint> voices = new();
    private bool disposed;
    private long cacheBytes;
    public void Prepare(Hitsound sound)
    {
        if (disposed) return;
        string key = sound.FilePath ?? $"{sound.Kind}/{sound.SampleSet}/{sound.Name}";
        if (cache.ContainsKey(key)) return;
        var data = Load(sound);
        if (cacheBytes + data.Length > 64 * 1024 * 1024) { cache.Clear(); cacheBytes = 0; }
        cache[key] = data; cacheBytes += data.Length;
    }
    public void Play(Hitsound sound)
    {
        if (disposed) return;
        try
        {
            for (int i = voices.Count - 1; i >= 0; i--)
                if (Playing(voices[i]) == 0) { Close(voices[i]); voices.RemoveAt(i); }
            if (voices.Count == 32) { Close(voices[0]); voices.RemoveAt(0); }
            string key = sound.FilePath ?? $"{sound.Kind}/{sound.SampleSet}/{sound.Name}";
            Prepare(sound);
            var data = cache[key];
            nint player = Open(data, data.Length);
            if (player == 0)
            {
                data = HitsoundSamples.CreateWave(sound);
                cache[key] = data;
                player = Open(data, data.Length);
                if (player == 0) return;
            }
            Volume(player, muted ? 0 : sound.Volume);
            if (PlayNative(player) == 0) Close(player); else voices.Add(player);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { MacPaths.Log(ex.ToString()); }
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
    public int ActiveVoices => voices.Count(v => Playing(v) != 0);
    public void Stop() { foreach (nint voice in voices) Close(voice); voices.Clear(); }
    public void Dispose() { disposed = true; Stop(); cache.Clear(); }
    private const string Library = "FruitsAtelierAudio";
    [DllImport(Library, EntryPoint = "fa_audio_open_data")] private static extern nint Open(byte[] data, int length);
    [DllImport(Library, EntryPoint = "fa_audio_close")] private static extern void Close(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_play")] private static extern int PlayNative(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_playing")] private static extern int Playing(nint handle);
    [DllImport(Library, EntryPoint = "fa_audio_volume")] private static extern void Volume(nint handle, float volume);
}
