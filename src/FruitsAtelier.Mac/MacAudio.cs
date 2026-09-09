using System.Runtime.InteropServices;
using System.Text;
using NVorbis;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

public sealed record MacAudioState(string? FilePath, double PositionMs, double DurationMs, bool IsPlaying, bool CanPlay, bool IsLoading, string? Error);

public sealed class MacAudio : IDisposable
{
    private readonly object gate = new();
    private nint player;
    private CancellationTokenSource? load;
    private string? path, error, cachePath;
    private bool loading, disposed, playbackRequested;
    private readonly bool muted;
    public MacAudio(bool muted = false) => this.muted = muted;
    public MacAudioState State
    {
        get
        {
            lock (gate)
            {
                double duration = player == 0 ? 0 : Duration(player) * 1000;
                double nativePosition = player == 0 ? 0 : Position(player) * 1000;
                // Native completion callbacks can lag behind the device reaching EOF.
                bool playing = player != 0 && Playing(player) != 0 && nativePosition < duration;
                double position = player == 0 ? 0 : playbackRequested && !playing ? duration : nativePosition;
                return new(path, position, duration, playing, player != 0, loading, error);
            }
        }
    }
    public async Task LoadAsync(string? filename)
    {
        CancellationToken token;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            load?.Cancel(); load?.Dispose(); load = new(); token = load.Token;
            Release(); path = filename; error = null; loading = !string.IsNullOrWhiteSpace(filename);
        }
        if (string.IsNullOrWhiteSpace(filename)) return;
        string? temporary = null;
        nint candidate = 0;
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                string source = filename;
                if (Path.GetExtension(filename).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.Combine(MacPaths.Artifacts, "audio"));
                    temporary = Path.Combine(MacPaths.Artifacts, "audio", Guid.NewGuid() + ".wav");
                    DecodeOgg(filename, temporary, token); source = temporary;
                }
                var message = new StringBuilder(2048);
                candidate = Open(source, message, message.Capacity);
                if (candidate == 0) throw new InvalidDataException(message.ToString());
                Volume(candidate, muted ? 0 : 1);
            }, token);
            lock (gate)
            {
                if (disposed || token.IsCancellationRequested) return;
                player = candidate; candidate = 0; cachePath = temporary; temporary = null; loading = false;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lock (gate) if (!disposed && !token.IsCancellationRequested) { loading = false; error = L.Get("files.failed", ex.Message); }
            MacPaths.Log(ex.ToString());
        }
        finally { if (candidate != 0) Close(candidate); DeleteCache(temporary); }
    }
    public void Play()
    {
        lock (gate) if (player != 0)
        {
            if (playbackRequested && Playing(player) == 0 || Position(player) >= Duration(player) - 0.001)
            {
                // AVAudioPlayer can deliver EOF cleanup after Play returns. A fresh player
                // isolates replay from that completed session, just like the Windows transport.
                var message = new StringBuilder(2048);
                nint replacement = Open(cachePath ?? path!, message, message.Capacity);
                if (replacement == 0) { error = L.Get("files.failed", message.ToString()); return; }
                Close(player); player = replacement; Volume(player, muted ? 0 : 1);
            }
            playbackRequested = PlayNative(player) != 0;
            if (!playbackRequested) error = L.Get("mac.audioPlayFailed");
        }
    }
    public void Pause() { lock (gate) if (player != 0) { PauseNative(player); playbackRequested = false; } }
    public void Seek(double ms) { lock (gate) if (player != 0 && double.IsFinite(ms)) { playbackRequested = Playing(player) != 0; SeekNative(player, Math.Clamp(ms / 1000, 0, Duration(player))); } }
    private static void DeleteCache(string? filename) { if (filename is not null) try { File.Delete(filename); } catch (IOException) { } }
    private void Release() { if (player != 0) Close(player); player = 0; playbackRequested = false; DeleteCache(cachePath); cachePath = null; }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; load?.Cancel(); load?.Dispose(); Release(); } }

    private static void DecodeOgg(string source, string destination, CancellationToken token)
    {
        using var reader = new VorbisReader(source);
        if (reader.Channels is < 1 or > 8 || reader.SampleRate is < 8000 or > 192000) throw new InvalidDataException(L.Get("mac.audioFormat"));
        using var stream = File.Create(destination);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)reader.Channels); writer.Write(reader.SampleRate);
        writer.Write(reader.SampleRate * reader.Channels * 2); writer.Write((short)(reader.Channels * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(0);
        float[] samples = new float[16384]; int count;
        while ((count = reader.ReadSamples(samples, 0, samples.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (stream.Length + count * 2 > 512L * 1024 * 1024) throw new InvalidDataException(L.Get("mac.audioLimit"));
            for (int i = 0; i < count; i++) writer.Write((short)Math.Clamp((int)(samples[i] * 32767), -32768, 32767));
        }
        long length = stream.Length; stream.Position = 4; writer.Write((int)length - 8); stream.Position = 40; writer.Write((int)length - 44);
    }
    private const string Library = "FruitsAtelierAudio";
    [DllImport(Library, EntryPoint="fa_audio_open")] private static extern nint Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, StringBuilder error, int capacity);
    [DllImport(Library, EntryPoint="fa_audio_close")] private static extern void Close(nint handle);
    [DllImport(Library, EntryPoint="fa_audio_play")] private static extern int PlayNative(nint handle);
    [DllImport(Library, EntryPoint="fa_audio_pause")] private static extern void PauseNative(nint handle);
    [DllImport(Library, EntryPoint="fa_audio_seek")] private static extern void SeekNative(nint handle, double seconds);
    [DllImport(Library, EntryPoint="fa_audio_position")] private static extern double Position(nint handle);
    [DllImport(Library, EntryPoint="fa_audio_duration")] private static extern double Duration(nint handle);
    [DllImport(Library, EntryPoint="fa_audio_playing")] private static extern int Playing(nint handle);
    [DllImport(Library, EntryPoint="fa_audio_volume")] private static extern void Volume(nint handle, float volume);
}
